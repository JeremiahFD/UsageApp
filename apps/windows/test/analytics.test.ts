import { describe, expect, it } from "vitest";

import {
  addCalendarDays,
  aggregateUsageByDate,
  filterUsageBuckets,
  getChartMaximum,
  getUsageFilterOptions,
  isDateInRange,
  localDateKey,
  resolveDateRange,
  scaleChartBars,
  scaleChartPoints,
  selectAndAggregateUsage,
  summarizeUsageBuckets,
} from "../src/renderer/analytics";
import type { UsageHistoryBucket } from "../src/shared/desktop";

function bucket(
  overrides: Partial<UsageHistoryBucket> = {},
): UsageHistoryBucket {
  return {
    date: "2026-07-20",
    model: "gpt-5",
    reasoningLevel: "high",
    inputTokens: 100,
    outputTokens: 50,
    cacheReadTokens: 20,
    cacheWriteTokens: 10,
    reasoningTokens: 5,
    totalTokens: 185,
    estimatedCostUsd: 0.25,
    requestCount: 2,
    ...overrides,
  };
}

describe("calendar date ranges", () => {
  it("resolves inclusive presets using calendar arithmetic", () => {
    expect(resolveDateRange("today", "2026-04-30")).toEqual({
      preset: "today",
      startDate: "2026-04-30",
      endDate: "2026-04-30",
    });
    expect(resolveDateRange("7d", "2026-03-09")).toEqual({
      preset: "7d",
      startDate: "2026-03-03",
      endDate: "2026-03-09",
    });
    expect(resolveDateRange("30d", "2026-03-31")).toEqual({
      preset: "30d",
      startDate: "2026-03-02",
      endDate: "2026-03-31",
    });
    expect(resolveDateRange("90d", "2026-04-30")).toEqual({
      preset: "90d",
      startDate: "2026-01-31",
      endDate: "2026-04-30",
    });
    expect(resolveDateRange("all", "2026-04-30")).toEqual({
      preset: "all",
      startDate: null,
      endDate: null,
    });
  });

  it("normalizes custom boundaries and includes both endpoints", () => {
    const range = resolveDateRange("custom", "2026-07-26", {
      startDate: "2026-07-20",
      endDate: "2026-07-10",
    });

    expect(range).toEqual({
      preset: "custom",
      startDate: "2026-07-10",
      endDate: "2026-07-20",
    });
    expect(isDateInRange("2026-07-10", range)).toBe(true);
    expect(isDateInRange("2026-07-20", range)).toBe(true);
    expect(isDateInRange("2026-07-21", range)).toBe(false);
    expect(isDateInRange("2026-02-30", range)).toBe(false);
  });

  it("handles month, leap-year, and local-date boundaries without timestamps", () => {
    expect(addCalendarDays("2024-03-01", -1)).toBe("2024-02-29");
    expect(addCalendarDays("2026-01-01", -1)).toBe("2025-12-31");
    expect(localDateKey(new Date(2026, 6, 26, 23, 55))).toBe("2026-07-26");
  });
});

describe("usage filtering and aggregation", () => {
  const buckets = [
    bucket(),
    bucket({
      date: "2026-07-20",
      model: "gpt-5",
      reasoningLevel: "low",
      inputTokens: 25,
      outputTokens: 10,
      cacheReadTokens: 0,
      cacheWriteTokens: 0,
      reasoningTokens: 1,
      totalTokens: 36,
      estimatedCostUsd: 0.05,
      requestCount: 1,
    }),
    bucket({
      date: "2026-07-21",
      model: "o3",
      reasoningLevel: "high",
      totalTokens: 300,
    }),
    bucket({
      date: "2026-07-22",
      model: null,
      reasoningLevel: null,
      totalTokens: 80,
    }),
  ];

  it("extracts sorted model and reasoning options without inventing unknowns", () => {
    expect(getUsageFilterOptions(buckets)).toEqual({
      models: ["gpt-5", "o3"],
      reasoningLevels: ["high", "low"],
    });
  });

  it("combines inclusive date, model, and reasoning filters", () => {
    const selected = filterUsageBuckets(buckets, {
      range: resolveDateRange("custom", "2026-07-26", {
        startDate: "2026-07-20",
        endDate: "2026-07-21",
      }),
      models: ["gpt-5"],
      reasoningLevels: ["high"],
    });

    expect(selected).toHaveLength(1);
    expect(selected[0]).toMatchObject({
      date: "2026-07-20",
      model: "gpt-5",
      reasoningLevel: "high",
    });
  });

  it("sums totals, token categories, cost, and requests", () => {
    const summary = summarizeUsageBuckets(buckets.slice(0, 2));

    expect(summary).toEqual({
      totalTokens: 221,
      inputTokens: 125,
      outputTokens: 60,
      cacheReadTokens: 20,
      cacheWriteTokens: 10,
      reasoningTokens: 6,
      estimatedCostUsd: 0.3,
      requestCount: 3,
      bucketCount: 2,
      dayCount: 1,
    });
  });

  it("keeps incomplete metrics null instead of treating them as zero", () => {
    const incomplete = [
      bucket(),
      bucket({
        date: "2026-07-21",
        inputTokens: null,
        estimatedCostUsd: null,
        requestCount: null,
        totalTokens: 25,
      }),
    ];
    const summary = summarizeUsageBuckets(incomplete);

    expect(summary.totalTokens).toBe(210);
    expect(summary.inputTokens).toBeNull();
    expect(summary.estimatedCostUsd).toBeNull();
    expect(summary.requestCount).toBeNull();
    expect(summary.outputTokens).toBe(100);
  });

  it("aggregates duplicate calendar days and returns selected analysis", () => {
    const daily = aggregateUsageByDate(buckets.slice(0, 3));
    expect(daily).toHaveLength(2);
    expect(daily[0]).toMatchObject({
      date: "2026-07-20",
      totalTokens: 221,
      bucketCount: 2,
    });

    const result = selectAndAggregateUsage(buckets, {
      models: ["o3"],
    });
    expect(result.selectedBuckets).toHaveLength(1);
    expect(result.daily).toHaveLength(1);
    expect(result.summary.totalTokens).toBe(300);
  });

  it("returns zero only for the known total when no buckets are selected", () => {
    const result = selectAndAggregateUsage([], {});

    expect(result.daily).toEqual([]);
    expect(result.summary).toEqual({
      totalTokens: 0,
      inputTokens: null,
      outputTokens: null,
      cacheReadTokens: null,
      cacheWriteTokens: null,
      reasoningTokens: null,
      estimatedCostUsd: null,
      requestCount: null,
      bucketCount: 0,
      dayCount: 0,
    });
  });
});

describe("chart scaling", () => {
  it("returns empty chart structures for empty data", () => {
    expect(scaleChartPoints([])).toEqual([]);
    expect(scaleChartBars([])).toEqual([]);
    expect(getChartMaximum([])).toBe(0);
  });

  it("centers a single point and bar without producing invalid numbers", () => {
    const data = [{ key: "one", label: "Jul 20", value: 10 }];
    const [point] = scaleChartPoints(data, {
      formatValue: (value) => `${value} tokens`,
    });
    const [bar] = scaleChartBars(data);

    expect(point).toMatchObject({
      xPercent: 50,
      yPercent: 0,
      ariaLabel: "Jul 20: 10 tokens",
    });
    expect(bar?.xPercent).toBeGreaterThan(0);
    expect(bar?.widthPercent).toBeLessThan(100);
    expect(bar?.heightPercent).toBe(100);
    expect(Number.isFinite(bar?.xPercent)).toBe(true);
  });

  it("keeps zero values on the baseline and null values unavailable", () => {
    const data = [
      { key: "zero", label: "No usage", value: 0 },
      { key: "missing", label: "Not reported", value: null },
    ];
    const points = scaleChartPoints(data);
    const bars = scaleChartBars(data);

    expect(points[0]).toMatchObject({ yPercent: 100 });
    expect(bars[0]).toMatchObject({ heightPercent: 0, yPercent: 100 });
    expect(points[1]).toMatchObject({
      yPercent: null,
      ariaLabel: "Not reported: unavailable",
    });
    expect(bars[1]).toMatchObject({
      heightPercent: null,
      yPercent: null,
    });
    expect(getChartMaximum([null, 0, 5])).toBe(5);
  });
});
