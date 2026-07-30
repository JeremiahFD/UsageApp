import { describe, expect, it } from "vitest";

import {
  decodeUsageSnapshot,
  describeCredits,
  formatRelativeTime,
  formatWindowDuration,
  getMostConstrainedRemaining,
  normalizeCodexSnapshot,
  summarizeForTray,
} from "../src";

const RATE_LIMITS_FIXTURE = {
  rateLimits: {
    limitId: "codex",
    primary: {
      usedPercent: 25,
      windowDurationMins: 300,
      resetsAt: 1_900_000_000,
    },
    secondary: {
      usedPercent: 60,
      windowDurationMins: 10_080,
      resetsAt: 1_900_400_000,
    },
    credits: { hasCredits: true, unlimited: false, balance: "12.50" },
    planType: "plus",
  },
  rateLimitsByLimitId: {
    codex: {
      limitId: "codex",
      primary: {
        usedPercent: 25,
        windowDurationMins: 300,
        resetsAt: 1_900_000_000,
      },
      secondary: {
        usedPercent: 60,
        windowDurationMins: 10_080,
        resetsAt: 1_900_400_000,
      },
      credits: { hasCredits: true, unlimited: false, balance: "12.50" },
      planType: "plus",
    },
    spark: {
      limitId: "spark",
      limitName: "Fast model",
      primary: {
        usedPercent: 10,
        windowDurationMins: 10_080,
        resetsAt: 1_900_500_000,
      },
    },
  },
  rateLimitResetCredits: {
    availableCount: 2,
    credits: [
      {
        id: "later",
        status: "available",
        grantedAt: 1_800_000_000,
        expiresAt: 1_900_900_000,
        title: "Full reset",
        description: "Second",
      },
      {
        id: "sooner",
        status: "available",
        grantedAt: 1_800_000_000,
        expiresAt: 1_900_800_000,
        title: "Full reset",
        description: "First",
      },
    ],
  },
};

describe("normalizeCodexSnapshot", () => {
  it("normalizes all returned windows and computes remaining percentages", () => {
    const snapshot = normalizeCodexSnapshot(
      RATE_LIMITS_FIXTURE,
      {
        summary: {
          lifetimeTokens: 123_456,
          peakDailyTokens: 10_000,
          longestRunningTurnSec: 60,
          currentStreakDays: 3,
          longestStreakDays: 4,
        },
        dailyUsageBuckets: [{ startDate: "2026-07-25", tokens: 900 }],
      },
      new Date("2026-07-26T12:00:00.000Z"),
    );

    expect(snapshot.status).toBe("live");
    expect(snapshot.windows).toHaveLength(3);
    expect(snapshot.windows[0]).toMatchObject({
      label: "5-hour",
      usedPercent: 25,
      remainingPercent: 75,
    });
    expect(snapshot.windows[1]).toMatchObject({
      label: "Weekly",
      remainingPercent: 40,
    });
    expect(snapshot.windows[2]?.label).toBe("Fast model · Weekly");
    expect(snapshot.bankedResets.availableCount).toBe(2);
    expect(snapshot.bankedResets.items.map((item) => item.id)).toEqual([
      "sooner",
      "later",
    ]);
    expect(snapshot.tokenUsage?.lifetimeTokens).toBe(123_456);
    expect(getMostConstrainedRemaining(snapshot)).toBe(40);
    expect(summarizeForTray(snapshot).tooltip).toContain("40% left");
    expect(summarizeForTray(snapshot).tooltip).toContain("Last known usage");
  });

  it("preserves an authoritative count when reset details are unavailable", () => {
    const snapshot = normalizeCodexSnapshot({
      ...RATE_LIMITS_FIXTURE,
      rateLimitResetCredits: { availableCount: 4, credits: null },
    }, null);

    expect(snapshot.bankedResets).toEqual({
      availableCount: 4,
      detailsAvailable: false,
      items: [],
    });
  });

  it("does not infer an authoritative count from a possibly capped detail list", () => {
    const snapshot = normalizeCodexSnapshot({
      ...RATE_LIMITS_FIXTURE,
      rateLimitResetCredits: {
        credits: RATE_LIMITS_FIXTURE.rateLimitResetCredits.credits,
      },
    }, null);

    expect(snapshot.bankedResets.availableCount).toBeNull();
    expect(snapshot.bankedResets.items).toHaveLength(2);
  });

  it("sanitizes malformed nonnegative and date-constrained activity fields", () => {
    const snapshot = normalizeCodexSnapshot(
      {
        ...RATE_LIMITS_FIXTURE,
        rateLimitsByLimitId: {
          codex: {
            limitId: "codex",
            primary: {
              usedPercent: 10,
              windowDurationMins: -300,
              resetsAt: 1_900_000_000,
            },
          },
        },
      },
      {
        summary: {
          lifetimeTokens: -1,
          peakDailyTokens: 10.8,
          longestRunningTurnSec: null,
          currentStreakDays: null,
          longestStreakDays: null,
        },
        dailyUsageBuckets: [
          { startDate: "not-a-date", tokens: 10 },
          { startDate: "2026-07-26", tokens: -4 },
        ],
      },
    );

    expect(snapshot.windows[0]?.durationMinutes).toBeNull();
    expect(snapshot.tokenUsage?.lifetimeTokens).toBeNull();
    expect(snapshot.tokenUsage?.peakDailyTokens).toBe(10);
    expect(snapshot.tokenUsage?.dailyUsageBuckets).toEqual([]);
  });

  it("rejects malformed snapshots at the phone/cache trust boundary", () => {
    const valid = normalizeCodexSnapshot(RATE_LIMITS_FIXTURE, null);
    expect(decodeUsageSnapshot(valid)).toEqual(valid);
    expect(
      decodeUsageSnapshot({
        ...valid,
        windows: [null],
        bankedResets: {},
      }),
    ).toBeNull();
  });
});

describe("credits", () => {
  it("reads the spending signals that sit beside the credits object", () => {
    const snapshot = normalizeCodexSnapshot({
      rateLimits: {
        limitId: "codex",
        primary: { usedPercent: 25, windowDurationMins: 300, resetsAt: 1_900_000_000 },
        credits: { hasCredits: false, unlimited: false, balance: "0" },
        spendControlReached: true,
        rateLimitReachedType: "weekly",
        planType: "prolite",
      },
    }, null);

    expect(snapshot.credits).toEqual({
      hasCredits: false,
      unlimited: false,
      balance: "0",
      spendControlReached: true,
      rateLimitReachedType: "weekly",
    });
  });

  it("reports absent spending signals as unknown rather than false", () => {
    const snapshot = normalizeCodexSnapshot({
      rateLimits: {
        limitId: "codex",
        primary: { usedPercent: 25, windowDurationMins: 300, resetsAt: 1_900_000_000 },
        credits: { hasCredits: false, unlimited: false, balance: "0" },
      },
    }, null);

    expect(snapshot.credits?.spendControlReached).toBeNull();
    expect(snapshot.credits?.rateLimitReachedType).toBeNull();
  });

  it("describes a zero balance quietly and a spending block loudly", () => {
    expect(describeCredits(null)).toMatchObject({
      headline: "None",
      tone: "none",
      present: false,
    });
    expect(
      describeCredits({
        hasCredits: false,
        unlimited: false,
        balance: "0",
        spendControlReached: false,
        rateLimitReachedType: null,
      }),
    ).toMatchObject({ headline: "None", tone: "none", present: true });
    expect(
      describeCredits({
        hasCredits: true,
        unlimited: false,
        balance: "12.50",
        spendControlReached: true,
        rateLimitReachedType: null,
      }),
    ).toMatchObject({
      headline: "12.50",
      detail: "Spending limit reached",
      tone: "warning",
    });
    expect(
      describeCredits({
        hasCredits: true,
        unlimited: true,
        balance: null,
        spendControlReached: false,
        rateLimitReachedType: null,
      }),
    ).toMatchObject({ headline: "Unlimited", tone: "normal" });
  });

  it("never reinterprets the provider's formatted balance", () => {
    const display = describeCredits({
      hasCredits: true,
      unlimited: false,
      balance: "$1,234.50",
      spendControlReached: false,
      rateLimitReachedType: null,
    });

    expect(display.headline).toBe("$1,234.50");
  });
});

describe("formatting", () => {
  it("labels common rolling windows without relying on primary/secondary", () => {
    expect(formatWindowDuration(300)).toBe("5-hour");
    expect(formatWindowDuration(10_080)).toBe("Weekly");
    expect(formatWindowDuration(2_880)).toBe("2-day");
  });

  it("formats a stable relative countdown", () => {
    expect(
      formatRelativeTime(
        "2026-07-26T14:30:00.000Z",
        new Date("2026-07-26T12:00:00.000Z"),
      ),
    ).toBe("in 2h 30m");
  });
});
