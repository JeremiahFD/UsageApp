import AsyncStorage from "@react-native-async-storage/async-storage";
import {
  decodeUsageSnapshot,
  type UsageSnapshot,
} from "@usageapp/core";
import * as SecureStore from "expo-secure-store";

const CONNECTION_KEY = "usageapp.connection.v1";
const SNAPSHOT_KEY = "usageapp.snapshot.v1";
const DEVICE_TOKEN_KEY = "usageapp.device-token.v1";

export interface ConnectionSettings {
  serverUrl: string;
  deviceName: string;
  deviceId: string | null;
}

export interface StoredViewerState {
  connection: ConnectionSettings;
  snapshot: UsageSnapshot | null;
  token: string | null;
}

const EMPTY_CONNECTION: ConnectionSettings = {
  serverUrl: "",
  deviceName: "Android phone",
  deviceId: null,
};

function parseJson<T>(value: string | null): T | null {
  if (!value) {
    return null;
  }

  try {
    return JSON.parse(value) as T;
  } catch {
    return null;
  }
}

interface SnapshotEnvelope {
  serverUrl: string;
  snapshot: UsageSnapshot;
}

function decodeSnapshotEnvelope(value: unknown): SnapshotEnvelope | null {
  if (
    typeof value !== "object" ||
    value === null ||
    Array.isArray(value)
  ) {
    return null;
  }
  const candidate = value as Record<string, unknown>;
  const snapshot = decodeUsageSnapshot(candidate.snapshot);
  return typeof candidate.serverUrl === "string" && snapshot
    ? { serverUrl: candidate.serverUrl, snapshot }
    : null;
}

export async function loadViewerState(): Promise<StoredViewerState> {
  const [connectionJson, snapshotJson, token] = await Promise.all([
    AsyncStorage.getItem(CONNECTION_KEY),
    AsyncStorage.getItem(SNAPSHOT_KEY),
    SecureStore.getItemAsync(DEVICE_TOKEN_KEY),
  ]);

  const storedConnection = parseJson<Partial<ConnectionSettings>>(connectionJson);
  const connection: ConnectionSettings = {
    serverUrl:
      typeof storedConnection?.serverUrl === "string"
        ? storedConnection.serverUrl
        : EMPTY_CONNECTION.serverUrl,
    deviceName:
      typeof storedConnection?.deviceName === "string" &&
      storedConnection.deviceName.trim()
        ? storedConnection.deviceName
        : EMPTY_CONNECTION.deviceName,
    deviceId:
      typeof storedConnection?.deviceId === "string"
        ? storedConnection.deviceId
        : null,
  };

  const envelope = decodeSnapshotEnvelope(parseJson<unknown>(snapshotJson));

  return {
    connection,
    snapshot:
      envelope?.serverUrl === connection.serverUrl
        ? envelope.snapshot
        : null,
    token,
  };
}

export async function savePairing(input: {
  connection: ConnectionSettings;
  token: string;
}): Promise<void> {
  await SecureStore.setItemAsync(DEVICE_TOKEN_KEY, input.token);

  try {
    await AsyncStorage.setItem(
      CONNECTION_KEY,
      JSON.stringify(input.connection),
    );
  } catch (error) {
    await SecureStore.deleteItemAsync(DEVICE_TOKEN_KEY);
    throw error;
  }
}

export async function saveSnapshot(
  snapshot: UsageSnapshot,
  serverUrl: string,
): Promise<void> {
  const envelope: SnapshotEnvelope = { serverUrl, snapshot };
  await AsyncStorage.setItem(SNAPSHOT_KEY, JSON.stringify(envelope));
}

export async function clearSnapshot(): Promise<void> {
  await AsyncStorage.removeItem(SNAPSHOT_KEY);
}

export async function forgetDeviceToken(): Promise<void> {
  await SecureStore.deleteItemAsync(DEVICE_TOKEN_KEY);
}

export async function restoreDeviceToken(token: string): Promise<void> {
  await SecureStore.setItemAsync(DEVICE_TOKEN_KEY, token);
}
