import { contextBridge, ipcRenderer } from "electron";
import type { AppSettings } from "@usageapp/core";
import {
  IPC,
  type DesktopState,
  type PairingCodeInfo,
  type UsageAppBridge,
} from "../shared/desktop";

const bridge: UsageAppBridge = {
  getState: () => ipcRenderer.invoke(IPC.getState) as Promise<DesktopState>,
  refresh: () => ipcRenderer.invoke(IPC.refresh) as Promise<DesktopState>,
  updateSettings: (patch: Partial<AppSettings>) =>
    ipcRenderer.invoke(IPC.updateSettings, patch) as Promise<DesktopState>,
  createPairingCode: () =>
    ipcRenderer.invoke(IPC.createPairingCode) as Promise<PairingCodeInfo>,
  revokePhoneTokens: () =>
    ipcRenderer.invoke(IPC.revokePhoneTokens) as Promise<DesktopState>,
  hideFlyout: () => ipcRenderer.invoke(IPC.hideFlyout) as Promise<void>,
  showFlyout: () => ipcRenderer.invoke(IPC.showFlyout) as Promise<void>,
  quit: () => ipcRenderer.invoke(IPC.quit) as Promise<void>,
  onStateChanged: (listener: (state: DesktopState) => void) => {
    const wrapped = (_event: Electron.IpcRendererEvent, state: DesktopState) => {
      listener(state);
    };
    ipcRenderer.on(IPC.stateChanged, wrapped);
    return () => {
      ipcRenderer.removeListener(IPC.stateChanged, wrapped);
    };
  },
};

contextBridge.exposeInMainWorld("usageApp", bridge);
