import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { TFunction } from 'i18next';
import { useTranslation } from 'react-i18next';
import { Card } from '@/components/ui/Card';
import { Button } from '@/components/ui/Button';
import { EmptyState } from '@/components/ui/EmptyState';
import { IconLayoutDashboard, IconRefreshCw, IconTrendingUp } from '@/components/ui/icons';
import { CODEX_CONFIG } from '@/components/quota/quotaConfigs';
import { authFilesApi } from '@/services/api';
import { useAuthStore, useConfigStore, useUsageStatsStore } from '@/stores';
import type { AuthFileItem, CodexQuotaWindow, CredentialInfo } from '@/types';
import { buildSourceInfoMap, resolveSourceDisplay } from '@/utils/sourceResolver';
import { parseTimestampMs } from '@/utils/timestamp';
import {
  collectUsageDetails,
  extractLatencyMs,
  extractTotalTokens,
  filterUsageByTimeRange,
  formatCompactNumber,
  formatDurationMs,
  normalizeAuthIndex,
} from '@/utils/usage';
import styles from './RuntimeOverviewPage.module.scss';

const OVERVIEW_RANGE = '24h' as const;
const RECENT_EVENTS_LIMIT = 5;

type OverviewQuotaState =
  | {
      status: 'success';
      planType: string | null;
      windows: CodexQuotaWindow[];
    }
  | {
      status: 'error';
      planType: null;
      windows: CodexQuotaWindow[];
      error: string;
      errorStatus?: number;
    };

type OverviewWindow = {
  id: string;
  label: string;
  shortLabel: string;
  remainingPercent: number | null;
  resetLabel: string;
};

type AccountOverview = {
  key: string;
  name: string;
  authIndex: string;
  requests: number;
  successCount: number;
  failureCount: number;
  successRate: number | null;
  totalTokens: number;
  planLabel: string;
  quotaState: OverviewQuotaState | null;
  windows: OverviewWindow[];
};

type RecentEventRow = {
  id: string;
  timestamp: string;
  timestampLabel: string;
  model: string;
  source: string;
  authIndex: string;
  failed: boolean;
  latencyMs: number | null;
  inputTokens: number;
  outputTokens: number;
  reasoningTokens: number;
  cachedTokens: number;
  totalTokens: number;
  timestampMs: number;
};

const getErrorMessage = (error: unknown) =>
  error instanceof Error
    ? error.message
    : typeof error === 'string'
      ? error
      : 'Request failed';

const toNumber = (value: unknown): number => {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : 0;
};

const toDisplayName = (name: string) =>
  name
    .replace(/^codex-/i, '')
    .replace(/\.json$/i, '')
    .trim() || name;

const buildFallbackWindows = (
  windows: CodexQuotaWindow[],
  t: TFunction
): OverviewWindow[] => {
  const definitions = [
    {
      id: 'five-hour',
      shortLabel: '5H',
      key: 'codex_quota.primary_window',
      fallbackLabel: '5-hour limit',
    },
    {
      id: 'weekly',
      shortLabel: '7D',
      key: 'codex_quota.secondary_window',
      fallbackLabel: 'Weekly limit',
    },
  ];

  return definitions.map((definition) => {
    const matchedWindow =
      windows.find((window) => window.id === definition.id) ??
      windows.find((window) => window.labelKey === definition.key);
    const usedPercent = matchedWindow?.usedPercent;
    const remainingPercent =
      typeof usedPercent === 'number'
        ? Math.max(0, Math.min(100, Math.round(100 - usedPercent)))
        : null;

    return {
      id: definition.id,
      label: matchedWindow
        ? matchedWindow.labelKey
          ? t(matchedWindow.labelKey, {
              ...matchedWindow.labelParams,
              defaultValue: matchedWindow.label,
            })
          : matchedWindow.label
        : t(definition.key, { defaultValue: definition.fallbackLabel }),
      shortLabel: definition.shortLabel,
      remainingPercent,
      resetLabel:
        matchedWindow?.resetLabel && matchedWindow.resetLabel !== '-'
          ? matchedWindow.resetLabel
          : definition.shortLabel,
    };
  });
};

export function RuntimeOverviewPage() {
  const { t, i18n } = useTranslation();
  const dt = useCallback((zh: string, _en: string) => zh, []);

  const connectionStatus = useAuthStore((state) => state.connectionStatus);
  const config = useConfigStore((state) => state.config);
  const fetchConfig = useConfigStore((state) => state.fetchConfig);

  const usage = useUsageStatsStore((state) => state.usage);
  const usageLoading = useUsageStatsStore((state) => state.loading);
  const usageError = useUsageStatsStore((state) => state.error);
  const lastUsageRefreshedAt = useUsageStatsStore((state) => state.lastRefreshedAt);
  const loadUsageStats = useUsageStatsStore((state) => state.loadUsageStats);

  const [authFiles, setAuthFiles] = useState<AuthFileItem[]>([]);
  const [authFilesLoading, setAuthFilesLoading] = useState(false);
  const [authFilesError, setAuthFilesError] = useState<string | null>(null);
  const [quotaByAuthIndex, setQuotaByAuthIndex] = useState<Record<string, OverviewQuotaState>>({});
  const [refreshing, setRefreshing] = useState(false);
  const [lastSyncedAt, setLastSyncedAt] = useState<number | null>(null);

  const requestTokenRef = useRef(0);

  const refreshOverview = useCallback(
    async (force: boolean) => {
      if (connectionStatus !== 'connected') {
        setAuthFiles([]);
        setQuotaByAuthIndex({});
        setAuthFilesError(null);
        setAuthFilesLoading(false);
        setRefreshing(false);
        return;
      }

      const requestId = (requestTokenRef.current += 1);
      setRefreshing(true);
      setAuthFilesLoading(true);
      setAuthFilesError(null);

      const [configResult, usageResult, authFilesResult] = await Promise.allSettled([
        fetchConfig(undefined, force),
        loadUsageStats({ force }),
        authFilesApi.list(),
      ]);

      if (requestId !== requestTokenRef.current) {
        return;
      }

      if (configResult.status === 'rejected') {
        console.warn('Runtime overview config refresh failed:', configResult.reason);
      }

      if (usageResult.status === 'rejected') {
        console.warn('Runtime overview usage refresh failed:', usageResult.reason);
      }

      if (authFilesResult.status === 'rejected') {
        setAuthFiles([]);
        setQuotaByAuthIndex({});
        setAuthFilesError(getErrorMessage(authFilesResult.reason));
        setAuthFilesLoading(false);
        setRefreshing(false);
        return;
      }

      const nextFiles = Array.isArray(authFilesResult.value?.files) ? authFilesResult.value.files : [];
      setAuthFiles(nextFiles);

      const codexFiles = nextFiles.filter((file) => CODEX_CONFIG.filterFn(file));
      if (codexFiles.length === 0) {
        setQuotaByAuthIndex({});
        setAuthFilesLoading(false);
        setRefreshing(false);
        setLastSyncedAt(Date.now());
        return;
      }

      const quotaResults = await Promise.allSettled(
        codexFiles.map(async (file) => {
          const authIndex = normalizeAuthIndex(file['auth_index'] ?? file.authIndex) ?? file.name.trim();
          const payload = await CODEX_CONFIG.fetchQuota(file, t);
          return [
            authIndex,
            {
              status: 'success',
              planType: payload.planType ?? null,
              windows: payload.windows,
            } as OverviewQuotaState,
          ] as const;
        })
      );

      if (requestId !== requestTokenRef.current) {
        return;
      }

      const nextQuotaState: Record<string, OverviewQuotaState> = {};
      quotaResults.forEach((result, index) => {
        const file = codexFiles[index];
        const authIndex = normalizeAuthIndex(file['auth_index'] ?? file.authIndex) ?? file.name.trim();
        if (!authIndex) return;

        if (result.status === 'fulfilled') {
          nextQuotaState[authIndex] = result.value[1];
          return;
        }

        const message = getErrorMessage(result.reason);
        const errorStatus =
          result.reason !== null &&
          typeof result.reason === 'object' &&
          'status' in result.reason &&
          typeof (result.reason as { status?: unknown }).status === 'number'
            ? ((result.reason as { status?: number }).status ?? undefined)
            : undefined;

        nextQuotaState[authIndex] = {
          status: 'error',
          planType: null,
          windows: [],
          error: message,
          errorStatus,
        };
      });

      setQuotaByAuthIndex(nextQuotaState);
      setAuthFilesLoading(false);
      setRefreshing(false);
      setLastSyncedAt(Date.now());
    },
    [connectionStatus, fetchConfig, loadUsageStats, t]
  );

  useEffect(() => {
    const timer = window.setTimeout(() => {
      void refreshOverview(false);
    }, 0);
    return () => window.clearTimeout(timer);
  }, [refreshOverview]);

  const filteredUsage = useMemo(
    () => filterUsageByTimeRange(usage, OVERVIEW_RANGE),
    [usage]
  );

  const usageDetails = useMemo(() => collectUsageDetails(filteredUsage), [filteredUsage]);

  const authFileMap = useMemo(() => {
    const map = new Map<string, CredentialInfo>();
    authFiles.forEach((file) => {
      const authIndex = normalizeAuthIndex(file['auth_index'] ?? file.authIndex);
      if (!authIndex) return;
      map.set(authIndex, {
        name: file.name || authIndex,
        type: String(file.type || file.provider || ''),
      });
    });
    return map;
  }, [authFiles]);

  const sourceInfoMap = useMemo(
    () =>
      buildSourceInfoMap({
        geminiApiKeys: config?.geminiApiKeys,
        claudeApiKeys: config?.claudeApiKeys,
        codexApiKeys: config?.codexApiKeys,
        vertexApiKeys: config?.vertexApiKeys,
        openaiCompatibility: config?.openaiCompatibility,
      }),
    [
      config?.claudeApiKeys,
      config?.codexApiKeys,
      config?.geminiApiKeys,
      config?.openaiCompatibility,
      config?.vertexApiKeys,
    ]
  );

  const accountCards = useMemo<AccountOverview[]>(() => {
    const detailsByAuthIndex = new Map<string, typeof usageDetails>();
    usageDetails.forEach((detail) => {
      const authIndex = normalizeAuthIndex(detail.auth_index);
      if (!authIndex) return;
      const bucket = detailsByAuthIndex.get(authIndex);
      if (bucket) {
        bucket.push(detail);
      } else {
        detailsByAuthIndex.set(authIndex, [detail]);
      }
    });

    return authFiles
      .filter((file) => CODEX_CONFIG.filterFn(file))
      .map((file) => {
        const authIndex = normalizeAuthIndex(file['auth_index'] ?? file.authIndex) ?? file.name.trim();
        const details = detailsByAuthIndex.get(authIndex) ?? [];
        const requests = details.length;
        const successCount = details.filter((detail) => detail.failed !== true).length;
        const failureCount = requests - successCount;
        const totalTokens = details.reduce(
          (sum, detail) => sum + Math.max(toNumber(detail.tokens?.total_tokens), extractTotalTokens(detail)),
          0
        );
        const successRate =
          requests > 0 ? Math.round((successCount / Math.max(requests, 1)) * 100) : null;
        const quotaState = quotaByAuthIndex[authIndex] ?? null;
        const planLabel =
          quotaState?.status === 'success' && quotaState.planType
            ? t(`codex_quota.${quotaState.planType}`, {
                defaultValue: quotaState.planType,
              })
            : t('codex_quota.plan_unknown', {
                defaultValue: dt('未知', 'Unknown'),
              });

        return {
          key: authIndex,
          name: toDisplayName(file.name),
          authIndex,
          requests,
          successCount,
          failureCount,
          successRate,
          totalTokens,
          planLabel,
          quotaState,
          windows:
            quotaState?.status === 'success'
              ? buildFallbackWindows(quotaState.windows, t)
              : buildFallbackWindows([], t),
        };
      })
      .sort((left, right) => {
        if (right.requests !== left.requests) {
          return right.requests - left.requests;
        }
        return left.name.localeCompare(right.name);
      });
  }, [authFiles, dt, quotaByAuthIndex, t, usageDetails]);

  const totalRequestCount = useMemo(
    () => accountCards.reduce((sum, card) => sum + card.requests, 0),
    [accountCards]
  );
  const totalTokenCount = useMemo(
    () => accountCards.reduce((sum, card) => sum + card.totalTokens, 0),
    [accountCards]
  );

  const recentEventRows = useMemo<RecentEventRow[]>(() => {
    return usageDetails
      .map((detail, index) => {
        const timestampMs =
          typeof detail.__timestampMs === 'number' && detail.__timestampMs > 0
            ? detail.__timestampMs
            : parseTimestampMs(detail.timestamp);
        const sourceInfo = resolveSourceDisplay(
          String(detail.source ?? '').trim(),
          detail.auth_index,
          sourceInfoMap,
          authFileMap
        );
        const latencyMs = extractLatencyMs(detail);
        const inputTokens = Math.max(toNumber(detail.tokens?.input_tokens), 0);
        const outputTokens = Math.max(toNumber(detail.tokens?.output_tokens), 0);
        const reasoningTokens = Math.max(toNumber(detail.tokens?.reasoning_tokens), 0);
        const cachedTokens = Math.max(
          Math.max(toNumber(detail.tokens?.cached_tokens), 0),
          Math.max(toNumber(detail.tokens?.cache_tokens), 0)
        );
        const totalTokens = Math.max(toNumber(detail.tokens?.total_tokens), extractTotalTokens(detail));

        return {
          id: `${detail.timestamp}-${detail.__modelName ?? 'model'}-${index}`,
          timestamp: detail.timestamp,
          timestampLabel:
            Number.isFinite(timestampMs) && timestampMs > 0
              ? new Date(timestampMs).toLocaleString(i18n.language)
              : detail.timestamp,
          model: String(detail.__modelName ?? '').trim() || '-',
          source: sourceInfo.displayName,
          authIndex: normalizeAuthIndex(detail.auth_index) ?? '-',
          failed: detail.failed === true,
          latencyMs,
          inputTokens,
          outputTokens,
          reasoningTokens,
          cachedTokens,
          totalTokens,
          timestampMs: Number.isFinite(timestampMs) ? timestampMs : 0,
        };
      })
      .sort((left, right) => right.timestampMs - left.timestampMs)
      .slice(0, RECENT_EVENTS_LIMIT);
  }, [authFileMap, i18n.language, sourceInfoMap, usageDetails]);

  const hasLatency = useMemo(
    () => recentEventRows.some((row) => row.latencyMs !== null),
    [recentEventRows]
  );

  const lastUpdatedLabel = useMemo(() => {
    const timestamp = lastSyncedAt ?? lastUsageRefreshedAt;
    if (!timestamp) {
      return dt('尚未刷新', 'Not refreshed yet');
    }
    return new Date(timestamp).toLocaleString(i18n.language);
  }, [dt, i18n.language, lastSyncedAt, lastUsageRefreshedAt]);

  const quotaUnavailableCount = useMemo(
    () => accountCards.filter((card) => card.quotaState?.status === 'error').length,
    [accountCards]
  );

  if (connectionStatus !== 'connected') {
    return (
      <div className={styles.page}>
        <div className={styles.header}>
          <div>
            <h1 className={styles.pageTitle}>{dt('24 小时概览', '24h Overview')}</h1>
            <p className={styles.pageSubtitle}>
              {dt('聚合 Codex 账号用量、额度和最近请求。', 'Aggregate Codex account usage, quota, and recent requests.')}
            </p>
          </div>
        </div>
        <EmptyState
          title={dt('需要先连接管理接口', 'Connect to the management API first')}
          description={dt(
            '连接成功后才会加载认证文件、24 小时用量和本地额度桥信息。',
            'Auth files, 24-hour usage, and the local quota bridge are loaded only after the management API is connected.'
          )}
        />
      </div>
    );
  }

  return (
    <div className={styles.page}>
      <div className={styles.header}>
        <div>
          <h1 className={styles.pageTitle}>{dt('24 小时概览', '24h Overview')}</h1>
          <p className={styles.pageSubtitle}>
            {dt('聚合 Codex 账号用量、额度和最近请求。', 'Aggregate Codex account usage, quota, and recent requests.')}
          </p>
        </div>
        <div className={styles.headerActions}>
          <div className={styles.lastUpdated}>
            {dt('最近刷新：', 'Last refreshed: ')}
            <span>{lastUpdatedLabel}</span>
          </div>
          <Button
            type="button"
            variant="secondary"
            onClick={() => void refreshOverview(true)}
            loading={refreshing}
          >
            <span className={styles.refreshButton}>
              <IconRefreshCw size={16} />
              {dt('刷新概览', 'Refresh overview')}
            </span>
          </Button>
        </div>
      </div>

      {(usageError || authFilesError || quotaUnavailableCount > 0) && (
        <div className={styles.statusStack}>
          {usageError && <div className="status-badge error">{usageError}</div>}
          {authFilesError && <div className="status-badge error">{authFilesError}</div>}
          {quotaUnavailableCount > 0 && (
            <div className="status-badge warning">
              {dt(
                `${quotaUnavailableCount} 个账号的本地额度桥不可用，页面已自动降级为只显示请求统计。`,
                `${quotaUnavailableCount} account quota bridge checks are unavailable, so the page has been downgraded to request statistics only.`
              )}
            </div>
          )}
        </div>
      )}

      <Card title={t('dashboard.account_stats', { defaultValue: dt('账号概览', 'Account overview') })}>
        <div className={styles.summaryRow}>
          <div className={styles.summaryPill}>
            <div className={styles.summaryIcon}>
              <IconLayoutDashboard size={18} />
            </div>
            <div>
              <div className={styles.summaryLabel}>{dt('24 小时请求数', 'Requests in 24h')}</div>
              <div className={styles.summaryValue}>{formatCompactNumber(totalRequestCount)}</div>
            </div>
          </div>
          <div className={styles.summaryPill}>
            <div className={styles.summaryIcon}>
              <IconTrendingUp size={18} />
            </div>
            <div>
              <div className={styles.summaryLabel}>{dt('24 小时 Tokens', 'Tokens in 24h')}</div>
              <div className={styles.summaryValue}>{formatCompactNumber(totalTokenCount)}</div>
            </div>
          </div>
        </div>

        {authFilesLoading && accountCards.length === 0 ? (
          <div className={styles.loadingHint}>{dt('正在加载概览数据...', 'Loading overview data...')}</div>
        ) : accountCards.length === 0 ? (
          <EmptyState
            title={t('dashboard.no_codex_accounts', {
              defaultValue: dt('暂无 Codex 账号', 'No Codex accounts'),
            })}
            description={dt(
              '上传并启用 Codex 认证文件后，这里会展示 24 小时账号概览和本地额度桥信息。',
              'Upload and enable Codex auth files to render 24-hour account stats and local quota bridge data here.'
            )}
          />
        ) : (
          <div className={styles.accountGrid}>
            {accountCards.map((card) => (
              <article key={card.key} className={styles.accountCard}>
                <div className={styles.accountHeader}>
                  <div>
                    <h2 className={styles.accountName}>{card.name}</h2>
                    <div className={styles.accountMeta}>
                      {dt('Auth Index', 'Auth Index')} {card.authIndex}
                    </div>
                  </div>
                  <span className={styles.planBadge}>{card.planLabel}</span>
                </div>

                <div className={styles.accountStats}>
                  <div className={styles.accountStat}>
                    <span className={styles.accountStatLabel}>{dt('请求', 'Requests')}</span>
                    <span className={styles.accountStatValue}>{card.requests.toLocaleString()}</span>
                  </div>
                  <div className={styles.accountStat}>
                    <span className={styles.accountStatLabel}>{dt('成功率', 'Success rate')}</span>
                    <span className={styles.accountStatValue}>
                      {card.successRate === null ? '--' : `${card.successRate}%`}
                    </span>
                  </div>
                  <div className={styles.accountStat}>
                    <span className={styles.accountStatLabel}>{dt('Tokens', 'Tokens')}</span>
                    <span className={styles.accountStatValue}>
                      {formatCompactNumber(card.totalTokens)}
                    </span>
                  </div>
                </div>

                <div className={styles.windowList}>
                  {card.windows.map((window) => (
                    <div key={`${card.key}-${window.id}`} className={styles.windowRow}>
                      <div className={styles.windowHeader}>
                        <span className={styles.windowLabel}>{window.label}</span>
                        <span className={styles.windowMeta}>
                          {window.remainingPercent === null
                            ? '--'
                            : `${window.remainingPercent}%`}{' '}
                          · {window.resetLabel}
                        </span>
                      </div>
                      <div className={styles.progressTrack}>
                        <div
                          className={styles.progressFill}
                          style={{
                            width: `${
                              window.remainingPercent === null ? 0 : Math.max(window.remainingPercent, 4)
                            }%`,
                          }}
                        />
                      </div>
                      <div className={styles.windowFootnote}>{window.shortLabel}</div>
                    </div>
                  ))}
                </div>

                {card.quotaState?.status === 'error' && (
                  <div className={styles.quotaFallback}>
                    {[404, 405, 501].includes(card.quotaState.errorStatus ?? 0)
                      ? dt('本地额度桥不可用', 'Local quota bridge unavailable')
                      : dt('额度数据暂不可用', 'Quota data unavailable')}
                  </div>
                )}
              </article>
            ))}
          </div>
        )}
      </Card>

      <Card title={dt('最近请求事件', 'Recent request events')}>
        {usageLoading && recentEventRows.length === 0 ? (
          <div className={styles.loadingHint}>{dt('正在加载最近请求...', 'Loading recent requests...')}</div>
        ) : recentEventRows.length === 0 ? (
          <EmptyState
            title={dt('24 小时内暂无请求', 'No requests in the last 24 hours')}
            description={dt(
              '只要最近 24 小时内有使用记录，这里就会展示最新的 5 条请求事件。',
              'The latest 5 request events appear here as soon as usage data exists within the last 24 hours.'
            )}
          />
        ) : (
          <div className={styles.tableWrapper}>
            <table className={styles.table}>
              <thead>
                <tr>
                  <th>{dt('时间', 'Timestamp')}</th>
                  <th>{dt('模型', 'Model')}</th>
                  <th>{dt('来源', 'Source')}</th>
                  <th>{dt('Auth Index', 'Auth Index')}</th>
                  <th>{dt('结果', 'Result')}</th>
                  {hasLatency && <th>{dt('耗时', 'Latency')}</th>}
                  <th>{dt('输入', 'Input')}</th>
                  <th>{dt('输出', 'Output')}</th>
                  <th>{dt('推理', 'Reasoning')}</th>
                  <th>{dt('缓存', 'Cached')}</th>
                  <th>{dt('总计', 'Total')}</th>
                </tr>
              </thead>
              <tbody>
                {recentEventRows.map((row) => (
                  <tr key={row.id}>
                    <td title={row.timestamp}>{row.timestampLabel}</td>
                    <td>{row.model}</td>
                    <td>{row.source}</td>
                    <td>{row.authIndex}</td>
                    <td>
                      <span
                        className={row.failed ? styles.resultFailed : styles.resultSuccess}
                      >
                        {row.failed ? dt('失败', 'Failed') : dt('成功', 'Success')}
                      </span>
                    </td>
                    {hasLatency && <td>{formatDurationMs(row.latencyMs)}</td>}
                    <td>{row.inputTokens.toLocaleString()}</td>
                    <td>{row.outputTokens.toLocaleString()}</td>
                    <td>{row.reasoningTokens.toLocaleString()}</td>
                    <td>{row.cachedTokens.toLocaleString()}</td>
                    <td>{row.totalTokens.toLocaleString()}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>
    </div>
  );
}
