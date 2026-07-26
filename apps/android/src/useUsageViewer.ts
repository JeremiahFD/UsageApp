import type { UsageSnapshot } from "@usageapp/core";
import { useCallback, useEffect, useRef, useState } from "react";
import { AppState } from "react-native";

import {
  ApiError,
  fetchSnapshot,
  normalizePrivateServerUrl,
  pairWithServer,
  revokeDevice,
} from "./api";
import {
  clearSnapshot,
  type ConnectionSettings,
  forgetDeviceToken,
  loadViewerState,
  restoreDeviceToken,
  savePairing,
  saveSnapshot,
} from "./storage";

const FOREGROUND_REFRESH_INTERVAL_MS = 5 * 60_000;

interface PairInput {
  serverUrl: string;
  code: string;
  deviceName: string;
}

function errorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    return error.message;
  }

  if (error instanceof Error && error.message) {
    return error.message;
  }

  return "Something went wrong while updating usage.";
}

export function useUsageViewer() {
  const [booting, setBooting] = useState(true);
  const [connection, setConnection] = useState<ConnectionSettings>({
    serverUrl: "",
    deviceName: "Android phone",
    deviceId: null,
  });
  const [token, setToken] = useState<string | null>(null);
  const [snapshot, setSnapshot] = useState<UsageSnapshot | null>(null);
  const [refreshing, setRefreshing] = useState(false);
  const [pairing, setPairing] = useState(false);
  const [refreshError, setRefreshError] = useState<string | null>(null);
  const [authRejected, setAuthRejected] = useState(false);
  const mountedRef = useRef(true);
  const connectionRef = useRef(connection);
  const tokenRef = useRef(token);
  const snapshotRef = useRef(snapshot);
  const requestIdRef = useRef(0);
  const refreshSuspendedRef = useRef(false);
  const activeRequestRef = useRef<{
    id: number;
    key: string;
    promise: Promise<void>;
  } | null>(null);

  const invalidateActiveRefresh = useCallback(() => {
    requestIdRef.current += 1;
    activeRequestRef.current = null;
    if (mountedRef.current) {
      setRefreshing(false);
    }
  }, []);

  useEffect(() => {
    connectionRef.current = connection;
  }, [connection]);

  useEffect(() => {
    tokenRef.current = token;
  }, [token]);

  useEffect(() => {
    snapshotRef.current = snapshot;
  }, [snapshot]);

  const performRefresh = useCallback(
    async (credentials?: { connection: ConnectionSettings; token: string }) => {
      if (!credentials && refreshSuspendedRef.current) {
        return;
      }

      const activeConnection = credentials?.connection ?? connectionRef.current;
      const activeToken = credentials?.token ?? tokenRef.current;

      if (!activeConnection.serverUrl || !activeToken) {
        return;
      }

      const requestKey = `${activeConnection.serverUrl}\n${activeToken}`;
      if (activeRequestRef.current?.key === requestKey) {
        return activeRequestRef.current.promise;
      }

      const requestId = ++requestIdRef.current;
      const request = (async () => {
        setRefreshing(true);
        try {
          const nextSnapshot = await fetchSnapshot({
            serverUrl: activeConnection.serverUrl,
            token: activeToken,
          });

          if (mountedRef.current && requestId === requestIdRef.current) {
            setSnapshot(nextSnapshot);
            setRefreshError(null);
            setAuthRejected(false);

            try {
              await saveSnapshot(nextSnapshot, activeConnection.serverUrl);
            } catch {
              // The live view remains usable even if the non-secret cache fails.
            }
          }
        } catch (error) {
          if (mountedRef.current && requestId === requestIdRef.current) {
            setRefreshError(errorMessage(error));
            setAuthRejected(
              error instanceof ApiError &&
                (error.status === 401 || error.status === 403),
            );
          }
        } finally {
          if (mountedRef.current && requestId === requestIdRef.current) {
            setRefreshing(false);
          }
          if (activeRequestRef.current?.id === requestId) {
            activeRequestRef.current = null;
          }
        }
      })();

      activeRequestRef.current = {
        id: requestId,
        key: requestKey,
        promise: request,
      };
      return request;
    },
    [],
  );

  useEffect(() => {
    mountedRef.current = true;

    void (async () => {
      try {
        const stored = await loadViewerState();
        if (!mountedRef.current) {
          return;
        }

        connectionRef.current = stored.connection;
        tokenRef.current = stored.token;
        setConnection(stored.connection);
        setToken(stored.token);
        setSnapshot(
          stored.snapshot
            ? {
                ...stored.snapshot,
                status:
                  stored.snapshot.status === "live"
                    ? "stale"
                    : stored.snapshot.status,
                message:
                  stored.snapshot.status === "live"
                    ? "Showing the last snapshot saved on this phone while a live refresh runs."
                    : stored.snapshot.message,
              }
            : null,
        );
        setBooting(false);

        if (stored.connection.serverUrl && stored.token) {
          await performRefresh({
            connection: stored.connection,
            token: stored.token,
          });
        }
      } catch (error) {
        if (mountedRef.current) {
          setBooting(false);
          setRefreshError(errorMessage(error));
        }
      }
    })();

    return () => {
      mountedRef.current = false;
    };
  }, [performRefresh]);

  useEffect(() => {
    const subscription = AppState.addEventListener("change", (nextState) => {
      if (nextState === "active") {
        void performRefresh();
      }
    });

    return () => subscription.remove();
  }, [performRefresh]);

  useEffect(() => {
    const intervalId = setInterval(() => {
      if (AppState.currentState === "active") {
        void performRefresh();
      }
    }, FOREGROUND_REFRESH_INTERVAL_MS);

    return () => clearInterval(intervalId);
  }, [performRefresh]);

  const pair = useCallback(
    async (input: PairInput) => {
      const serverUrl = normalizePrivateServerUrl(input.serverUrl);
      const code = input.code.replace(/\D/g, "");
      const deviceName = input.deviceName.trim();

      if (!/^\d{6}$/.test(code)) {
        throw new ApiError("Enter the 6-digit code shown by the Windows app.");
      }

      if (deviceName.length < 2 || deviceName.length > 60) {
        throw new ApiError("Give this phone a name between 2 and 60 characters.");
      }

      setPairing(true);
      refreshSuspendedRef.current = true;
      let issuedCredentials: { serverUrl: string; token: string } | null = null;
      let pairingCommitted = false;
      try {
        const pairingResult = await pairWithServer({
          serverUrl,
          code,
          deviceName,
        });
        issuedCredentials = { serverUrl, token: pairingResult.token };

        // A refresh started with the previous PC/account may still be in
        // flight. Invalidate it before changing any local pairing state so its
        // eventual response cannot repopulate the cleared view or cache.
        invalidateActiveRefresh();

        const previousConnection = connectionRef.current;
        const previousToken = tokenRef.current;
        const nextConnection = {
          serverUrl,
          deviceName,
          deviceId: pairingResult.deviceId,
        };

        // The same PC address can represent a different signed-in Codex
        // account, so server-origin matching alone is not sufficient.
        setSnapshot(null);
        await clearSnapshot();

        await savePairing({
          connection: nextConnection,
          token: pairingResult.token,
        });
        pairingCommitted = true;

        connectionRef.current = nextConnection;
        tokenRef.current = pairingResult.token;
        setConnection(nextConnection);
        setToken(pairingResult.token);
        setRefreshError(null);
        setAuthRejected(false);

        if (previousConnection.serverUrl && previousToken) {
          try {
            await revokeDevice({
              serverUrl: previousConnection.serverUrl,
              token: previousToken,
            });
          } catch {
            // Windows can revoke all remaining tokens if the old PC is offline.
          }
        }

        await performRefresh({
          connection: nextConnection,
          token: pairingResult.token,
        });
      } catch (error) {
        if (issuedCredentials && !pairingCommitted) {
          try {
            await revokeDevice(issuedCredentials);
          } catch {
            // The Windows revoke-all action remains the recovery path when the
            // newly paired PC cannot be reached.
          }
        }
        throw error;
      } finally {
        refreshSuspendedRef.current = false;
        setPairing(false);
      }
    },
    [invalidateActiveRefresh, performRefresh],
  );

  const disconnect = useCallback(async () => {
    refreshSuspendedRef.current = true;
    invalidateActiveRefresh();

    const currentConnection = connectionRef.current;
    const currentToken = tokenRef.current;
    const currentSnapshot = snapshotRef.current;

    try {
      const [tokenCleanup, snapshotCleanup] = await Promise.allSettled([
        forgetDeviceToken(),
        clearSnapshot(),
      ]);

      if (
        tokenCleanup.status === "rejected" ||
        snapshotCleanup.status === "rejected"
      ) {
        const rollback: Promise<unknown>[] = [];
        if (tokenCleanup.status === "fulfilled" && currentToken) {
          rollback.push(restoreDeviceToken(currentToken));
        }
        if (
          snapshotCleanup.status === "fulfilled" &&
          currentSnapshot &&
          currentConnection.serverUrl
        ) {
          rollback.push(
            saveSnapshot(currentSnapshot, currentConnection.serverUrl),
          );
        }

        const rollbackResults = await Promise.allSettled(rollback);
        const rollbackFailed = rollbackResults.some(
          (result) => result.status === "rejected",
        );
        throw new ApiError(
          rollbackFailed
            ? "Android could not fully remove or restore the saved pairing. The phone still shows its previous state; restart the app before trying again."
            : "Android could not remove the saved pairing, so this phone remains connected. Try again.",
        );
      }

      tokenRef.current = null;
      snapshotRef.current = null;
      setToken(null);
      setSnapshot(null);
      setAuthRejected(false);
      setRefreshError(null);

      if (currentConnection.serverUrl && currentToken) {
        try {
          await revokeDevice({
            serverUrl: currentConnection.serverUrl,
            token: currentToken,
          });
        } catch {
          // Local forgetting still succeeds. The Windows app exposes an explicit
          // revoke-all action for a PC that was unreachable during disconnect.
        }
      }
    } finally {
      refreshSuspendedRef.current = false;
    }
  }, [invalidateActiveRefresh]);

  return {
    authRejected,
    booting,
    connection,
    disconnect,
    pair,
    paired: Boolean(connection.serverUrl && token),
    pairing,
    refresh: performRefresh,
    refreshError,
    refreshing,
    snapshot,
  };
}
