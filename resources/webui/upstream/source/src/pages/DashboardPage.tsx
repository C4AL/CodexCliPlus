import { useCallback, useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Button } from '@/components/ui/Button';
import {
  IconBot,
  IconKey,
  IconSatellite,
  IconShield,
} from '@/components/ui/icons';
import {
  isDesktopMode,
  requestLocalDependencySnapshot,
  runLocalDependencyRepair,
  type LocalDependencyItem,
  type LocalDependencyRepairProgress,
  type LocalDependencySnapshot,
} from '@/desktop/bridge';
import { useDesktopDataChanged } from '@/hooks/useDesktopDataChanged';
import { useAuthStore, useConfigStore, useModelsStore, useNotificationStore } from '@/stores';
import { apiKeysApi, providersApi, authFilesApi } from '@/services/api';
import styles from './DashboardPage.module.scss';

interface QuickStat {
  label: string;
  value: number | string;
  icon: React.ReactNode;
  path: string;
  loading?: boolean;
  sublabel?: string;
}

type TimeOfDay = 'morning' | 'afternoon' | 'evening' | 'night';
const DASHBOARD_MODELS_FETCH_TIMEOUT_MS = 1_500;

type LocalRepairTarget = {
  itemId: string;
  actionId: string;
};

type IdleWindow = Window & {
  requestIdleCallback?: (
    callback: IdleRequestCallback,
    options?: IdleRequestOptions
  ) => number;
  cancelIdleCallback?: (handle: number) => void;
};

function scheduleDashboardIdleTask(callback: () => void): () => void {
  const win = window as IdleWindow;
  let timeoutId: number | null = null;
  let idleId: number | null = null;
  const frameId = window.requestAnimationFrame(() => {
    if (typeof win.requestIdleCallback === 'function') {
      idleId = win.requestIdleCallback(callback, { timeout: 1_000 });
      return;
    }

    timeoutId = window.setTimeout(callback, 250);
  });

  return () => {
    window.cancelAnimationFrame(frameId);
    if (timeoutId !== null) {
      window.clearTimeout(timeoutId);
    }
    if (idleId !== null && typeof win.cancelIdleCallback === 'function') {
      win.cancelIdleCallback(idleId);
    }
  };
}

function getTimeOfDay(date = new Date()): TimeOfDay {
  const hour = date.getHours();
  if (hour >= 5 && hour < 12) return 'morning';
  if (hour >= 12 && hour < 17) return 'afternoon';
  if (hour >= 17 && hour < 21) return 'evening';
  return 'night';
}

export function DashboardPage() {
  const { t, i18n } = useTranslation();
  const connectionStatus = useAuthStore((state) => state.connectionStatus);
  const serverVersion = useAuthStore((state) => state.serverVersion);
  const serverBuildDate = useAuthStore((state) => state.serverBuildDate);
  const apiBase = useAuthStore((state) => state.apiBase);
  const config = useConfigStore((state) => state.config);
  const { showConfirmation, showNotification } = useNotificationStore();

  const models = useModelsStore((state) => state.models);
  const modelsLoading = useModelsStore((state) => state.loading);
  const fetchModelsFromStore = useModelsStore((state) => state.fetchModels);

  const [stats, setStats] = useState<{
    apiKeys: number | null;
    authFiles: number | null;
  }>({
    apiKeys: null,
    authFiles: null,
  });

  const [codexConfigCount, setCodexConfigCount] = useState<number | null>(null);

  const [loading, setLoading] = useState(true);
  const [localEnvironmentOpen, setLocalEnvironmentOpen] = useState(false);
  const [localEnvironmentLoading, setLocalEnvironmentLoading] = useState(false);
  const [localEnvironmentError, setLocalEnvironmentError] = useState('');
  const [localEnvironmentSnapshot, setLocalEnvironmentSnapshot] =
    useState<LocalDependencySnapshot | null>(null);
  const [repairingTarget, setRepairingTarget] = useState<LocalRepairTarget | null>(null);
  const [localRepairProgressItemId, setLocalRepairProgressItemId] = useState<string | null>(null);
  const [localRepairProgress, setLocalRepairProgress] =
    useState<LocalDependencyRepairProgress | null>(null);

  // Time-of-day state for dynamic greeting
  const [timeOfDay, setTimeOfDay] = useState<TimeOfDay>(getTimeOfDay);
  const [currentTime, setCurrentTime] = useState(() => new Date());

  const apiKeysCache = useRef<string[]>([]);
  const localEnvironmentRequestRef = useRef<Promise<LocalDependencySnapshot> | null>(null);

  useEffect(() => {
    apiKeysCache.current = [];
  }, [apiBase, config?.apiKeys]);

  // Update time every 60 seconds
  useEffect(() => {
    const id = setInterval(() => {
      if (document.hidden) return;
      const nextTime = new Date();
      setTimeOfDay((current) => {
        const nextTimeOfDay = getTimeOfDay(nextTime);
        return current === nextTimeOfDay ? current : nextTimeOfDay;
      });
      setCurrentTime(nextTime);
    }, 60_000);
    return () => clearInterval(id);
  }, []);

  const normalizeApiKeyList = (input: unknown): string[] => {
    if (!Array.isArray(input)) return [];
    const seen = new Set<string>();
    const keys: string[] = [];

    input.forEach((item) => {
      const record =
        item !== null && typeof item === 'object' && !Array.isArray(item)
          ? (item as Record<string, unknown>)
          : null;
      const value =
        typeof item === 'string'
          ? item
          : record
            ? (record['api-key'] ?? record['apiKey'] ?? record.key ?? record.Key)
            : '';
      const trimmed = String(value ?? '').trim();
      if (!trimmed || seen.has(trimmed)) return;
      seen.add(trimmed);
      keys.push(trimmed);
    });

    return keys;
  };

  const resolveApiKeysForModels = useCallback(async () => {
    if (apiKeysCache.current.length) {
      return apiKeysCache.current;
    }

    const configKeys = normalizeApiKeyList(config?.apiKeys);
    if (configKeys.length) {
      apiKeysCache.current = configKeys;
      return configKeys;
    }

    try {
      const list = await apiKeysApi.list();
      const normalized = normalizeApiKeyList(list);
      if (normalized.length) {
        apiKeysCache.current = normalized;
      }
      return normalized;
    } catch {
      return [];
    }
  }, [config?.apiKeys]);

  const fetchModels = useCallback(async () => {
    if (connectionStatus !== 'connected' || !apiBase) {
      return;
    }

    try {
      const apiKeys = await resolveApiKeysForModels();
      const primaryKey = apiKeys[0];
      await fetchModelsFromStore(apiBase, primaryKey, false, {
        timeoutMs: DASHBOARD_MODELS_FETCH_TIMEOUT_MS
      });
    } catch {
      // Ignore model fetch errors on dashboard
    }
  }, [connectionStatus, apiBase, resolveApiKeysForModels, fetchModelsFromStore]);

  const fetchLocalEnvironment = useCallback(async () => {
    if (!isDesktopMode()) {
      setLocalEnvironmentError('');
      setLocalEnvironmentSnapshot(null);
      return;
    }

    setLocalEnvironmentLoading(true);
    setLocalEnvironmentError('');
    const existingRequest = localEnvironmentRequestRef.current;
    const request = existingRequest ?? requestLocalDependencySnapshot();
    localEnvironmentRequestRef.current = request;
    try {
      const snapshot = await request;
      setLocalEnvironmentSnapshot(snapshot);
    } catch (error) {
      const message = error instanceof Error ? error.message : t('common.unknown_error');
      setLocalEnvironmentError(message);
    } finally {
      if (localEnvironmentRequestRef.current === request) {
        localEnvironmentRequestRef.current = null;
      }
      setLocalEnvironmentLoading(false);
    }
  }, [t]);

  const copyLocalEnvironmentDiagnostics = useCallback(async () => {
    if (!localEnvironmentError) return;
    const diagnostic = [
      `time=${new Date().toISOString()}`,
      `desktopMode=${isDesktopMode() ? 'true' : 'false'}`,
      `message=${localEnvironmentError}`,
    ].join('\n');

    try {
      await navigator.clipboard.writeText(diagnostic);
      showNotification(t('dashboard.local_environment_diagnostics_copied'), 'success');
    } catch {
      showNotification(diagnostic, 'warning');
    }
  }, [localEnvironmentError, showNotification, t]);

  const fetchStats = useCallback(async () => {
      setLoading(true);
      if (typeof performance !== 'undefined') {
        performance.mark('ccp-dashboard-data-start');
      }

      try {
        const [keysRes, filesRes, codexRes] = await Promise.allSettled([
          apiKeysApi.list(),
          authFilesApi.list(),
          providersApi.getCodexConfigs(),
        ]);

        setStats({
          apiKeys: keysRes.status === 'fulfilled' ? keysRes.value.length : null,
          authFiles: filesRes.status === 'fulfilled' ? filesRes.value.files.length : null,
        });

        setCodexConfigCount(codexRes.status === 'fulfilled' ? codexRes.value.length : null);
      } finally {
        setLoading(false);
        if (typeof performance !== 'undefined') {
          performance.mark('ccp-dashboard-data-loaded');
          performance.measure(
            'ccp-dashboard-data-load',
            'ccp-dashboard-data-start',
            'ccp-dashboard-data-loaded'
          );
        }
      }
    }, []);

  useEffect(() => {
    const cleanups: Array<() => void> = [];
    const timer = window.setTimeout(() => {
      if (connectionStatus === 'connected') {
        fetchStats();
        cleanups.push(scheduleDashboardIdleTask(() => void fetchModels()));
      } else {
        setLoading(false);
      }
    }, 0);

    return () => {
      window.clearTimeout(timer);
      cleanups.forEach((cleanup) => cleanup());
    };
  }, [connectionStatus, fetchModels, fetchStats]);

  useDesktopDataChanged(['config', 'providers', 'auth-files', 'quota'], () => {
    if (connectionStatus === 'connected') {
      void fetchStats();
      scheduleDashboardIdleTask(() => void fetchModels());
    }
  }, connectionStatus === 'connected');

  const getLocalStatusLabel = (status: LocalDependencyItem['status']) => {
    const labels: Record<LocalDependencyItem['status'], string> = {
      ready: t('dashboard.local_environment_status_ready'),
      warning: t('dashboard.local_environment_status_warning'),
      missing: t('dashboard.local_environment_status_missing'),
      error: t('dashboard.local_environment_status_error'),
      optionalUnavailable: t('dashboard.local_environment_status_optional'),
      repairing: t('dashboard.local_environment_status_repairing'),
    };
    return labels[status];
  };

  const getLocalStatusClass = (status: LocalDependencyItem['status']) => {
    if (status === 'ready') return styles.localStatusReady;
    if (status === 'warning' || status === 'repairing') return styles.localStatusWarning;
    if (status === 'optionalUnavailable') return styles.localStatusOptional;
    return styles.localStatusError;
  };

  const getLocalRepairPhaseLabel = (phase: string) => {
    const labels: Record<string, string> = {
      starting: t('dashboard.local_environment_repair_phase_starting'),
      running: t('dashboard.local_environment_repair_phase_running'),
      commandRunning: t('dashboard.local_environment_repair_phase_command_running'),
      commandCompleted: t('dashboard.local_environment_repair_phase_command_completed'),
      completed: t('dashboard.local_environment_repair_phase_completed'),
      failed: t('dashboard.local_environment_repair_phase_failed'),
    };
    return labels[phase] ?? t('dashboard.local_environment_repair_phase_unknown');
  };

  const handleRepairLocalDependency = (item: LocalDependencyItem) => {
    if (!item.repairActionId) return;

    if (!isDesktopMode()) {
      showNotification(t('dashboard.local_environment_desktop_required'), 'warning');
      return;
    }

    const capability = localEnvironmentSnapshot?.repairCapabilities.find(
      (candidate) => candidate.actionId === item.repairActionId
    );
    if (capability && !capability.isAvailable) {
      showNotification(capability.detail || t('dashboard.local_environment_repair_unavailable'), 'warning');
      return;
    }

    showConfirmation({
      title: t('dashboard.local_environment_repair_confirm_title'),
      message: t('dashboard.local_environment_repair_confirm_message', { name: item.name }),
      confirmText: t('common.confirm'),
      variant: 'danger',
      onConfirm: async () => {
        const actionId = item.repairActionId!;
        setRepairingTarget({ itemId: item.id, actionId });
        setLocalRepairProgressItemId(item.id);
        setLocalRepairProgress({
          actionId,
          phase: 'starting',
          message: t('dashboard.local_environment_repair_progress_starting'),
          commandLine: null,
          recentOutput: [],
          logPath: null,
          updatedAt: new Date().toISOString(),
          exitCode: null,
        });
        try {
          const response = await runLocalDependencyRepair(actionId, (progress) => {
            setLocalRepairProgressItemId(item.id);
            setLocalRepairProgress(progress);
          });
          setLocalRepairProgress((current) => ({
            actionId,
            phase: response.result.succeeded ? 'completed' : 'failed',
            message: response.result.summary,
            commandLine: current?.actionId === actionId ? current.commandLine : null,
            recentOutput: current?.actionId === actionId ? current.recentOutput : [],
            logPath: response.result.logPath ?? (current?.actionId === actionId ? current.logPath : null),
            updatedAt: new Date().toISOString(),
            exitCode: response.result.exitCode ?? null,
          }));
          if (response.snapshot) {
            setLocalEnvironmentSnapshot(response.snapshot);
          } else {
            await fetchLocalEnvironment();
          }
          showNotification(
            response.result.summary,
            response.result.succeeded ? 'success' : 'error'
          );
        } catch (error) {
          const message = error instanceof Error ? error.message : t('common.unknown_error');
          showNotification(message, 'error');
        } finally {
          setRepairingTarget(null);
        }
      },
    });
  };

  const quickStats: QuickStat[] = [
    {
      label: t('dashboard.management_keys'),
      value: stats.apiKeys ?? '-',
      icon: <IconKey size={24} />,
      path: '/config',
      loading: loading && stats.apiKeys === null,
      sublabel: t('nav.config_management'),
    },
    {
      label: t('nav.account_center'),
      value:
        loading || codexConfigCount === null || stats.authFiles === null
          ? '-'
          : codexConfigCount + stats.authFiles,
      icon: <IconBot size={24} />,
      path: '/accounts',
      loading: loading,
      sublabel: `${t('dashboard.provider_keys_detail', { count: codexConfigCount ?? '-' })} / ${t('dashboard.oauth_credentials')} ${stats.authFiles ?? '-'}`,
    },
    {
      label: t('dashboard.available_models'),
      value: modelsLoading ? '-' : models.length,
      icon: <IconSatellite size={24} />,
      path: '/system',
      loading: modelsLoading,
      sublabel: t('dashboard.available_models_desc'),
    },
  ];

  const localRequiredItems =
    localEnvironmentSnapshot?.items.filter((item) => item.severity === 'required') ?? [];
  const localRequiredReady = localRequiredItems.filter((item) => item.status === 'ready').length;
  const localOptionalUnavailable =
    localEnvironmentSnapshot?.items.filter((item) => item.status === 'optionalUnavailable').length ?? 0;
  const localScore = localEnvironmentSnapshot?.readinessScore ?? null;
  const localCheckedAt =
    localEnvironmentSnapshot?.checkedAt &&
    new Date(localEnvironmentSnapshot.checkedAt).toLocaleString(i18n.language);
  const localEnvironmentDesktopMode = isDesktopMode();

  const routingStrategyRaw = config?.routingStrategy?.trim() || '';
  const routingSessionAffinity = Boolean(config?.routingSessionAffinity);
  const routingStrategyDisplay = (() => {
    if (!routingStrategyRaw) return '-';
    if (routingStrategyRaw === 'fill-first' && routingSessionAffinity) {
      return t('basic_settings.routing_strategy_fill_first');
    }
    if (routingStrategyRaw === 'round-robin' && routingSessionAffinity) {
      return t('basic_settings.routing_strategy_session_round_robin');
    }
    if (routingStrategyRaw === 'round-robin') return t('basic_settings.routing_strategy_round_robin');
    if (routingStrategyRaw === 'fill-first') return t('basic_settings.routing_strategy_fill_first');
    return routingStrategyRaw;
  })();
  const routingStrategyBadgeClass = !routingStrategyRaw
    ? styles.configBadgeUnknown
    : routingStrategyRaw === 'round-robin'
      ? styles.configBadgeRoundRobin
      : routingStrategyRaw === 'fill-first'
        ? styles.configBadgeFillFirst
        : styles.configBadgeUnknown;

  // Derived time-based values
  const greetingKey = `dashboard.greeting_${timeOfDay}`;
  const caringKey = `dashboard.caring_${timeOfDay}`;

  const formattedDate = currentTime.toLocaleDateString(i18n.language, {
    weekday: 'long',
    year: 'numeric',
    month: 'long',
    day: 'numeric',
  });

  const formattedTime = currentTime.toLocaleTimeString(i18n.language, {
    hour: '2-digit',
    minute: '2-digit',
  });

  return (
    <div className={styles.dashboard}>
      {/* Decorative background orbs */}
      <div className={styles.backgroundOrbs} aria-hidden="true">
        <div className={styles.orb1} />
        <div className={styles.orb2} />
      </div>

      {/* Hero welcome section */}
      <section className={styles.hero}>
        <span className={styles.heroWatermark} aria-hidden="true">
          OVERVIEW
        </span>
        <div className={styles.heroContent}>
          <span className={styles.heroGreeting}>{t(greetingKey)}</span>
          <h1 className={styles.heroTitle}>{t('dashboard.welcome_back')}</h1>
          <p className={styles.heroCaring}>{t(caringKey)}</p>
        </div>
        <div className={styles.heroMeta}>
          <div className={styles.dateTimeBlock}>
            <span className={styles.time}>{formattedTime}</span>
            <span className={styles.date}>{formattedDate}</span>
          </div>
          <div className={styles.connectionPill}>
            <span
              className={`${styles.statusDot} ${
                connectionStatus === 'connected'
                  ? styles.connected
                  : connectionStatus === 'connecting'
                    ? styles.connecting
                    : styles.disconnected
              }`}
            />
            <span className={styles.pillText}>
              {serverVersion
                ? `v${serverVersion.trim().replace(/^[vV]+/, '')}`
                : t(
                    connectionStatus === 'connected'
                      ? 'common.connected'
                      : connectionStatus === 'connecting'
                        ? 'common.connecting'
                        : 'common.disconnected'
                  )}
            </span>
          </div>
          {serverBuildDate && (
            <span className={styles.buildDate}>
              {new Date(serverBuildDate).toLocaleDateString(i18n.language)}
            </span>
          )}
        </div>
      </section>

      {/* Bento stats grid */}
      <section className={styles.statsSection}>
        <h2 className={styles.sectionHeading}>{t('dashboard.system_overview')}</h2>
        <div className={styles.bentoGrid}>
          {quickStats.map((stat, index) => (
            <Link
              key={stat.path}
              to={stat.path}
              className={`${styles.bentoCard} ${index === 0 ? styles.bentoLarge : ''}`}
              style={{ animationDelay: `${index * 80}ms` }}
            >
              <div className={styles.bentoIcon}>{stat.icon}</div>
              <div className={styles.bentoContent}>
                <span className={styles.bentoValue}>{stat.loading ? '...' : stat.value}</span>
                <span className={styles.bentoLabel}>{stat.label}</span>
                {stat.sublabel && !stat.loading && (
                  <span className={styles.bentoSublabel}>{stat.sublabel}</span>
                )}
              </div>
            </Link>
          ))}
          <button
            type="button"
            className={`${styles.bentoCard} ${styles.localEnvironmentCard}`}
            onClick={() => {
              setLocalEnvironmentOpen((open) => {
                const nextOpen = !open;
                if (nextOpen && localEnvironmentDesktopMode) {
                  void fetchLocalEnvironment();
                }
                return nextOpen;
              });
            }}
          >
            <div className={styles.bentoIcon}>
              <IconShield size={24} />
            </div>
            <div className={styles.bentoContent}>
              <span className={styles.bentoValue}>
                {localEnvironmentLoading ? '...' : localScore === null ? '-' : `${localScore}%`}
              </span>
              <span className={styles.bentoLabel}>{t('dashboard.local_environment')}</span>
              <span className={styles.bentoSublabel}>
                {!localEnvironmentDesktopMode
                  ? t('dashboard.local_environment_desktop_only')
                  : localEnvironmentSnapshot
                  ? t('dashboard.local_environment_sublabel', {
                      ready: localRequiredReady,
                      total: localRequiredItems.length,
                      optional: localOptionalUnavailable,
                    })
                  : t('dashboard.local_environment_click_to_check')}
              </span>
            </div>
          </button>
        </div>
        {localEnvironmentOpen && (
          <div className={styles.localEnvironmentPanel}>
            <div className={styles.localEnvironmentHeader}>
              <div>
                <h3>{t('dashboard.local_environment_detail')}</h3>
                <p>
                  {localEnvironmentSnapshot?.summary ??
                    (localEnvironmentDesktopMode
                      ? t('dashboard.local_environment_click_to_check')
                      : t('dashboard.local_environment_desktop_unavailable_summary'))}
                </p>
                {localCheckedAt && (
                  <span>{t('dashboard.local_environment_checked_at', { time: localCheckedAt })}</span>
                )}
              </div>
            </div>
            {localEnvironmentError && (
              <div className={styles.localEnvironmentError}>
                <strong>{t('dashboard.local_environment_bridge_error_title')}</strong>
                <span>{localEnvironmentError}</span>
                <div className={styles.localEnvironmentErrorActions}>
                  <Button
                    type="button"
                    variant="secondary"
                    size="sm"
                    onClick={() => void fetchLocalEnvironment()}
                    loading={localEnvironmentLoading}
                  >
                    {t('dashboard.local_environment_bridge_retry')}
                  </Button>
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    onClick={() => void copyLocalEnvironmentDiagnostics()}
                  >
                    {t('dashboard.local_environment_copy_diagnostics')}
                  </Button>
                </div>
              </div>
            )}
            {!localEnvironmentDesktopMode && (
              <div className={styles.localEnvironmentNotice}>
                {t('dashboard.local_environment_desktop_required')}
              </div>
            )}
            {localEnvironmentSnapshot && (
              <div className={styles.localEnvironmentItems}>
                {localEnvironmentSnapshot.items.map((item) => {
                  const isRepairingItem =
                    repairingTarget?.itemId === item.id &&
                    repairingTarget.actionId === item.repairActionId;
                  const itemRepairProgress =
                    item.id === localRepairProgressItemId &&
                    item.repairActionId &&
                    localRepairProgress?.actionId === item.repairActionId
                      ? localRepairProgress
                      : null;
                  return (
                    <div key={item.id} className={styles.localDependencyItem}>
                      <div className={styles.localDependencyMain}>
                        <div className={styles.localDependencyTitleRow}>
                          <strong>{item.name}</strong>
                          <span
                            className={`${styles.localStatusBadge} ${getLocalStatusClass(item.status)}`}
                          >
                            {getLocalStatusLabel(item.status)}
                          </span>
                        </div>
                        <div className={styles.localDependencyMeta}>
                          {item.version && <span>{item.version}</span>}
                          {item.path && <span>{item.path}</span>}
                        </div>
                        <p>{item.detail}</p>
                        {item.recommendation && <small>{item.recommendation}</small>}
                      </div>
                      {item.repairActionId && localEnvironmentDesktopMode && (
                        <Button
                          type="button"
                          variant="secondary"
                          size="sm"
                          onClick={() => handleRepairLocalDependency(item)}
                          loading={isRepairingItem}
                          disabled={repairingTarget !== null}
                        >
                          {isRepairingItem
                            ? t('dashboard.local_environment_repairing_button')
                            : t('dashboard.local_environment_repair')}
                        </Button>
                      )}
                      {itemRepairProgress && (
                        <div className={styles.localDependencyRepairProgress}>
                          <div className={styles.localDependencyRepairProgressHeader}>
                            <strong>{getLocalRepairPhaseLabel(itemRepairProgress.phase)}</strong>
                            {itemRepairProgress.exitCode !== null &&
                              itemRepairProgress.exitCode !== undefined && (
                                <span>
                                  {t('dashboard.local_environment_repair_exit_code', {
                                    code: itemRepairProgress.exitCode,
                                  })}
                                </span>
                              )}
                          </div>
                          <p>{itemRepairProgress.message}</p>
                          {itemRepairProgress.commandLine && (
                            <div className={styles.localDependencyRepairField}>
                              <span>{t('dashboard.local_environment_repair_command')}</span>
                              <code>{itemRepairProgress.commandLine}</code>
                            </div>
                          )}
                          {itemRepairProgress.recentOutput.length > 0 && (
                            <div className={styles.localDependencyRepairField}>
                              <span>{t('dashboard.local_environment_repair_recent_output')}</span>
                              <pre>{itemRepairProgress.recentOutput.join('\n')}</pre>
                            </div>
                          )}
                          {itemRepairProgress.logPath && (
                            <div className={styles.localDependencyRepairField}>
                              <span>{t('dashboard.local_environment_repair_log_path')}</span>
                              <code>{itemRepairProgress.logPath}</code>
                            </div>
                          )}
                        </div>
                      )}
                    </div>
                  );
                })}
              </div>
            )}
          </div>
        )}
      </section>

      {/* Config pills section */}
      {config && (
        <section className={styles.configSection}>
          <h2 className={styles.sectionHeading}>{t('dashboard.current_config')}</h2>
          <div className={styles.configPillGrid}>
            <div className={styles.configPill}>
              <span className={styles.configPillLabel}>{t('basic_settings.debug_enable')}</span>
              <span
                className={`${styles.configPillValue} ${config.debug ? styles.on : styles.off}`}
              >
                {config.debug ? t('common.yes') : t('common.no')}
              </span>
            </div>
            <div className={styles.configPill}>
              <span className={styles.configPillLabel}>
                {t('basic_settings.usage_statistics_enable')}
              </span>
              <span
                className={`${styles.configPillValue} ${config.usageStatisticsEnabled ? styles.on : styles.off}`}
              >
                {config.usageStatisticsEnabled ? t('common.yes') : t('common.no')}
              </span>
            </div>
            <div className={styles.configPill}>
              <span className={styles.configPillLabel}>
                {t('basic_settings.logging_to_file_enable')}
              </span>
              <span
                className={`${styles.configPillValue} ${config.loggingToFile ? styles.on : styles.off}`}
              >
                {config.loggingToFile ? t('common.yes') : t('common.no')}
              </span>
            </div>
            <div className={styles.configPill}>
              <span className={styles.configPillLabel}>
                {t('basic_settings.retry_count_label')}
              </span>
              <span className={styles.configPillValue}>{config.requestRetry ?? 0}</span>
            </div>
            <div className={styles.configPill}>
              <span className={styles.configPillLabel}>{t('basic_settings.ws_auth_enable')}</span>
              <span
                className={`${styles.configPillValue} ${config.wsAuth ? styles.on : styles.off}`}
              >
                {config.wsAuth ? t('common.yes') : t('common.no')}
              </span>
            </div>
            <div className={styles.configPill}>
              <span className={styles.configPillLabel}>{t('dashboard.routing_strategy')}</span>
              <span className={`${styles.configBadge} ${routingStrategyBadgeClass}`}>
                {routingStrategyDisplay}
              </span>
            </div>
            {config.proxyUrl && (
              <div className={`${styles.configPill} ${styles.configPillWide}`}>
                <span className={styles.configPillLabel}>
                  {t('basic_settings.proxy_url_label')}
                </span>
                <span className={styles.configPillMono}>{config.proxyUrl}</span>
              </div>
            )}
          </div>
          <Link to="/config" className={styles.viewMoreLink}>
            {t('dashboard.edit_settings')} →
          </Link>
        </section>
      )}
    </div>
  );
}
