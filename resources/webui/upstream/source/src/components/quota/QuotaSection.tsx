/**
 * Generic quota section component.
 */

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Card } from '@/components/ui/Card';
import { Button } from '@/components/ui/Button';
import { EmptyState } from '@/components/ui/EmptyState';
import { triggerHeaderRefresh } from '@/hooks/useHeaderRefresh';
import { useNotificationStore, useQuotaStore, useThemeStore } from '@/stores';
import type { AuthFileItem, ResolvedTheme } from '@/types';
import { getStatusFromError } from '@/utils/quota';
import { QuotaCard } from './QuotaCard';
import type { QuotaStatusState } from './QuotaCard';
import { useQuotaLoader } from './useQuotaLoader';
import type { QuotaConfig } from './quotaConfigs';
import { useGridColumns } from './useGridColumns';
import { IconRefreshCw } from '@/components/ui/icons';
import styles from '@/pages/QuotaPage.module.scss';

type QuotaUpdater<T> = T | ((prev: T) => T);

type QuotaSetter<T> = (updater: QuotaUpdater<T>) => void;

type QuotaScope = 'page' | 'all';

interface QuotaSectionProps<TState extends QuotaStatusState, TData> {
  config: QuotaConfig<TState, TData>;
  files: AuthFileItem[];
  loading: boolean;
  disabled: boolean;
}

export function QuotaSection<TState extends QuotaStatusState, TData>({
  config,
  files,
  loading,
  disabled
}: QuotaSectionProps<TState, TData>) {
  const { t } = useTranslation();
  const resolvedTheme: ResolvedTheme = useThemeStore((state) => state.resolvedTheme);
  const showNotification = useNotificationStore((state) => state.showNotification);
  const setQuota = useQuotaStore((state) => state[config.storeSetter]) as QuotaSetter<
    Record<string, TState>
  >;

  const [, gridRef] = useGridColumns(380);
  const [sectionLoading, setSectionLoading] = useState(false);
  const [autoRefreshEnabled, setAutoRefreshEnabled] = useState(false);
  const [countdown, setCountdown] = useState<number | null>(null);
  const pendingQuotaRefreshRef = useRef(false);
  const prevFilesLoadingRef = useRef(loading);
  const autoRefreshTimerRef = useRef<ReturnType<typeof window.setInterval> | null>(null);

  const filteredFiles = useMemo(
    () => files.filter((file) => config.filterFn(file)),
    [files, config]
  );

  const { quota, loadQuota } = useQuotaLoader(config);

  const setLoading = useCallback((isLoading: boolean, _scope?: QuotaScope | null) => {
    setSectionLoading(isLoading);
  }, []);

  const runQuotaRefresh = useCallback(
    async (targets: AuthFileItem[], scheduleNext: boolean) => {
      if (targets.length === 0) return;
      await loadQuota(targets, 'all', setLoading);
      if (scheduleNext) {
        setCountdown(5);
      }
    },
    [loadQuota, setLoading]
  );

  const handleRefresh = useCallback(() => {
    pendingQuotaRefreshRef.current = true;
    void triggerHeaderRefresh();
  }, []);

  useEffect(() => {
    const wasLoading = prevFilesLoadingRef.current;
    prevFilesLoadingRef.current = loading;

    if (!pendingQuotaRefreshRef.current) return;
    if (loading) return;
    if (!wasLoading) return;

    pendingQuotaRefreshRef.current = false;
    void runQuotaRefresh(filteredFiles, autoRefreshEnabled);
  }, [autoRefreshEnabled, filteredFiles, loading, runQuotaRefresh]);

  useEffect(() => {
    if (!autoRefreshEnabled || countdown === null) return;
    if (autoRefreshTimerRef.current) {
      window.clearInterval(autoRefreshTimerRef.current);
    }

    autoRefreshTimerRef.current = window.setInterval(() => {
      setCountdown((current) => {
        if (current === null) return null;
        if (current <= 1) {
          if (autoRefreshTimerRef.current) {
            window.clearInterval(autoRefreshTimerRef.current);
            autoRefreshTimerRef.current = null;
          }
          pendingQuotaRefreshRef.current = true;
          void triggerHeaderRefresh();
          return null;
        }
        return current - 1;
      });
    }, 1000);

    return () => {
      if (autoRefreshTimerRef.current) {
        window.clearInterval(autoRefreshTimerRef.current);
        autoRefreshTimerRef.current = null;
      }
    };
  }, [autoRefreshEnabled, countdown]);

  useEffect(() => {
    if (autoRefreshEnabled) return;
    const timer = window.setTimeout(() => setCountdown(null), 0);
    return () => window.clearTimeout(timer);
  }, [autoRefreshEnabled]);

  useEffect(() => {
    if (loading) return;
    if (filteredFiles.length === 0) {
      setQuota({});
      return;
    }
    setQuota((prev) => {
      const nextState: Record<string, TState> = {};
      filteredFiles.forEach((file) => {
        const cached = prev[file.name];
        if (cached) {
          nextState[file.name] = cached;
        }
      });
      return nextState;
    });
  }, [filteredFiles, loading, setQuota]);

  const refreshQuotaForFile = useCallback(
    async (file: AuthFileItem) => {
      if (disabled || file.disabled) return;
      if (quota[file.name]?.status === 'loading') return;

      setQuota((prev) => ({
        ...prev,
        [file.name]: config.buildLoadingState()
      }));

      try {
        const data = await config.fetchQuota(file, t);
        setQuota((prev) => ({
          ...prev,
          [file.name]: config.buildSuccessState(data)
        }));
        showNotification(t('auth_files.quota_refresh_success', { name: file.name }), 'success');
      } catch (err: unknown) {
        const message = err instanceof Error ? err.message : t('common.unknown_error');
        const status = getStatusFromError(err);
        setQuota((prev) => ({
          ...prev,
          [file.name]: config.buildErrorState(message, status)
        }));
        showNotification(
          t('auth_files.quota_refresh_failed', { name: file.name, message }),
          'error'
        );
      }
    },
    [config, disabled, quota, setQuota, showNotification, t]
  );

  const titleNode = (
    <div className={styles.titleWrapper}>
      <span>{t(`${config.i18nPrefix}.title`)}</span>
      {filteredFiles.length > 0 && <span className={styles.countBadge}>{filteredFiles.length}</span>}
    </div>
  );

  const isRefreshing = sectionLoading || loading;

  return (
    <Card
      title={titleNode}
      extra={
        <div className={styles.headerActions}>
          <label className={styles.autoRefreshToggle}>
            <input
              type="checkbox"
              checked={autoRefreshEnabled}
              onChange={(event) => setAutoRefreshEnabled(event.currentTarget.checked)}
              disabled={disabled}
            />
            <span>{t('quota_management.auto_refresh')}</span>
            {countdown !== null && (
              <span className={styles.autoRefreshCountdown}>
                {t('quota_management.auto_refresh_countdown', { seconds: countdown })}
              </span>
            )}
          </label>
          <Button
            variant="secondary"
            size="sm"
            className={styles.refreshAllButton}
            onClick={handleRefresh}
            disabled={disabled || isRefreshing}
            loading={isRefreshing}
            title={t('quota_management.refresh_all_credentials')}
            aria-label={t('quota_management.refresh_all_credentials')}
          >
            {!isRefreshing && <IconRefreshCw size={16} />}
            {t('quota_management.refresh_all_credentials')}
          </Button>
        </div>
      }
    >
      {filteredFiles.length === 0 ? (
        <EmptyState
          title={t(`${config.i18nPrefix}.empty_title`)}
          description={t(`${config.i18nPrefix}.empty_desc`)}
        />
      ) : (
        <div ref={gridRef} className={config.gridClassName}>
          {filteredFiles.map((item) => (
            <QuotaCard
              key={item.name}
              item={item}
              quota={quota[item.name]}
              resolvedTheme={resolvedTheme}
              i18nPrefix={config.i18nPrefix}
              cardIdleMessageKey={config.cardIdleMessageKey}
              cardClassName={config.cardClassName}
              defaultType={config.type}
              canRefresh={!disabled && !item.disabled}
              onRefresh={() => void refreshQuotaForFile(item)}
              renderQuotaItems={config.renderQuotaItems}
            />
          ))}
        </div>
      )}
    </Card>
  );
}
