import { useCallback, useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Button } from '@/components/ui/Button';
import {
  IconBot,
  IconChevronDown,
  IconChevronUp,
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
const LOCAL_ENVIRONMENT_AUTO_REFRESH_MS = 6_000;

type LocalRepairTarget = {
  itemId: string;
  actionId: string;
};

const REQUIRED_ENVIRONMENT_REPAIR_ACTION_ID = 'repair-required-env-install-latest-codex';
const REQUIRED_ENVIRONMENT_REPAIR_TARGET_ID = 'required-env-codex';
const NODE_NPM_DISPLAY_ID = 'node-npm';
const NODE_NPM_REPAIR_ACTION_ID = 'install-node-npm';
const LOCAL_ENVIRONMENT_DISPLAY_ORDER = [
  NODE_NPM_DISPLAY_ID,
  'codex-cli',
  'path',
  'powershell',
  'winget',
  'wsl',
];
const LOCAL_DEPENDENCY_STATUS_WEIGHT: Record<LocalDependencyItem['status'], number> = {
  ready: 0,
  optionalUnavailable: 1,
  warning: 2,
  repairing: 3,
  missing: 4,
  error: 5,
};
const LOCAL_DEPENDENCY_SEVERITY_WEIGHT: Record<LocalDependencyItem['severity'], number> = {
  optional: 0,
  repairTool: 1,
  required: 2,
};
const LOCAL_REPAIR_PHASE_PROGRESS: Record<string, number> = {
  starting: 8,
  running: 24,
  commandRunning: 58,
  commandCompleted: 82,
  completed: 100,
  failed: 100,
};

type IdleWindow = Window & {
  requestIdleCallback?: (callback: IdleRequestCallback, options?: IdleRequestOptions) => number;
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

function getLocalRepairProgressPercent(phase: string): number {
  return LOCAL_REPAIR_PHASE_PROGRESS[phase] ?? 36;
}

function isLocalRepairProgressActive(phase: string): boolean {
  return phase !== 'completed' && phase !== 'failed';
}

function getTimeOfDay(date = new Date()): TimeOfDay {
  const hour = date.getHours();
  if (hour >= 5 && hour < 12) return 'morning';
  if (hour >= 12 && hour < 17) return 'afternoon';
  if (hour >= 17 && hour < 21) return 'evening';
  return 'night';
}

function buildLocalEnvironmentDisplayItems(items: LocalDependencyItem[]): LocalDependencyItem[] {
  const byId = new Map(items.map((item) => [item.id, item]));
  const consumed = new Set<string>();
  const displayItems: LocalDependencyItem[] = [];
  const nodeNpmItems = [byId.get('node'), byId.get('npm')].filter(
    (item): item is LocalDependencyItem => Boolean(item)
  );

  if (nodeNpmItems.length > 0) {
    nodeNpmItems.forEach((item) => consumed.add(item.id));
    displayItems.push(mergeNodeNpmDisplayItem(nodeNpmItems));
  }

  LOCAL_ENVIRONMENT_DISPLAY_ORDER.slice(1).forEach((id) => {
    const item = byId.get(id);
    if (!item) return;
    consumed.add(item.id);
    displayItems.push(item);
  });

  items.forEach((item) => {
    if (!consumed.has(item.id)) {
      displayItems.push(item);
    }
  });

  return displayItems;
}

function mergeNodeNpmDisplayItem(items: LocalDependencyItem[]): LocalDependencyItem {
  const representative = selectMostSevereLocalDependency(items);

  return {
    id: NODE_NPM_DISPLAY_ID,
    name: 'Node.js / npm',
    status: representative.status,
    severity: selectMostSevereLocalDependencySeverity(items),
    version: mergeLocalDependencyValues(items, (item) => item.version),
    path: mergeLocalDependencyValues(items, (item) => item.path),
    detail: mergeLocalDependencyMessages(items, (item) => item.detail),
    recommendation: mergeLocalDependencyMessages(items, (item) => item.recommendation),
    repairActionId: selectLocalDependencyRepairAction(items, representative),
  };
}

function selectMostSevereLocalDependency(items: LocalDependencyItem[]): LocalDependencyItem {
  return items.reduce((selected, item) =>
    LOCAL_DEPENDENCY_STATUS_WEIGHT[item.status] > LOCAL_DEPENDENCY_STATUS_WEIGHT[selected.status]
      ? item
      : selected
  );
}

function selectMostSevereLocalDependencySeverity(
  items: LocalDependencyItem[]
): LocalDependencyItem['severity'] {
  return items.reduce(
    (selected, item) =>
      LOCAL_DEPENDENCY_SEVERITY_WEIGHT[item.severity] > LOCAL_DEPENDENCY_SEVERITY_WEIGHT[selected]
        ? item.severity
        : selected,
    items[0]?.severity ?? 'required'
  );
}

function selectLocalDependencyRepairAction(
  items: LocalDependencyItem[],
  representative: LocalDependencyItem
) {
  if (representative.repairActionId) {
    return representative.repairActionId;
  }

  const sharedNodeNpmAction = items.find(
    (item) => item.repairActionId === NODE_NPM_REPAIR_ACTION_ID
  )?.repairActionId;
  return sharedNodeNpmAction ?? items.find((item) => item.repairActionId)?.repairActionId ?? null;
}

function mergeLocalDependencyMessages(
  items: LocalDependencyItem[],
  selectValue: (item: LocalDependencyItem) => string
) {
  return items
    .map((item) => {
      const value = selectValue(item).trim();
      return value ? `${item.name}：${value}` : '';
    })
    .filter(Boolean)
    .join('；');
}

function mergeLocalDependencyValues(
  items: LocalDependencyItem[],
  selectValue: (item: LocalDependencyItem) => string | null | undefined
) {
  const values = items
    .map((item) => {
      const value = selectValue(item)?.trim();
      return value ? `${item.name} ${value}` : '';
    })
    .filter(Boolean);

  return values.length > 0 ? values.join(' / ') : null;
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
  const [localReadyItemsOpen, setLocalReadyItemsOpen] = useState(false);
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
  const localEnvironmentDesktopMode = isDesktopMode();
  const unknownErrorMessage = t('common.unknown_error');

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
        timeoutMs: DASHBOARD_MODELS_FETCH_TIMEOUT_MS,
      });
    } catch {
      // Ignore model fetch errors on dashboard
    }
  }, [connectionStatus, apiBase, resolveApiKeysForModels, fetchModelsFromStore]);

  const fetchLocalEnvironment = useCallback(async () => {
    if (!localEnvironmentDesktopMode) {
      setLocalEnvironmentError('');
      setLocalEnvironmentSnapshot(null);
      setLocalReadyItemsOpen(false);
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
      const message = error instanceof Error ? error.message : unknownErrorMessage;
      setLocalEnvironmentError(message);
    } finally {
      if (localEnvironmentRequestRef.current === request) {
        localEnvironmentRequestRef.current = null;
      }
      setLocalEnvironmentLoading(false);
    }
  }, [localEnvironmentDesktopMode, unknownErrorMessage]);

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

  useDesktopDataChanged(
    ['config', 'providers', 'auth-files', 'quota'],
    () => {
      if (connectionStatus === 'connected') {
        void fetchStats();
        scheduleDashboardIdleTask(() => void fetchModels());
      }
    },
    connectionStatus === 'connected'
  );

  useEffect(() => {
    if (!localEnvironmentDesktopMode || repairingTarget) return;

    let disposed = false;
    const refresh = () => {
      if (disposed || document.hidden) return;
      void fetchLocalEnvironment();
    };
    refresh();
    const intervalId = window.setInterval(refresh, LOCAL_ENVIRONMENT_AUTO_REFRESH_MS);
    const handleVisibilityChange = () => {
      if (!document.hidden) {
        refresh();
      }
    };

    document.addEventListener('visibilitychange', handleVisibilityChange);
    return () => {
      disposed = true;
      window.clearInterval(intervalId);
      document.removeEventListener('visibilitychange', handleVisibilityChange);
    };
  }, [fetchLocalEnvironment, localEnvironmentDesktopMode, repairingTarget]);

  useDesktopDataChanged(
    ['local-environment'],
    () => {
      if (!repairingTarget) {
        void fetchLocalEnvironment();
      }
    },
    localEnvironmentDesktopMode
  );

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

  const startLocalDependencyRepair = async (itemId: string, actionId: string) => {
    setRepairingTarget({ itemId, actionId });
    setLocalRepairProgressItemId(itemId);
    setLocalRepairProgress({
      actionId,
      phase: 'starting',
      message: t('dashboard.local_environment_repair_progress_starting'),
      detail: null,
      commandLine: null,
      recentOutput: [],
      logPath: null,
      updatedAt: new Date().toISOString(),
      exitCode: null,
    });
    try {
      const response = await runLocalDependencyRepair(actionId, (progress) => {
        setLocalRepairProgressItemId(itemId);
        setLocalRepairProgress(progress);
      });
      setLocalRepairProgress((current) => ({
        actionId,
        phase: response.result.succeeded ? 'completed' : 'failed',
        message: response.result.summary,
        detail: response.result.detail || null,
        commandLine: current?.actionId === actionId ? current.commandLine : null,
        recentOutput: current?.actionId === actionId ? current.recentOutput : [],
        logPath:
          response.result.logPath ?? (current?.actionId === actionId ? current.logPath : null),
        updatedAt: new Date().toISOString(),
        exitCode: response.result.exitCode ?? null,
      }));
      if (response.snapshot) {
        setLocalEnvironmentSnapshot(response.snapshot);
        setLocalReadyItemsOpen(false);
      } else {
        await fetchLocalEnvironment();
      }
    } catch (error) {
      const message = error instanceof Error ? error.message : t('common.unknown_error');
      setLocalRepairProgressItemId(itemId);
      setLocalRepairProgress((current) => ({
        actionId,
        phase: 'failed',
        message,
        detail: message,
        commandLine: current?.actionId === actionId ? current.commandLine : null,
        recentOutput: current?.actionId === actionId ? current.recentOutput : [],
        logPath: current?.actionId === actionId ? current.logPath : null,
        updatedAt: new Date().toISOString(),
        exitCode: current?.actionId === actionId ? current.exitCode : null,
      }));
      showNotification(message, 'error');
    } finally {
      setRepairingTarget((current) =>
        current?.itemId === itemId && current.actionId === actionId ? null : current
      );
    }
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
      showNotification(
        capability.detail || t('dashboard.local_environment_repair_unavailable'),
        'warning'
      );
      return;
    }

    showConfirmation({
      title: t('dashboard.local_environment_repair_confirm_title'),
      message: t('dashboard.local_environment_repair_confirm_message', { name: item.name }),
      confirmText: t('common.confirm'),
      variant: 'danger',
      onConfirm: () => {
        const actionId = item.repairActionId!;
        void startLocalDependencyRepair(item.id, actionId);
      },
    });
  };

  const handleRepairRequiredEnvironment = () => {
    if (!isDesktopMode()) {
      showNotification(t('dashboard.local_environment_desktop_required'), 'warning');
      return;
    }

    const capability = localEnvironmentSnapshot?.repairCapabilities.find(
      (candidate) => candidate.actionId === REQUIRED_ENVIRONMENT_REPAIR_ACTION_ID
    );
    if (!capability?.isAvailable) {
      showNotification(
        capability?.detail || t('dashboard.local_environment_required_repair_unavailable'),
        'warning'
      );
      return;
    }

    showConfirmation({
      title: t('dashboard.local_environment_required_repair_confirm_title'),
      message: t('dashboard.local_environment_required_repair_confirm_message'),
      confirmText: t('common.confirm'),
      variant: 'danger',
      onConfirm: () => {
        void startLocalDependencyRepair(
          REQUIRED_ENVIRONMENT_REPAIR_TARGET_ID,
          REQUIRED_ENVIRONMENT_REPAIR_ACTION_ID
        );
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
  const localEnvironmentItems = buildLocalEnvironmentDisplayItems(
    localEnvironmentSnapshot?.items ?? []
  );
  const localReadyItems = localEnvironmentItems.filter((item) => item.status === 'ready');
  const localIssueItems = localEnvironmentItems.filter((item) => item.status !== 'ready');
  const localRequiredReady = localRequiredItems.filter((item) => item.status === 'ready').length;
  const localOptionalUnavailable =
    localEnvironmentSnapshot?.items.filter((item) => item.status === 'optionalUnavailable')
      .length ?? 0;
  const localScore = localEnvironmentSnapshot?.readinessScore ?? null;
  const localCheckedAt =
    localEnvironmentSnapshot?.checkedAt &&
    new Date(localEnvironmentSnapshot.checkedAt).toLocaleString(i18n.language);
  const requiredEnvironmentRepairCapability = localEnvironmentSnapshot?.repairCapabilities.find(
    (candidate) => candidate.actionId === REQUIRED_ENVIRONMENT_REPAIR_ACTION_ID
  );
  const isRequiredEnvironmentRepairing =
    repairingTarget?.itemId === REQUIRED_ENVIRONMENT_REPAIR_TARGET_ID &&
    repairingTarget.actionId === REQUIRED_ENVIRONMENT_REPAIR_ACTION_ID;
  const requiredEnvironmentRepairProgress =
    localRepairProgressItemId === REQUIRED_ENVIRONMENT_REPAIR_TARGET_ID &&
    localRepairProgress?.actionId === REQUIRED_ENVIRONMENT_REPAIR_ACTION_ID
      ? localRepairProgress
      : null;
  const requiredEnvironmentRepairAvailable =
    localEnvironmentDesktopMode &&
    Boolean(localEnvironmentSnapshot) &&
    requiredEnvironmentRepairCapability?.isAvailable === true;
  const requiredEnvironmentRepairHint =
    requiredEnvironmentRepairCapability?.detail ??
    (localEnvironmentLoading
      ? t('dashboard.local_environment_required_repair_loading')
      : t('dashboard.local_environment_required_repair_unavailable'));

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
    if (routingStrategyRaw === 'round-robin')
      return t('basic_settings.routing_strategy_round_robin');
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

  const renderLocalRepairProgress = (progress: LocalDependencyRepairProgress) => {
    const progressPercent = getLocalRepairProgressPercent(progress.phase);
    const progressActive = isLocalRepairProgressActive(progress.phase);

    return (
      <div className={styles.localDependencyRepairProgress}>
        <div className={styles.localDependencyRepairProgressHeader}>
          <strong>{getLocalRepairPhaseLabel(progress.phase)}</strong>
          {progress.exitCode !== null && progress.exitCode !== undefined && (
            <span>
              {t('dashboard.local_environment_repair_exit_code', {
                code: progress.exitCode,
              })}
            </span>
          )}
        </div>
        <div
          className={styles.localDependencyRepairProgressTrack}
          role="progressbar"
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={progressPercent}
          aria-busy={progressActive}
        >
          <div
            className={`${styles.localDependencyRepairProgressFill} ${
              progressActive ? styles.localDependencyRepairProgressFillActive : ''
            } ${progress.phase === 'failed' ? styles.localDependencyRepairProgressFillFailed : ''}`}
            style={{ width: `${progressPercent}%` }}
          />
        </div>
        <p>{progress.message}</p>
        {progress.detail && progress.detail !== progress.message && (
          <div className={styles.localDependencyRepairField}>
            <span>{t('dashboard.local_environment_repair_detail')}</span>
            <p>{progress.detail}</p>
          </div>
        )}
        {progress.commandLine && (
          <div className={styles.localDependencyRepairField}>
            <span>{t('dashboard.local_environment_repair_command')}</span>
            <code>{progress.commandLine}</code>
          </div>
        )}
        {progress.recentOutput.length > 0 && (
          <div className={styles.localDependencyRepairField}>
            <span>{t('dashboard.local_environment_repair_recent_output')}</span>
            <pre>{progress.recentOutput.join('\n')}</pre>
          </div>
        )}
        {progress.logPath && (
          <div className={styles.localDependencyRepairField}>
            <span>{t('dashboard.local_environment_repair_log_path')}</span>
            <code>{progress.logPath}</code>
          </div>
        )}
      </div>
    );
  };

  const renderLocalDependencyItem = (item: LocalDependencyItem) => {
    const isRepairingItem =
      repairingTarget?.itemId === item.id && repairingTarget.actionId === item.repairActionId;
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
            <span className={`${styles.localStatusBadge} ${getLocalStatusClass(item.status)}`}>
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
            className={styles.localEnvironmentRepairButton}
            onClick={() => handleRepairLocalDependency(item)}
            loading={isRepairingItem}
            disabled={repairingTarget !== null}
          >
            {isRepairingItem
              ? t('dashboard.local_environment_repairing_button')
              : t('dashboard.local_environment_repair')}
          </Button>
        )}
        {itemRepairProgress && renderLocalRepairProgress(itemRepairProgress)}
      </div>
    );
  };

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
            onClick={() => setLocalEnvironmentOpen((open) => !open)}
          >
            <div className={styles.bentoIcon}>
              <IconShield size={24} />
            </div>
            <div className={styles.bentoContent}>
              <span className={styles.bentoValue}>
                {localEnvironmentLoading && localScore === null
                  ? '...'
                  : localScore === null
                    ? '-'
                    : `${localScore}%`}
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
                    : t('dashboard.local_environment_auto_detecting')}
              </span>
            </div>
          </button>
        </div>
        {localEnvironmentOpen && (
          <div className={styles.localEnvironmentPanel}>
            <div className={styles.localEnvironmentHeader}>
              <div className={styles.localEnvironmentHeaderMain}>
                <h3>{t('dashboard.local_environment_detail')}</h3>
                <p>
                  {localEnvironmentSnapshot?.summary ??
                    (localEnvironmentDesktopMode
                      ? t('dashboard.local_environment_auto_detecting')
                      : t('dashboard.local_environment_desktop_unavailable_summary'))}
                </p>
                {localCheckedAt && (
                  <span>
                    {t('dashboard.local_environment_checked_at', { time: localCheckedAt })}
                  </span>
                )}
              </div>
              <div className={styles.localEnvironmentHeaderActions}>
                <Button
                  type="button"
                  variant="secondary"
                  size="sm"
                  className={styles.localEnvironmentRepairButton}
                  onClick={handleRepairRequiredEnvironment}
                  loading={isRequiredEnvironmentRepairing}
                  disabled={
                    repairingTarget !== null ||
                    localEnvironmentLoading ||
                    !requiredEnvironmentRepairAvailable
                  }
                >
                  {isRequiredEnvironmentRepairing
                    ? t('dashboard.local_environment_required_repairing_button')
                    : t('dashboard.local_environment_required_repair')}
                </Button>
                <small>{requiredEnvironmentRepairHint}</small>
              </div>
            </div>
            {requiredEnvironmentRepairProgress &&
              renderLocalRepairProgress(requiredEnvironmentRepairProgress)}
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
                {localReadyItems.length > 0 && (
                  <div className={styles.localReadyGroup}>
                    <button
                      type="button"
                      className={styles.localReadyToggle}
                      aria-expanded={localReadyItemsOpen}
                      onClick={() => setLocalReadyItemsOpen((open) => !open)}
                    >
                      <span className={styles.localReadyToggleText}>
                        <strong>
                          {t('dashboard.local_environment_ready_group_title', {
                            count: localReadyItems.length,
                          })}
                        </strong>
                        <span>
                          {localReadyItemsOpen
                            ? t('dashboard.local_environment_ready_group_collapse')
                            : t('dashboard.local_environment_ready_group_expand')}
                        </span>
                      </span>
                      {localReadyItemsOpen ? (
                        <IconChevronUp size={16} />
                      ) : (
                        <IconChevronDown size={16} />
                      )}
                    </button>
                    {localReadyItemsOpen && (
                      <div className={styles.localReadyGroupItems}>
                        {localReadyItems.map(renderLocalDependencyItem)}
                      </div>
                    )}
                  </div>
                )}
                {localIssueItems.map(renderLocalDependencyItem)}
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
