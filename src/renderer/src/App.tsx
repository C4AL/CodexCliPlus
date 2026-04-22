import { startTransition, useEffect, useState } from "react";

import type { PluginRuntimeState } from "../../shared/product";
import type { ShellState } from "../../shared/ipc";

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
              <span>updatedAt</span>
              <code>{formatTimestamp(state.codex.updatedAt)}</code>
            </div>
          </div>
        </article>

        <article className="panel">
          <div className="panel__header">
            <p className="eyebrow">后端接管</p>
            <h2>CPA Runtime</h2>
          </div>
          <p className="support-copy">{state.cpaRuntime.message}</p>
          <div className="path-grid">
            <div className="path-row">
              <span>phase</span>
              <code>{state.cpaRuntime.phase}</code>
            </div>
            <div className="path-row">
              <span>running</span>
              <code>{state.cpaRuntime.running ? "true" : "false"}</code>
            </div>
            <div className="path-row">
              <span>pid</span>
              <code>{formatPid(state.cpaRuntime.pid)}</code>
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
          </div>
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
