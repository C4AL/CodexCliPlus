import { startTransition, useEffect, useState } from "react";

import type { PluginRuntimeState } from "../../shared/product";
import type { ShellState } from "../../shared/ipc";

const OFFICIAL_CODEX_WINDOWS_GUIDE_URL = "https://developers.openai.com/codex/windows";

function pathList(paths: Record<string, string>) {
  return Object.entries(paths);
}

function formatTimestamp(value: string | null) {
  if (!value) {
    return "尚未生成";
  }

  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) {
    return value;
  }

  return new Intl.DateTimeFormat("zh-CN", {
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
  }).format(parsed);
}

function formatPid(value: number | null) {
  return value === null ? "未运行" : String(value);
}

function formatBool(value: boolean) {
  return value ? "true" : "false";
}

function pluginSummary(plugin: PluginRuntimeState) {
  if (!plugin.installed) {
    return "未安装";
  }
  if (!plugin.enabled) {
    return "已安装，已禁用";
  }
  if (plugin.needsUpdate) {
    return "已安装，可更新";
  }
  return "已安装，已启用";
}

function buildReleaseChecks(state: ShellState) {
  return [
    {
      label: "服务宿主已安装",
      ok: state.serviceManager.installed,
      detail: state.serviceManager.installed
        ? `当前状态 ${state.serviceManager.state}`
        : "Windows 服务尚未安装",
    },
    {
      label: "CPA Runtime 已构建",
      ok: state.cpaRuntime.binaryExists,
      detail: state.cpaRuntime.binaryExists
        ? state.cpaRuntime.managedBinary
        : "受控运行时二进制尚未生成",
    },
    {
      label: "CPA Runtime 正在运行",
      ok: state.cpaRuntime.running,
      detail: state.cpaRuntime.running
        ? `pid=${formatPid(state.cpaRuntime.pid)}`
        : state.cpaRuntime.message,
    },
    {
      label: "CPA Runtime 健康检查通过",
      ok: state.cpaRuntime.healthCheck.healthy,
      detail: state.cpaRuntime.healthCheck.message,
    },
    {
      label: "管理入口已启用",
      ok: state.cpaRuntime.configInsight.managementEnabled,
      detail: state.cpaRuntime.configInsight.managementEnabled
        ? "remote-management.secret-key 已配置"
        : "管理密钥为空，/v0/management 将返回 404",
    },
    {
      label: "Codex App Server 根代理已启用",
      ok: state.cpaRuntime.configInsight.codexAppServerProxyEnabled,
      detail: state.cpaRuntime.configInsight.codexAppServerProxyEnabled
        ? state.cpaRuntime.configInsight.codexRemoteUrl
        : "codex-app-server-proxy 当前未开启",
    },
    {
      label: "当前 Codex 模式可实际启动",
      ok: state.codex.launchReady,
      detail: state.codex.launchReady
        ? state.codex.launchMessage
        : state.codex.launchMessage || `当前模式 ${state.codex.mode} 尚未达到可启动条件`,
    },
  ];
}

export function App() {
  const [state, setState] = useState<ShellState | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [busyAction, setBusyAction] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const loadState = () => {
    setLoading(true);
    setError(null);

    return window.cpad
      .getShellState()
      .then((shellState) => {
        startTransition(() => {
          setState(shellState);
        });
      })
      .catch((caughtError: unknown) => {
        setError(
          caughtError instanceof Error
            ? caughtError.message
            : String(caughtError),
        );
      })
      .finally(() => {
        setLoading(false);
      });
  };

  const runAction = async (
    label: string,
    action: () => Promise<ShellState>,
  ) => {
    setBusyAction(label);
    setNotice(null);
    setError(null);

    try {
      const nextState = await action();
      startTransition(() => {
        setState(nextState);
      });
      setNotice(`${label} 已完成。`);
    } catch (caughtError) {
      setError(
        caughtError instanceof Error
          ? caughtError.message
          : String(caughtError),
      );
    } finally {
      setBusyAction(null);
    }
  };

  useEffect(() => {
    void loadState();
  }, []);

  if (error && !state) {
    return (
      <main className="app-shell">
        <section className="hero-card hero-card--error">
          <p className="eyebrow">启动失败</p>
          <h1>桌面壳读取宿主状态失败</h1>
          <p>{error}</p>
        </section>
      </main>
    );
  }

  if (!state || loading) {
    return (
      <main className="app-shell">
        <section className="hero-card">
          <p className="eyebrow">CPAD</p>
          <h1>正在读取受控状态</h1>
          <p>加载服务宿主、CPA Runtime、模式切换、更新中心与插件市场状态。</p>
        </section>
      </main>
    );
  }

  const releaseChecks = buildReleaseChecks(state);

  return (
    <main className="app-shell">
      <section className="hero-card">
        <div className="hero-card__topline">
          <p className="eyebrow">
            {state.productShortName} / {state.milestone}
          </p>
          <div className="action-row">
            <button
              className="ghost-button"
              disabled={busyAction !== null}
              onClick={() => void loadState()}
              type="button"
            >
              刷新总状态
            </button>
            <button
              className="ghost-button"
              disabled={busyAction !== null}
              onClick={() =>
                void window.cpad.openPath(state.installLayout.installRoot)
              }
              type="button"
            >
              打开安装目录
            </button>
          </div>
        </div>
        <div className="hero-card__headline">
          <div>
            <h1>{state.productShortName}</h1>
            <p className="hero-card__tagline">{state.tagline}</p>
            <div className="status-meta">
              <span>服务模式: {state.service.mode}</span>
              <span>服务阶段: {state.service.state}</span>
              <span>更新时间: {formatTimestamp(state.service.updatedAt)}</span>
            </div>
          </div>
          <div className="status-badge">
            <span>当前里程碑</span>
            <strong>{state.milestone}</strong>
          </div>
        </div>
        <div className="hero-card__footer">
          <p>版本 {state.version}</p>
          <p>{notice ?? state.service.message ?? "等待宿主写入更多状态。"}</p>
        </div>
        {error ? <p className="inline-error">{error}</p> : null}
      </section>

      <section className="overview-grid">
        <article className="panel panel--warm">
          <div className="panel__header">
            <p className="eyebrow">产品边界</p>
            <h2>当前桌面端职责面</h2>
          </div>
          <div className="chip-grid">
            {state.primarySurfaces.map((item) => (
              <span className="chip" key={item}>
                {item}
              </span>
            ))}
          </div>
        </article>

        <article className="panel panel--dark">
          <div className="panel__header">
            <p className="eyebrow">安装布局</p>
            <h2>{state.installLayout.installRoot}</h2>
          </div>
          <div className="path-grid">
            {pathList(state.installLayout.directories).map(
              ([name, targetPath]) => (
                <div className="path-row" key={name}>
                  <span>{name}</span>
                  <code>{targetPath}</code>
                </div>
              ),
            )}
            {pathList(state.installLayout.files).map(([name, targetPath]) => (
              <div className="path-row path-row--file" key={name}>
                <span>{name}</span>
                <code>{targetPath}</code>
              </div>
            ))}
          </div>
        </article>
      </section>

      <section className="process-strip">
        {state.processModel.map((processCard) => (
          <article className="process-card" key={processCard.title}>
            <p className="eyebrow">{processCard.title}</p>
            <h3>{processCard.summary}</h3>
            <ul>
              {processCard.responsibilities.map((responsibility) => (
                <li key={responsibility}>{responsibility}</li>
              ))}
            </ul>
          </article>
        ))}
      </section>

      <section className="overview-grid overview-grid--bottom">
        <article className="panel">
          <div className="panel__header">
            <p className="eyebrow">首版自检</p>
            <h2>发布前可用性检查</h2>
          </div>
          <p className="support-copy">
            这组检查直接反映当前环境是否接近“可装、可跑、可控、可诊断”。
          </p>
          <ul className="check-list">
            {releaseChecks.map((item) => (
              <li key={item.label}>
                <strong>{item.ok ? "OK" : "待补"}</strong>
                {" "}
                {item.label}
                {"："}
                {item.detail}
              </li>
            ))}
          </ul>
        </article>

        <article className="panel">
          <div className="panel__header">
            <p className="eyebrow">服务交付</p>
            <h2>Windows 服务管理</h2>
          </div>
          <p className="support-copy">
            安装、启动、停止和卸载正式服务宿主。涉及服务管理器写入时通常需要管理员权限。
          </p>
          <div className="action-row action-row--dense">
            <button
              className="ghost-button"
              disabled={busyAction !== null || state.serviceManager.installed}
              onClick={() =>
                void runAction("安装服务", () => window.cpad.installService())
              }
              type="button"
            >
              安装服务
            </button>
            <button
              className="ghost-button"
              disabled={
                busyAction !== null ||
                !state.serviceManager.installed ||
                state.serviceManager.state === "running"
              }
              onClick={() =>
                void runAction("启动服务", () => window.cpad.startService())
              }
              type="button"
            >
              启动服务
            </button>
            <button
              className="ghost-button"
              disabled={
                busyAction !== null ||
                !state.serviceManager.installed ||
                state.serviceManager.state !== "running"
              }
              onClick={() =>
                void runAction("停止服务", () => window.cpad.stopService())
              }
              type="button"
            >
              停止服务
            </button>
            <button
              className="ghost-button"
              disabled={busyAction !== null || !state.serviceManager.installed}
              onClick={() =>
                void runAction("卸载服务", () => window.cpad.removeService())
              }
              type="button"
            >
              卸载服务
            </button>
            <button
              className="ghost-button"
              disabled={busyAction !== null || !state.service.hostBinary}
              onClick={() => void window.cpad.openPath(state.service.hostBinary)}
              type="button"
            >
              打开服务二进制
            </button>
          </div>
          <div className="path-grid">
            <div className="path-row">
              <span>installed</span>
              <code>{formatBool(state.serviceManager.installed)}</code>
            </div>
            <div className="path-row">
              <span>state</span>
              <code>{state.serviceManager.state}</code>
            </div>
            <div className="path-row">
              <span>startType</span>
              <code>{state.serviceManager.startType || "未安装"}</code>
            </div>
            <div className="path-row">
              <span>binaryPath</span>
              <code>{state.serviceManager.binaryPath || state.service.hostBinary}</code>
            </div>
          </div>
        </article>

        <article className="panel">
          <div className="panel__header">
            <p className="eyebrow">模式切换</p>
            <h2>Codex 受控模式</h2>
          </div>
          <p className="support-copy">{state.codex.message}</p>
          <div className="action-row action-row--dense">
            <button
              className="ghost-button"
              disabled={busyAction !== null || state.codex.mode === "official"}
              onClick={() =>
                void runAction("切换到官方模式", () =>
                  window.cpad.setCodexMode("official"),
                )
              }
              type="button"
            >
              切到官方
            </button>
            <button
              className="ghost-button"
              disabled={busyAction !== null || state.codex.mode === "cpa"}
              onClick={() =>
                void runAction("切换到 CPA 模式", () =>
                  window.cpad.setCodexMode("cpa"),
                )
              }
              type="button"
            >
              切到 CPA
            </button>
            <button
              className="ghost-button"
              disabled={busyAction !== null}
              onClick={() => void window.cpad.openPath(state.codex.modeFile)}
              type="button"
            >
              打开模式文件
            </button>
            <button
              className="ghost-button"
              disabled={busyAction !== null}
              onClick={() => void window.cpad.openUrl(OFFICIAL_CODEX_WINDOWS_GUIDE_URL)}
              type="button"
            >
              官方 Windows 指南
            </button>
          </div>
          <div className="path-grid">
            <div className="path-row">
              <span>currentMode</span>
              <code>{state.codex.mode}</code>
            </div>
            <div className="path-row">
              <span>shimPath</span>
              <code>{state.codex.shimPath}</code>
            </div>
            <div className="path-row">
              <span>targetPath</span>
              <code>{state.codex.targetPath}</code>
            </div>
            <div className="path-row">
              <span>targetExists</span>
              <code>{state.codex.targetExists ? "true" : "false"}</code>
            </div>
            <div className="path-row">
              <span>launchReady</span>
              <code>{formatBool(state.codex.launchReady)}</code>
            </div>
            <div className="path-row">
              <span>launchArgs</span>
              <code>
                {state.codex.launchArgs.length > 0
                  ? state.codex.launchArgs.join(" ")
                  : "(none)"}
              </code>
            </div>
            <div className="path-row">
              <span>launchMessage</span>
              <code>{state.codex.launchMessage}</code>
            </div>
            <div className="path-row">
              <span>updatedAt</span>
              <code>{formatTimestamp(state.codex.updatedAt)}</code>
            </div>
          </div>
          {state.codex.launchArgs.length > 0 ? (
            <code className="command-line">
              {`${state.codex.targetPath} ${state.codex.launchArgs.join(" ")}`}
            </code>
          ) : null}
          {state.codex.mode === "official" && !state.codex.targetExists ? (
            <code className="command-line">
              {"# OpenAI 当前官方 Windows 指南推荐在 WSL2 中安装 Codex CLI\n"}
              {"wsl --install\n"}
              {"wsl\n"}
              {"npm i -g @openai/codex\n"}
              {"codex"}
            </code>
          ) : null}
          {state.codex.mode === "cpa" && !state.codex.launchReady ? (
            <code className="command-line">
              {"# 在 CPA Runtime 配置中启用 Codex App Server 根代理\n"}
              {"codex-app-server-proxy:\n"}
              {"  enable: true\n"}
              {"  restrict-to-localhost: true\n"}
              {"  codex-bin: \"codex\"\n"}
              {"  use-pool-plan-type: true"}
            </code>
          ) : null}
        </article>

        <article className="panel">
          <div className="panel__header">
            <p className="eyebrow">后端接管</p>
            <h2>CPA Runtime</h2>
          </div>
          <p className="support-copy">{state.cpaRuntime.message}</p>
          <div className="action-row action-row--dense">
            <button
              className="ghost-button"
              disabled={busyAction !== null || !state.cpaRuntime.sourceExists}
              onClick={() =>
                void runAction("构建 CPA Runtime", () =>
                  window.cpad.buildCpaRuntime(),
                )
              }
              type="button"
            >
              受控构建
            </button>
            <button
              className="ghost-button"
              disabled={busyAction !== null || state.cpaRuntime.running}
              onClick={() =>
                void runAction("启动 CPA Runtime", () =>
                  window.cpad.startCpaRuntime(),
                )
              }
              type="button"
            >
              启动 Runtime
            </button>
            <button
              className="ghost-button"
              disabled={busyAction !== null || !state.cpaRuntime.running}
              onClick={() =>
                void runAction("停止 CPA Runtime", () =>
                  window.cpad.stopCpaRuntime(),
                )
              }
              type="button"
            >
              停止 Runtime
            </button>
            <button
              className="ghost-button"
              disabled={busyAction !== null}
              onClick={() =>
                void window.cpad.openUrl(state.cpaRuntime.configInsight.healthUrl)
              }
              type="button"
            >
              打开 healthz
            </button>
            <button
              className="ghost-button"
              disabled={
                busyAction !== null ||
                !state.cpaRuntime.configInsight.controlPanelEnabled
              }
              onClick={() =>
                void window.cpad.openUrl(
                  state.cpaRuntime.configInsight.managementUrl,
                )
              }
              type="button"
            >
              打开管理页
            </button>
            <button
              className="ghost-button"
              disabled={busyAction !== null}
              onClick={() =>
                void window.cpad.openUrl(state.cpaRuntime.configInsight.usageUrl)
              }
              type="button"
            >
              打开 usage
            </button>
            <button
              className="ghost-button"
              disabled={busyAction !== null}
              onClick={() => void window.cpad.openPath(state.cpaRuntime.configPath)}
              type="button"
            >
              打开配置
            </button>
            <button
              className="ghost-button"
              disabled={busyAction !== null}
              onClick={() => void window.cpad.openPath(state.cpaRuntime.logPath)}
              type="button"
            >
              打开日志
            </button>
          </div>
          <div className="path-grid">
            <div className="path-row">
              <span>phase</span>
              <code>{state.cpaRuntime.phase}</code>
            </div>
            <div className="path-row">
              <span>running</span>
              <code>{formatBool(state.cpaRuntime.running)}</code>
            </div>
            <div className="path-row">
              <span>pid</span>
              <code>{formatPid(state.cpaRuntime.pid)}</code>
            </div>
            <div className="path-row">
              <span>sourceExists</span>
              <code>{formatBool(state.cpaRuntime.sourceExists)}</code>
            </div>
            <div className="path-row">
              <span>binaryExists</span>
              <code>{formatBool(state.cpaRuntime.binaryExists)}</code>
            </div>
            <div className="path-row">
              <span>sourceRoot</span>
              <code>{state.cpaRuntime.sourceRoot}</code>
            </div>
            <div className="path-row">
              <span>managedBinary</span>
              <code>{state.cpaRuntime.managedBinary}</code>
            </div>
            <div className="path-row">
              <span>configPath</span>
              <code>{state.cpaRuntime.configPath}</code>
            </div>
            <div className="path-row">
              <span>logPath</span>
              <code>{state.cpaRuntime.logPath}</code>
            </div>
            <div className="path-row">
              <span>updatedAt</span>
              <code>{formatTimestamp(state.cpaRuntime.updatedAt)}</code>
            </div>
          </div>
          <div className="inline-status">
            <span>host: {state.cpaRuntime.configInsight.host || "0.0.0.0 / all"}</span>
            <span>port: {state.cpaRuntime.configInsight.port || "未解析"}</span>
            <span>tls: {formatBool(state.cpaRuntime.configInsight.tlsEnabled)}</span>
            <span>
              managementEnabled:{" "}
              {formatBool(state.cpaRuntime.configInsight.managementEnabled)}
            </span>
            <span>
              controlPanelEnabled:{" "}
              {formatBool(state.cpaRuntime.configInsight.controlPanelEnabled)}
            </span>
            <span>
              rootProxyEnabled:{" "}
              {formatBool(
                state.cpaRuntime.configInsight.codexAppServerProxyEnabled,
              )}
            </span>
            <span>
              healthz:{" "}
              {state.cpaRuntime.healthCheck.healthy
                ? "pass"
                : state.cpaRuntime.healthCheck.checked
                  ? `fail(${state.cpaRuntime.healthCheck.statusCode || "n/a"})`
                  : "not-checked"}
            </span>
          </div>
          <div className="path-grid path-grid--compact">
            <div className="path-row">
              <span>baseUrl</span>
              <code>{state.cpaRuntime.configInsight.baseUrl || "未解析"}</code>
            </div>
            <div className="path-row">
              <span>healthUrl</span>
              <code>{state.cpaRuntime.configInsight.healthUrl || "未解析"}</code>
            </div>
            <div className="path-row">
              <span>managementUrl</span>
              <code>
                {state.cpaRuntime.configInsight.managementUrl || "未解析"}
              </code>
            </div>
            <div className="path-row">
              <span>usageUrl</span>
              <code>{state.cpaRuntime.configInsight.usageUrl || "未解析"}</code>
            </div>
            <div className="path-row">
              <span>panelRepository</span>
              <code>
                {state.cpaRuntime.configInsight.panelRepository || "未配置"}
              </code>
            </div>
            <div className="path-row">
              <span>healthMessage</span>
              <code>{state.cpaRuntime.healthCheck.message || "未生成"}</code>
            </div>
            <div className="path-row">
              <span>healthCheckedAt</span>
              <code>{formatTimestamp(state.cpaRuntime.healthCheck.checkedAt)}</code>
            </div>
          </div>
          {state.cpaRuntime.configInsight.codexAppServerProxyEnabled ? (
            <code className="command-line">
              {`codex --remote ${state.cpaRuntime.configInsight.codexRemoteUrl}`}
            </code>
          ) : null}
        </article>

        <article className="panel">
          <div className="panel__header">
            <p className="eyebrow">更新中心</p>
            <h2>受控更新来源</h2>
          </div>
          <div className="action-row action-row--dense">
            <button
              className="ghost-button"
              disabled={busyAction !== null}
              onClick={() =>
                void runAction("刷新更新中心", () => window.cpad.checkUpdates())
              }
              type="button"
            >
              刷新更新来源
            </button>
            <button
              className="ghost-button"
              disabled={busyAction !== null}
              onClick={() =>
                void runAction("同步官方双基线", () =>
                  window.cpad.syncOfficialBaselines(),
                )
              }
              type="button"
            >
              同步官方双基线
            </button>
            <button
              className="ghost-button"
              disabled={busyAction !== null}
              onClick={() =>
                void window.cpad.openPath(state.updateCenter.stateFile)
              }
              type="button"
            >
              打开状态文件
            </button>
          </div>
          <div className="source-list">
            {state.updateCenter.sources.map((source) => (
              <article className="source-card" key={source.id}>
                <div className="source-card__top">
                  <strong>{source.name}</strong>
                  <span>{source.kind}</span>
                </div>
                <p>{source.message}</p>
                <div className="inline-status">
                  <span>current: {source.currentRef || "未建立"}</span>
                  <span>latest: {source.latestRef || "未知"}</span>
                  <span>dirty: {source.dirty ? "yes" : "no"}</span>
                  <span>available: {source.available ? "yes" : "no"}</span>
                </div>
                <code>{source.source}</code>
              </article>
            ))}
          </div>
        </article>
      </section>

      <section className="panel panel--market">
        <div className="panel__header">
          <div>
            <p className="eyebrow">插件市场</p>
            <h2>自有插件安装、更新、启停与诊断</h2>
          </div>
          <div className="action-row action-row--dense">
            <button
              className="ghost-button"
              disabled={busyAction !== null}
              onClick={() =>
                void runAction("刷新插件市场", () =>
                  window.cpad.refreshPluginMarket(),
                )
              }
              type="button"
            >
              刷新插件清单
            </button>
            <button
              className="ghost-button"
              disabled={busyAction !== null}
              onClick={() =>
                void window.cpad.openPath(state.pluginMarket.pluginsDir)
              }
              type="button"
            >
              打开安装插件目录
            </button>
            <button
              className="ghost-button"
              disabled={busyAction !== null}
              onClick={() =>
                void window.cpad.openPath(state.pluginMarket.sourceRoot)
              }
              type="button"
            >
              打开插件源目录
            </button>
          </div>
        </div>
        <p className="support-copy">
          当前插件源目录：<code>{state.pluginMarket.sourceRoot}</code>
          {"；"}最近刷新：{formatTimestamp(state.pluginMarket.updatedAt)}
        </p>
        {state.pluginMarket.plugins.length === 0 ? (
          <div className="empty-state">
            <p>
              当前还没有已加载的插件清单。先刷新插件市场，再执行安装或诊断。
            </p>
          </div>
        ) : (
          <div className="plugin-grid">
            {state.pluginMarket.plugins.map((plugin) => (
              <article className="plugin-card" key={plugin.id}>
                <div className="plugin-card__header">
                  <div>
                    <p className="eyebrow">{plugin.id}</p>
                    <h3>{plugin.name}</h3>
                  </div>
                  <span className="plugin-version">{plugin.version}</span>
                </div>
                <p className="plugin-description">{plugin.description}</p>
                <div className="inline-status">
                  <span>{pluginSummary(plugin)}</span>
                  <span>源目录: {plugin.sourceExists ? "ok" : "missing"}</span>
                  <span>README: {plugin.readmeExists ? "yes" : "no"}</span>
                </div>
                <div className="action-row action-row--dense">
                  <button
                    className="ghost-button"
                    disabled={busyAction !== null || !plugin.sourceExists}
                    onClick={() =>
                      void runAction(
                        plugin.installed
                          ? `更新 ${plugin.id}`
                          : `安装 ${plugin.id}`,
                        () =>
                          plugin.installed
                            ? window.cpad.updatePlugin(plugin.id)
                            : window.cpad.installPlugin(plugin.id),
                      )
                    }
                    type="button"
                  >
                    {plugin.installed ? "更新" : "安装"}
                  </button>
                  <button
                    className="ghost-button"
                    disabled={
                      busyAction !== null || !plugin.installed || plugin.enabled
                    }
                    onClick={() =>
                      void runAction(`启用 ${plugin.id}`, () =>
                        window.cpad.enablePlugin(plugin.id),
                      )
                    }
                    type="button"
                  >
                    启用
                  </button>
                  <button
                    className="ghost-button"
                    disabled={
                      busyAction !== null ||
                      !plugin.installed ||
                      !plugin.enabled
                    }
                    onClick={() =>
                      void runAction(`禁用 ${plugin.id}`, () =>
                        window.cpad.disablePlugin(plugin.id),
                      )
                    }
                    type="button"
                  >
                    禁用
                  </button>
                  <button
                    className="ghost-button"
                    disabled={busyAction !== null}
                    onClick={() =>
                      void runAction(`诊断 ${plugin.id}`, () =>
                        window.cpad.diagnosePlugin(plugin.id),
                      )
                    }
                    type="button"
                  >
                    诊断
                  </button>
                </div>
                <div className="path-grid path-grid--compact">
                  <div className="path-row">
                    <span>sourcePath</span>
                    <code>{plugin.sourcePath}</code>
                  </div>
                  <div className="path-row">
                    <span>installPath</span>
                    <code>{plugin.installPath}</code>
                  </div>
                  <div className="path-row">
                    <span>installedVersion</span>
                    <code>{plugin.installedVersion || "尚未安装"}</code>
                  </div>
                  <div className="path-row">
                    <span>updatedAt</span>
                    <code>{formatTimestamp(plugin.updatedAt)}</code>
                  </div>
                </div>
                <p className="plugin-message">
                  {plugin.message || "等待插件动作写入状态。"}
                </p>
              </article>
            ))}
          </div>
        )}
      </section>

      <section className="overview-grid">
        <article className="panel">
          <div className="panel__header">
            <p className="eyebrow">日志与诊断</p>
            <h2>服务宿主日志预览</h2>
          </div>
          <div className="action-row action-row--dense">
            <button
              className="ghost-button"
              disabled={busyAction !== null}
              onClick={() =>
                void window.cpad.openPath(state.logs.serviceLogPath)
              }
              type="button"
            >
              打开服务日志
            </button>
          </div>
          <div className="log-preview">
            {state.logs.serviceTail.length === 0 ? (
              <p className="log-empty">当前还没有服务日志内容。</p>
            ) : (
              state.logs.serviceTail.map((line, index) => (
                <code className="log-line" key={`${index}-${line}`}>
                  {line}
                </code>
              ))
            )}
          </div>
        </article>

        <article className="panel">
          <div className="panel__header">
            <p className="eyebrow">日志与诊断</p>
            <h2>CPA Runtime 日志预览</h2>
          </div>
          <div className="action-row action-row--dense">
            <button
              className="ghost-button"
              disabled={busyAction !== null}
              onClick={() =>
                void window.cpad.openPath(state.logs.cpaRuntimeLogPath)
              }
              type="button"
            >
              打开 Runtime 日志
            </button>
          </div>
          <div className="log-preview">
            {state.logs.cpaRuntimeTail.length === 0 ? (
              <p className="log-empty">当前还没有 CPA Runtime 日志内容。</p>
            ) : (
              state.logs.cpaRuntimeTail.map((line, index) => (
                <code className="log-line" key={`${index}-${line}`}>
                  {line}
                </code>
              ))
            )}
          </div>
        </article>
      </section>
    </main>
  );
}
