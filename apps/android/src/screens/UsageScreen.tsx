import {
  formatRelativeTime,
  formatObservedTime,
  formatResetTime,
  getMostConstrainedRemaining,
  type UsageSnapshot,
} from "@usageapp/core";
import {
  ActivityIndicator,
  Pressable,
  RefreshControl,
  ScrollView,
  StyleSheet,
  Text,
  View,
} from "react-native";

import { BankedResetsCard } from "../components/BankedResetsCard";
import { StatusBanner } from "../components/StatusBanner";
import { UsageWindowCard } from "../components/UsageWindowCard";
import { colors, radii, spacing } from "../theme";

interface UsageScreenProps {
  authRejected: boolean;
  now: Date;
  onOpenSettings: () => void;
  onRefresh: () => void;
  paired: boolean;
  refreshError: string | null;
  refreshing: boolean;
  snapshot: UsageSnapshot | null;
}

function clampPercent(value: number): number {
  if (!Number.isFinite(value)) {
    return 0;
  }

  return Math.min(100, Math.max(0, value));
}

function remainingColor(remaining: number): string {
  if (remaining <= 20) {
    return colors.danger;
  }
  if (remaining <= 45) {
    return colors.warning;
  }
  return colors.accent;
}

function EmptyUsage({
  onOpenSettings,
  onRefresh,
  paired,
  refreshError,
  refreshing,
}: Pick<
  UsageScreenProps,
  "onOpenSettings" | "onRefresh" | "paired" | "refreshError" | "refreshing"
>) {
  return (
    <View style={styles.emptyCard}>
      <View style={styles.emptyLogo}>
        <Text style={styles.emptyLogoText}>U</Text>
      </View>
      <Text style={styles.emptyTitle}>
        {paired ? "Waiting for usage data" : "Pair your Windows app"}
      </Text>
      <Text style={styles.emptyBody}>
        {paired
          ? refreshError ??
            "Usage will appear here as soon as the PC service responds."
          : "Connect this phone to the desktop companion to see Codex limits, reset times, and banked resets."}
      </Text>
      {paired ? (
        <Pressable
          accessibilityRole="button"
          disabled={refreshing}
          onPress={onRefresh}
          style={({ pressed }) => [
            styles.primaryButton,
            pressed ? styles.buttonPressed : null,
          ]}
        >
          {refreshing ? (
            <ActivityIndicator color={colors.background} size="small" />
          ) : (
            <Text style={styles.primaryButtonText}>Try again</Text>
          )}
        </Pressable>
      ) : (
        <Pressable
          accessibilityRole="button"
          onPress={onOpenSettings}
          style={({ pressed }) => [
            styles.primaryButton,
            pressed ? styles.buttonPressed : null,
          ]}
        >
          <Text style={styles.primaryButtonText}>Open pairing</Text>
        </Pressable>
      )}
    </View>
  );
}

export function UsageScreen({
  authRejected,
  now,
  onOpenSettings,
  onRefresh,
  paired,
  refreshError,
  refreshing,
  snapshot,
}: UsageScreenProps) {
  if (!snapshot) {
    return (
      <ScrollView
        contentContainerStyle={styles.emptyPage}
        refreshControl={
          paired ? (
            <RefreshControl
              colors={[colors.accent]}
              onRefresh={onRefresh}
              refreshing={refreshing}
              tintColor={colors.accent}
            />
          ) : undefined
        }
      >
        <EmptyUsage
          onOpenSettings={onOpenSettings}
          onRefresh={onRefresh}
          paired={paired}
          refreshError={refreshError}
          refreshing={refreshing}
        />
      </ScrollView>
    );
  }

  const constrained = getMostConstrainedRemaining(snapshot);
  const remaining =
    constrained === null ? null : clampPercent(Math.round(constrained));
  const heroColor = remaining === null ? colors.textMuted : remainingColor(remaining);
  const observedRelative = formatRelativeTime(snapshot.observedAt, now);
  const observedExact = formatObservedTime(snapshot.observedAt);
  const isDisconnected = !paired;
  const isOffline = Boolean(refreshError) || isDisconnected;
  const isSynchronizing = paired && refreshing;

  return (
    <ScrollView
      contentContainerStyle={styles.content}
      refreshControl={
        paired ? (
          <RefreshControl
            colors={[colors.accent]}
            onRefresh={onRefresh}
            progressBackgroundColor={colors.surfaceRaised}
            refreshing={refreshing}
            tintColor={colors.accent}
          />
        ) : undefined
      }
      showsVerticalScrollIndicator={false}
    >
      <View style={styles.topBar}>
        <View style={styles.brandRow}>
          <View style={styles.brandMark}>
            <Text style={styles.brandMarkText}>U</Text>
          </View>
          <View>
            <Text style={styles.appName}>Usage Viewer</Text>
            <Text style={styles.provider}>{snapshot.providerName}</Text>
          </View>
        </View>
        <View style={styles.livePill}>
          <View
            style={[
              styles.liveDot,
              {
                backgroundColor:
                  snapshot.status === "live" && !isOffline && !isSynchronizing
                    ? colors.accent
                    : colors.warning,
              },
            ]}
          />
          <Text style={styles.liveText}>
            {isDisconnected
              ? "CACHED"
              : isSynchronizing
                ? "SYNCING"
              : refreshError
                ? "OFFLINE"
                : snapshot.status.replace("-", " ").toUpperCase()}
          </Text>
        </View>
      </View>

      {isDisconnected ? (
        <StatusBanner
          body={`Live refresh is off. This saved snapshot was observed ${observedRelative}. Pair in Settings to reconnect.`}
          title="Disconnected — cached data"
          tone="warning"
        />
      ) : authRejected ? (
        <StatusBanner
          body="The PC rejected this device token. Open Settings and enter a new one-time code."
          title="Pair this phone again"
          tone="danger"
        />
      ) : isOffline ? (
        <StatusBanner
          body={`${refreshError} Showing the last saved snapshot from ${observedRelative}.`}
          title="PC unavailable — cached data"
          tone="warning"
        />
      ) : snapshot.status !== "live" ? (
        <StatusBanner
          body={
            snapshot.message ??
            "The desktop source could not confirm fresh usage. Values below may be out of date."
          }
          title={
            snapshot.status === "stale"
              ? "Usage data is stale"
              : snapshot.status === "auth-required"
                ? "ChatGPT sign-in needs attention"
                : "Usage source unavailable"
          }
          tone={snapshot.status === "auth-required" ? "danger" : "warning"}
        />
      ) : snapshot.message ? (
        <StatusBanner body={snapshot.message} title="Provider note" />
      ) : null}

      <View style={styles.hero}>
        <View style={styles.heroCopy}>
          <Text style={styles.heroEyebrow}>
            {snapshot.planType?.toUpperCase() ?? "CHATGPT / CODEX"}
          </Text>
          <Text style={styles.heroTitle}>Most limited window</Text>
          <Text style={styles.heroMeta}>
            Last known usage {observedExact} · {observedRelative}
          </Text>
        </View>
        <View style={[styles.heroGauge, { borderColor: heroColor }]}>
          <Text style={[styles.heroNumber, { color: heroColor }]}>
            {remaining === null ? "—" : remaining}
          </Text>
          <Text style={styles.heroUnit}>{remaining === null ? "NO DATA" : "% LEFT"}</Text>
        </View>
      </View>

      <View style={styles.sectionHeader}>
        <Text style={styles.sectionTitle}>Usage windows</Text>
        <Text style={styles.sectionHint}>Pull down to refresh</Text>
      </View>

      {snapshot.windows.length > 0 ? (
        <View style={styles.windowList}>
          {snapshot.windows.map((window) => (
            <UsageWindowCard key={window.id} now={now} window={window} />
          ))}
        </View>
      ) : (
        <View style={styles.noWindows}>
          <Text style={styles.noWindowsText}>
            No usage windows were returned by ChatGPT.
          </Text>
        </View>
      )}

      <View style={styles.sectionHeader}>
        <Text style={styles.sectionTitle}>Resets</Text>
      </View>
      <BankedResetsCard bankedResets={snapshot.bankedResets} now={now} />

      <Text style={styles.footerNote}>
        Values come from the paired Windows companion and may lag behind activity
        shown directly in ChatGPT.
      </Text>
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  content: {
    gap: spacing.medium,
    paddingBottom: spacing.xlarge,
    paddingHorizontal: spacing.medium,
    paddingTop: spacing.medium,
  },
  emptyPage: {
    flexGrow: 1,
    justifyContent: "center",
    padding: spacing.large,
  },
  emptyCard: {
    alignItems: "center",
    backgroundColor: colors.surface,
    borderColor: colors.border,
    borderRadius: radii.large,
    borderWidth: 1,
    padding: spacing.xlarge,
  },
  emptyLogo: {
    alignItems: "center",
    backgroundColor: colors.accentMuted,
    borderColor: "#285947",
    borderRadius: 24,
    borderWidth: 1,
    height: 72,
    justifyContent: "center",
    marginBottom: spacing.large,
    transform: [{ rotate: "-5deg" }],
    width: 72,
  },
  emptyLogoText: {
    color: colors.accent,
    fontSize: 34,
    fontWeight: "900",
    transform: [{ rotate: "5deg" }],
  },
  emptyTitle: {
    color: colors.text,
    fontSize: 22,
    fontWeight: "800",
    textAlign: "center",
  },
  emptyBody: {
    color: colors.textMuted,
    fontSize: 14,
    lineHeight: 21,
    marginTop: spacing.small,
    textAlign: "center",
  },
  primaryButton: {
    alignItems: "center",
    backgroundColor: colors.accent,
    borderRadius: radii.medium,
    height: 48,
    justifyContent: "center",
    marginTop: spacing.large,
    minWidth: 170,
    paddingHorizontal: spacing.large,
  },
  primaryButtonText: {
    color: colors.background,
    fontSize: 15,
    fontWeight: "800",
  },
  buttonPressed: {
    opacity: 0.78,
  },
  topBar: {
    alignItems: "center",
    flexDirection: "row",
    justifyContent: "space-between",
    marginBottom: 2,
  },
  brandRow: {
    alignItems: "center",
    flexDirection: "row",
  },
  brandMark: {
    alignItems: "center",
    backgroundColor: colors.accentMuted,
    borderRadius: 13,
    height: 42,
    justifyContent: "center",
    marginRight: 11,
    width: 42,
  },
  brandMarkText: {
    color: colors.accent,
    fontSize: 19,
    fontWeight: "900",
  },
  appName: {
    color: colors.text,
    fontSize: 17,
    fontWeight: "800",
  },
  provider: {
    color: colors.textMuted,
    fontSize: 12,
    marginTop: 2,
  },
  livePill: {
    alignItems: "center",
    backgroundColor: colors.surfaceRaised,
    borderRadius: radii.pill,
    flexDirection: "row",
    paddingHorizontal: 10,
    paddingVertical: 7,
  },
  liveDot: {
    borderRadius: radii.pill,
    height: 7,
    marginRight: 6,
    width: 7,
  },
  liveText: {
    color: colors.textMuted,
    fontSize: 9,
    fontWeight: "800",
    letterSpacing: 0.7,
  },
  hero: {
    alignItems: "center",
    backgroundColor: colors.surface,
    borderColor: colors.border,
    borderRadius: radii.large,
    borderWidth: 1,
    flexDirection: "row",
    padding: spacing.large,
  },
  heroCopy: {
    flex: 1,
    paddingRight: spacing.small,
  },
  heroEyebrow: {
    color: colors.accent,
    fontSize: 11,
    fontWeight: "800",
    letterSpacing: 1.2,
  },
  heroTitle: {
    color: colors.text,
    fontSize: 20,
    fontWeight: "800",
    lineHeight: 25,
    marginTop: 5,
  },
  heroMeta: {
    color: colors.textDim,
    fontSize: 11,
    lineHeight: 16,
    marginTop: 7,
  },
  heroGauge: {
    alignItems: "center",
    borderRadius: radii.pill,
    borderWidth: 7,
    height: 96,
    justifyContent: "center",
    width: 96,
  },
  heroNumber: {
    fontSize: 28,
    fontVariant: ["tabular-nums"],
    fontWeight: "900",
    lineHeight: 31,
  },
  heroUnit: {
    color: colors.textDim,
    fontSize: 8,
    fontWeight: "800",
    letterSpacing: 0.8,
  },
  sectionHeader: {
    alignItems: "center",
    flexDirection: "row",
    justifyContent: "space-between",
    marginTop: spacing.xsmall,
    paddingHorizontal: 2,
  },
  sectionTitle: {
    color: colors.text,
    fontSize: 17,
    fontWeight: "800",
  },
  sectionHint: {
    color: colors.textDim,
    fontSize: 11,
  },
  windowList: {
    gap: spacing.small,
  },
  noWindows: {
    backgroundColor: colors.surface,
    borderColor: colors.border,
    borderRadius: radii.medium,
    borderWidth: 1,
    padding: spacing.medium,
  },
  noWindowsText: {
    color: colors.textMuted,
    fontSize: 13,
  },
  footerNote: {
    color: colors.textDim,
    fontSize: 11,
    lineHeight: 17,
    paddingHorizontal: spacing.small,
    textAlign: "center",
  },
});
