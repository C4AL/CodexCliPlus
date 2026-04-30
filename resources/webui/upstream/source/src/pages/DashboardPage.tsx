import { useCallback, useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Button } from '@/components/ui/Button';
import {
  IconBot,
  IconFileText,
  IconKey,
  IconSatellite,
  IconShield,
} from '@/components/ui/icons';
import {
  isDesktopMode,
  requestLocalDependencySnapshot,
  runLocalDependencyRepair,
  type LocalDependencyItem,
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

interface ProviderStats {
  gemini: number | null;
  codex: number | null;
  claude: number | null;
  openai: number | null;
}

type TimeOfDay = 'morning' | 'afternoon' | 'evening' | 'night';

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

  const [providerStats, setProviderStats] = useState<ProviderStats>({
    gemini: null,
    codex: null,
    claude: null,
    openai: null,
  });

  const [loading, setLoading] = useState(true);
  const [localEnvironmentOpen, setLocalEnvironmentOpen] = useState(false);
  const [localEnvironmentLoading, setLocalEnvironmentLoading] = useState(false);
  const [localEnvironmentError, setLocalEnvironmentError] = useState('');
  const [localEnvironmentSnapshot, setLocalEnvironmentSnapshot] =
    useState<LocalDependencySnapshot | null>(null);
  const [repairingActionId, setRepairingActionId] = useState<string | null>(null);

  // Time-of-day state for dynamic greeting
  const [timeOfDay, setTimeOfDay] = useState<TimeOfDay>(getTimeOfDay);
  const [currentTime, setCurrentTime] = useState(() => new Date());

  const apiKeysCache = useRef<string[]>([]);

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
      await fetchModelsFromStore(apiBase, primaryKey);
    } catch {
      // Ignore model fetch errors on dashboard
    }
  }, [connectionStatus, apiBase, resolveApiKeysForModels, fetchModelsFromStore]);

  const fetchLocalEnvironment = useCallback(async () => {
    if (!isDesktopMode()) {
      setLocalEnvironmentError(t('dashboard.local_environment_desktop_required'));
      return;
    }

    setLocalEnvironmentLoading(true);
    setLocalEnvironmentError('');
    try {
      const snapshot = await requestLocalDependencySnapshot();
      setLocalEnvironmentSnapshot(snapshot);
    } catch (error) {
      const message = error instanceof Error ? error.message : t('common.unknown_error');
      setLocalEnvironmentError(message);
    } finally {
      setLocalEnvironmentLoading(false);
    }
  }, [t]);

  const fetchStats = useCallback(async () => {
      setLoading(true);
      if (typeof performance !== 'undefined') {
        performance.mark('ccp-dashboard-data-start');
      }

      try {
        const [keysRes, filesRes, geminiRes, codexRes, claudeRes, openaiRes] =
          await Promise.allSettled([
            apiKeysApi.list(),
            authFilesApi.list(),
            providersApi.getGeminiKeys(),
            providersApi.getCodexConfigs(),
            providersApi.getClaudeConfigs(),
            providersApi.getOpenAIProviders(),
          ]);

        setStats({
          apiKeys: keysRes.status === 'fulfilled' ? keysRes.value.length : null,
          authFiles: filesRes.status === 'fulfilled' ? filesRes.value.files.length : null,
        });

        setProviderStats({
          gemini: geminiRes.status === 'fulfilled' ? geminiRes.value.length : null,
          codex: codexRes.status === 'fulfilled' ? codexRes.value.length : null,
          claude: claudeRes.status === 'fulfilled' ? claudeRes.value.length : null,
          openai: openaiRes.status === 'fulfilled' ? openaiRes.value.length : null,
        });
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
    const timer = window.setTimeout(() => {
      if (connectionStatus === 'connected') {
        fetchStats();
        fetchModels();
      } else {
        setLoading(false);
      }
    }, 0);

    return () => window.clearTimeout(timer);
  }, [connectionStatus, fetchModels, fetchStats]);

  useDesktopDataChanged(['config', 'providers', 'auth-files', 'quota'], () => {
    if (connectionStatus === 'connected') {
      void fetchStats();
      void fetchModels();
    }
  }, connectionStatus === 'connected');

  useDesktopDataChanged(['local-environment'], () => {
    void fetchLocalEnvironment();
  }, isDesktopMode());

  useEffect(() => {
    if (!isDesktopMode()) {
      return;
    }

    const timer = window.setTimeout(() => {
      void fetchLocalEnvironment();
    }, 0);
    return () => window.clearTimeout(timer);
  }, [fetchLocalEnvironment]);

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
        setRepairingActionId(item.repairActionId ?? null);
        try {
          const response = await runLocalDependencyRepair(item.repairActionId!);
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
          setRepairingActionId(null);
        }
      },
    });
  };

  // Calculate total provider keys only when all provider stats are available.
  const providerStatsReady =
    providerStats.gemini !== null &&
    providerStats.codex !== null &&
    providerStats.claude !== null &&
    providerStats.openai !== null;
  const hasProviderStats =
    providerStats.gemini !== null ||
    providerStats.codex !== null ||
    providerStats.claude !== null ||
    providerStats.openai !== null;
  const totalProviderKeys = providerStatsReady
    ? (providerStats.gemini ?? 0) +
      (providerStats.codex ?? 0) +
      (providerStats.claude ?? 0) +
      (providerStats.openai ?? 0)
    : 0;

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
      label: t('nav.ai_providers'),
      value: loading ? '-' : providerStatsReady ? totalProviderKeys : '-',
      icon: <IconBot size={24} />,
      path: '/ai-providers',
      loading: loading,
      sublabel: hasProviderStats
        ? t('dashboard.provider_keys_detail', {
            gemini: providerStats.gemini ?? '-',
            codex: providerStats.codex ?? '-',
            claude: providerStats.claude ?? '-',
            openai: providerStats.openai ?? '-',
          })
        : undefined,
    },
    {
      label: t('nav.auth_files'),
      value: stats.authFiles ?? '-',
      icon: <IconFileText size={24} />,
      path: '/auth-files',
      loading: loading && stats.authFiles === null,
      sublabel: t('dashboard.oauth_credentials'),
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

  const routingStrategyRaw = config?.routingStrategy?.trim() || '';
  const routingStrategyDisplay = !routingStrategyRaw
    ? '-'
    : routingStrategyRaw === 'round-robin'
      ? t('basic_settings.routing_strategy_round_robin')
      : routingStrategyRaw === 'fill-first'
        ? t('basic_settings.routing_strategy_fill_first')
        : routingStrategyRaw;
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
              setLocalEnvironmentOpen((open) => !open);
              if (!localEnvironmentSnapshot && !localEnvironmentLoading) {
                void fetchLocalEnvironment();
              }
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
                {localEnvironmentSnapshot
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
                    t('dashboard.local_environment_desktop_required')}
                </p>
                {localCheckedAt && (
                  <span>{t('dashboard.local_environment_checked_at', { time: localCheckedAt })}</span>
                )}
              </div>
            </div>
            {localEnvironmentError && (
              <div className={styles.localEnvironmentError}>{localEnvironmentError}</div>
            )}
            {!isDesktopMode() && (
              <div className={styles.localEnvironmentNotice}>
                {t('dashboard.local_environment_repair_desktop_only')}
              </div>
            )}
            {localEnvironmentSnapshot && (
              <div className={styles.localEnvironmentItems}>
                {localEnvironmentSnapshot.items.map((item) => (
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
                    {item.repairActionId && isDesktopMode() && (
                      <Button
                        type="button"
                        variant="secondary"
                        size="sm"
                        onClick={() => handleRepairLocalDependency(item)}
                        loading={repairingActionId === item.repairActionId}
                        disabled={repairingActionId !== null}
                      >
                        {t('dashboard.local_environment_repair')}
                      </Button>
                    )}
                  </div>
                ))}
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
