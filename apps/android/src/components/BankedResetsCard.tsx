import {
  formatRelativeTime,
  formatResetTime,
  type UsageSnapshot,
} from "@usageapp/core";
import { StyleSheet, Text, View } from "react-native";

import { colors, radii, spacing } from "../theme";

type BankedReset = UsageSnapshot["bankedResets"]["items"][number];

interface BankedResetsCardProps {
  bankedResets: UsageSnapshot["bankedResets"];
  now: Date;
}

function sortByExpiry(items: BankedReset[]): BankedReset[] {
  return [...items].sort((left, right) => {
    if (!left.expiresAt && !right.expiresAt) {
      return 0;
    }
    if (!left.expiresAt) {
      return 1;
    }
    if (!right.expiresAt) {
      return -1;
    }
    return Date.parse(left.expiresAt) - Date.parse(right.expiresAt);
  });
}

export function BankedResetsCard({
  bankedResets,
  now,
}: BankedResetsCardProps) {
  const items = sortByExpiry(bankedResets.items);

  return (
    <View style={styles.card}>
      <View style={styles.header}>
        <View>
          <Text style={styles.eyebrow}>BANKED RESETS</Text>
          <Text style={styles.heading}>Available boosts</Text>
        </View>
        <View style={styles.countBubble}>
          <Text style={styles.count}>
            {bankedResets.availableCount ?? "—"}
          </Text>
        </View>
      </View>

      {bankedResets.availableCount === 0 ? (
        <Text style={styles.emptyText}>
          No banked resets are currently available.
        </Text>
      ) : items.length === 0 ? (
        <Text style={styles.emptyText}>
          {bankedResets.availableCount === null
            ? "Your provider did not report a banked reset count or individual expiry times."
            : `${bankedResets.availableCount} banked ${
                bankedResets.availableCount === 1 ? "reset is" : "resets are"
              } available, but individual expiry times were not returned.`}
        </Text>
      ) : (
        <View style={styles.list}>
          {items.map((item, index) => {
            const relativeExpiry = item.expiresAt
              ? formatRelativeTime(item.expiresAt, now)
              : null;
            const exactExpiry = item.expiresAt
              ? formatResetTime(item.expiresAt, now)
              : null;

            return (
              <View
                key={item.id}
                style={[
                  styles.item,
                  index < items.length - 1 ? styles.itemWithDivider : null,
                ]}
              >
                <View style={styles.itemIcon}>
                  <Text style={styles.itemIconText}>+</Text>
                </View>
                <View style={styles.itemCopy}>
                  <View style={styles.itemTitleRow}>
                    <Text style={styles.itemTitle}>
                      {item.title?.trim() || "Banked reset"}
                    </Text>
                    <Text style={styles.status}>{item.status}</Text>
                  </View>
                  {item.description?.trim() ? (
                    <Text style={styles.description}>{item.description}</Text>
                  ) : null}
                  <Text style={styles.expiry}>
                    {relativeExpiry
                      ? `Expires ${relativeExpiry}`
                      : "Expiry time unavailable"}
                  </Text>
                  {exactExpiry ? (
                    <Text style={styles.exactExpiry}>{exactExpiry}</Text>
                  ) : null}
                </View>
              </View>
            );
          })}
        </View>
      )}
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
    alignItems: "center",
    flexDirection: "row",
    justifyContent: "space-between",
  },
  eyebrow: {
    color: colors.textDim,
    fontSize: 11,
    fontWeight: "800",
    letterSpacing: 1.3,
  },
  heading: {
    color: colors.text,
    fontSize: 18,
    fontWeight: "700",
    marginTop: 4,
  },
  countBubble: {
    alignItems: "center",
    backgroundColor: colors.accentMuted,
    borderColor: "#285947",
    borderRadius: radii.pill,
    borderWidth: 1,
    height: 46,
    justifyContent: "center",
    width: 46,
  },
  count: {
    color: colors.accent,
    fontSize: 20,
    fontVariant: ["tabular-nums"],
    fontWeight: "800",
  },
  emptyText: {
    color: colors.textMuted,
    fontSize: 13,
    lineHeight: 20,
    marginTop: spacing.medium,
  },
  list: {
    marginTop: spacing.medium,
  },
  item: {
    flexDirection: "row",
    paddingVertical: 12,
  },
  itemWithDivider: {
    borderBottomColor: colors.border,
    borderBottomWidth: StyleSheet.hairlineWidth,
  },
  itemIcon: {
    alignItems: "center",
    backgroundColor: colors.accentMuted,
    borderRadius: radii.small,
    height: 32,
    justifyContent: "center",
    marginRight: 12,
    width: 32,
  },
  itemIconText: {
    color: colors.accent,
    fontSize: 20,
    fontWeight: "500",
    lineHeight: 22,
  },
  itemCopy: {
    flex: 1,
  },
  itemTitleRow: {
    alignItems: "center",
    flexDirection: "row",
    gap: spacing.small,
    justifyContent: "space-between",
  },
  itemTitle: {
    color: colors.text,
    flex: 1,
    fontSize: 14,
    fontWeight: "700",
  },
  status: {
    color: colors.textDim,
    fontSize: 10,
    fontWeight: "700",
    textTransform: "uppercase",
  },
  description: {
    color: colors.textMuted,
    fontSize: 12,
    lineHeight: 18,
    marginTop: 4,
  },
  expiry: {
    color: colors.warning,
    fontSize: 13,
    fontWeight: "600",
    marginTop: 7,
  },
  exactExpiry: {
    color: colors.textDim,
    fontSize: 12,
    marginTop: 2,
  },
});
