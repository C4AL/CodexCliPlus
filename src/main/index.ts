import { execFile } from "node:child_process";
import { access, constants, readFile } from "node:fs/promises";
import { join, resolve } from "node:path";
import { promisify } from "node:util";

import { app, BrowserWindow, ipcMain, shell } from "electron";

import {
  CURRENT_MILESTONE,
  PRODUCT_NAME,
  PRODUCT_SHORT_NAME,
  PRODUCT_TAGLINE,
  PRIMARY_SURFACES,
  PROCESS_MODEL,
  SERVICE_NAME,
  buildInstallLayout,
} from "../shared/product";
import type { ShellState } from "../shared/ipc";

const execFileAsync = promisify(execFile);

type HostSnapshot = {
  installRoot: string;
  serviceState: {
    mode: string;
    phase: string;
    message?: string;
    updatedAt?: string;
  } | null;
  managerStatus: {
    serviceName: string;
    installed: boolean;
    state: string;
    startType: string;
    binaryPath: string;
  };
  codex: ShellState["codex"];
  cpaRuntime: ShellState["cpaRuntime"];
  pluginMarket: ShellState["pluginMarket"];
  updateCenter: ShellState["updateCenter"];
};

function isStateStale(
  updatedAt: string | undefined,
  phase: string | undefined,
) {
  if (!updatedAt || phase !== "running") {
    return false;
  }

  const parsed = Date.parse(updatedAt);
  if (Number.isNaN(parsed)) {
    return false;
  }

  return Date.now() - parsed > 45_000;
}

async function pathExists(targetPath: string) {
  try {
    await access(targetPath, constants.F_OK);
    return true;
  } catch {
    return false;
  }
}

async function readLogTail(targetPath: string, limit: number) {
  try {
    const content = await readFile(targetPath, "utf8");
    return content
      .split(/\r?\n/)
      .map((line) => line.trim())
      .filter((line) => line.length > 0)
      .slice(-limit);
  } catch {
    return [];
  }
}

function resolveRepoRoot() {
  if (process.env.CPAD_REPO_ROOT) {
    return resolve(process.env.CPAD_REPO_ROOT);
  }

  return resolve(__dirname, "../..");
}

function normalizeTimestamp(value: string | undefined) {
  if (!value || value.startsWith("0001-01-01")) {
    return null;
  }

  return value;
}

async function resolveServiceExecutable(installRoot: string) {
  const candidates = [
    process.env.CPAD_SERVICE_BINARY,
    join(resolveRepoRoot(), "service", "bin", "cpad-service.exe"),
    join(installRoot, `${PRODUCT_NAME} Service.exe`),
  ].filter(
    (candidate): candidate is string =>
      typeof candidate === "string" && candidate.length > 0,
  );

  for (const candidate of candidates) {
    if (await pathExists(candidate)) {
      return candidate;
    }
  }

  return null;
}

async function runServiceJsonCommand(installRoot: string, args: string[]) {
  const stdout = await runServiceCommand(installRoot, args);
  return JSON.parse(stdout);
}

async function runServiceCommand(installRoot: string, args: string[]) {
  const serviceExecutable = await resolveServiceExecutable(installRoot);

  if (serviceExecutable) {
    const { stdout } = await execFileAsync(serviceExecutable, args, {
      windowsHide: true,
      maxBuffer: 16 * 1024 * 1024,
    });
    return stdout;
  }

  const repoRoot = resolveRepoRoot();
  const serviceRoot = join(repoRoot, "service");
  const { stdout } = await execFileAsync(
    "go",
    ["-C", serviceRoot, "run", "./cmd/cpad-service", ...args],
    {
      cwd: repoRoot,
      windowsHide: true,
      maxBuffer: 16 * 1024 * 1024,
    },
  );

  return stdout;
}

async function createShellState(): Promise<ShellState> {
  const installLayout = buildInstallLayout(
    app.getPath("home"),
    process.env.CPAD_INSTALL_ROOT,
  );
  const hostSnapshot = (await runServiceJsonCommand(installLayout.installRoot, [
    "status",
  ])) as HostSnapshot;
  const stale = isStateStale(
    hostSnapshot.serviceState?.updatedAt,
    hostSnapshot.serviceState?.phase,
  );

  return {
    productName: PRODUCT_NAME,
    productShortName: PRODUCT_SHORT_NAME,
    tagline: PRODUCT_TAGLINE,
    version: app.getVersion(),
    milestone: CURRENT_MILESTONE,
    installLayout,
    processModel: PROCESS_MODEL,
    primarySurfaces: PRIMARY_SURFACES,
    service: {
      serviceName: SERVICE_NAME,
      hostBinary:
        hostSnapshot.managerStatus.binaryPath ||
        (await resolveServiceExecutable(installLayout.installRoot)) ||
        join(installLayout.installRoot, `${PRODUCT_NAME} Service.exe`),
      state: stale
        ? "stale"
        : (hostSnapshot.serviceState?.phase ?? "not-initialized"),
      mode: hostSnapshot.serviceState?.mode ?? "uninitialized",
      stale,
      updatedAt: hostSnapshot.serviceState?.updatedAt ?? null,
      message: stale
        ? "最近一次服务心跳已超时，当前持久化状态可能已经过期。"
        : (hostSnapshot.serviceState?.message ?? null),
    },
    serviceManager: hostSnapshot.managerStatus,
    codex: {
      ...hostSnapshot.codex,
      updatedAt: hostSnapshot.codex.updatedAt ?? null,
    },
    cpaRuntime: {
      ...hostSnapshot.cpaRuntime,
      pid: hostSnapshot.cpaRuntime.pid > 0 ? hostSnapshot.cpaRuntime.pid : null,
      updatedAt: normalizeTimestamp(hostSnapshot.cpaRuntime.updatedAt),
      healthCheck: {
        ...hostSnapshot.cpaRuntime.healthCheck,
        checkedAt: normalizeTimestamp(
          hostSnapshot.cpaRuntime.healthCheck.checkedAt ?? undefined,
        ),
      },
    },
    logs: {
      serviceLogPath: installLayout.files.serviceLog,
      cpaRuntimeLogPath: installLayout.files.cpaRuntimeLog,
      serviceTail: await readLogTail(installLayout.files.serviceLog, 16),
      cpaRuntimeTail: await readLogTail(installLayout.files.cpaRuntimeLog, 16),
    },
    pluginMarket: hostSnapshot.pluginMarket,
    updateCenter: hostSnapshot.updateCenter,
  };
}

async function mutateShellState(args: string[]) {
  const installLayout = buildInstallLayout(
    app.getPath("home"),
    process.env.CPAD_INSTALL_ROOT,
  );
  await runServiceJsonCommand(installLayout.installRoot, args);
  return createShellState();
}

async function mutateShellStateWithCommand(args: string[]) {
  const installLayout = buildInstallLayout(
    app.getPath("home"),
    process.env.CPAD_INSTALL_ROOT,
  );
  await runServiceCommand(installLayout.installRoot, args);
  return createShellState();
}

async function installManagedService() {
  const installLayout = buildInstallLayout(
    app.getPath("home"),
    process.env.CPAD_INSTALL_ROOT,
  );
  const serviceExecutable = await resolveServiceExecutable(installLayout.installRoot);
  if (!serviceExecutable) {
    throw new Error(
      "未找到可安装的服务二进制。请先构建 service/bin/cpad-service.exe，或提供已打包的服务可执行文件。",
    );
  }

  await runServiceCommand(installLayout.installRoot, [
    "install",
    serviceExecutable,
  ]);
  return createShellState();
}

function createWindow(): void {
  const browserWindow = new BrowserWindow({
    width: 1520,
    height: 980,
    minWidth: 1180,
    minHeight: 780,
    show: false,
    backgroundColor: "#efe4d3",
    title: PRODUCT_NAME,
    webPreferences: {
      preload: join(__dirname, "../preload/index.js"),
      sandbox: false,
    },
  });

  browserWindow.once("ready-to-show", () => {
    browserWindow.show();
  });

  if (process.env.ELECTRON_RENDERER_URL) {
    browserWindow.loadURL(process.env.ELECTRON_RENDERER_URL);
  } else {
    browserWindow.loadFile(join(__dirname, "../renderer/index.html"));
  }
}

app.whenReady().then(() => {
  app.setAppUserModelId("BlackblockInc.CPAD");

  ipcMain.handle("cpad:get-shell-state", async () => createShellState());
  ipcMain.handle("cpad:open-path", async (_event, targetPath: string) =>
    shell.openPath(targetPath),
  );
  ipcMain.handle("cpad:open-url", async (_event, targetUrl: string) =>
    shell.openExternal(targetUrl),
  );
  ipcMain.handle("cpad:install-service", async () => installManagedService());
  ipcMain.handle("cpad:remove-service", async () =>
    mutateShellStateWithCommand(["remove"]),
  );
  ipcMain.handle("cpad:start-service", async () =>
    mutateShellStateWithCommand(["start"]),
  );
  ipcMain.handle("cpad:stop-service", async () =>
    mutateShellStateWithCommand(["stop"]),
  );
  ipcMain.handle(
    "cpad:set-codex-mode",
    async (_event, mode: "official" | "cpa") =>
      mutateShellState(["codex-mode", mode]),
  );
  ipcMain.handle("cpad:build-cpa-runtime", async () =>
    mutateShellState(["cpa-runtime", "build"]),
  );
  ipcMain.handle("cpad:start-cpa-runtime", async () =>
    mutateShellState(["cpa-runtime", "start"]),
  );
  ipcMain.handle("cpad:stop-cpa-runtime", async () =>
    mutateShellState(["cpa-runtime", "stop"]),
  );
  ipcMain.handle("cpad:refresh-plugin-market", async () =>
    mutateShellState(["plugin-market", "refresh"]),
  );
  ipcMain.handle("cpad:install-plugin", async (_event, id: string) =>
    mutateShellState(["plugin-market", "install", id]),
  );
  ipcMain.handle("cpad:update-plugin", async (_event, id: string) =>
    mutateShellState(["plugin-market", "update", id]),
  );
  ipcMain.handle("cpad:enable-plugin", async (_event, id: string) =>
    mutateShellState(["plugin-market", "enable", id]),
  );
  ipcMain.handle("cpad:disable-plugin", async (_event, id: string) =>
    mutateShellState(["plugin-market", "disable", id]),
  );
  ipcMain.handle("cpad:diagnose-plugin", async (_event, id: string) =>
    mutateShellState(["plugin-market", "diagnose", id]),
  );
  ipcMain.handle("cpad:check-updates", async () =>
    mutateShellState(["update-center", "check"]),
  );
  ipcMain.handle("cpad:sync-official-baselines", async () =>
    mutateShellState(["update-center", "sync"]),
  );

  createWindow();

  app.on("activate", () => {
    if (BrowserWindow.getAllWindows().length === 0) {
      createWindow();
    }
  });
});

app.on("window-all-closed", () => {
  if (process.platform !== "darwin") {
    app.quit();
  }
});
