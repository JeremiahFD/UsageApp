import { mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";

import { afterEach, describe, expect, it } from "vitest";

import {
  ClaudePlanUsageWatcher,
  parseClaudePlanUsage,
  snapshotFromClaudePlanUsage,
  type ClaudePlanUsageSample,
} from "../src/main/claude-plan-usage";
import {
  ClaudeSnapshotStore,
  parseStoredClaudeSnapshot,
} from "../src/main/claude-snapshot-store";

const temporaryDirectories: string[] = [];

async function temporaryDirectory(): Promise<string> {
  const directory = await mkdtemp(join(tmpdir(), "usageapp-plan-"));
  temporaryDirectories.push(directory);
  return directory;
}

function history(samples: unknown[]): string {
  return JSON.stringify({ version: 2, samples });
}

afterEach(async () => {
  await Promise.all(
    temporaryDirectories.splice(0).map((directory) =>
      rm(directory, { recursive: true, force: true }),
    ),
  );
});

describe("parseClaudePlanUsage", () => {
  const now = new Date("2026-07-28T16:30:00.000Z");

  it("returns the newest usable sample regardless of file order", () => {
    const sample = parseClaudePlanUsage(
      {
        version: 2,
        samples: [
          { t: Date.parse("2026-07-28T16:24:00.000Z"), u: { fh: 14, sd: 20 } },
          { t: Date.parse("2026-07-28T15:59:00.000Z"), u: { fh: 10, sd: 20 } },
        ],
      },
      now,
    );

    expect(sample).toEqual({
      observedAt: "2026-07-28T16:24:00.000Z",
      fiveHourPercent: 14,
      sevenDayPercent: 20,
    });
  });

  it("drops an organization identifier rather than carrying it forward", () => {
    const sample = parseClaudePlanUsage(
      {
        version: 2,
        samples: [
          {
            t: Date.parse("2026-07-28T16:24:00.000Z"),
            org: "74618269-5ec0-4c2f-aa64-085cc9eebdd6",
            u: { fh: 14, sd: 20 },
          },
        ],
      },
      now,
    );

    expect(sample).not.toBeNull();
    expect(JSON.stringify(sample)).not.toContain("74618269");
  });

  it("ignores malformed, out-of-range, and far-future entries", () => {
    expect(parseClaudePlanUsage({ samples: "nope" }, now)).toBeNull();
    expect(parseClaudePlanUsage({ version: 2, samples: [] }, now)).toBeNull();
    expect(
      parseClaudePlanUsage(
        { version: 2, samples: [{ t: 1, u: { fh: 140, sd: -3 } }] },
        now,
      ),
    ).toBeNull();
    expect(
      parseClaudePlanUsage(
        {
          version: 2,
          samples: [{ t: now.getTime() + 3_600_000, u: { fh: 5, sd: 5 } }],
        },
        now,
      ),
    ).toBeNull();
  });

  it("accepts a partial reading when only one window is reported", () => {
    const sample = parseClaudePlanUsage(
      {
        version: 2,
        samples: [{ t: Date.parse("2026-07-28T16:24:00.000Z"), u: { fh: 14 } }],
      },
      now,
    );

    expect(sample?.fiveHourPercent).toBe(14);
    expect(sample?.sevenDayPercent).toBeNull();
  });

  it("survives a future schema revision it does not recognize", () => {
    expect(
      parseClaudePlanUsage({ version: 9, samples: [{ t: 1, usage: {} }] }, now),
    ).toBeNull();
  });
});

describe("snapshotFromClaudePlanUsage", () => {
  const sample: ClaudePlanUsageSample = {
    observedAt: "2026-07-28T16:24:00.000Z",
    fiveHourPercent: 14,
    sevenDayPercent: 20,
  };

  it("reports a recent reading as live with no reset times", () => {
    const snapshot = snapshotFromClaudePlanUsage(
      sample,
      new Date("2026-07-28T16:26:00.000Z"),
    );

    expect(snapshot.status).toBe("live");
    expect(snapshot.windows.map((window) => window.id)).toEqual([
      "claude:five-hour",
      "claude:seven-day",
    ]);
    expect(snapshot.windows[0]?.usedPercent).toBe(14);
    expect(snapshot.windows[0]?.remainingPercent).toBe(86);
    expect(snapshot.windows.every((window) => window.resetsAt === null)).toBe(
      true,
    );
  });

  it("goes stale once the desktop app stops recording", () => {
    const snapshot = snapshotFromClaudePlanUsage(
      sample,
      new Date("2026-07-28T17:30:00.000Z"),
    );

    expect(snapshot.status).toBe("stale");
    expect(snapshot.message).toContain("last plan usage");
  });
});

describe("ClaudePlanUsageWatcher", () => {
  it("re-reads only when the file changes and reports each new sample", async () => {
    const directory = await temporaryDirectory();
    const filePath = join(directory, "plan-usage-history.json");
    const observed: Array<ClaudePlanUsageSample | null> = [];
    const watcher = new ClaudePlanUsageWatcher({
      filePath,
      onSample: (sample) => observed.push(sample),
    });

    // Missing file: the desktop app may simply not be installed.
    expect(await watcher.read()).toBeNull();
    expect(observed).toHaveLength(0);

    await writeFile(
      filePath,
      history([{ t: Date.now() - 60_000, u: { fh: 10, sd: 20 } }]),
      "utf8",
    );
    expect((await watcher.read())?.fiveHourPercent).toBe(10);
    expect(observed).toHaveLength(1);

    // An unchanged file must not re-notify.
    await watcher.read();
    expect(observed).toHaveLength(1);

    await writeFile(
      filePath,
      history([{ t: Date.now(), u: { fh: 14, sd: 20 } }]),
      "utf8",
    );
    expect((await watcher.read(true))?.fiveHourPercent).toBe(14);
    expect(observed).toHaveLength(2);
  });

  it("keeps the last sample when the file is mid-write", async () => {
    const directory = await temporaryDirectory();
    const filePath = join(directory, "plan-usage-history.json");
    const watcher = new ClaudePlanUsageWatcher({
      filePath,
      onSample: () => undefined,
    });

    await writeFile(
      filePath,
      history([{ t: Date.now(), u: { fh: 42, sd: 7 } }]),
      "utf8",
    );
    await watcher.read();

    await writeFile(filePath, '{"version":2,"samp', "utf8");
    expect((await watcher.read(true))?.fiveHourPercent).toBe(42);
  });
});

describe("ClaudeSnapshotStore", () => {
  it("restores a saved reading and lets the caller re-judge its freshness", async () => {
    const directory = await temporaryDirectory();
    const store = new ClaudeSnapshotStore(join(directory, "snapshot.json"));

    expect(await store.load()).toBeNull();

    await store.save({
      snapshot: {
        schemaVersion: 1,
        providerId: "anthropic-claude",
        providerName: "Claude",
        observedAt: "2026-07-28T16:24:00.000Z",
        status: "live",
        windows: [
          {
            id: "claude:five-hour",
            limitId: "claude",
            limitName: null,
            kind: "primary",
            label: "5-hour",
            usedPercent: 14,
            remainingPercent: 86,
            durationMinutes: 300,
            resetsAt: "2026-07-28T20:00:00.000Z",
          },
        ],
        bankedResets: {
          availableCount: null,
          detailsAvailable: false,
          items: [],
        },
        credits: null,
        planType: null,
        tokenUsage: null,
        message: null,
      },
      liveDetails: {
        model: "claude-opus-5",
        reasoningLevel: "medium",
        thinkingEnabled: true,
        inputTokens: 2,
        outputTokens: 565,
        cacheReadTokens: 40_145,
        cacheWriteTokens: 24_527,
        estimatedSessionCostUsd: 0.279,
      },
    });

    const restored = await store.load();
    expect(restored?.snapshot.windows[0]?.usedPercent).toBe(14);
    expect(restored?.snapshot.windows[0]?.resetsAt).toBe(
      "2026-07-28T20:00:00.000Z",
    );
    expect(restored?.liveDetails?.model).toBe("claude-opus-5");
  });

  it("rejects a cache that does not match the contract it wrote", () => {
    expect(parseStoredClaudeSnapshot(null)).toBeNull();
    expect(parseStoredClaudeSnapshot({ version: 2, snapshot: {} })).toBeNull();
    expect(
      parseStoredClaudeSnapshot({
        version: 1,
        snapshot: {
          schemaVersion: 1,
          providerId: "openai-codex",
          observedAt: "2026-07-28T16:24:00.000Z",
          windows: [],
        },
      }),
    ).toBeNull();
    expect(
      parseStoredClaudeSnapshot({
        version: 1,
        snapshot: {
          schemaVersion: 1,
          providerId: "anthropic-claude",
          observedAt: "2026-07-28T16:24:00.000Z",
          windows: [{ id: "claude:five-hour", usedPercent: 400 }],
        },
      }),
    ).toBeNull();
  });
});
