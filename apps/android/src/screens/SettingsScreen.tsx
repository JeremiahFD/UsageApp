import { useEffect, useState } from "react";
import {
  ActivityIndicator,
  Alert,
  KeyboardAvoidingView,
  Platform,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  View,
} from "react-native";

import type { ConnectionSettings } from "../storage";
import { colors, radii, spacing } from "../theme";

interface SettingsScreenProps {
  connection: ConnectionSettings;
  onDisconnect: () => Promise<void>;
  onPair: (input: {
    serverUrl: string;
    code: string;
    deviceName: string;
  }) => Promise<void>;
  onPairComplete: () => void;
  paired: boolean;
  pairing: boolean;
}

function inputStyle(focused: boolean) {
  return [
    styles.input,
    focused ? styles.inputFocused : null,
  ];
}

export function SettingsScreen({
  connection,
  onDisconnect,
  onPair,
  onPairComplete,
  paired,
  pairing,
}: SettingsScreenProps) {
  const [serverUrl, setServerUrl] = useState(connection.serverUrl);
  const [deviceName, setDeviceName] = useState(connection.deviceName);
  const [code, setCode] = useState("");
  const [focusedField, setFocusedField] = useState<string | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const [disconnecting, setDisconnecting] = useState(false);

  useEffect(() => {
    setServerUrl(connection.serverUrl);
    setDeviceName(connection.deviceName);
  }, [connection.deviceName, connection.serverUrl]);

  const submitPairing = async () => {
    setFormError(null);
    try {
      await onPair({ serverUrl, code, deviceName });
      setCode("");
      onPairComplete();
    } catch (error) {
      setFormError(
        error instanceof Error
          ? error.message
          : "Pairing failed. Check the code and try again.",
      );
    }
  };

  const requestDisconnect = () => {
    Alert.alert(
      "Disconnect this phone?",
      "UsageApp will remove this phone's saved snapshot and pairing token. It will also revoke the device on the Windows app when the PC is reachable.",
      [
        { style: "cancel", text: "Cancel" },
        {
          style: "destructive",
          text: "Disconnect",
          onPress: () => {
            setDisconnecting(true);
            void onDisconnect()
              .catch((error: unknown) => {
                Alert.alert(
                  "Could not disconnect",
                  error instanceof Error
                    ? error.message
                    : "Android could not remove the saved pairing. Try again.",
                );
              })
              .finally(() => setDisconnecting(false));
          },
        },
      ],
    );
  };

  const pairDisabled =
    pairing ||
    disconnecting ||
    !serverUrl.trim() ||
    code.length !== 6 ||
    deviceName.trim().length < 2;

  return (
    <KeyboardAvoidingView
      behavior={Platform.OS === "ios" ? "padding" : undefined}
      style={styles.flex}
    >
      <ScrollView
        contentContainerStyle={styles.content}
        keyboardShouldPersistTaps="handled"
        showsVerticalScrollIndicator={false}
      >
        <View style={styles.header}>
          <Text style={styles.eyebrow}>CONNECTION</Text>
          <Text style={styles.title}>Pair with your PC</Text>
          <Text style={styles.subtitle}>
            In the Windows companion, choose “Pair phone,” then enter its
            address and one-time code below.
          </Text>
        </View>

        <View style={styles.connectionCard}>
          <View
            style={[
              styles.connectionIcon,
              paired ? styles.connectionIconLive : null,
            ]}
          >
            <View
              style={[
                styles.connectionDot,
                {
                  backgroundColor: paired ? colors.accent : colors.textDim,
                },
              ]}
            />
          </View>
          <View style={styles.connectionCopy}>
            <Text style={styles.connectionTitle}>
              {paired ? "Paired" : "Not paired"}
            </Text>
            <Text numberOfLines={1} style={styles.connectionDetail}>
              {paired ? connection.serverUrl : "No PC connection saved"}
            </Text>
          </View>
        </View>

        <View style={styles.formCard}>
          <Text style={styles.label}>PC address</Text>
          <TextInput
            accessibilityLabel="PC address"
            autoCapitalize="none"
            autoCorrect={false}
            keyboardType="url"
            onBlur={() => setFocusedField(null)}
            onChangeText={setServerUrl}
            onFocus={() => setFocusedField("url")}
            placeholder="http://192.168.1.42:43120"
            placeholderTextColor={colors.textDim}
            returnKeyType="next"
            selectionColor={colors.accent}
            style={inputStyle(focusedField === "url")}
            value={serverUrl}
          />
          <Text style={styles.helpText}>
            Include the port shown by the Windows app. You can omit “http://”.
          </Text>

          <Text style={[styles.label, styles.nextLabel]}>One-time code</Text>
          <TextInput
            accessibilityLabel="Six-digit one-time pairing code"
            autoComplete="one-time-code"
            keyboardType="number-pad"
            maxLength={6}
            onBlur={() => setFocusedField(null)}
            onChangeText={(value) => setCode(value.replace(/\D/g, ""))}
            onFocus={() => setFocusedField("code")}
            placeholder="000000"
            placeholderTextColor={colors.textDim}
            selectionColor={colors.accent}
            style={[
              ...inputStyle(focusedField === "code"),
              styles.codeInput,
            ]}
            textContentType="oneTimeCode"
            value={code}
          />
          <Text style={styles.helpText}>
            Codes expire quickly and can be used only once.
          </Text>

          <Text style={[styles.label, styles.nextLabel]}>Device name</Text>
          <TextInput
            accessibilityLabel="Device name"
            autoCapitalize="words"
            maxLength={60}
            onBlur={() => setFocusedField(null)}
            onChangeText={setDeviceName}
            onFocus={() => setFocusedField("name")}
            placeholder="Android phone"
            placeholderTextColor={colors.textDim}
            returnKeyType="done"
            selectionColor={colors.accent}
            style={inputStyle(focusedField === "name")}
            value={deviceName}
          />
          <Text style={styles.helpText}>
            This is how the phone will appear in the Windows app.
          </Text>

          {formError ? (
            <View accessibilityRole="alert" style={styles.errorBox}>
              <Text style={styles.errorText}>{formError}</Text>
            </View>
          ) : null}

          <Pressable
            accessibilityRole="button"
            disabled={pairDisabled}
            onPress={() => void submitPairing()}
            style={({ pressed }) => [
              styles.pairButton,
              pairDisabled ? styles.buttonDisabled : null,
              pressed && !pairDisabled ? styles.buttonPressed : null,
            ]}
          >
            {pairing ? (
              <ActivityIndicator color={colors.background} size="small" />
            ) : (
              <Text style={styles.pairButtonText}>
                {paired ? "Pair again" : "Pair this phone"}
              </Text>
            )}
          </Pressable>
        </View>

        <View style={styles.securityCard}>
          <View style={styles.shield}>
            <Text style={styles.shieldText}>!</Text>
          </View>
          <View style={styles.securityCopy}>
            <Text style={styles.securityTitle}>Trusted private network only</Text>
            <Text style={styles.securityBody}>
              This viewer intentionally uses unencrypted HTTP for a PC on your
              private LAN. Use it only on a network you trust. Do not use public
              Wi-Fi or expose the Windows service to the internet.
            </Text>
            <Text style={styles.securityDetail}>
              The device token is stored in Android’s encrypted Keystore. Usage
              snapshots are cached separately for offline viewing.
            </Text>
          </View>
        </View>

        {paired ? (
          <Pressable
            accessibilityRole="button"
            disabled={disconnecting || pairing}
            onPress={requestDisconnect}
            style={({ pressed }) => [
              styles.disconnectButton,
              pressed ? styles.buttonPressed : null,
            ]}
          >
            {disconnecting ? (
              <ActivityIndicator color={colors.danger} size="small" />
            ) : (
              <Text style={styles.disconnectText}>Disconnect this phone</Text>
            )}
          </Pressable>
        ) : null}

        <Text style={styles.version}>Usage Viewer · Android preview</Text>
      </ScrollView>
    </KeyboardAvoidingView>
  );
}

const styles = StyleSheet.create({
  flex: {
    flex: 1,
  },
  content: {
    gap: spacing.medium,
    paddingBottom: spacing.xlarge,
    paddingHorizontal: spacing.medium,
    paddingTop: spacing.medium,
  },
  header: {
    paddingHorizontal: 2,
    paddingVertical: spacing.small,
  },
  eyebrow: {
    color: colors.accent,
    fontSize: 11,
    fontWeight: "800",
    letterSpacing: 1.3,
  },
  title: {
    color: colors.text,
    fontSize: 27,
    fontWeight: "800",
    marginTop: 6,
  },
  subtitle: {
    color: colors.textMuted,
    fontSize: 14,
    lineHeight: 21,
    marginTop: 8,
  },
  connectionCard: {
    alignItems: "center",
    backgroundColor: colors.surface,
    borderColor: colors.border,
    borderRadius: radii.medium,
    borderWidth: 1,
    flexDirection: "row",
    padding: spacing.medium,
  },
  connectionIcon: {
    alignItems: "center",
    backgroundColor: colors.surfaceRaised,
    borderRadius: 14,
    height: 42,
    justifyContent: "center",
    marginRight: 12,
    width: 42,
  },
  connectionIconLive: {
    backgroundColor: colors.accentMuted,
  },
  connectionDot: {
    borderRadius: radii.pill,
    height: 12,
    width: 12,
  },
  connectionCopy: {
    flex: 1,
  },
  connectionTitle: {
    color: colors.text,
    fontSize: 15,
    fontWeight: "700",
  },
  connectionDetail: {
    color: colors.textMuted,
    fontSize: 12,
    marginTop: 3,
  },
  formCard: {
    backgroundColor: colors.surface,
    borderColor: colors.border,
    borderRadius: radii.large,
    borderWidth: 1,
    padding: spacing.medium,
  },
  label: {
    color: colors.text,
    fontSize: 13,
    fontWeight: "700",
    marginBottom: 7,
  },
  nextLabel: {
    marginTop: spacing.medium,
  },
  input: {
    backgroundColor: colors.surfaceMuted,
    borderColor: colors.borderStrong,
    borderRadius: radii.small,
    borderWidth: 1,
    color: colors.text,
    fontSize: 15,
    minHeight: 49,
    paddingHorizontal: 13,
    paddingVertical: 10,
  },
  inputFocused: {
    borderColor: colors.accent,
  },
  codeInput: {
    fontSize: 22,
    fontVariant: ["tabular-nums"],
    fontWeight: "700",
    letterSpacing: 7,
  },
  helpText: {
    color: colors.textDim,
    fontSize: 11,
    lineHeight: 16,
    marginTop: 6,
  },
  errorBox: {
    backgroundColor: colors.dangerMuted,
    borderColor: "#70403b",
    borderRadius: radii.small,
    borderWidth: 1,
    marginTop: spacing.medium,
    padding: 12,
  },
  errorText: {
    color: colors.danger,
    fontSize: 12,
    lineHeight: 18,
  },
  pairButton: {
    alignItems: "center",
    backgroundColor: colors.accent,
    borderRadius: radii.medium,
    height: 50,
    justifyContent: "center",
    marginTop: spacing.large,
  },
  pairButtonText: {
    color: colors.background,
    fontSize: 15,
    fontWeight: "800",
  },
  buttonDisabled: {
    opacity: 0.38,
  },
  buttonPressed: {
    opacity: 0.76,
  },
  securityCard: {
    backgroundColor: colors.warningMuted,
    borderColor: "#66502a",
    borderRadius: radii.medium,
    borderWidth: 1,
    flexDirection: "row",
    padding: spacing.medium,
  },
  shield: {
    alignItems: "center",
    backgroundColor: "#4a371b",
    borderRadius: 12,
    height: 34,
    justifyContent: "center",
    marginRight: 12,
    width: 34,
  },
  shieldText: {
    color: colors.warning,
    fontSize: 17,
    fontWeight: "900",
  },
  securityCopy: {
    flex: 1,
  },
  securityTitle: {
    color: colors.text,
    fontSize: 14,
    fontWeight: "800",
  },
  securityBody: {
    color: colors.textMuted,
    fontSize: 12,
    lineHeight: 18,
    marginTop: 5,
  },
  securityDetail: {
    color: colors.textDim,
    fontSize: 11,
    lineHeight: 17,
    marginTop: 8,
  },
  disconnectButton: {
    alignItems: "center",
    borderColor: "#70403b",
    borderRadius: radii.medium,
    borderWidth: 1,
    height: 48,
    justifyContent: "center",
  },
  disconnectText: {
    color: colors.danger,
    fontSize: 14,
    fontWeight: "700",
  },
  version: {
    color: colors.textDim,
    fontSize: 11,
    marginTop: spacing.small,
    textAlign: "center",
  },
});
