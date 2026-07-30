import { randomBytes, createHash } from "node:crypto";
import {
  mkdir,
  readFile,
  rename,
  unlink,
  writeFile,
} from "node:fs/promises";
import {
  createServer,
  type IncomingMessage,
  type Server,
  type ServerResponse,
} from "node:http";
import { homedir } from "node:os";
import {
  dirname,
  isAbsolute,
  join,
  relative,
  resolve,
} from "node:path";
import { gunzipSync } from "node:zlib";
import { isDeepStrictEqual } from "node:util";

import type {
  ClaudeIntegrationState,
  ClaudeIntegrationStatus,
} from "../shared/desktop";

const LOOPBACK_HOST = "127.0.0.1";
const STATE_VERSION = 1;
const INTEGRATION_STATE_NAME = "claude-integration.json";
const WRAPPER_DIRECTORY_NAME = "claude-integration";
const WRAPPER_FILE_NAME = "statusline-wrapper.ps1";
const PRIOR_COMMAND_FILE_NAME = "prior-statusline.cmd";

export const CLAUDE_INTEGRATION_MAX_BODY_BYTES = 8 * 1024 * 1024;

/**
 * How long a delivery signal keeps counting as "connected".
 *
 * Claude only pushes while it is running, so a received flag that never
 * expires would report a healthy connection long after the data stopped.
 */
export const CLAUDE_DELIVERY_TTL_MS = 15 * 60_000;

const TELEMETRY_ENVIRONMENT_KEYS = [
  "CLAUDE_CODE_ENABLE_TELEMETRY",
  "OTEL_LOGS_EXPORTER",
  "OTEL_EXPORTER_OTLP_LOGS_PROTOCOL",
  "OTEL_EXPORTER_OTLP_LOGS_ENDPOINT",
  "OTEL_LOGS_EXPORT_INTERVAL",
] as const;

const OTEL_TAKEOVER_KEYS = [
  ...TELEMETRY_ENVIRONMENT_KEYS,
  "OTEL_EXPORTER_OTLP_PROTOCOL",
  "OTEL_EXPORTER_OTLP_ENDPOINT",
  "OTEL_EXPORTER_OTLP_HEADERS",
  "OTEL_EXPORTER_OTLP_LOGS_HEADERS",
] as const;

const SENSITIVE_CONTENT_KEYS = [
  "OTEL_LOG_USER_PROMPTS",
  "OTEL_LOG_ASSISTANT_RESPONSES",
  "OTEL_LOG_TOOL_DETAILS",
  "OTEL_LOG_TOOL_CONTENT",
  "OTEL_LOG_RAW_API_BODIES",
] as const;

interface StoredValueChange {
  existed: boolean;
  priorValue?: unknown;
  installedValue: unknown;
}

interface StoredInstallation {
  settingsPath: string;
  settingsFileExisted: boolean;
  envContainerExisted: boolean;
  connectedAt: string;
  backupPath: string | null;
  telemetryInstalled: boolean;
  envChanges: Record<string, StoredValueChange>;
  statusLineChange: StoredValueChange | null;
  wrapperSha256: string | null;
  priorCommandSha256: string | null;
  conflicts: string[];
}

interface StoredIntegrationState {
  version: typeof STATE_VERSION;
  pathToken: string;
  installation: StoredInstallation | null;
}

interface SettingsDocument {
  existed: boolean;
  raw: string | null;
  value: Record<string, unknown>;
}

export interface ClaudeIntegrationManagerOptions {
  userDataPath: string;
  port: number;
  onStatusLine: (raw: unknown) => void | Promise<void>;
  onOtlpLogs: (raw: unknown) => void | Promise<void>;
  onChanged: () => void;
  /**
   * Test/portable override. Production defaults to
   * %USERPROFILE%\.claude\settings.json through os.homedir().
   */
  claudeSettingsPath?: string;
  homePath?: string;
}

export interface ClaudeIntegrationConfiguration {
  enabled?: boolean;
  port?: number;
  /** AppSettings-compatible aliases. */
  claudeEnabled?: boolean;
  claudeTelemetryPort?: number;
}

class BodyTooLargeError extends Error {}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function hasOwn(value: object, key: PropertyKey): boolean {
  return Object.prototype.hasOwnProperty.call(value, key);
}

function sha256(value: string | Buffer): string {
  return createHash("sha256").update(value).digest("hex");
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : "Unknown error.";
}

function isStoredValueChange(value: unknown): value is StoredValueChange {
  return (
    isRecord(value) &&
    typeof value.existed === "boolean" &&
    hasOwn(value, "installedValue") &&
    (!value.existed || hasOwn(value, "priorValue"))
  );
}

function isStoredInstallation(value: unknown): value is StoredInstallation {
  if (
    !isRecord(value) ||
    typeof value.settingsPath !== "string" ||
    typeof value.settingsFileExisted !== "boolean" ||
    typeof value.envContainerExisted !== "boolean" ||
    typeof value.connectedAt !== "string" ||
    !(
      value.backupPath === null ||
      typeof value.backupPath === "string"
    ) ||
    typeof value.telemetryInstalled !== "boolean" ||
    !isRecord(value.envChanges) ||
    !(
      value.statusLineChange === null ||
      isStoredValueChange(value.statusLineChange)
    ) ||
    !(
      value.wrapperSha256 === null ||
      typeof value.wrapperSha256 === "string"
    ) ||
    !(
      value.priorCommandSha256 === null ||
      typeof value.priorCommandSha256 === "string"
    ) ||
    !Array.isArray(value.conflicts) ||
    !value.conflicts.every((entry) => typeof entry === "string")
  ) {
    return false;
  }
  return Object.values(value.envChanges).every(isStoredValueChange);
}

function isStoredState(value: unknown): value is StoredIntegrationState {
  return (
    isRecord(value) &&
    value.version === STATE_VERSION &&
    typeof value.pathToken === "string" &&
    /^[A-Za-z0-9_-]{32,128}$/.test(value.pathToken) &&
    (value.installation === null ||
      isStoredInstallation(value.installation))
  );
}

function assertPort(port: number): void {
  if (!Number.isInteger(port) || port < 0 || port > 65_535) {
    throw new Error("Claude telemetry port must be an integer from 0 to 65535.");
  }
}

function jsonResponse(
  response: ServerResponse,
  statusCode: number,
  body: unknown,
): void {
  const payload = JSON.stringify(body);
  response.writeHead(statusCode, {
    "Cache-Control": "no-store",
    "Content-Type": "application/json; charset=utf-8",
    "Content-Length": Buffer.byteLength(payload),
    "X-Content-Type-Options": "nosniff",
  });
  response.end(payload);
}

function isJsonContentType(value: string | undefined): boolean {
  return (
    value
      ?.split(";", 1)[0]
      ?.trim()
      .toLowerCase() === "application/json"
  );
}

async function readRequestBuffer(
  request: IncomingMessage,
): Promise<Buffer> {
  const advertisedLength = Number(request.headers["content-length"]);
  if (
    Number.isFinite(advertisedLength) &&
    advertisedLength > CLAUDE_INTEGRATION_MAX_BODY_BYTES
  ) {
    request.resume();
    throw new BodyTooLargeError("Request body is too large.");
  }

  const chunks: Buffer[] = [];
  let length = 0;
  for await (const chunk of request) {
    const buffer = Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk);
    length += buffer.length;
    if (length > CLAUDE_INTEGRATION_MAX_BODY_BYTES) {
      throw new BodyTooLargeError("Request body is too large.");
    }
    chunks.push(buffer);
  }
  return Buffer.concat(chunks);
}

async function readJsonRequest(request: IncomingMessage): Promise<unknown> {
  const encoding = (request.headers["content-encoding"] ?? "identity")
    .trim()
    .toLowerCase();
  if (encoding !== "identity" && encoding !== "gzip") {
    throw new TypeError("Unsupported content encoding.");
  }

  const encoded = await readRequestBuffer(request);
  let decoded: Buffer;
  if (encoding === "gzip") {
    try {
      decoded = gunzipSync(encoded, {
        maxOutputLength: CLAUDE_INTEGRATION_MAX_BODY_BYTES,
      });
    } catch (error) {
      if (
        (error as NodeJS.ErrnoException).code === "ERR_BUFFER_TOO_LARGE"
      ) {
        throw new BodyTooLargeError("Request body is too large.");
      }
      throw new SyntaxError("Invalid gzip request body.");
    }
  } else {
    decoded = encoded;
  }

  if (decoded.length > CLAUDE_INTEGRATION_MAX_BODY_BYTES) {
    throw new BodyTooLargeError("Request body is too large.");
  }
  if (decoded.length === 0) {
    throw new SyntaxError("A JSON request body is required.");
  }
  return JSON.parse(decoded.toString("utf8")) as unknown;
}

function timestampForFileName(date = new Date()): string {
  return date
    .toISOString()
    .replace(/[-:]/g, "")
    .replace(/\.\d{3}Z$/, "Z");
}

async function atomicWrite(
  path: string,
  contents: string,
): Promise<void> {
  await mkdir(dirname(path), { recursive: true });
  const temporaryPath = `${path}.${process.pid}.${randomBytes(6).toString(
    "hex",
  )}.tmp`;
  try {
    await writeFile(temporaryPath, contents, {
      encoding: "utf8",
      mode: 0o600,
    });
    await rename(temporaryPath, path);
  } catch (error) {
    await unlink(temporaryPath).catch(() => {
      // The temporary file may not have been created.
    });
    throw error;
  }
}

async function atomicWriteJson(
  path: string,
  value: unknown,
): Promise<void> {
  await atomicWrite(path, `${JSON.stringify(value, null, 2)}\n`);
}

function isPathInside(parentPath: string, candidatePath: string): boolean {
  const pathFromParent = relative(resolve(parentPath), resolve(candidatePath));
  return (
    pathFromParent === "" ||
    (!pathFromParent.startsWith("..") && !isAbsolute(pathFromParent))
  );
}

function powershellSingleQuoted(value: string): string {
  return `'${value.replace(/'/g, "''")}'`;
}

function statusLineCommand(wrapperPath: string): string {
  return `powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "${wrapperPath.replace(
    /"/g,
    '""',
  )}"`;
}

function statusLineWrapper(
  endpoint: string,
  priorCommandPath: string | null,
): string {
  const priorPathLiteral =
    priorCommandPath === null
      ? "$null"
      : powershellSingleQuoted(priorCommandPath);
  return `# Generated by UsageApp. This bridge is intentionally loopback-only.
$ErrorActionPreference = 'Stop'
$rawJson = [Console]::In.ReadToEnd()

try {
  $utf8 = New-Object System.Text.UTF8Encoding($false)
  $body = $utf8.GetBytes($rawJson)
  Invoke-WebRequest -UseBasicParsing -DisableKeepAlive -Method Post -Uri ${powershellSingleQuoted(
    endpoint,
  )} -ContentType 'application/json' -Body $body -TimeoutSec 1 | Out-Null
} catch {
  # UsageApp must never prevent an existing status line from running.
}

$priorCommandPath = ${priorPathLiteral}
if ([string]::IsNullOrWhiteSpace($priorCommandPath)) {
  exit 0
}

try {
  $process = New-Object System.Diagnostics.Process
  $process.StartInfo = New-Object System.Diagnostics.ProcessStartInfo
  $process.StartInfo.FileName = if ([string]::IsNullOrWhiteSpace($env:ComSpec)) { 'cmd.exe' } else { $env:ComSpec }
  $escapedPath = $priorCommandPath.Replace('"', '""')
  $process.StartInfo.Arguments = '/d /s /c ""' + $escapedPath + '""'
  $process.StartInfo.UseShellExecute = $false
  $process.StartInfo.CreateNoWindow = $true
  $process.StartInfo.RedirectStandardInput = $true
  $process.StartInfo.StandardInputEncoding = New-Object System.Text.UTF8Encoding($false)
  if (-not $process.Start()) {
    exit 0
  }
  $process.StandardInput.Write($rawJson)
  $process.StandardInput.Close()
  $process.WaitForExit()
  exit $process.ExitCode
} catch {
  # Fail open if the preserved command can no longer be started.
  exit 0
}
`;
}

function priorCommandScript(command: string): string {
  return `@echo off\r\n${command}\r\n`;
}

function environmentValues(
  port: number,
  pathToken: string,
): Record<(typeof TELEMETRY_ENVIRONMENT_KEYS)[number], string> {
  return {
    CLAUDE_CODE_ENABLE_TELEMETRY: "1",
    OTEL_LOGS_EXPORTER: "otlp",
    OTEL_EXPORTER_OTLP_LOGS_PROTOCOL: "http/json",
    OTEL_EXPORTER_OTLP_LOGS_ENDPOINT:
      `http://${LOOPBACK_HOST}:${port}/v1/logs/${pathToken}`,
    OTEL_LOGS_EXPORT_INTERVAL: "5000",
  };
}

function contentLoggingEnabled(value: unknown): boolean {
  if (typeof value !== "string") {
    return value === true || (typeof value === "number" && value !== 0);
  }
  return !["", "0", "false", "off", "no"].includes(
    value.trim().toLowerCase(),
  );
}

export class ClaudeIntegrationManager {
  private readonly userDataPath: string;
  private readonly statePath: string;
  private readonly wrapperDirectory: string;
  private readonly wrapperPath: string;
  private readonly priorCommandPath: string;
  private readonly claudeSettingsPath: string;
  private readonly onStatusLine: (
    raw: unknown,
  ) => void | Promise<void>;
  private readonly onOtlpLogs: (
    raw: unknown,
  ) => void | Promise<void>;
  private readonly onChanged: () => void;

  private configuredPort: number;
  private listeningPort: number | null = null;
  private server: Server | null = null;
  private listening = false;
  private storedState: StoredIntegrationState | null = null;
  private stateLoaded = false;
  private operationQueue: Promise<void> = Promise.resolve();

  /** Derived from Claude's settings file, never from received data. */
  private installationState: ClaudeIntegrationState = "disconnected";
  private statusLineConfigured = false;
  private telemetryConfigured = false;
  private message: string | null = "Claude monitoring is not connected.";

  // Delivery signals, held as timestamps so they can go quiet on their own.
  private statusLineQuotaAt: number | null = null;
  private statusLineSeenAt: number | null = null;
  private telemetryAt: number | null = null;
  private planUsageAt: number | null = null;

  constructor(options: ClaudeIntegrationManagerOptions) {
    assertPort(options.port);
    this.userDataPath = resolve(options.userDataPath);
    this.statePath = join(
      this.userDataPath,
      INTEGRATION_STATE_NAME,
    );
    this.wrapperDirectory = join(
      this.userDataPath,
      WRAPPER_DIRECTORY_NAME,
    );
    this.wrapperPath = join(
      this.wrapperDirectory,
      WRAPPER_FILE_NAME,
    );
    this.priorCommandPath = join(
      this.wrapperDirectory,
      PRIOR_COMMAND_FILE_NAME,
    );
    const homePath = resolve(options.homePath ?? homedir());
    this.claudeSettingsPath = resolve(
      options.claudeSettingsPath ??
        join(homePath, ".claude", "settings.json"),
    );
    this.configuredPort = options.port;
    this.onStatusLine = options.onStatusLine;
    this.onOtlpLogs = options.onOtlpLogs;
    this.onChanged = options.onChanged;
  }

  status(now = Date.now()): ClaudeIntegrationStatus {
    const fresh = (at: number | null): boolean =>
      at !== null && now - at <= CLAUDE_DELIVERY_TTL_MS;
    const quotaLive = fresh(this.statusLineQuotaAt);
    const telemetryLive = fresh(this.telemetryAt);
    const planUsageLive = fresh(this.planUsageAt);
    const base = {
      statusLineConfigured: this.statusLineConfigured,
      telemetryConfigured: this.telemetryConfigured,
      statusLineConnected: quotaLive,
      telemetryConnected: telemetryLive,
      receiverListening: this.listening,
    };

    // A conflicting or unconfigured installation has a message worth keeping;
    // delivery only refines the fully-configured case.
    if (this.installationState !== "awaiting-session") {
      return {
        ...base,
        state: this.installationState,
        message: this.message,
      };
    }

    // Quota is covered by either source. The status line additionally supplies
    // reset times, which is the only thing the desktop app cannot provide.
    const quotaCovered = quotaLive || planUsageLive;
    if (quotaCovered && telemetryLive) {
      return {
        ...base,
        state: "connected",
        message: quotaLive
          ? "Claude quota and local activity are receiving data."
          : "Claude quota is tracking the Claude desktop app, and local activity is recording. Reset times need a Claude Code terminal session.",
      };
    }
    if (quotaCovered || telemetryLive) {
      return {
        ...base,
        state: "partial",
        message: quotaCovered
          ? "Claude quota is receiving data. Waiting for local activity telemetry."
          : "Claude activity telemetry is receiving data. Waiting for a quota source.",
      };
    }
    if (fresh(this.statusLineSeenAt)) {
      return {
        ...base,
        state: "awaiting-session",
        message:
          "Claude Code is running. Waiting for its first response to report quota.",
      };
    }

    const everReceived =
      this.statusLineQuotaAt !== null ||
      this.telemetryAt !== null ||
      this.planUsageAt !== null;
    return {
      ...base,
      state: "awaiting-session",
      message: everReceived
        ? "Claude has gone quiet. Quota updates resume when Claude Code or the Claude desktop app runs."
        : "Claude setup is ready. Close and start a new Claude Code session to send its first usage update.",
    };
  }

  start(): Promise<ClaudeIntegrationStatus> {
    return this.runSerialized(async () => {
      await this.startInternal();
    });
  }

  stop(): Promise<ClaudeIntegrationStatus> {
    return this.runSerialized(async () => {
      await this.stopInternal();
    });
  }

  configure(
    configuration: ClaudeIntegrationConfiguration,
  ): Promise<ClaudeIntegrationStatus> {
    return this.runSerialized(async () => {
      await this.loadState();
      const nextPort =
        configuration.port ??
        configuration.claudeTelemetryPort ??
        this.configuredPort;
      assertPort(nextPort);
      const enabled =
        configuration.enabled ??
        configuration.claudeEnabled ??
        this.storedState?.installation !== null;

      if (nextPort !== this.configuredPort) {
        if (this.storedState?.installation) {
          await this.disconnectInternal();
          if (
            this.installationState === "conflict" ||
            this.installationState === "error"
          ) {
            await this.stopInternal();
            this.configuredPort = nextPort;
            return;
          }
        }
        await this.stopInternal();
        this.configuredPort = nextPort;
      }

      if (enabled) {
        await this.startInternal();
        if (this.listening) {
          await this.connectInternal();
        }
      } else {
        await this.disconnectInternal();
        await this.stopInternal();
      }
    });
  }

  connect(): Promise<ClaudeIntegrationStatus> {
    return this.runSerialized(async () => {
      await this.startInternal();
      if (this.listening) {
        await this.connectInternal();
      }
    });
  }

  disconnect(): Promise<ClaudeIntegrationStatus> {
    return this.runSerialized(async () => {
      await this.disconnectInternal();
    });
  }

  /**
   * @param hasQuota Whether the payload actually carried rate limits. Claude
   * omits them until a session's first API response, so a status line alone
   * does not mean quota is flowing.
   */
  markStatusLineReceived(hasQuota: boolean): void {
    const now = Date.now();
    this.statusLineSeenAt = now;
    if (hasQuota) {
      this.statusLineQuotaAt = now;
    }
    this.notifyChanged();
  }

  markTelemetryReceived(): void {
    this.telemetryAt = Date.now();
    this.notifyChanged();
  }

  /** @param observedAtMs When the desktop app took the reading, not now. */
  markPlanUsageReceived(observedAtMs: number | null): void {
    this.planUsageAt = observedAtMs;
    this.notifyChanged();
  }

  private runSerialized(
    action: () => Promise<void>,
  ): Promise<ClaudeIntegrationStatus> {
    let result: ClaudeIntegrationStatus | null = null;
    const operation = this.operationQueue.then(async () => {
      try {
        await action();
      } catch (error) {
        this.installationState = "error";
        this.message = `Claude integration failed: ${errorMessage(error)}`;
        this.notifyChanged();
      }
      result = this.status();
    });
    this.operationQueue = operation.catch(() => {
      // Keep later lifecycle/settings operations usable after a failure.
    });
    return operation.then(() => {
      if (!result) {
        throw new Error("Claude integration did not return a status.");
      }
      return result;
    });
  }

  private notifyChanged(): void {
    try {
      this.onChanged();
    } catch {
      // A UI notification failure must not break collection or restoration.
    }
  }

  private async loadState(): Promise<void> {
    if (this.stateLoaded) {
      return;
    }
    try {
      const parsed = JSON.parse(
        await readFile(this.statePath, "utf8"),
      ) as unknown;
      if (!isStoredState(parsed)) {
        throw new Error(
          "UsageApp's Claude integration state is invalid; settings were not changed.",
        );
      }
      this.storedState = parsed;
      this.applyStoredStatus();
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code !== "ENOENT") {
        throw error;
      }
      this.storedState = {
        version: STATE_VERSION,
        pathToken: randomBytes(32).toString("base64url"),
        installation: null,
      };
      await this.persistState();
    }
    this.stateLoaded = true;
  }

  private async persistState(): Promise<void> {
    if (!this.storedState) {
      throw new Error("Claude integration state is not initialized.");
    }
    await atomicWriteJson(this.statePath, this.storedState);
  }

  private applyStoredStatus(): void {
    const installation = this.storedState?.installation;
    if (!installation) {
      this.installationState = "disconnected";
      this.statusLineConfigured = false;
      this.telemetryConfigured = false;
      this.clearDeliverySignals();
      this.message = "Claude monitoring is not connected.";
      return;
    }

    this.statusLineConfigured = installation.statusLineChange !== null;
    this.telemetryConfigured = installation.telemetryInstalled;
    if (
      this.statusLineConfigured &&
      this.telemetryConfigured &&
      installation.conflicts.length === 0
    ) {
      this.installationState = "awaiting-session";
      this.message =
        "Claude setup is ready. Close and start a new Claude Code session to send its first usage update.";
      return;
    }
    if (this.statusLineConfigured || this.telemetryConfigured) {
      this.installationState = "partial";
      this.message = this.connectionMessage(
        "Claude monitoring is partially connected.",
        installation.conflicts,
      );
      return;
    }
    this.installationState = "conflict";
    this.message = this.connectionMessage(
      "Claude settings conflict with UsageApp.",
      installation.conflicts,
    );
  }

  /**
   * Forgets that Claude Code ever reported in. Only correct when the
   * installation itself changed; ordinary silence is handled by the delivery
   * TTL. The desktop app's plan-usage signal is deliberately kept: it does not
   * depend on anything UsageApp installs.
   */
  private clearDeliverySignals(): void {
    this.statusLineQuotaAt = null;
    this.statusLineSeenAt = null;
    this.telemetryAt = null;
  }

  private connectionMessage(prefix: string, conflicts: string[]): string {
    return conflicts.length > 0
      ? `${prefix} ${conflicts.join(" ")}`
      : prefix;
  }

  private async startInternal(): Promise<void> {
    await this.loadState();
    if (this.server) {
      return;
    }

    const server = createServer((request, response) => {
      void this.handleRequest(request, response).catch(() => {
        if (!response.headersSent) {
          jsonResponse(response, 500, {
            error: "The local Claude receiver could not process the request.",
          });
        } else if (!response.writableEnded) {
          response.end();
        }
      });
    });
    server.requestTimeout = 10_000;
    server.headersTimeout = 5_000;
    server.keepAliveTimeout = 1_000;

    try {
      await new Promise<void>((resolvePromise, reject) => {
        const onError = (error: Error) => {
          server.off("listening", onListening);
          reject(error);
        };
        const onListening = () => {
          server.off("error", onError);
          resolvePromise();
        };
        server.once("error", onError);
        server.once("listening", onListening);
        server.listen({
          host: LOOPBACK_HOST,
          port: this.configuredPort,
          exclusive: true,
        });
      });
    } catch (error) {
      server.close();
      throw error;
    }

    const address = server.address();
    if (!address || typeof address === "string") {
      server.close();
      throw new Error("Claude receiver did not obtain a TCP loopback port.");
    }
    if (address.address !== LOOPBACK_HOST) {
      server.close();
      throw new Error("Claude receiver refused a non-loopback binding.");
    }

    this.server = server;
    this.listening = true;
    this.listeningPort = address.port;
    server.on("error", (error) => {
      this.listening = false;
      this.listeningPort = null;
      this.installationState = "error";
      this.message = `Claude receiver stopped: ${error.message}`;
      this.notifyChanged();
    });
    this.notifyChanged();
  }

  private async stopInternal(): Promise<void> {
    const server = this.server;
    this.server = null;
    this.listening = false;
    this.listeningPort = null;
    if (!server) {
      return;
    }
    server.closeAllConnections();
    await new Promise<void>((resolvePromise) => {
      server.close(() => resolvePromise());
    });
    this.notifyChanged();
  }

  private async connectInternal(): Promise<void> {
    await this.loadState();
    if (!this.storedState || this.listeningPort === null) {
      throw new Error("Claude receiver is not listening.");
    }
    if (this.storedState.installation) {
      await this.inspectExistingInstallation();
      return;
    }

    const document = await this.readSettingsDocument();
    const nextSettings: Record<string, unknown> = {
      ...document.value,
    };
    const conflicts: string[] = [];
    const envChanges: Record<string, StoredValueChange> = {};
    const envValue = document.value.env;
    const envContainerExisted = hasOwn(document.value, "env");
    const existingEnv = isRecord(envValue) ? envValue : {};

    let telemetryConflict = false;
    if (envContainerExisted && !isRecord(envValue)) {
      telemetryConflict = true;
      conflicts.push(
        "The existing Claude env setting is not an object, so telemetry was not changed.",
      );
    }
    for (const key of OTEL_TAKEOVER_KEYS) {
      if (hasOwn(existingEnv, key)) {
        telemetryConflict = true;
        conflicts.push(
          `Existing ${key} configuration was preserved; UsageApp did not take over the OTel destination.`,
        );
      }
    }
    if (hasOwn(document.value, "otelHeadersHelper")) {
      telemetryConflict = true;
      conflicts.push(
        "The existing otelHeadersHelper was preserved; UsageApp did not take over the OTel destination.",
      );
    }
    for (const key of SENSITIVE_CONTENT_KEYS) {
      if (hasOwn(existingEnv, key) && contentLoggingEnabled(existingEnv[key])) {
        telemetryConflict = true;
        conflicts.push(
          `${key} enables sensitive telemetry content, so UsageApp did not connect its receiver.`,
        );
      }
    }

    let telemetryInstalled = false;
    if (!telemetryConflict) {
      const nextEnv: Record<string, unknown> = {
        ...existingEnv,
      };
      const installedValues = environmentValues(
        this.listeningPort,
        this.storedState.pathToken,
      );
      for (const [key, installedValue] of Object.entries(installedValues)) {
        const existed = hasOwn(existingEnv, key);
        envChanges[key] = {
          existed,
          ...(existed ? { priorValue: existingEnv[key] } : {}),
          installedValue,
        };
        nextEnv[key] = installedValue;
      }
      nextSettings.env = nextEnv;
      telemetryInstalled = true;
    }

    let statusLineChange: StoredValueChange | null = null;
    let wrapperSha256: string | null = null;
    let priorCommandSha256: string | null = null;
    const priorStatusLine = document.value.statusLine;
    const statusLineExisted = hasOwn(document.value, "statusLine");
    if (statusLineExisted && !isRecord(priorStatusLine)) {
      conflicts.push(
        "The existing statusLine setting is not an object, so the quota bridge was not installed.",
      );
    } else {
      const priorObject = isRecord(priorStatusLine)
        ? priorStatusLine
        : {};
      const priorCommand =
        typeof priorObject.command === "string" &&
        priorObject.command.trim().length > 0
          ? priorObject.command
          : null;
      if (
        priorCommand !== null &&
        priorCommand
          .toLocaleLowerCase("en-US")
          .includes(this.wrapperPath.toLocaleLowerCase("en-US"))
      ) {
        conflicts.push(
          "The existing statusLine already references UsageApp's wrapper but has no active restoration journal, so it was left untouched.",
        );
      } else {
        let priorPath: string | null = null;
        if (priorCommand !== null) {
          const priorScript = priorCommandScript(priorCommand);
          await atomicWrite(this.priorCommandPath, priorScript);
          priorCommandSha256 = sha256(priorScript);
          priorPath = this.priorCommandPath;
        } else {
          await unlink(this.priorCommandPath).catch((error) => {
            if ((error as NodeJS.ErrnoException).code !== "ENOENT") {
              throw error;
            }
          });
        }

        const endpoint =
          `http://${LOOPBACK_HOST}:${this.listeningPort}` +
          `/v1/statusline/${this.storedState.pathToken}`;
        const wrapper = statusLineWrapper(endpoint, priorPath);
        await atomicWrite(this.wrapperPath, wrapper);
        wrapperSha256 = sha256(wrapper);

        const installedStatusLine: Record<string, unknown> = {
          ...priorObject,
          type: "command",
          command: statusLineCommand(this.wrapperPath),
          refreshInterval:
            typeof priorObject.refreshInterval === "number"
              ? priorObject.refreshInterval
              : 5,
        };
        statusLineChange = {
          existed: statusLineExisted,
          ...(statusLineExisted
            ? { priorValue: priorStatusLine }
            : {}),
          installedValue: installedStatusLine,
        };
        nextSettings.statusLine = installedStatusLine;
      }
    }

    const changed =
      telemetryInstalled || statusLineChange !== null;
    const backupPath = changed
      ? await this.createSettingsBackup(document)
      : null;
    const installation: StoredInstallation = {
      settingsPath: this.claudeSettingsPath,
      settingsFileExisted: document.existed,
      envContainerExisted,
      connectedAt: new Date().toISOString(),
      backupPath,
      telemetryInstalled,
      envChanges,
      statusLineChange,
      wrapperSha256,
      priorCommandSha256,
      conflicts,
    };

    // Persist the exact restoration journal before changing Claude settings.
    // If the final rename is interrupted, disconnect remains fail-safe because
    // it restores only values that exactly match the intended installation.
    this.storedState.installation = installation;
    await this.persistState();
    if (changed) {
      await atomicWriteJson(this.claudeSettingsPath, nextSettings);
    }
    this.applyStoredStatus();
    this.notifyChanged();
  }

  private async inspectExistingInstallation(): Promise<void> {
    const installation = this.storedState?.installation;
    if (!installation) {
      this.applyStoredStatus();
      return;
    }
    if (resolve(installation.settingsPath) !== this.claudeSettingsPath) {
      this.installationState = "conflict";
      this.statusLineConfigured = false;
      this.telemetryConfigured = false;
      this.clearDeliverySignals();
      this.message =
        "UsageApp's stored Claude installation points to a different settings file; no settings were changed.";
      this.notifyChanged();
      return;
    }

    const document = await this.readSettingsDocument();
    const currentEnv = isRecord(document.value.env)
      ? document.value.env
      : {};
    const drift: string[] = [];
    let statusLineMatches =
      installation.statusLineChange !== null &&
      hasOwn(document.value, "statusLine") &&
      isDeepStrictEqual(
        document.value.statusLine,
        installation.statusLineChange.installedValue,
      );
    if (
      statusLineMatches &&
      installation.statusLineChange &&
      isRecord(installation.statusLineChange.installedValue) &&
      typeof installation.statusLineChange.installedValue.refreshInterval !== "number"
    ) {
      const upgradedStatusLine = {
        ...installation.statusLineChange.installedValue,
        refreshInterval: 5,
      };
      installation.statusLineChange.installedValue = upgradedStatusLine;
      await this.persistState();
      await atomicWriteJson(this.claudeSettingsPath, {
        ...document.value,
        statusLine: upgradedStatusLine,
      });
      statusLineMatches = true;
    }
    if (installation.statusLineChange && !statusLineMatches) {
      drift.push("The Claude statusLine setting changed after connection.");
    }

    let telemetryMatches = installation.telemetryInstalled;
    for (const [key, change] of Object.entries(
      installation.envChanges,
    )) {
      if (
        !hasOwn(currentEnv, key) ||
        !isDeepStrictEqual(currentEnv[key], change.installedValue)
      ) {
        telemetryMatches = false;
        drift.push(`${key} changed after connection.`);
      }
    }

    this.statusLineConfigured = statusLineMatches;
    this.telemetryConfigured = telemetryMatches;
    this.clearDeliverySignals();
    const allConflicts = [...installation.conflicts, ...drift];
    if (
      statusLineMatches &&
      telemetryMatches &&
      allConflicts.length === 0
    ) {
      this.installationState = "awaiting-session";
      this.message =
        "Claude setup is ready. Close and start a new Claude Code session to send its first usage update.";
    } else if (statusLineMatches || telemetryMatches) {
      this.installationState = "partial";
      this.message = this.connectionMessage(
        "Claude monitoring is partially connected.",
        allConflicts,
      );
    } else {
      this.installationState = "conflict";
      this.message = this.connectionMessage(
        "Claude settings conflict with UsageApp.",
        allConflicts,
      );
    }
    this.notifyChanged();
  }

  private async disconnectInternal(): Promise<void> {
    await this.loadState();
    const installation = this.storedState?.installation;
    if (!this.storedState || !installation) {
      this.installationState = "disconnected";
      this.clearDeliverySignals();
      this.message = "Claude monitoring is not connected.";
      this.notifyChanged();
      return;
    }
    if (resolve(installation.settingsPath) !== this.claudeSettingsPath) {
      throw new Error(
        "Stored Claude integration state points to a different settings file; refusing to restore it.",
      );
    }

    const document = await this.readSettingsDocument();
    const nextSettings: Record<string, unknown> = {
      ...document.value,
    };
    const conflicts: string[] = [];
    let settingsChanged = false;
    let statusLineRestored = installation.statusLineChange === null;

    const statusChange = installation.statusLineChange;
    if (statusChange) {
      if (
        hasOwn(document.value, "statusLine") &&
        isDeepStrictEqual(
          document.value.statusLine,
          statusChange.installedValue,
        )
      ) {
        if (statusChange.existed) {
          nextSettings.statusLine = statusChange.priorValue;
        } else {
          delete nextSettings.statusLine;
        }
        settingsChanged = true;
        statusLineRestored = true;
      } else {
        conflicts.push(
          "The statusLine setting changed after connection, so UsageApp left it untouched.",
        );
      }
    }

    const envEntries = Object.entries(installation.envChanges);
    if (envEntries.length > 0) {
      if (!isRecord(document.value.env)) {
        for (const [key] of envEntries) {
          conflicts.push(
            `${key} changed after connection, so UsageApp left it untouched.`,
          );
        }
      } else {
        const nextEnv: Record<string, unknown> = {
          ...document.value.env,
        };
        for (const [key, change] of envEntries) {
          if (
            hasOwn(document.value.env, key) &&
            isDeepStrictEqual(
              document.value.env[key],
              change.installedValue,
            )
          ) {
            if (change.existed) {
              nextEnv[key] = change.priorValue;
            } else {
              delete nextEnv[key];
            }
            settingsChanged = true;
          } else {
            conflicts.push(
              `${key} changed after connection, so UsageApp left it untouched.`,
            );
          }
        }
        if (
          !installation.envContainerExisted &&
          Object.keys(nextEnv).length === 0
        ) {
          delete nextSettings.env;
        } else {
          nextSettings.env = nextEnv;
        }
      }
    }

    if (settingsChanged) {
      if (
        !installation.settingsFileExisted &&
        Object.keys(nextSettings).length === 0
      ) {
        await unlink(this.claudeSettingsPath).catch((error) => {
          if ((error as NodeJS.ErrnoException).code !== "ENOENT") {
            throw error;
          }
        });
      } else {
        await atomicWriteJson(this.claudeSettingsPath, nextSettings);
      }
    }

    if (statusLineRestored) {
      if (
        !(await this.removeOwnedFileIfUnchanged(
          this.wrapperPath,
          installation.wrapperSha256,
        ))
      ) {
        conflicts.push(
          "The generated status-line wrapper was modified, so UsageApp left it in place.",
        );
      }
      if (
        !(await this.removeOwnedFileIfUnchanged(
          this.priorCommandPath,
          installation.priorCommandSha256,
        ))
      ) {
        conflicts.push(
          "The preserved status-line command file was modified, so UsageApp left it in place.",
        );
      }
    }

    this.storedState.installation = null;
    await this.persistState();
    this.clearDeliverySignals();
    this.statusLineConfigured = false;
    this.telemetryConfigured = false;
    if (conflicts.length > 0) {
      this.installationState = "conflict";
      this.message = this.connectionMessage(
        "Claude monitoring was disconnected with user changes preserved.",
        conflicts,
      );
    } else {
      this.installationState = "disconnected";
      this.message = "Claude monitoring is not connected.";
    }
    this.notifyChanged();
  }

  private async readSettingsDocument(): Promise<SettingsDocument> {
    try {
      const raw = await readFile(this.claudeSettingsPath, "utf8");
      const parsed = JSON.parse(raw) as unknown;
      if (!isRecord(parsed)) {
        throw new Error(
          "Claude settings must contain a JSON object; settings were not changed.",
        );
      }
      return { existed: true, raw, value: parsed };
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code === "ENOENT") {
        return { existed: false, raw: null, value: {} };
      }
      if (error instanceof SyntaxError) {
        throw new Error(
          "Claude settings are not valid JSON; settings were not changed.",
        );
      }
      throw error;
    }
  }

  private async createSettingsBackup(
    document: SettingsDocument,
  ): Promise<string | null> {
    if (!document.existed || document.raw === null) {
      return null;
    }
    const backupPath =
      `${this.claudeSettingsPath}.usageapp-` +
      `${timestampForFileName()}-${randomBytes(4).toString("hex")}.bak`;
    await writeFile(backupPath, document.raw, {
      encoding: "utf8",
      flag: "wx",
      mode: 0o600,
    });
    return backupPath;
  }

  private async removeOwnedFileIfUnchanged(
    path: string,
    expectedSha256: string | null,
  ): Promise<boolean> {
    if (expectedSha256 === null) {
      return true;
    }
    if (
      !isPathInside(this.wrapperDirectory, path) ||
      !isPathInside(this.userDataPath, path)
    ) {
      return false;
    }
    try {
      const contents = await readFile(path);
      if (sha256(contents) !== expectedSha256) {
        return false;
      }
      await unlink(path);
      return true;
    } catch (error) {
      return (error as NodeJS.ErrnoException).code === "ENOENT";
    }
  }

  private async handleRequest(
    request: IncomingMessage,
    response: ServerResponse,
  ): Promise<void> {
    const remoteAddress = request.socket.remoteAddress;
    if (
      remoteAddress !== LOOPBACK_HOST &&
      remoteAddress !== `::ffff:${LOOPBACK_HOST}`
    ) {
      jsonResponse(response, 403, { error: "Loopback requests only." });
      return;
    }
    if (!this.storedState || this.listeningPort === null) {
      jsonResponse(response, 503, {
        error: "Claude receiver is not ready.",
      });
      return;
    }

    const url = new URL(
      request.url ?? "/",
      `http://${LOOPBACK_HOST}:${this.listeningPort}`,
    );
    const statusLinePath =
      `/v1/statusline/${this.storedState.pathToken}`;
    const logsPath = `/v1/logs/${this.storedState.pathToken}`;
    const isStatusLine = url.pathname === statusLinePath;
    const isLogs = url.pathname === logsPath;
    if (!isStatusLine && !isLogs) {
      jsonResponse(response, 404, { error: "Not found." });
      return;
    }
    if (request.method !== "POST") {
      response.setHeader("Allow", "POST");
      jsonResponse(response, 405, { error: "Method not allowed." });
      return;
    }
    if (!isJsonContentType(request.headers["content-type"])) {
      jsonResponse(response, 415, {
        error: "Content-Type must be application/json.",
      });
      return;
    }

    let raw: unknown;
    try {
      raw = await readJsonRequest(request);
    } catch (error) {
      if (error instanceof BodyTooLargeError) {
        jsonResponse(response, 413, {
          error: "Request body is too large.",
        });
      } else if (
        error instanceof TypeError &&
        error.message === "Unsupported content encoding."
      ) {
        jsonResponse(response, 415, {
          error: "Content-Encoding must be identity or gzip.",
        });
      } else {
        jsonResponse(response, 400, {
          error: "A valid JSON request body is required.",
        });
      }
      return;
    }

    if (isStatusLine) {
      await this.onStatusLine(raw);
    } else {
      await this.onOtlpLogs(raw);
    }
    jsonResponse(response, 200, {});
    this.notifyChanged();
  }
}
