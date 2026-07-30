import { mkdtemp, mkdir, readFile, readdir, rm, writeFile } from "node:fs/promises";
import { request as httpRequest, type Server } from "node:http";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { gzipSync } from "node:zlib";

import { afterEach, describe, expect, it, vi } from "vitest";

import {
  CLAUDE_INTEGRATION_MAX_BODY_BYTES,
  CLAUDE_DELIVERY_TTL_MS,
  ClaudeIntegrationManager,
} from "../src/main/claude-integration";

interface TestManager {
  manager: ClaudeIntegrationManager;
  root: string;
  userDataPath: string;
  settingsPath: string;
  onStatusLine: ReturnType<typeof vi.fn>;
  onOtlpLogs: ReturnType<typeof vi.fn>;
  onChanged: ReturnType<typeof vi.fn>;
}

interface HttpResult {
  status: number;
  body: string;
}

const managers: ClaudeIntegrationManager[] = [];
const temporaryRoots: string[] = [];

async function createTestManager(
  initialSettings?: Record<string, unknown>,
): Promise<TestManager> {
  const root = await mkdtemp(join(tmpdir(), "usageapp-claude-test-"));
  const userDataPath = join(root, "usageapp");
  const settingsPath = join(root, "home", ".claude", "settings.json");
  if (initialSettings) {
    await mkdir(dirname(settingsPath), { recursive: true });
    await writeFile(
      settingsPath,
      `${JSON.stringify(initialSettings, null, 2)}\n`,
      "utf8",
    );
  }
  const onStatusLine = vi.fn();
  const onOtlpLogs = vi.fn();
  const onChanged = vi.fn();
  const manager = new ClaudeIntegrationManager({
    userDataPath,
    port: 0,
    claudeSettingsPath: settingsPath,
    onStatusLine,
    onOtlpLogs,
    onChanged,
  });
  managers.push(manager);
  temporaryRoots.push(root);
  return {
    manager,
    root,
    userDataPath,
    settingsPath,
    onStatusLine,
    onOtlpLogs,
    onChanged,
  };
}

async function readSettings(
  settingsPath: string,
): Promise<Record<string, unknown>> {
  return JSON.parse(await readFile(settingsPath, "utf8")) as Record<
    string,
    unknown
  >;
}

function asRecord(value: unknown): Record<string, unknown> {
  expect(value).toBeTypeOf("object");
  expect(value).not.toBeNull();
  expect(Array.isArray(value)).toBe(false);
  return value as Record<string, unknown>;
}

function rawRequest(
  urlString: string,
  options: {
    method?: string;
    headers?: Record<string, string>;
    body?: Buffer | string;
  } = {},
): Promise<HttpResult> {
  const url = new URL(urlString);
  return new Promise<HttpResult>((resolvePromise, reject) => {
    const request = httpRequest(
      {
        hostname: url.hostname,
        port: Number(url.port),
        path: `${url.pathname}${url.search}`,
        method: options.method ?? "POST",
        headers: options.headers,
      },
      (response) => {
        const chunks: Buffer[] = [];
        response.on("data", (chunk) => {
          chunks.push(Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk));
        });
        response.on("end", () => {
          resolvePromise({
            status: response.statusCode ?? 0,
            body: Buffer.concat(chunks).toString("utf8"),
          });
        });
      },
    );
    request.on("error", reject);
    if (options.body !== undefined) {
      request.write(options.body);
    }
    request.end();
  });
}

afterEach(async () => {
  await Promise.all(
    managers.splice(0).map(async (manager) => {
      await manager.stop();
    }),
  );
  for (const root of temporaryRoots.splice(0)) {
    const resolvedRoot = resolve(root);
    const resolvedTemp = resolve(tmpdir());
    if (
      resolvedRoot !== resolvedTemp &&
      resolvedRoot.startsWith(`${resolvedTemp}\\`)
    ) {
      await rm(resolvedRoot, { recursive: true, force: true });
    }
  }
});

describe("ClaudeIntegrationManager settings integration", () => {
  it("connects without replacing unrelated settings and restores exact prior values", async () => {
    const initialSettings = {
      theme: "dark",
      env: {
        USAGEAPP_TEST_KEEP: "unchanged",
      },
      statusLine: {
        type: "command",
        command: "echo original-status",
        padding: 3,
        refreshInterval: 500,
      },
      permissions: {
        allow: ["Read"],
      },
    };
    const {
      manager,
      settingsPath,
      userDataPath,
      onChanged,
    } = await createTestManager(initialSettings);

    const connected = await manager.connect();

    expect(connected).toMatchObject({
      state: "awaiting-session",
      statusLineConfigured: true,
      telemetryConfigured: true,
      statusLineConnected: false,
      telemetryConnected: false,
      receiverListening: true,
    });
    expect(onChanged).toHaveBeenCalled();

    const installed = await readSettings(settingsPath);
    expect(installed.theme).toBe("dark");
    expect(installed.permissions).toEqual(initialSettings.permissions);
    const installedEnv = asRecord(installed.env);
    expect(installedEnv).toMatchObject({
      USAGEAPP_TEST_KEEP: "unchanged",
      CLAUDE_CODE_ENABLE_TELEMETRY: "1",
      OTEL_LOGS_EXPORTER: "otlp",
      OTEL_EXPORTER_OTLP_LOGS_PROTOCOL: "http/json",
      OTEL_LOGS_EXPORT_INTERVAL: "5000",
    });
    expect(installedEnv.OTEL_EXPORTER_OTLP_LOGS_ENDPOINT).toMatch(
      /^http:\/\/127\.0\.0\.1:\d+\/v1\/logs\/[A-Za-z0-9_-]{32,128}$/,
    );
    expect(installedEnv).not.toHaveProperty("OTEL_EXPORTER_OTLP_ENDPOINT");
    expect(installedEnv).not.toHaveProperty("OTEL_EXPORTER_OTLP_HEADERS");
    expect(installedEnv).not.toHaveProperty("OTEL_LOG_USER_PROMPTS");

    const installedStatusLine = asRecord(installed.statusLine);
    expect(installedStatusLine).toMatchObject({
      type: "command",
      padding: 3,
      refreshInterval: 500,
    });
    expect(installedStatusLine.command).toContain(
      "statusline-wrapper.ps1",
    );

    const wrapper = await readFile(
      join(
        userDataPath,
        "claude-integration",
        "statusline-wrapper.ps1",
      ),
      "utf8",
    );
    expect(wrapper).toContain("[Console]::In.ReadToEnd()");
    expect(wrapper).toContain("127.0.0.1");
    expect(wrapper).toContain("Invoke-WebRequest");
    expect(wrapper).toContain("$process.StandardInput.Write($rawJson)");
    expect(
      await readFile(
        join(
          userDataPath,
          "claude-integration",
          "prior-statusline.cmd",
        ),
        "utf8",
      ),
    ).toContain("echo original-status");

    const settingsDirectory = dirname(settingsPath);
    const backups = (await readdir(settingsDirectory)).filter((entry) =>
      /^settings\.json\.usageapp-\d{8}T\d{6}Z-[a-f0-9]{8}\.bak$/.test(
        entry,
      ),
    );
    expect(backups).toHaveLength(1);
    expect(
      await readFile(join(settingsDirectory, backups[0] as string), "utf8"),
    ).toBe(`${JSON.stringify(initialSettings, null, 2)}\n`);

    const disconnected = await manager.disconnect();
    expect(disconnected).toMatchObject({
      state: "disconnected",
      statusLineConnected: false,
      telemetryConnected: false,
      receiverListening: true,
    });
    expect(await readSettings(settingsPath)).toEqual(initialSettings);
    await expect(
      readFile(
        join(
          userDataPath,
          "claude-integration",
          "statusline-wrapper.ps1",
        ),
      ),
    ).rejects.toMatchObject({ code: "ENOENT" });
  });

  it("preserves an existing OTel destination and connects only the quota bridge", async () => {
    const initialSettings = {
      env: {
        KEEP: "yes",
        OTEL_EXPORTER_OTLP_ENDPOINT: "http://collector.example:4318",
      },
      statusLine: {
        type: "command",
        command: "echo keep-me",
        padding: 1,
      },
    };
    const { manager, settingsPath } =
      await createTestManager(initialSettings);

    const status = await manager.connect();

    expect(status.state).toBe("partial");
    expect(status.statusLineConfigured).toBe(true);
    expect(status.statusLineConnected).toBe(false);
    expect(status.telemetryConnected).toBe(false);
    expect(status.message).toContain("did not take over");
    const installed = await readSettings(settingsPath);
    const env = asRecord(installed.env);
    expect(env).toEqual(initialSettings.env);
    expect(env).not.toHaveProperty("OTEL_LOGS_EXPORTER");
    expect(asRecord(installed.statusLine).command).not.toBe(
      initialSettings.statusLine.command,
    );

    expect((await manager.disconnect()).state).toBe("disconnected");
    expect(await readSettings(settingsPath)).toEqual(initialSettings);
  });

  it("restores only unchanged installed values and reports user edits", async () => {
    const initialSettings = {
      unrelated: {
        keep: true,
      },
    };
    const { manager, settingsPath, userDataPath } =
      await createTestManager(initialSettings);
    await manager.connect();

    const userEdited = await readSettings(settingsPath);
    const editedEnv = asRecord(userEdited.env);
    editedEnv.OTEL_LOGS_EXPORT_INTERVAL = "9999";
    const editedStatusLine = asRecord(userEdited.statusLine);
    editedStatusLine.padding = 9;
    await writeFile(
      settingsPath,
      `${JSON.stringify(userEdited, null, 2)}\n`,
      "utf8",
    );

    const status = await manager.disconnect();

    expect(status.state).toBe("conflict");
    expect(status.message).toContain("user changes preserved");
    const after = await readSettings(settingsPath);
    expect(after.unrelated).toEqual(initialSettings.unrelated);
    const afterEnv = asRecord(after.env);
    expect(afterEnv).toEqual({
      OTEL_LOGS_EXPORT_INTERVAL: "9999",
    });
    const afterStatusLine = asRecord(after.statusLine);
    expect(afterStatusLine.padding).toBe(9);
    expect(afterStatusLine.command).toContain("statusline-wrapper.ps1");
    expect(
      await readFile(
        join(
          userDataPath,
          "claude-integration",
          "statusline-wrapper.ps1",
        ),
        "utf8",
      ),
    ).toContain("Invoke-WebRequest");
  });
});

describe("ClaudeIntegrationManager delivery reporting", () => {
  it("stops reporting a connection once Claude goes quiet", async () => {
    const { manager } = await createTestManager({ theme: "dark" });
    await manager.connect();

    manager.markStatusLineReceived(true);
    manager.markTelemetryReceived();
    expect(manager.status()).toMatchObject({
      state: "connected",
      statusLineConnected: true,
      telemetryConnected: true,
    });

    const afterTtl = Date.now() + CLAUDE_DELIVERY_TTL_MS + 1_000;
    expect(manager.status(afterTtl)).toMatchObject({
      state: "awaiting-session",
      statusLineConnected: false,
      telemetryConnected: false,
    });
    expect(manager.status(afterTtl).message).toContain("gone quiet");
  });

  it("does not claim quota is flowing from a status line without rate limits", async () => {
    const { manager } = await createTestManager({ theme: "dark" });
    await manager.connect();

    manager.markStatusLineReceived(false);

    expect(manager.status()).toMatchObject({
      state: "awaiting-session",
      statusLineConnected: false,
    });
    expect(manager.status().message).toContain("first response");
  });

  it("reports a healthy connection from the desktop app alone", async () => {
    const { manager } = await createTestManager({ theme: "dark" });
    await manager.connect();

    manager.markPlanUsageReceived(Date.now());

    const status = manager.status();
    expect(status.state).toBe("partial");
    expect(status.message).toContain("local activity telemetry");
    // The Claude Code bridges genuinely are not delivering, and saying so is
    // what tells the user why local activity history is not filling in.
    expect(status.statusLineConnected).toBe(false);
    expect(status.telemetryConnected).toBe(false);
  });

  it("counts the desktop app as full quota coverage alongside telemetry", async () => {
    const { manager } = await createTestManager({ theme: "dark" });
    await manager.connect();

    // The exact state on this machine: telemetry flowing, no status line.
    manager.markTelemetryReceived();
    manager.markPlanUsageReceived(Date.now());

    const status = manager.status();
    expect(status.state).toBe("connected");
    expect(status.message).toContain("Reset times");
  });
});

describe("ClaudeIntegrationManager loopback receiver", () => {
  it("authenticates tokenized JSON paths, accepts gzip, and enforces the body cap", async () => {
    const {
      manager,
      settingsPath,
      onStatusLine,
      onOtlpLogs,
    } = await createTestManager({});
    await manager.connect();

    const installed = await readSettings(settingsPath);
    const env = asRecord(installed.env);
    const logsEndpoint = String(
      env.OTEL_EXPORTER_OTLP_LOGS_ENDPOINT,
    );
    const statusEndpoint = logsEndpoint.replace(
      "/v1/logs/",
      "/v1/statusline/",
    );
    const privateServer = (
      manager as unknown as { server: Server | null }
    ).server;
    const address = privateServer?.address();
    expect(address).not.toBeNull();
    expect(typeof address).not.toBe("string");
    if (!address || typeof address === "string") {
      throw new Error("Expected a loopback TCP address.");
    }
    expect(address.address).toBe("127.0.0.1");

    const statusPayload = {
      session_id: "session-test",
      rate_limits: {
        five_hour: {
          used_percentage: 25,
          resets_at: 1_900_000_000,
        },
      },
    };
    const statusResponse = await rawRequest(statusEndpoint, {
      headers: {
        "content-type": "application/json; charset=utf-8",
      },
      body: JSON.stringify(statusPayload),
    });
    expect(statusResponse).toEqual({ status: 200, body: "{}" });
    expect(onStatusLine).toHaveBeenCalledWith(statusPayload);

    const logsPayload = {
      resourceLogs: [
        {
          scopeLogs: [
            {
              logRecords: [
                {
                  attributes: [
                    {
                      key: "event.name",
                      value: { stringValue: "api_request" },
                    },
                  ],
                },
              ],
            },
          ],
        },
      ],
    };
    const compressed = gzipSync(
      Buffer.from(JSON.stringify(logsPayload), "utf8"),
    );
    const logsResponse = await rawRequest(logsEndpoint, {
      headers: {
        "content-type": "application/json",
        "content-encoding": "gzip",
        "content-length": String(compressed.length),
      },
      body: compressed,
    });
    expect(logsResponse).toEqual({ status: 200, body: "{}" });
    expect(onOtlpLogs).toHaveBeenCalledWith(logsPayload);

    const wrongToken = await rawRequest(`${logsEndpoint}-wrong`, {
      headers: { "content-type": "application/json" },
      body: "{}",
    });
    expect(wrongToken.status).toBe(404);
    expect(onOtlpLogs).toHaveBeenCalledTimes(1);

    const wrongType = await rawRequest(logsEndpoint, {
      headers: { "content-type": "text/plain" },
      body: "{}",
    });
    expect(wrongType.status).toBe(415);

    const wrongMethod = await rawRequest(logsEndpoint, {
      method: "GET",
    });
    expect(wrongMethod.status).toBe(405);

    const oversized = await rawRequest(logsEndpoint, {
      headers: {
        "content-type": "application/json",
        "content-length": String(
          CLAUDE_INTEGRATION_MAX_BODY_BYTES + 1,
        ),
      },
    });
    expect(oversized.status).toBe(413);
    expect(onOtlpLogs).toHaveBeenCalledTimes(1);
  });
});
