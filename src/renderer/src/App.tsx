import { startTransition, useEffect, useState } from "react";

import type { SelfTestResult, ShellState } from "../../shared/ipc";
import type { PluginRuntimeState, UpdateSourceState } from "../../shared/product";

const OFFICIAL_CODEX_WINDOWS_GUIDE_URL =
  "https://developers.openai.com/codex/windows";
const OFFICIAL_CORE_REPOSITORY_URL =
  "https://github.com/router-for-me/CLIProxyAPI";
const OFFICIAL_PANEL_REPOSITORY_URL =
  "https://github.com/router-for-me/Cli-Proxy-API-Management-Center";

type ChipTone = "ok" | "warn" | "danger" | "neutral";
type SourceTone = "healthy" | "drift" | "missing";
type PluginTone = "live" | "idle" | "warning";

function formatTimestamp(value: string | null) {
  if (!value) {
    return "尚未记录";
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

function shortRef(value: string) {
  return value ? value.slice(0, 12) : "未记录";
}

function boolLabel(value: boolean) {
  return value ? "是" : "否";
}

function isOfficialBackendSource(state: ShellState) {
  const sourceRoot = state.cpaRuntime.sourceRoot.toLowerCase();
  return (
    sourceRoot.endsWith("\\official-backend") ||
    sourceRoot.endsWith("\\cliproxyapi")
  );
}

function sourceTone(source: UpdateSourceState | null): SourceTone {
  if (!source || !source.available || !source.currentRef) {
    return "missing";
  }
  if (source.latestRef && source.currentRef !== source.latestRef) {
    return "drift";
  }
  return "healthy";
}

function sourceToneLabel(source: UpdateSourceState | null) {
  switch (sourceTone(source)) {
    case "healthy":
      return "已对齐";
    case "drift":
      return "待同步";
    default:
      return "未就绪";
  }
}

function chipToneForRuntime(state: ShellState): ChipTone {
  if (!state.cpaRuntime.sourceExists || !isOfficialBackendSource(state)) {
    return "warn";
  }
  return state.cpaRuntime.running ? "ok" : "warn";
}

function chipToneForCodex(state: ShellState): ChipTone {
  if (!state.codex.globalExists) {
    return "danger";
  }
  return state.codex.mode === "official" ? "ok" : "warn";
}

function pluginSummary(plugin: PluginRuntimeState) {
  if (!plugin.installed) {
    return "未安装";
  }
  if (plugin.needsUpdate) {
    return "已安装，待更新";
  }
  if (!plugin.enabled) {
    return "已安装，未启用";
  }
  return "已安装，已启用";
}

function pluginTone(plugin: PluginRuntimeState): PluginTone {
  if (!plugin.installed || plugin.needsUpdate || !plugin.sourceExists) {
    return "warning";
  }
  if (!plugin.enabled) {
    return "idle";
  }
  return "live";
}

function pluginMessage(plugin: PluginRuntimeState) {
  if (plugin.message) {
    return plugin.message;
  }
  if (!plugin.sourceExists) {
    return "插件源码目录不可用，当前无法执行安装或更新。";
  }
  if (!plugin.installed) {
    return "插件已经出现在市场中，但尚未同步到受控插件目录。";
  }
  if (plugin.needsUpdate) {
    return "插件已安装，但仓库版本高于受控目录版本，可直接更新。";
  }
  if (!plugin.enabled) {
    return "插件文件已就位，当前被标记为禁用。";
  }
  return "插件已进入桌面内核管理，可直接诊断或卸载。";
}

function statusLabel(state: ShellState) {
  if (!state.cpaRuntime.sourceExists) {
    return "后端源码缺失";
  }
  if (!isOfficialBackendSource(state)) {
    return "后端未切换到官方基线";
  }
  if (!state.cpaRuntime.running) {
    return "后端未运行";
  }
  return "统一内核运行中";
}

function sortPlugins(a: PluginRuntimeState, b: PluginRuntimeState) {
  return (
    Number(b.installed) - Number(a.installed) ||
    Number(b.enabled) - Number(a.enabled) ||
    Number(b.needsUpdate) - Number(a.needsUpdate) ||
    a.name.localeCompare(b.name, "zh-CN")
  );
}

function isPluginBusy(busyAction: string | null, pluginId: string) {
  return busyAction === `plugin:${pluginId}`;
}

function StatusChip(props: {
  label: string;
  value: string;
  tone: ChipTone;
}) {
  return (
    <div className={`status-chip status-chip--${props.tone}`}>
      <span>{props.label}</span>
      <strong>{props.value}</strong>
    </div>
  );
}

function MetricCard(props: { label: string; value: string; helper?: string }) {
  return (
    <div className="metric-card">
      <span>{props.label}</span>
      <strong>{props.value}</strong>
      {props.helper ? <p>{props.helper}</p> : null}
    </div>
  );
}

function SourceCard(props: {
  title: string;
  summary: string;
  source: UpdateSourceState | null;
  actionLabel?: string;
  onAction?: () => void;
}) {
  const tone = sourceTone(props.source);

  return (
    <article className={`source-card source-card--${tone}`}>
      <div className="source-card__header">
        <div>
          <p className="section-label">{props.title}</p>
          <h3>{sourceToneLabel(props.source)}</h3>
        </div>
        {props.actionLabel && props.onAction ? (
          <button
            className="button button--ghost button--small"
            onClick={props.onAction}
            type="button"
          >
            {props.actionLabel}
          </button>
        ) : null}
      </div>
      <p className="source-card__summary">{props.summary}</p>
      <dl className="detail-list">
        <div>
          <dt>本地路径</dt>
          <dd>{props.source?.source ?? "未记录"}</dd>
        </div>
        <div>
          <dt>当前提交</dt>
          <dd>{shortRef(props.source?.currentRef ?? "")}</dd>
        </div>
        <div>
          <dt>最新提交</dt>
          <dd>{shortRef(props.source?.latestRef ?? "")}</dd>
        </div>
        <div>
          <dt>本地改动</dt>
          <dd>{boolLabel(props.source?.dirty ?? false)}</dd>
        </div>
      </dl>
      <p className="source-card__message">
        {props.source?.message ?? "尚未读取到源码快照状态。"}
      </p>
    </article>
  );
}

function LogPanel(props: { title: string; lines: string[]; empty: string }) {
  return (
    <div className="log-panel">
      <div className="log-panel__header">
        <h3>{props.title}</h3>
        <span>{props.lines.length} 条</span>
      </div>
      {props.lines.length === 0 ? (
        <p className="log-panel__empty">{props.empty}</p>
      ) : (
        <div className="log-panel__body">
          {props.lines.map((line, index) => (
            <code className="log-line" key={`${props.title}-${index}`}>
              {line}
            </code>
          ))}
        </div>
      )}
    </div>
  );
}

export function App() {
  const [state, setState] = useState<ShellState | null>(null);
  const [selfTest, setSelfTest] = useState<SelfTestResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [busyAction, setBusyAction] = useState<string | null>(null);
  const [busyToken, setBusyToken] = useState<string | null>(null);
  const [selfTesting, setSelfTesting] = useState(false);

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
    busyKey = label,
  ) => {
    setBusyAction(label);
    setBusyToken(busyKey);
    setNotice(null);
    setError(null);

    try {
      const nextState = await action();
      startTransition(() => {
        setState(nextState);
      });
      setNotice(`${label} 已完成`);
    } catch (caughtError) {
      setError(
        caughtError instanceof Error
          ? caughtError.message
          : String(caughtError),
      );
    } finally {
      setBusyAction(null);
      setBusyToken(null);
    }
  };

  const runPluginAction = (
    plugin: PluginRuntimeState,
    verb: string,
    action: () => Promise<ShellState>,
  ) => runAction(`${plugin.name}：${verb}`, action, `plugin:${plugin.id}`);

  const runSelfTest = async () => {
    setSelfTesting(true);
    setNotice(null);
    setError(null);

    try {
      const result = await window.cpad.runSelfTest();
      startTransition(() => {
        setSelfTest(result);
      });

      const shellState = await window.cpad.getShellState();
      startTransition(() => {
        setState(shellState);
      });

      setNotice(`全量自测完成：${result.summary}`);
    } catch (caughtError) {
      setError(
        caughtError instanceof Error
          ? caughtError.message
          : String(caughtError),
      );
    } finally {
      setSelfTesting(false);
    }
  };

  useEffect(() => {
    void loadState();
  }, []);

  if (!state) {
    if (error) {
      return (
        <main className="cpad-shell cpad-shell--error" data-cpad-view="error">
          <section className="hero">
            <p className="section-label">启动失败</p>
            <h1>桌面内核未能读取状态</h1>
            <p className="hero__copy">{error}</p>
          </section>
        </main>
      );
    }

    return (
      <main className="cpad-shell" data-cpad-ready="false">
        <section className="hero">
          <p className="section-label">CPAD Frontend Kernel</p>
          <h1>正在装载统一产品内核</h1>
          <p className="hero__copy">
            正在读取 CPA Runtime、Codex、自测能力和仓库内源码快照。
          </p>
        </section>
      </main>
    );
  }

  const officialCoreBaseline =
    state.updateCenter.sources.find(
      (source) => source.id === "official-core-baseline",
    ) ?? null;
  const officialPanelBaseline =
    state.updateCenter.sources.find(
      (source) => source.id === "official-panel-baseline",
    ) ?? null;
  const overlaySource =
    state.updateCenter.sources.find((source) => source.id === "cpa-source") ??
    null;
  const installedPlugins = state.pluginMarket.plugins.filter(
    (plugin) => plugin.installed,
  );
  const enabledPlugins = installedPlugins.filter((plugin) => plugin.enabled);
  const outdatedPlugins = installedPlugins.filter((plugin) => plugin.needsUpdate);
  const sortedPlugins = [...state.pluginMarket.plugins].sort(sortPlugins);
  const selfTestPassedCount =
    selfTest?.checks.filter((check) => check.ok).length ?? 0;
  const panelRepository = state.cpaRuntime.configInsight.panelRepository;
  const panelRepositoryLegacy = panelRepository.includes("CPA-UV");
  const actionsDisabled = busyToken !== null || selfTesting;

  return (
    <main className="cpad-shell" data-cpad-ready="true" data-cpad-view="ready">
      <section className="hero">
        <div className="hero__topline">
          <p className="section-label">CPAD Frontend Kernel</p>
          <button
            className="button button--ghost"
            disabled={actionsDisabled}
            onClick={() => void window.cpad.openPath(state.installLayout.installRoot)}
            type="button"
          >
            打开安装目录
          </button>
        </div>

        <div className="hero__headline">
          <div>
            <h1>{state.productShortName}</h1>
            <p className="hero__tagline">{state.tagline}</p>
            <p className="hero__copy">
              当前首页继续保持 CPAD 自己的桌面内核，不回退到旧 HTML/WebUI
              的直接搬运。插件市场、运行时、自测和源码基线现在都在同一层视图内收口。
            </p>
          </div>
          <div className="hero__watermark" aria-hidden="true">
            CPAD
          </div>
        </div>

        <div className="chip-row">
          <StatusChip
            label="CPA Runtime"
            tone={chipToneForRuntime(state)}
            value={state.cpaRuntime.running ? "运行中" : "未运行"}
          />
          <StatusChip
            label="Codex"
            tone={chipToneForCodex(state)}
            value={state.codex.globalExists ? "独立可用" : "未检测到"}
          />
          <StatusChip
            label="官方后端"
            tone={sourceTone(officialCoreBaseline) === "healthy" ? "ok" : "warn"}
            value={sourceToneLabel(officialCoreBaseline)}
          />
          <StatusChip
            label="插件市场"
            tone={state.pluginMarket.sourceExists ? "ok" : "warn"}
            value={`${enabledPlugins.length}/${state.pluginMarket.plugins.length} 已启用`}
          />
        </div>

        <div className="metric-grid">
          <MetricCard label="里程碑" value={state.milestone} />
          <MetricCard label="版本" value={state.version} />
          <MetricCard label="统一状态" value={statusLabel(state)} />
          <MetricCard
            label="最近自测"
            value={
              selfTest
                ? `${selfTestPassedCount}/${selfTest.checks.length}`
                : "未执行"
            }
            helper={selfTest ? formatTimestamp(selfTest.completedAt) : "尚未运行"}
          />
        </div>

        <div className="hero__actions">
          <button
            className="button button--primary"
            disabled={actionsDisabled}
            onClick={() => void loadState()}
            type="button"
          >
            {loading ? "刷新中..." : "刷新状态"}
          </button>
          <button
            className="button button--secondary"
            disabled={actionsDisabled}
            onClick={() =>
              void runAction("同步源码快照", () => window.cpad.syncOfficialBaselines())
            }
            type="button"
          >
            同步源码快照
          </button>
          <button
            className="button button--secondary"
            disabled={actionsDisabled}
            onClick={() =>
              void runAction("构建 CPA Runtime", () => window.cpad.buildCpaRuntime())
            }
            type="button"
          >
            构建后端
          </button>
          <button
            className="button button--secondary"
            disabled={actionsDisabled}
            onClick={() =>
              void runAction("启动 CPA Runtime", () => window.cpad.startCpaRuntime())
            }
            type="button"
          >
            启动后端
          </button>
          <button
            className="button button--danger"
            disabled={actionsDisabled}
            onClick={() =>
              void runAction("停止 CPA Runtime", () => window.cpad.stopCpaRuntime())
            }
            type="button"
          >
            停止后端
          </button>
          <button
            className="button button--primary"
            disabled={actionsDisabled}
            onClick={() => void runSelfTest()}
            type="button"
          >
            {selfTesting ? "自测中..." : "运行全量自测"}
          </button>
        </div>

        {busyAction ? (
          <p className="feedback feedback--busy">正在执行：{busyAction}</p>
        ) : null}
        {notice ? <p className="feedback feedback--notice">{notice}</p> : null}
        {error ? <p className="feedback feedback--error">{error}</p> : null}
      </section>

      <section className="panel-grid">
        <section className="panel">
          <div className="panel__header">
            <p className="section-label">统一产品视角</p>
            <h2>当前运行与安装反馈</h2>
          </div>

          <dl className="detail-list detail-list--wide">
            <div>
              <dt>安装目录</dt>
              <dd>{state.installLayout.installRoot}</dd>
            </div>
            <div>
              <dt>源码目录</dt>
              <dd>{state.installLayout.directories.sources}</dd>
            </div>
            <div>
              <dt>后端源码</dt>
              <dd>{state.cpaRuntime.sourceRoot}</dd>
            </div>
            <div>
              <dt>运行进程 PID</dt>
              <dd>{formatPid(state.cpaRuntime.pid)}</dd>
            </div>
            <div>
              <dt>健康检查</dt>
              <dd>{state.cpaRuntime.healthCheck.message}</dd>
            </div>
            <div>
              <dt>服务安装</dt>
              <dd>{state.serviceManager.installed ? "已安装" : "未安装"}</dd>
            </div>
          </dl>

          {state.service.message ? (
            <div
              className={`inline-note ${
                state.service.stale ? "inline-note--warn" : "inline-note--info"
              }`}
            >
              {state.service.message}
            </div>
          ) : null}

          {panelRepositoryLegacy ? (
            <div className="inline-note inline-note--warn">
              当前后端配置仍保留旧的 `panel-github-repository`：
              <code>{panelRepository}</code>。这说明运行链路虽然已经切到官方完整后端，
              但旧控制面板配置还没有清干净。
            </div>
          ) : null}

          {state.cpaRuntime.message ? (
            <p className="panel__copy panel__copy--compact">
              {state.cpaRuntime.message}
            </p>
          ) : null}

          <div className="button-row">
            <button
              className="button button--ghost button--small"
              onClick={() => void window.cpad.openPath(state.cpaRuntime.configPath)}
              type="button"
            >
              打开后端配置
            </button>
            <button
              className="button button--ghost button--small"
              onClick={() => void window.cpad.openPath(state.cpaRuntime.logPath)}
              type="button"
            >
              打开后端日志
            </button>
            <button
              className="button button--ghost button--small"
              onClick={() => void window.cpad.openPath(state.installLayout.directories.sources)}
              type="button"
            >
              打开统一源码目录
            </button>
          </div>
        </section>

        <section className="panel">
          <div className="panel__header">
            <p className="section-label">源码整合</p>
            <h2>三套源码快照已经进入当前仓库</h2>
          </div>

          <div className="source-grid">
            <SourceCard
              title="官方完整后端"
              summary="受控 CPA Runtime 已从当前仓库内的官方完整后端源码构建。"
              source={officialCoreBaseline}
              actionLabel="打开仓库"
              onAction={() => void window.cpad.openUrl(OFFICIAL_CORE_REPOSITORY_URL)}
            />
            <SourceCard
              title="官方管理中心"
              summary="当前仍是基线源码快照，后续会继续吸收到统一 CPAD 前端结构。"
              source={officialPanelBaseline}
              actionLabel="打开仓库"
              onAction={() => void window.cpad.openUrl(OFFICIAL_PANEL_REPOSITORY_URL)}
            />
            <SourceCard
              title="CPA-UV 覆盖层"
              summary="作为覆盖层与参照物保留，不能继续作为默认首页或默认后端。"
              source={overlaySource}
              actionLabel="打开目录"
              onAction={() =>
                void window.cpad.openPath(
                  state.installLayout.directories.cpaOverlaySource,
                )
              }
            />
          </div>
        </section>

        <section className="panel">
          <div className="panel__header">
            <p className="section-label">Codex 与自测</p>
            <h2>当前只做隔离验证，不直接接开发版后端</h2>
          </div>

          <dl className="detail-list">
            <div>
              <dt>模式</dt>
              <dd>{state.codex.mode}</dd>
            </div>
            <div>
              <dt>系统 Codex</dt>
              <dd>{state.codex.globalExists ? state.codex.globalPath : "未检测到"}</dd>
            </div>
            <div>
              <dt>受控目标</dt>
              <dd>{state.codex.targetPath}</dd>
            </div>
            <div>
              <dt>启动说明</dt>
              <dd>{state.codex.launchMessage}</dd>
            </div>
          </dl>

          <p className="panel__copy">
            当前策略是让 Codex 先以独立 CLI 方式完成全量自测，保证“像用户一样能跑测试”，
            再决定是否继续下沉到受控运行时里。
          </p>

          <div className="button-row">
            <button
              className="button button--ghost button--small"
              onClick={() => void window.cpad.openUrl(OFFICIAL_CODEX_WINDOWS_GUIDE_URL)}
              type="button"
            >
              查看官方 Codex Windows 指南
            </button>
          </div>

          {selfTest ? (
            <div className="check-panel">
              <div className="check-panel__header">
                <strong>{selfTest.summary}</strong>
                <span>{formatTimestamp(selfTest.completedAt)}</span>
              </div>
              <ul className="check-list">
                {selfTest.checks.map((check) => (
                  <li key={check.id} className={check.ok ? "check--ok" : "check--warn"}>
                    <span>{check.label}</span>
                    <code>{check.detail}</code>
                  </li>
                ))}
              </ul>
            </div>
          ) : (
            <p className="panel__copy">
              尚未执行全量自测。当前可通过上方按钮触发后端、页面、日志、Codex
              独立执行的联合检查。
            </p>
          )}
        </section>
      </section>

      <section className="panel panel--full">
        <div className="panel__header">
          <div>
            <p className="section-label">插件市场</p>
            <h2>插件状态与操作已经接入桌面内核</h2>
          </div>
          <div className="button-row button-row--tight">
            <button
              className="button button--primary button--small"
              disabled={actionsDisabled}
              onClick={() =>
                void runAction(
                  "刷新插件市场",
                  () => window.cpad.refreshPluginMarket(),
                  "plugin:refresh",
                )
              }
              type="button"
            >
              刷新插件市场
            </button>
            <button
              className="button button--ghost button--small"
              disabled={actionsDisabled}
              onClick={() => void window.cpad.openPath(state.pluginMarket.sourceRoot)}
              type="button"
            >
              打开插件源目录
            </button>
            <button
              className="button button--ghost button--small"
              disabled={actionsDisabled}
              onClick={() => void window.cpad.openPath(state.pluginMarket.pluginsDir)}
              type="button"
            >
              打开受控插件目录
            </button>
            <button
              className="button button--ghost button--small"
              disabled={actionsDisabled}
              onClick={() => void window.cpad.openPath(state.pluginMarket.statePath)}
              type="button"
            >
              打开插件状态文件
            </button>
          </div>
        </div>

        <p className="panel__copy">
          安装、卸载、启用、禁用、诊断和刷新都直接反馈到当前首页，不再让插件市场停留在只读展示。
        </p>

        <div className="metric-grid metric-grid--compact">
          <MetricCard label="市场总数" value={String(state.pluginMarket.plugins.length)} />
          <MetricCard label="已安装" value={String(installedPlugins.length)} />
          <MetricCard label="已启用" value={String(enabledPlugins.length)} />
          <MetricCard
            label="待更新"
            value={String(outdatedPlugins.length)}
            helper={formatTimestamp(state.pluginMarket.updatedAt)}
          />
        </div>

        {!state.pluginMarket.sourceExists ? (
          <div className="inline-note inline-note--warn">
            当前插件源目录不存在：<code>{state.pluginMarket.sourceRoot}</code>
          </div>
        ) : null}

        {sortedPlugins.length === 0 ? (
          <p className="panel__copy">
            插件市场当前没有可展示的插件。可以先执行“刷新插件市场”重建清单。
          </p>
        ) : (
          <div className="plugin-list">
            {sortedPlugins.map((plugin) => {
              const cardTone = pluginTone(plugin);
              const pluginBusy = isPluginBusy(busyToken, plugin.id);
              const installLabel = !plugin.installed
                ? "安装"
                : plugin.needsUpdate
                  ? "更新"
                  : "已是最新";

              return (
                <article
                  className={`plugin-card plugin-card--${cardTone}`}
                  key={plugin.id}
                >
                  <div className="plugin-card__header">
                    <div className="plugin-card__headline">
                      <div className="plugin-card__eyebrow">
                        <span className="section-label">Plugin</span>
                        <code>{plugin.id}</code>
                        {pluginBusy ? (
                          <span className="plugin-card__busy">执行中</span>
                        ) : null}
                      </div>
                      <h3>{plugin.name}</h3>
                      <p>{plugin.description || "未填写插件描述。"}</p>
                    </div>
                    <span className={`plugin-card__status plugin-card__status--${cardTone}`}>
                      {pluginSummary(plugin)}
                    </span>
                  </div>

                  <div className="plugin-card__meta">
                    <div>
                      <span>仓库版本</span>
                      <strong>{plugin.version || "未记录"}</strong>
                    </div>
                    <div>
                      <span>已装版本</span>
                      <strong>{plugin.installedVersion || "未安装"}</strong>
                    </div>
                    <div>
                      <span>源码状态</span>
                      <strong>{plugin.sourceExists ? "可用" : "缺失"}</strong>
                    </div>
                    <div>
                      <span>最近反馈</span>
                      <strong>{formatTimestamp(plugin.updatedAt)}</strong>
                    </div>
                  </div>

                  <p className="plugin-card__message">{pluginMessage(plugin)}</p>

                  <div className="plugin-card__actions">
                    <button
                      className="button button--primary button--small"
                      disabled={
                        actionsDisabled ||
                        !plugin.sourceExists ||
                        (plugin.installed && !plugin.needsUpdate)
                      }
                      onClick={() =>
                        void runPluginAction(
                          plugin,
                          plugin.installed ? "更新插件" : "安装插件",
                          () =>
                            plugin.installed
                              ? window.cpad.updatePlugin(plugin.id)
                              : window.cpad.installPlugin(plugin.id),
                        )
                      }
                      type="button"
                    >
                      {installLabel}
                    </button>
                    <button
                      className="button button--secondary button--small"
                      disabled={actionsDisabled || !plugin.installed || plugin.enabled}
                      onClick={() =>
                        void runPluginAction(plugin, "启用插件", () =>
                          window.cpad.enablePlugin(plugin.id),
                        )
                      }
                      type="button"
                    >
                      启用
                    </button>
                    <button
                      className="button button--secondary button--small"
                      disabled={actionsDisabled || !plugin.installed || !plugin.enabled}
                      onClick={() =>
                        void runPluginAction(plugin, "禁用插件", () =>
                          window.cpad.disablePlugin(plugin.id),
                        )
                      }
                      type="button"
                    >
                      禁用
                    </button>
                    <button
                      className="button button--ghost button--small"
                      disabled={actionsDisabled || !plugin.installed}
                      onClick={() =>
                        void runPluginAction(plugin, "诊断插件", () =>
                          window.cpad.diagnosePlugin(plugin.id),
                        )
                      }
                      type="button"
                    >
                      诊断
                    </button>
                    <button
                      className="button button--danger button--small"
                      disabled={actionsDisabled || !plugin.installed}
                      onClick={() => {
                        if (
                          !window.confirm(
                            `确认卸载插件“${plugin.name}”吗？这会移除受控插件目录中的已安装副本。`,
                          )
                        ) {
                          return;
                        }

                        void runPluginAction(plugin, "卸载插件", () =>
                          window.cpad.uninstallPlugin(plugin.id),
                        );
                      }}
                      type="button"
                    >
                      卸载
                    </button>
                  </div>

                  <div className="plugin-card__tools">
                    <button
                      className="button button--ghost button--small"
                      disabled={actionsDisabled || !plugin.sourceExists}
                      onClick={() => void window.cpad.openPath(plugin.sourcePath)}
                      type="button"
                    >
                      打开源码
                    </button>
                    <button
                      className="button button--ghost button--small"
                      disabled={actionsDisabled || !plugin.readmeExists}
                      onClick={() => void window.cpad.openPath(plugin.readmePath)}
                      type="button"
                    >
                      打开 README
                    </button>
                    <button
                      className="button button--ghost button--small"
                      disabled={actionsDisabled || !plugin.installed}
                      onClick={() => void window.cpad.openPath(plugin.installPath)}
                      type="button"
                    >
                      打开安装目录
                    </button>
                  </div>
                </article>
              );
            })}
          </div>
        )}

        <div className="log-stack">
          <LogPanel
            title="CPA Runtime 日志"
            lines={state.logs.cpaRuntimeTail}
            empty="当前没有读取到后端日志。"
          />
          <LogPanel
            title="服务宿主日志"
            lines={state.logs.serviceTail}
            empty="当前没有读取到服务日志。"
          />
        </div>
      </section>

      <section className="panel panel--full">
        <div className="panel__header">
          <p className="section-label">统一结构目标</p>
          <h2>当前这些模块会继续吸收成一个整体 CPAD 源码仓</h2>
        </div>

        <div className="stack-grid">
          {state.processModel.map((card) => (
            <article className="stack-card" key={card.title}>
              <p className="section-label">{card.title}</p>
              <h3>{card.summary}</h3>
              <ul>
                {card.responsibilities.map((responsibility) => (
                  <li key={responsibility}>{responsibility}</li>
                ))}
              </ul>
            </article>
          ))}
        </div>

        <div className="surface-strip">
          {state.primarySurfaces.map((surface) => (
            <span className="surface-pill" key={surface}>
              {surface}
            </span>
          ))}
        </div>
      </section>
    </main>
  );
}
