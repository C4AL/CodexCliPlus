import type {
  CPARuntimeState,
  CodexMode,
  CodexShimState,
  InstallLayout,
  PluginMarketState,
  ProcessCard,
  ServiceManagerState,
  UpdateCenterState,
} from "./product";

export type ShellState = {
  productName: string;
  productShortName: string;
  tagline: string;
  version: string;
  milestone: string;
  installLayout: InstallLayout;
  processModel: ProcessCard[];
  primarySurfaces: string[];
  service: {
    serviceName: string;
    hostBinary: string;
    state: string;
    mode: string;
    stale: boolean;
    updatedAt: string | null;
    message: string | null;
  };
  serviceManager: ServiceManagerState;
  codex: CodexShimState;
  cpaRuntime: CPARuntimeState;
  logs: {
    serviceLogPath: string;
    cpaRuntimeLogPath: string;
    serviceTail: string[];
    cpaRuntimeTail: string[];
  };
  pluginMarket: PluginMarketState;
  updateCenter: UpdateCenterState;
};

export type CpadApi = {
  getShellState: () => Promise<ShellState>;
  openPath: (targetPath: string) => Promise<string>;
  openUrl: (targetUrl: string) => Promise<void>;
  installService: () => Promise<ShellState>;
  removeService: () => Promise<ShellState>;
  startService: () => Promise<ShellState>;
  stopService: () => Promise<ShellState>;
  setCodexMode: (mode: CodexMode) => Promise<ShellState>;
  buildCpaRuntime: () => Promise<ShellState>;
  startCpaRuntime: () => Promise<ShellState>;
  stopCpaRuntime: () => Promise<ShellState>;
  refreshPluginMarket: () => Promise<ShellState>;
  installPlugin: (id: string) => Promise<ShellState>;
  updatePlugin: (id: string) => Promise<ShellState>;
  enablePlugin: (id: string) => Promise<ShellState>;
  disablePlugin: (id: string) => Promise<ShellState>;
  diagnosePlugin: (id: string) => Promise<ShellState>;
  checkUpdates: () => Promise<ShellState>;
  syncOfficialBaselines: () => Promise<ShellState>;
};
