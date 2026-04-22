import { win32 } from "node:path";

export const PRODUCT_NAME = "Cli Proxy API Desktop";
export const PRODUCT_SHORT_NAME = "CPAD";
export const PRODUCT_TAGLINE =
  "面向 Windows 的 CPA、Codex 与自有插件统一控制平面。";
export const SERVICE_NAME = "CliProxyAPIDesktopService";
export const INSTALL_DIR_NAME = "Cli Proxy API Desktop";

export type InstallLayout = {
  installRoot: string;
  directories: {
    data: string;
    codexData: string;
    cpaData: string;
    codexRuntime: string;
    cpaRuntime: string;
    plugins: string;
    logs: string;
    tmp: string;
    upstream: string;
    officialCoreBaseline: string;
    officialPanelBaseline: string;
  };
  files: {
    database: string;
    serviceState: string;
    serviceLog: string;
    codexMode: string;
    cpaRuntimeState: string;
    cpaRuntimeLog: string;
    pluginCatalog: string;
    pluginState: string;
    updateCenterState: string;
  };
};

export type CodexMode = "official" | "cpa";

export type CodexShimState = {
  mode: CodexMode;
  modeFile: string;
  shimPath: string;
  targetPath: string;
  targetExists: boolean;
  message: string;
  updatedAt: string | null;
};

export type CPARuntimeState = {
  sourceRoot: string;
  sourceExists: boolean;
  buildPackage: string;
  managedBinary: string;
  binaryExists: boolean;
  configPath: string;
  configExists: boolean;
  stateFile: string;
  logPath: string;
  phase: string;
  pid: number | null;
  running: boolean;
  message: string;
  updatedAt: string | null;
};

export type PluginRuntimeState = {
  id: string;
  name: string;
  version: string;
  description: string;
  sourcePath: string;
  sourceExists: boolean;
  readmePath: string;
  readmeExists: boolean;
  installPath: string;
  installed: boolean;
  enabled: boolean;
  installedVersion: string;
  needsUpdate: boolean;
  message: string;
  updatedAt: string | null;
};

export type PluginMarketState = {
  sourceRoot: string;
  sourceExists: boolean;
  catalogPath: string;
  statePath: string;
  pluginsDir: string;
  plugins: PluginRuntimeState[];
  updatedAt: string | null;
};

export type UpdateSourceState = {
  id: string;
  name: string;
  kind: string;
  source: string;
  currentRef: string;
  latestRef: string;
  dirty: boolean;
  available: boolean;
  message: string;
  updatedAt: string | null;
};

export type UpdateCenterState = {
  productName: string;
  stateFile: string;
  sources: UpdateSourceState[];
  updatedAt: string | null;
};

export type ProcessCard = {
  title: string;
  summary: string;
  responsibilities: string[];
};

export const PROCESS_MODEL: ProcessCard[] = [
  {
    title: "桌面壳",
    summary: "Electron/Chromium 前端，负责设置、状态、操作入口和可视化。",
    responsibilities: [
      "概览与状态",
      "模式切换入口",
      "更新中心",
      "插件市场",
      "日志与诊断",
    ],
  },
  {
    title: "Windows 服务",
    summary: "机器级后台宿主，持有受控状态、运行时守护和持久化职责。",
    responsibilities: [
      "开机常驻",
      "未登录运行",
      "后端守护",
      "更新检查",
      "状态持久化",
    ],
  },
  {
    title: "CPA Runtime",
    summary: "由服务宿主管理的后端运行时，负责构建、启动、停止和后续版本切换。",
    responsibilities: ["受控构建", "受控启动", "受控停止", "运行状态落盘"],
  },
  {
    title: "Codex Shim",
    summary: "机器 PATH 前置入口，根据当前模式把命令转发到受控运行时。",
    responsibilities: [
      "受控入口",
      "模式转发",
      "兼容官方运行时",
      "屏蔽失控升级",
    ],
  },
];

export const PRIMARY_SURFACES = [
  "总览",
  "运行状态",
  "模式切换",
  "Codex 设置",
  "CPA 设置",
  "插件市场",
  "更新中心",
  "日志与诊断",
];

export function buildInstallLayout(
  homeDir: string,
  explicitRoot?: string,
): InstallLayout {
  const installRoot = explicitRoot
    ? win32.normalize(explicitRoot)
    : win32.join(homeDir, INSTALL_DIR_NAME);
  const data = win32.join(installRoot, "data");
  const logs = win32.join(installRoot, "logs");

  return {
    installRoot,
    directories: {
      data,
      codexData: win32.join(data, "codex"),
      cpaData: win32.join(data, "cpa"),
      codexRuntime: win32.join(installRoot, "runtime", "codex"),
      cpaRuntime: win32.join(installRoot, "runtime", "cpa"),
      plugins: win32.join(installRoot, "plugins"),
      logs,
      tmp: win32.join(installRoot, "tmp"),
      upstream: win32.join(installRoot, "upstream"),
      officialCoreBaseline: win32.join(
        installRoot,
        "upstream",
        "CLIProxyAPI",
      ),
      officialPanelBaseline: win32.join(
        installRoot,
        "upstream",
        "Cli-Proxy-API-Management-Center",
      ),
    },
    files: {
      database: win32.join(data, "app.db"),
      serviceState: win32.join(data, "service-state.json"),
      serviceLog: win32.join(logs, "service-host.log"),
      codexMode: win32.join(data, "codex-mode.json"),
      cpaRuntimeState: win32.join(data, "cpa-runtime.json"),
      cpaRuntimeLog: win32.join(logs, "cpa-runtime.log"),
      pluginCatalog: win32.join(data, "plugin-catalog.json"),
      pluginState: win32.join(data, "plugin-state.json"),
      updateCenterState: win32.join(data, "update-center.json"),
    },
  };
}

export function normalizeCodexMode(value: string | undefined): CodexMode {
  return value === "cpa" ? "cpa" : "official";
}

export function getCodexShimPath(layout: InstallLayout) {
  return win32.join(layout.installRoot, "codex.exe");
}

export function getCPASourceRoot(homeDir: string, explicitRoot?: string) {
  return explicitRoot
    ? win32.normalize(explicitRoot)
    : win32.join(homeDir, "workspace", "CPA-UV-publish");
}

export function getCPAManagedBinaryPath(layout: InstallLayout) {
  return win32.join(layout.directories.cpaRuntime, "CPA-UV.exe");
}

export function getCPAManagedConfigPath(layout: InstallLayout) {
  return win32.join(layout.directories.cpaRuntime, "config.yaml");
}

export function getPluginSourceRoot(homeDir: string, explicitRoot?: string) {
  return explicitRoot
    ? win32.normalize(explicitRoot)
    : win32.join(homeDir, "workspace", "omni-bot-plugins-oss");
}

export function getCodexRuntimeCandidates(
  layout: InstallLayout,
  mode: CodexMode,
) {
  const runtimeRoot =
    mode === "cpa"
      ? layout.directories.cpaRuntime
      : layout.directories.codexRuntime;
  const override =
    mode === "cpa"
      ? process.env.CPAD_CODEX_CPA_EXECUTABLE
      : process.env.CPAD_CODEX_OFFICIAL_EXECUTABLE;

  const candidates = [
    win32.join(runtimeRoot, "codex.exe"),
    win32.join(runtimeRoot, "codex.cmd"),
    win32.join(runtimeRoot, "bin", "codex.exe"),
    win32.join(runtimeRoot, "bin", "codex.cmd"),
  ];

  return override ? [win32.normalize(override), ...candidates] : candidates;
}
