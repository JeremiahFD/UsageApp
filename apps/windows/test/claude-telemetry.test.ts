import {
  appendFile,
  mkdtemp,
  readFile,
  rm,
} from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";

import { afterEach, describe, expect, it } from "vitest";

import {
  ClaudeActivityStore,
  aggregateClaudeActivity,
  createEmptyClaudeAnalytics,
  normalizeClaudeStatusLine,
  parseClaudeOtlpLogs,
  type ClaudeApiRequestEvent,
} from "../src/main/claude-telemetry";

const temporaryDirectories: string[] = [];

function attribute(key: string, value: unknown) {
  const encoded =
    typeof value === "number"
      ? Number.isInteger(value)
        ? { intValue: String(value) }
        : { doubleValue: value }
      : { stringValue: String(value) };
  return { key, value: encoded };
}

function apiRequestRecord(
  values: {
    timestamp?: string;
    sequence?: number;
    sessionId?: string;
    requestId?: string;
    clientRequestId?: string;
    model?: string;
    effort?: string;
    inputTokens?: number;
    outputTokens?: number;
    cacheReadTokens?: number;
    cacheCreationTokens?: number;
    costUsd?: number;
  } = {},
) {
  const attributes = [
    attribute("event.name", "api_request"),
    attribute(
      "event.timestamp",
      values.timestamp ?? "2026-07-26T15:00:00.000Z",
    ),
    attribute("event.sequence", values.sequence ?? 9),
    attribute("session.id", values.sessionId ?? "session-private-123"),
    attribute("model", values.model ?? "claude-sonnet-4-6"),
    attribute("effort", values.effort ?? "high"),
    attribute("input_tokens", values.inputTokens ?? 100),
    attribute("output_tokens", values.outputTokens ?? 20),
    attribute("cache_read_tokens", values.cacheReadTokens ?? 50),
    attribute(
      "cache_creation_tokens",
      values.cacheCreationTokens ?? 10,
    ),
    attribute("cost_usd", values.costUsd ?? 0.25),
    attribute("user.email", "private@example.com"),
    attribute("organization.id", "org_private"),
    attribute("prompt", "secret prompt contents"),
    attribute("response", "secret response contents"),
  ];
  if (values.requestId !== undefined) {
    attributes.push(attribute("request_id", values.requestId));
  } else {
    attributes.push(
      attribute(
        "client_request_id",
        values.clientRequestId ?? "client-request-123",
      ),
    );
  }
  return {
    body: { stringValue: "claude_code.api_request" },
    attributes,
  };
}

function otlpPayload(records: unknown[]) {
  return {
    resourceLogs: [
      {
        resource: {
          attributes: [
            attribute("service.name", "claude-code"),
            attribute("session.id", "resource-session-private"),
            attribute("user.account_uuid", "account-private"),
            attribute("user.email", "resource-private@example.com"),
          ],
        },
        scopeLogs: [
          {
            scope: {
              name: "com.anthropic.claude_code",
              attributes: [attribute("organization.id", "scope-org-private")],
            },
            logRecords: records,
          },
        ],
      },
    ],
  };
}

afterEach(async () => {
  await Promise.all(
    temporaryDirectories.splice(0).map((directory) =>
      rm(directory, { recursive: true, force: true }),
    ),
  );
});

describe("normalizeClaudeStatusLine", () => {
  it("normalizes documented limits and current live details without session metadata", () => {
    const observedAt = "2026-07-26T12:00:00.000Z";
    const result = normalizeClaudeStatusLine(
      {
        session_id: "must-not-survive",
        transcript_path: "C:\\private\\transcript.jsonl",
        cwd: "C:\\private",
        model: {
          id: "claude-opus-4-7",
          display_name: "Opus",
        },
        effort: { level: "high" },
        thinking: { enabled: true },
        cost: { total_cost_usd: 1.75 },
        context_window: {
          total_input_tokens: 99_999,
          total_output_tokens: 88_888,
          current_usage: {
            input_tokens: 8_500,
            output_tokens: 1_200,
            cache_creation_input_tokens: 5_000,
            cache_read_input_tokens: 2_000,
          },
        },
        rate_limits: {
          five_hour: {
            used_percentage: 23.5,
            resets_at: Date.parse("2026-07-26T15:00:00.000Z") / 1_000,
          },
          seven_day: {
            used_percentage: 41.2,
            resets_at: Date.parse("2026-07-30T12:00:00.000Z") / 1_000,
          },
        },
      },
      {
        observedAt,
        now: "2026-07-26T12:05:00.000Z",
      },
    );

    expect(result.snapshot).toMatchObject({
      providerId: "anthropic-claude",
      providerName: "Claude",
      observedAt,
      status: "live",
      tokenUsage: null,
      bankedResets: {
        availableCount: null,
        detailsAvailable: false,
        items: [],
      },
    });
    expect(result.snapshot.windows).toEqual([
      expect.objectContaining({
        id: "claude:five-hour",
        label: "5-hour",
        usedPercent: 23.5,
        remainingPercent: 76.5,
        durationMinutes: 300,
        resetsAt: "2026-07-26T15:00:00.000Z",
      }),
      expect.objectContaining({
        id: "claude:seven-day",
        label: "Weekly",
        usedPercent: 41.2,
        remainingPercent: 58.8,
        durationMinutes: 10_080,
        resetsAt: "2026-07-30T12:00:00.000Z",
      }),
    ]);
    expect(result.liveDetails).toEqual({
      model: "claude-opus-4-7",
      reasoningLevel: "high",
      thinkingEnabled: true,
      inputTokens: 8_500,
      outputTokens: 1_200,
      cacheReadTokens: 2_000,
      cacheWriteTokens: 5_000,
      estimatedSessionCostUsd: 1.75,
    });
    expect(JSON.stringify(result)).not.toContain("must-not-survive");
    expect(JSON.stringify(result)).not.toContain("transcript");
    expect(JSON.stringify(result)).not.toContain("C:\\\\private");
  });

  it("marks old or expired quota observations stale", () => {
    const raw = {
      rate_limits: {
        five_hour: {
          used_percentage: 20,
          resets_at: Date.parse("2026-07-26T12:10:00.000Z") / 1_000,
        },
      },
    };
    const old = normalizeClaudeStatusLine(raw, {
      observedAt: "2026-07-26T11:44:59.000Z",
      now: "2026-07-26T12:00:00.000Z",
    });
    expect(old.snapshot.status).toBe("stale");

    const expired = normalizeClaudeStatusLine(raw, {
      observedAt: "2026-07-26T12:00:00.000Z",
      now: "2026-07-26T12:10:00.000Z",
    });
    expect(expired.snapshot.status).toBe("stale");
    expect(expired.snapshot.message).toContain("reset data has expired");
  });

  it("does not clamp or invent malformed values", () => {
    const result = normalizeClaudeStatusLine(
      {
        model: { id: "contains a prompt-like value" },
        effort: { level: "superhuman" },
        thinking: { enabled: "yes" },
        cost: { total_cost_usd: -1 },
        context_window: {
          total_input_tokens: 123,
          total_output_tokens: 456,
          current_usage: {
            input_tokens: -1,
            output_tokens: 1.5,
            cache_read_input_tokens: "200",
          },
        },
        rate_limits: {
          five_hour: { used_percentage: 120, resets_at: "soon" },
          seven_day: { used_percentage: -1, resets_at: 0 },
        },
      },
      {
        observedAt: "2026-07-26T12:00:00.000Z",
        now: "2026-07-26T12:00:00.000Z",
      },
    );

    expect(result.snapshot.status).toBe("unavailable");
    expect(result.snapshot.windows).toEqual([]);
    expect(result.liveDetails).toEqual({
      model: null,
      reasoningLevel: null,
      thinkingEnabled: null,
      inputTokens: null,
      outputTokens: null,
      cacheReadTokens: null,
      cacheWriteTokens: null,
      estimatedSessionCostUsd: null,
    });
  });
});

describe("parseClaudeOtlpLogs", () => {
  it("whitelists API request usage and strips PII, content, and raw payloads", () => {
    const payload = otlpPayload([
      apiRequestRecord({ requestId: "req_011ABC" }),
      {
        body: { stringValue: "claude_code.user_prompt" },
        attributes: [
          attribute("event.name", "user_prompt"),
          attribute("event.timestamp", "2026-07-26T15:01:00.000Z"),
          attribute("prompt", "another private prompt"),
          attribute("model", "claude-opus-4-7"),
        ],
      },
      {
        body: { stringValue: "claude_code.api_request" },
        attributes: [
          attribute("event.name", "api_request"),
          attribute("event.timestamp", "not-a-date"),
          attribute("prompt", "malformed private prompt"),
        ],
      },
    ]);

    const events = parseClaudeOtlpLogs(JSON.stringify(payload));
    expect(events).toHaveLength(1);
    expect(events[0]).toEqual({
      eventId: expect.stringMatching(/^[a-f0-9]{64}$/),
      timestamp: "2026-07-26T15:00:00.000Z",
      sequence: 9,
      requestId: "req_011ABC",
      model: "claude-sonnet-4-6",
      effort: "high",
      inputTokens: 100,
      outputTokens: 20,
      cacheReadTokens: 50,
      cacheCreationTokens: 10,
      costUsd: 0.25,
    });

    const serialized = JSON.stringify(events);
    for (const sensitive of [
      "private@example.com",
      "resource-private@example.com",
      "org_private",
      "account-private",
      "secret prompt",
      "secret response",
      "resource-session-private",
      "session-private-123",
    ]) {
      expect(serialized).not.toContain(sensitive);
    }
  });

  it("uses the session only inside the deterministic event hash", () => {
    const first = parseClaudeOtlpLogs(
      otlpPayload([
        apiRequestRecord({
          sessionId: "session-one",
          requestId: "req_same",
        }),
      ]),
    )[0];
    const duplicate = parseClaudeOtlpLogs(
      otlpPayload([
        apiRequestRecord({
          sessionId: "session-one",
          requestId: "req_same",
        }),
      ]),
    )[0];
    const otherSession = parseClaudeOtlpLogs(
      otlpPayload([
        apiRequestRecord({
          sessionId: "session-two",
          requestId: "req_same",
        }),
      ]),
    )[0];

    expect(first?.eventId).toBe(duplicate?.eventId);
    expect(first?.eventId).not.toBe(otherSession?.eventId);
    expect(JSON.stringify(first)).not.toContain("session-one");
  });

  it("rejects malformed JSON and non-Claude records", () => {
    expect(parseClaudeOtlpLogs("{ definitely not json")).toEqual([]);
    expect(
      parseClaudeOtlpLogs({
        resourceLogs: [
          {
            resource: {
              attributes: [attribute("service.name", "other-product")],
            },
            scopeLogs: [
              {
                logRecords: [
                  {
                    attributes: [
                      attribute("event.name", "api_request"),
                      attribute(
                        "event.timestamp",
                        "2026-07-26T15:00:00.000Z",
                      ),
                      attribute("input_tokens", 100),
                    ],
                  },
                ],
              },
            ],
          },
        ],
      }),
    ).toEqual([]);
  });
});

describe("Claude activity analytics and persistence", () => {
  it("builds an empty capability-aware state", () => {
    const analytics = createEmptyClaudeAnalytics(
      "2026-07-26T12:00:00.000Z",
    );
    expect(analytics).toMatchObject({
      source: "claude-otel",
      observedAt: "2026-07-26T12:00:00.000Z",
      recordingSince: null,
      buckets: [],
      capabilities: {
        dailyTotals: true,
        tokenCategories: true,
        modelFilter: true,
        reasoningFilter: true,
        estimatedCost: true,
      },
    });
  });

  it("aggregates by local calendar day, model, and effort", () => {
    const localMorning = new Date(2026, 6, 26, 9, 0, 0).toISOString();
    const localEvening = new Date(2026, 6, 26, 18, 0, 0).toISOString();
    const nextLocalDay = new Date(2026, 6, 27, 9, 0, 0).toISOString();
    const base: Omit<ClaudeApiRequestEvent, "eventId" | "timestamp"> = {
      sequence: 1,
      requestId: null,
      model: "claude-sonnet-4-6",
      effort: "high",
      inputTokens: 10,
      outputTokens: 5,
      cacheReadTokens: 20,
      cacheCreationTokens: 2,
      costUsd: 0.1,
    };
    const events: ClaudeApiRequestEvent[] = [
      { ...base, eventId: "a".repeat(64), timestamp: localMorning },
      {
        ...base,
        eventId: "b".repeat(64),
        timestamp: localEvening,
        sequence: 2,
        inputTokens: 30,
        cacheCreationTokens: null,
        costUsd: 0.2,
      },
      {
        ...base,
        eventId: "c".repeat(64),
        timestamp: nextLocalDay,
        sequence: 3,
        model: "claude-opus-4-7",
        effort: "xhigh",
      },
    ];

    const analytics = aggregateClaudeActivity(
      events,
      "2026-07-28T12:00:00.000Z",
    );
    expect(analytics.buckets).toHaveLength(2);
    expect(analytics.buckets[0]).toMatchObject({
      date: "2026-07-26",
      model: "claude-sonnet-4-6",
      reasoningLevel: "high",
      inputTokens: 40,
      outputTokens: 10,
      cacheReadTokens: 40,
      cacheWriteTokens: 2,
      totalTokens: 92,
      requestCount: 2,
    });
    expect(analytics.buckets[0]?.estimatedCostUsd).toBeCloseTo(0.3);
    expect(analytics.buckets[1]).toMatchObject({
      date: "2026-07-27",
      model: "claude-opus-4-7",
      reasoningLevel: "xhigh",
      requestCount: 1,
    });
    expect(analytics.recordingSince).toBe(localMorning);
  });

  it("appends only sanitized deduplicated NDJSON and tolerates corrupt startup lines", async () => {
    const directory = await mkdtemp(join(tmpdir(), "usageapp-claude-test-"));
    temporaryDirectories.push(directory);
    const path = join(directory, "activity.ndjson");
    const payload = otlpPayload([
      apiRequestRecord({
        sessionId: "private-session",
        requestId: "req_persist",
      }),
    ]);
    const [event] = parseClaudeOtlpLogs(payload);
    expect(event).toBeDefined();

    const store = new ClaudeActivityStore(path);
    expect(await store.load()).toEqual({ loaded: 0, ignored: 0 });
    expect(await store.append([event!, event!])).toBe(1);
    expect(await store.append([event!])).toBe(0);

    const written = await readFile(path, "utf8");
    expect(written.trim().split(/\r?\n/)).toHaveLength(1);
    expect(written).not.toContain("private-session");
    expect(written).not.toContain("private@example.com");
    expect(written).not.toContain("secret prompt");
    expect(written).not.toContain("rawPayload");

    await appendFile(
      path,
      [
        "{broken json",
        JSON.stringify({
          schemaVersion: 1,
          ...event,
          eventId: "d".repeat(64),
          sessionId: "must-be-rejected",
          prompt: "must-be-rejected",
        }),
        "",
      ].join("\n"),
      "utf8",
    );

    const restarted = new ClaudeActivityStore(path);
    expect(await restarted.load()).toEqual({ loaded: 1, ignored: 2 });
    expect(restarted.size).toBe(1);
    expect(await restarted.append([event!])).toBe(0);
    expect(restarted.analytics().buckets[0]).toMatchObject({
      model: "claude-sonnet-4-6",
      reasoningLevel: "high",
      requestCount: 1,
      totalTokens: 180,
    });
  });
});
