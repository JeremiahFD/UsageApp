import { StatusBar } from "expo-status-bar";
import { useEffect, useState } from "react";
import {
  ActivityIndicator,
  Pressable,
  StyleSheet,
  Text,
  View,
} from "react-native";
import {
  initialWindowMetrics,
  SafeAreaProvider,
  SafeAreaView,
} from "react-native-safe-area-context";

import { SettingsScreen } from "./src/screens/SettingsScreen";
import { UsageScreen } from "./src/screens/UsageScreen";
import { colors } from "./src/theme";
import { useUsageViewer } from "./src/useUsageViewer";

type Tab = "usage" | "settings";

function BottomNavigation({
  activeTab,
  onChange,
}: {
  activeTab: Tab;
  onChange: (tab: Tab) => void;
}) {
  return (
    <View style={styles.navigation}>
      <Pressable
        accessibilityRole="tab"
        accessibilityState={{ selected: activeTab === "usage" }}
        onPress={() => onChange("usage")}
        style={({ pressed }) => [
          styles.navItem,
          pressed ? styles.navPressed : null,
        ]}
      >
        <View
          style={[
            styles.navIcon,
            activeTab === "usage" ? styles.navIconActive : null,
          ]}
        >
          <View
            style={[
              styles.usageGlyph,
              activeTab === "usage" ? styles.usageGlyphActive : null,
            ]}
          />
        </View>
        <Text
          style={[
            styles.navText,
            activeTab === "usage" ? styles.navTextActive : null,
          ]}
        >
          Usage
        </Text>
      </Pressable>

      <Pressable
        accessibilityRole="tab"
        accessibilityState={{ selected: activeTab === "settings" }}
        onPress={() => onChange("settings")}
        style={({ pressed }) => [
          styles.navItem,
          pressed ? styles.navPressed : null,
        ]}
      >
        <View
          style={[
            styles.navIcon,
            activeTab === "settings" ? styles.navIconActive : null,
          ]}
        >
          <Text
            style={[
              styles.settingsGlyph,
              activeTab === "settings" ? styles.settingsGlyphActive : null,
            ]}
          >
            ⚙
          </Text>
        </View>
        <Text
          style={[
            styles.navText,
            activeTab === "settings" ? styles.navTextActive : null,
          ]}
        >
          Settings
        </Text>
      </Pressable>
    </View>
  );
}

function ViewerApp() {
  const viewer = useUsageViewer();
  const [activeTab, setActiveTab] = useState<Tab>("usage");
  const [now, setNow] = useState(() => new Date());

  useEffect(() => {
    const intervalId = setInterval(() => setNow(new Date()), 30_000);
    return () => clearInterval(intervalId);
  }, []);

  if (viewer.booting) {
    return (
      <SafeAreaView style={styles.loading}>
        <StatusBar style="light" />
        <View style={styles.loadingMark}>
          <Text style={styles.loadingMarkText}>U</Text>
        </View>
        <ActivityIndicator color={colors.accent} size="small" />
        <Text style={styles.loadingText}>Loading saved usage…</Text>
      </SafeAreaView>
    );
  }

  return (
    <SafeAreaView edges={["top", "bottom"]} style={styles.safeArea}>
      <StatusBar style="light" />
      <View style={styles.screen}>
        {activeTab === "usage" ? (
          <UsageScreen
            authRejected={viewer.authRejected}
            now={now}
            onOpenSettings={() => setActiveTab("settings")}
            onRefresh={() => void viewer.refresh()}
            paired={viewer.paired}
            refreshError={viewer.refreshError}
            refreshing={viewer.refreshing}
            snapshot={viewer.snapshot}
          />
        ) : (
          <SettingsScreen
            connection={viewer.connection}
            onDisconnect={viewer.disconnect}
            onPair={viewer.pair}
            onPairComplete={() => setActiveTab("usage")}
            paired={viewer.paired}
            pairing={viewer.pairing}
          />
        )}
      </View>
      <BottomNavigation activeTab={activeTab} onChange={setActiveTab} />
    </SafeAreaView>
  );
}

export default function App() {
  return (
    <SafeAreaProvider initialMetrics={initialWindowMetrics}>
      <ViewerApp />
    </SafeAreaProvider>
  );
}

const styles = StyleSheet.create({
  safeArea: {
    backgroundColor: colors.background,
    flex: 1,
  },
  screen: {
    flex: 1,
  },
  loading: {
    alignItems: "center",
    backgroundColor: colors.background,
    flex: 1,
    justifyContent: "center",
  },
  loadingMark: {
    alignItems: "center",
    backgroundColor: colors.accentMuted,
    borderRadius: 18,
    height: 56,
    justifyContent: "center",
    marginBottom: 20,
    width: 56,
  },
  loadingMarkText: {
    color: colors.accent,
    fontSize: 26,
    fontWeight: "900",
  },
  loadingText: {
    color: colors.textMuted,
    fontSize: 13,
    marginTop: 12,
  },
  navigation: {
    backgroundColor: colors.surfaceMuted,
    borderTopColor: colors.border,
    borderTopWidth: StyleSheet.hairlineWidth,
    flexDirection: "row",
    minHeight: 66,
    paddingHorizontal: 36,
    paddingTop: 5,
  },
  navItem: {
    alignItems: "center",
    flex: 1,
    justifyContent: "center",
  },
  navPressed: {
    opacity: 0.68,
  },
  navIcon: {
    alignItems: "center",
    borderRadius: 13,
    height: 27,
    justifyContent: "center",
    width: 42,
  },
  navIconActive: {
    backgroundColor: colors.accentMuted,
  },
  usageGlyph: {
    borderColor: colors.textDim,
    borderRadius: 8,
    borderWidth: 2,
    height: 16,
    width: 16,
  },
  usageGlyphActive: {
    borderColor: colors.accent,
    borderRightWidth: 5,
  },
  settingsGlyph: {
    color: colors.textDim,
    fontSize: 17,
    lineHeight: 20,
  },
  settingsGlyphActive: {
    color: colors.accent,
  },
  navText: {
    color: colors.textDim,
    fontSize: 10,
    fontWeight: "700",
    marginTop: 2,
  },
  navTextActive: {
    color: colors.accent,
  },
});
