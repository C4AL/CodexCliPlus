import { contextBridge, ipcRenderer } from "electron";

import type { CpadApi } from "../shared/ipc";

const api: CpadApi = {
  getShellState: async () => ipcRenderer.invoke("cpad:get-shell-state"),
  openPath: async (targetPath) =>
    ipcRenderer.invoke("cpad:open-path", targetPath),
  setCodexMode: async (mode) => ipcRenderer.invoke("cpad:set-codex-mode", mode),
  refreshPluginMarket: async () =>
    ipcRenderer.invoke("cpad:refresh-plugin-market"),
  installPlugin: async (id) => ipcRenderer.invoke("cpad:install-plugin", id),
  updatePlugin: async (id) => ipcRenderer.invoke("cpad:update-plugin", id),
  enablePlugin: async (id) => ipcRenderer.invoke("cpad:enable-plugin", id),
  disablePlugin: async (id) => ipcRenderer.invoke("cpad:disable-plugin", id),
  diagnosePlugin: async (id) => ipcRenderer.invoke("cpad:diagnose-plugin", id),
  checkUpdates: async () => ipcRenderer.invoke("cpad:check-updates"),
};

contextBridge.exposeInMainWorld("cpad", api);
