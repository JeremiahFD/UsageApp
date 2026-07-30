import type { UsageSnapshot } from "@usageapp/core";
import type { UsageAnalytics } from "../shared/desktop";

export function codexAnalyticsFromSnapshot(
  snapshot: UsageSnapshot | null,
  now = new Date(),
): UsageAnalytics {
  const daily = snapshot?.tokenUsage?.dailyUsageBuckets ?? null;
  const buckets =
    daily?.map((bucket) => ({
      date: bucket.startDate,
      model: null,
      reasoningLevel: null,
      inputTokens: null,
      outputTokens: null,
      cacheReadTokens: null,
      cacheWriteTokens: null,
      reasoningTokens: null,
      totalTokens: bucket.tokens,
      estimatedCostUsd: null,
      requestCount: null,
    })) ?? [];

  return {
    source: "codex-account",
    observedAt: snapshot?.observedAt ?? now.toISOString(),
    recordingSince:
      buckets.length > 0
        ? buckets.reduce(
            (earliest, bucket) =>
              bucket.date < earliest ? bucket.date : earliest,
            buckets[0]?.date ?? now.toISOString().slice(0, 10),
          )
        : null,
    buckets,
    capabilities: {
      dailyTotals: daily !== null,
      tokenCategories: false,
      modelFilter: false,
      reasoningFilter: false,
    estimatedCost: false,
    tokensPerMinute: false,
    },
    message:
      daily === null
        ? "Codex did not return daily token activity for this account."
        : "Codex supplies daily token totals, but not historical model or reasoning-level attribution.",
  };
}
