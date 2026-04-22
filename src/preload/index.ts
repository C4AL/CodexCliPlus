import { contextBridge, ipcRenderer } from "electron";

import type { CpadApi } from "../shared/ipc";

const api: CpadApi = {
  getShellState: async () => ipcRenderer.invoke("cpad:get-shell-state"),
  openPath: async (targetPath) =>
    ipcRenderer.invoke("cpad:open-path", targetPath),
  installService: async () => ipcRenderer.invoke("cpad:install-service"),
  removeService: async () => ipcRenderer.invoke("cpad:remove-service"),
  startService: async () => ipcRenderer.invoke("cpad:start-service"),
  stopService: async () => ipcRenderer.invoke("cpad:stop-service"),
  setCodexMode: async (mode) => ipcRenderer.invoke("cpad:set-codex-mode", mode),
  buildCpaRuntime: async () => ipcRenderer.invoke("cpad:build-cpa-runtime"),
  startCpaRuntime: async () => ipcRenderer.invoke("cpad:start-cpa-runtime"),
  stopCpaRuntime: async () => ipcRenderer.invoke("cpad:stop-cpa-runtime"),
  refreshPluginMarket: async () =>
    ipcRenderer.invoke("cpad:refresh-plugin-market"),
  installPlugin: async (id) => ipcRenderer.invoke("cpad:install-plugin", id),
  updatePlugin: async (id) => ipcRenderer.invoke("cpad:update-plugin", id),
  enablePlugin: async (id) => ipcRenderer.invoke("cpad:enable-plugin", id),
  disablePlugin: async (id) => ipcRenderer.invoke("cpad:disable-plugin", id),
  diagnosePlugin: async (id) => ipcRenderer.invoke("cpad:diagnose-plugin", id),
  checkUpdates: async () => ipcRenderer.invoke("cpad:check-updates"),
  syncOfficialBaselines: async () =>
    ipcRenderer.invoke("cpad:sync-official-baselines"),
};

contextBridge.exposeInMainWorld("cpad", api);
