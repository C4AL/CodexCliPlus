import { execFile } from "node:child_process";
import {
  access,
  constants,
  mkdir,
  readFile,
  rm,
  writeFile,
} from "node:fs/promises";
import { dirname, isAbsolute, join, relative, resolve } from "node:path";
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
import type { SelfTestCheck, SelfTestResult, ShellState } from "../shared/ipc";

const execFileAsync = promisify(execFile);
const hasSingleInstanceLock = app.requestSingleInstanceLock();

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

type PluginStateEntry = {
  id?: string;
  installed?: boolean;
  enabled?: boolean;
  installedVersion?: string;
  installPath?: string;
  message?: string;
  updatedAt?: string | null;
};

type PluginStateFile = {
  productName?: string;
  plugins?: Record<string, PluginStateEntry>;
  updatedAt?: string | null;
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
  return getDevelopmentRootCandidates()[0] ?? resolve(__dirname, "../..");
}

function uniquePaths(candidates: Array<string | null | undefined>) {
  const seen = new Set<string>();
  const resolvedPaths: string[] = [];

  for (const candidate of candidates) {
    if (!candidate) {
      continue;
    }

    const resolvedPath = resolve(candidate);
    const key = resolvedPath.toLowerCase();

    if (seen.has(key)) {
      continue;
    }

    seen.add(key);
    resolvedPaths.push(resolvedPath);
  }

  return resolvedPaths;
}

function getDevelopmentRootCandidates() {
  return uniquePaths([
    process.env.CPAD_REPO_ROOT,
    app.getAppPath(),
    resolve(__dirname, "../.."),
  ]);
}

function getPackagedRootCandidates() {
  if (!app.isPackaged) {
    return [];
  }

  const appPath = app.getAppPath();

  return uniquePaths([
    process.resourcesPath,
    resolve(process.resourcesPath, "app.asar.unpacked"),
    appPath,
    resolve(appPath, ".."),
    resolve(appPath, "..", "app.asar.unpacked"),
    resolve(__dirname, "../.."),
  ]);
}

function getRuntimeAssetRoots() {
  return app.isPackaged
    ? [...getPackagedRootCandidates(), ...getDevelopmentRootCandidates()]
    : [...getDevelopmentRootCandidates(), ...getPackagedRootCandidates()];
}

function isPackagedArchivePath(targetPath: string) {
  const normalizedPath = targetPath.replace(/\\/g, "/").toLowerCase();
  return (
    normalizedPath.includes("/app.asar/") ||
    normalizedPath.endsWith("/app.asar")
  );
}

function normalizeTimestamp(value: string | undefined) {
  if (!value || value.startsWith("0001-01-01")) {
    return null;
  }

  return value;
}

function resolveManagedInstallRoot() {
  if (process.env.CPAD_INSTALL_ROOT) {
    return resolve(process.env.CPAD_INSTALL_ROOT);
  }

  if (app.isPackaged) {
    return dirname(process.execPath);
  }

  return join(app.getPath("home"), "Cli Proxy API Desktop");
}

function createInstallLayout() {
  return buildInstallLayout(
    app.getPath("home"),
    resolveManagedInstallRoot(),
    app.isPackaged ? undefined : resolveRepoRoot(),
  );
}

function formatError(error: unknown) {
  return error instanceof Error ? error.message : String(error);
}

function isPathInside(basePath: string, targetPath: string) {
  const relativePath = relative(resolve(basePath), resolve(targetPath));
  return (
    relativePath.length > 0 &&
    !relativePath.startsWith("..") &&
    !isAbsolute(relativePath)
  );
}

async function readPluginStateFile(statePath: string): Promise<PluginStateFile> {
  try {
    const content = await readFile(statePath, "utf8");
    const parsed = JSON.parse(content) as PluginStateFile;

    return {
      productName: parsed.productName ?? PRODUCT_NAME,
      plugins: parsed.plugins ?? {},
      updatedAt: parsed.updatedAt ?? null,
    };
  } catch (error) {
    if ((error as NodeJS.ErrnoException).code === "ENOENT") {
      return {
        productName: PRODUCT_NAME,
        plugins: {},
        updatedAt: null,
      };
    }

    throw error;
  }
}

async function writePluginStateFile(statePath: string, state: PluginStateFile) {
  await mkdir(dirname(statePath), { recursive: true });
  await writeFile(statePath, `${JSON.stringify(state, null, 2)}\n`, "utf8");
}

async function resolveServiceExecutable(installRoot: string) {
  const explicitServiceBinary = process.env.CPAD_SERVICE_BINARY
    ? resolve(process.env.CPAD_SERVICE_BINARY)
    : null;
  const candidates = [
    join(installRoot, `${PRODUCT_NAME} Service.exe`),
    ...getRuntimeAssetRoots().flatMap((root) => [
      join(root, "bin", "cpad-service.exe"),
      join(root, "service", "bin", "cpad-service.exe"),
      join(root, "cpad-service.exe"),
      join(root, `${PRODUCT_NAME} Service.exe`),
    ]),
  ];

  for (const candidate of uniquePaths([explicitServiceBinary, ...candidates])) {
    // Windows cannot execute a bundled binary from inside app.asar.
    if (candidate !== explicitServiceBinary && isPackagedArchivePath(candidate)) {
      continue;
    }

    if (await pathExists(candidate)) {
      return candidate;
    }
  }

  return null;
}

async function resolveAppIcon() {
  const candidates = getRuntimeAssetRoots().flatMap((root) => [
    join(root, "ico", "ico-transparent.png"),
    join(root, "ico", "ico.png"),
  ]);

  for (const candidate of uniquePaths(candidates)) {
    if (await pathExists(candidate)) {
      return candidate;
    }
  }

  return undefined;
}

async function runServiceJsonCommand(installRoot: string, args: string[]) {
  const stdout = await runServiceCommand(installRoot, args);
  return JSON.parse(stdout);
}

async function runServiceCommand(installRoot: string, args: string[]) {
  const serviceExecutable = await resolveServiceExecutable(installRoot);
  const commandEnvironment = {
    ...process.env,
    CPAD_INSTALL_ROOT: installRoot,
  };

  if (serviceExecutable) {
    const { stdout } = await execFileAsync(serviceExecutable, args, {
      env: commandEnvironment,
      windowsHide: true,
      maxBuffer: 16 * 1024 * 1024,
    });
    return stdout;
  }

  if (app.isPackaged) {
    throw new Error(
      "Unable to locate the bundled service executable in packaged app resources.",
    );
  }

  const repoRoot = resolveRepoRoot();
  const serviceRoot = join(repoRoot, "service");
  const { stdout } = await execFileAsync(
    "go",
    ["-C", serviceRoot, "run", "./cmd/cpad-service", ...args],
    {
      cwd: repoRoot,
      env: commandEnvironment,
      windowsHide: true,
      maxBuffer: 16 * 1024 * 1024,
    },
  );

  return stdout;
}

async function createShellState(): Promise<ShellState> {
  const installLayout = createInstallLayout();
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
      launchArgs: hostSnapshot.codex.launchArgs ?? [],
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
  const installLayout = createInstallLayout();
  await runServiceJsonCommand(installLayout.installRoot, args);
  return createShellState();
}

async function mutateShellStateWithCommand(args: string[]) {
  const installLayout = createInstallLayout();
  await runServiceCommand(installLayout.installRoot, args);
  return createShellState();
}

async function installManagedService() {
  const installLayout = createInstallLayout();
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

async function uninstallManagedPlugin(id: string) {
  const pluginId = id.trim();
  if (!pluginId) {
    throw new Error("plugin uninstall requires a plugin id");
  }

  const shellState = await createShellState();
  const plugin = shellState.pluginMarket.plugins.find(
    (entry) => entry.id === pluginId,
  );
  if (!plugin) {
    throw new Error(`plugin not found: ${pluginId}`);
  }
  if (!plugin.installed) {
    throw new Error(`plugin is not installed yet: ${pluginId}`);
  }

  const installLayout = createInstallLayout();
  const pluginsRoot = resolve(installLayout.directories.plugins);
  const installPath = resolve(plugin.installPath || join(pluginsRoot, pluginId));

  if (!isPathInside(pluginsRoot, installPath)) {
    throw new Error(`refusing to uninstall outside managed plugins root: ${installPath}`);
  }

  await rm(installPath, { recursive: true, force: true });

  const updatedAt = new Date().toISOString();
  const pluginState = await readPluginStateFile(installLayout.files.pluginState);
  const nextPlugins = pluginState.plugins ?? {};

  nextPlugins[pluginId] = {
    ...nextPlugins[pluginId],
    id: pluginId,
    installed: false,
    enabled: false,
    installedVersion: "",
    installPath,
    message: `插件 ${pluginId} 已卸载。`,
    updatedAt,
  };

  await writePluginStateFile(installLayout.files.pluginState, {
    productName: pluginState.productName || PRODUCT_NAME,
    plugins: nextPlugins,
    updatedAt,
  });

  return createShellState();
}

async function probeHttpEndpoint(
  id: string,
  label: string,
  targetUrl: string,
): Promise<SelfTestCheck> {
  try {
    const response = await fetch(targetUrl);
    return {
      id,
      label,
      ok: response.ok,
      detail: `HTTP ${response.status} ${targetUrl}`,
    };
  } catch (error) {
    return {
      id,
      label,
      ok: false,
      detail: `${targetUrl} ${formatError(error)}`,
    };
  }
}

async function runStandaloneCodexSelfTest(
  shellState: ShellState,
): Promise<SelfTestCheck> {
  const codexExecutable =
    shellState.codex.globalPath ||
    (shellState.codex.mode === "official" && shellState.codex.targetExists
      ? shellState.codex.targetPath
      : "");

  if (!codexExecutable) {
    return {
      id: "codex-self-test",
      label: "独立 Codex exec 自检",
      ok: false,
      detail: "未找到可独立执行的本机 Codex CLI。",
    };
  }

  const outputPath = join(
    app.getPath("temp"),
    `cpad-codex-selftest-${Date.now()}.txt`,
  );

  try {
    const { stdout, stderr } = await execFileAsync(
      codexExecutable,
      [
        "exec",
        "--skip-git-repo-check",
        "--dangerously-bypass-approvals-and-sandbox",
        "--output-last-message",
        outputPath,
        "Reply with exactly CPAD_Codex_OK and nothing else.",
      ],
      {
        cwd: resolveRepoRoot(),
        shell:
          codexExecutable.toLowerCase().endsWith(".cmd") ||
          codexExecutable.toLowerCase().endsWith(".ps1"),
        windowsHide: true,
        maxBuffer: 16 * 1024 * 1024,
      },
    );

    const fileOutput = await readFile(outputPath, "utf8").catch(() => "");
    const combinedOutput = [fileOutput, stdout, stderr]
      .map((value) => value.trim())
      .filter((value) => value.length > 0)
      .join("\n");

    return {
      id: "codex-self-test",
      label: "独立 Codex exec 自检",
      ok: combinedOutput.includes("CPAD_Codex_OK"),
      detail: combinedOutput.includes("CPAD_Codex_OK")
        ? `通过 ${codexExecutable}`
        : `输出异常：${combinedOutput || "未获得输出"}`,
    };
  } catch (error) {
    return {
      id: "codex-self-test",
      label: "独立 Codex exec 自检",
      ok: false,
      detail: `${codexExecutable} ${formatError(error)}`,
    };
  }
}

async function runSelfTest(): Promise<SelfTestResult> {
  const startedAt = new Date().toISOString();
  const checks: SelfTestCheck[] = [];

  let shellState = await createShellState();
  const officialCoreBaseline =
    shellState.updateCenter.sources.find(
      (source) => source.id === "official-core-baseline",
    ) ?? null;

  checks.push({
    id: "official-backend-source",
    label: "受控后端源根使用官方完整基线",
    ok: shellState.cpaRuntime.sourceRoot.toLowerCase().endsWith("\\cliproxyapi"),
    detail: shellState.cpaRuntime.sourceRoot,
  });

  checks.push({
    id: "official-core-sync",
    label: "官方主程序基线已同步",
    ok: Boolean(
      officialCoreBaseline?.available &&
        officialCoreBaseline.currentRef &&
        officialCoreBaseline.currentRef === officialCoreBaseline.latestRef,
    ),
    detail: officialCoreBaseline?.message || "未读取到官方主程序基线状态。",
  });

  if (!shellState.cpaRuntime.running) {
    try {
      shellState = await mutateShellState(["cpa-runtime", "start"]);
      checks.push({
        id: "runtime-start",
        label: "官方后端可由桌面受控启动",
        ok: shellState.cpaRuntime.running,
        detail: shellState.cpaRuntime.message,
      });
    } catch (error) {
      checks.push({
        id: "runtime-start",
        label: "官方后端可由桌面受控启动",
        ok: false,
        detail: formatError(error),
      });
      shellState = await createShellState().catch(() => shellState);
    }
  } else {
    checks.push({
      id: "runtime-start",
      label: "官方后端可由桌面受控启动",
      ok: true,
      detail: `已在运行，pid=${shellState.cpaRuntime.pid ?? "unknown"}`,
    });
  }

  checks.push(
    await probeHttpEndpoint(
      "healthz",
      "后端健康检查可达",
      shellState.cpaRuntime.configInsight.healthUrl,
    ),
  );
  checks.push(
    await probeHttpEndpoint(
      "management-page",
      "官方管理页接口可达",
      shellState.cpaRuntime.configInsight.managementUrl,
    ),
  );
  checks.push(
    await probeHttpEndpoint(
      "usage-api",
      "usage 接口可达",
      shellState.cpaRuntime.configInsight.usageUrl,
    ),
  );
  checks.push(await runStandaloneCodexSelfTest(shellState));

  const passedCount = checks.filter((check) => check.ok).length;
  return {
    startedAt,
    completedAt: new Date().toISOString(),
    success: checks.every((check) => check.ok),
    summary: `${passedCount}/${checks.length} 项通过`,
    checks,
  };
}

function focusPrimaryWindow() {
  const [browserWindow] = BrowserWindow.getAllWindows();
  if (!browserWindow) {
    return;
  }

  if (browserWindow.isMinimized()) {
    browserWindow.restore();
  }

  if (!browserWindow.isVisible()) {
    browserWindow.show();
  }

  browserWindow.focus();
}

function createWindow(): void {
  void (async () => {
    const browserWindow = new BrowserWindow({
      width: 1520,
      height: 980,
      minWidth: 1180,
      minHeight: 780,
      show: false,
      autoHideMenuBar: true,
      backgroundColor: "#efe4d3",
      title: PRODUCT_NAME,
      icon: await resolveAppIcon(),
      webPreferences: {
        preload: join(__dirname, "../preload/index.js"),
        sandbox: false,
      },
    });

    browserWindow.once("ready-to-show", () => {
      browserWindow.show();
    });
    browserWindow.removeMenu();
    browserWindow.setMenuBarVisibility(false);

    if (process.env.ELECTRON_RENDERER_URL) {
      await browserWindow.loadURL(process.env.ELECTRON_RENDERER_URL);
    } else {
      await browserWindow.loadFile(join(__dirname, "../renderer/index.html"));
    }
  })();
}

if (!hasSingleInstanceLock) {
  app.quit();
} else {
  app.on("second-instance", () => {
    focusPrimaryWindow();
  });

  app.whenReady().then(() => {
    app.setAppUserModelId("BlackblockInc.CPAD");

    ipcMain.handle("cpad:get-shell-state", async () => createShellState());
    ipcMain.handle("cpad:open-path", async (_event, targetPath: string) =>
      shell.openPath(targetPath),
    );
    ipcMain.handle("cpad:open-url", async (_event, targetUrl: string) =>
      shell.openExternal(targetUrl),
    );
    ipcMain.handle("cpad:run-self-test", async () => runSelfTest());
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
    ipcMain.handle("cpad:uninstall-plugin", async (_event, id: string) =>
      uninstallManagedPlugin(id),
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
}
