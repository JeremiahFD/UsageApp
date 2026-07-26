import { StyleSheet, Text, View } from "react-native";

import { colors, radii, spacing } from "../theme";

type BannerTone = "info" | "warning" | "danger";

interface StatusBannerProps {
  title: string;
  body: string;
  tone?: BannerTone;
}

const toneColors: Record<
  BannerTone,
  { background: string; border: string; icon: string }
> = {
  info: {
    background: colors.infoMuted,
    border: "#295074",
    icon: colors.info,
  },
  warning: {
    background: colors.warningMuted,
    border: "#66502a",
    icon: colors.warning,
  },
  danger: {
    background: colors.dangerMuted,
    border: "#70403b",
    icon: colors.danger,
  },
};

export function StatusBanner({
  body,
  title,
  tone = "info",
}: StatusBannerProps) {
  const palette = toneColors[tone];

  return (
    <View
      accessibilityRole="alert"
      style={[
        styles.container,
        {
          backgroundColor: palette.background,
          borderColor: palette.border,
        },
      ]}
    >
      <View style={[styles.indicator, { backgroundColor: palette.icon }]} />
      <View style={styles.copy}>
        <Text style={styles.title}>{title}</Text>
        <Text style={styles.body}>{body}</Text>
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    borderRadius: radii.medium,
    borderWidth: 1,
    flexDirection: "row",
    padding: spacing.medium,
  },
  indicator: {
    borderRadius: radii.pill,
    height: 9,
    marginRight: 12,
    marginTop: 5,
    width: 9,
  },
  copy: {
    flex: 1,
  },
  title: {
    color: colors.text,
    fontSize: 14,
    fontWeight: "700",
    marginBottom: 4,
  },
  body: {
    color: colors.textMuted,
    fontSize: 13,
    lineHeight: 19,
  },
});
