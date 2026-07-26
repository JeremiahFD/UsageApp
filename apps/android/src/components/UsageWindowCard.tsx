import {
  formatRelativeTime,
  formatResetTime,
  type UsageSnapshot,
} from "@usageapp/core";
import { StyleSheet, Text, View } from "react-native";

import { colors, radii, spacing } from "../theme";

type UsageWindow = UsageSnapshot["windows"][number];

interface UsageWindowCardProps {
  now: Date;
  window: UsageWindow;
}

function clampPercent(value: number): number {
  if (!Number.isFinite(value)) {
    return 0;
  }

  return Math.min(100, Math.max(0, value));
}

function usageColor(remaining: number): string {
  if (remaining <= 20) {
    return colors.danger;
  }
  if (remaining <= 45) {
    return colors.warning;
  }
  return colors.accent;
}

function windowDuration(minutes: number | null): string | null {
  if (!minutes || minutes <= 0) {
    return null;
  }

  if (minutes % 10_080 === 0) {
    const weeks = minutes / 10_080;
    return `${weeks}-week window`;
  }
  if (minutes % 1_440 === 0) {
    const days = minutes / 1_440;
    return `${days}-day window`;
  }
  if (minutes % 60 === 0) {
    const hours = minutes / 60;
    return `${hours}-hour window`;
  }

  return `${minutes}-minute window`;
}

function windowLabel(window: UsageWindow): string {
  if (window.label.trim()) {
    return window.label;
  }
  if (window.limitName?.trim()) {
    return window.limitName;
  }
  return window.kind === "primary" ? "Primary usage" : "Secondary usage";
}

export function UsageWindowCard({ now, window }: UsageWindowCardProps) {
  const remaining = clampPercent(window.remainingPercent);
  const color = usageColor(remaining);
  const duration = windowDuration(window.durationMinutes);
  const relativeReset = window.resetsAt
    ? formatRelativeTime(window.resetsAt, now)
    : null;
  const exactReset = window.resetsAt
    ? formatResetTime(window.resetsAt, now)
    : null;

  return (
    <View style={styles.card}>
      <View style={styles.header}>
        <View style={styles.headerCopy}>
          <Text style={styles.label}>{windowLabel(window)}</Text>
          {duration ? <Text style={styles.kind}>{duration}</Text> : null}
        </View>
        <Text style={[styles.percent, { color }]}>
          {Math.round(remaining)}% left
        </Text>
      </View>

      <View
        accessibilityLabel={`${Math.round(remaining)} percent remaining`}
        accessibilityRole="progressbar"
        accessibilityValue={{
          max: 100,
          min: 0,
          now: Math.round(remaining),
        }}
        style={styles.track}
      >
        <View
          style={[
            styles.fill,
            {
              backgroundColor: color,
              width: `${remaining}%`,
            },
          ]}
        />
      </View>

      <View style={styles.footer}>
        <View>
          <Text style={styles.resetLabel}>
            {relativeReset ? `Resets ${relativeReset}` : "Reset time unavailable"}
          </Text>
          {exactReset ? <Text style={styles.resetExact}>{exactReset}</Text> : null}
        </View>
        <Text style={styles.used}>{Math.round(clampPercent(window.usedPercent))}% used</Text>
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  card: {
    backgroundColor: colors.surface,
    borderColor: colors.border,
    borderRadius: radii.large,
    borderWidth: 1,
    padding: spacing.medium,
  },
  header: {
    alignItems: "flex-start",
    flexDirection: "row",
    gap: spacing.small,
    justifyContent: "space-between",
  },
  headerCopy: {
    flex: 1,
  },
  label: {
    color: colors.text,
    fontSize: 16,
    fontWeight: "700",
    lineHeight: 21,
  },
  kind: {
    color: colors.textDim,
    fontSize: 12,
    marginTop: 3,
    textTransform: "uppercase",
  },
  percent: {
    fontSize: 16,
    fontVariant: ["tabular-nums"],
    fontWeight: "800",
  },
  track: {
    backgroundColor: colors.surfaceRaised,
    borderRadius: radii.pill,
    height: 9,
    marginVertical: spacing.medium,
    overflow: "hidden",
  },
  fill: {
    borderRadius: radii.pill,
    height: "100%",
  },
  footer: {
    alignItems: "flex-end",
    flexDirection: "row",
    gap: spacing.small,
    justifyContent: "space-between",
  },
  resetLabel: {
    color: colors.textMuted,
    fontSize: 13,
    fontWeight: "600",
  },
  resetExact: {
    color: colors.textDim,
    fontSize: 12,
    marginTop: 3,
  },
  used: {
    color: colors.textDim,
    fontSize: 12,
    fontVariant: ["tabular-nums"],
  },
});
