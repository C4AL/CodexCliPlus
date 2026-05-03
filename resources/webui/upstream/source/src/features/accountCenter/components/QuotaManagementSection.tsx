/**
 * Quota management page - coordinates the three quota sections.
 */

import { useCallback, useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useDesktopDataChanged } from '@/hooks/useDesktopDataChanged';
import { useAuthStore, useNotificationStore } from '@/stores';
import { authFilesApi, configFileApi } from '@/services/api';
import { QuotaSection, CODEX_CONFIG } from '@/components/quota';
import { runAccountRefreshSteps } from '@/features/accountCenter/refresh';
import { getManagementAccessBlockedMessage } from '@/utils/managementAccess';
import type { AuthFileItem } from '@/types';
import styles from '@/pages/QuotaPage.module.scss';

type RefreshLoadOptions = {
  throwOnError?: boolean;
};

export function QuotaManagementSection() {
  const { t } = useTranslation();
  const connectionStatus = useAuthStore((state) => state.connectionStatus);
  const showNotification = useNotificationStore((state) => state.showNotification);

  const [files, setFiles] = useState<AuthFileItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const refreshInFlightRef = useRef<Promise<void> | null>(null);
  const refreshQueuedRef = useRef(false);
  const refreshScheduleRef = useRef<number | null>(null);
  const filesLoadedRef = useRef(false);

  const disableControls = connectionStatus !== 'connected';

  const loadConfig = useCallback(
    async (options?: RefreshLoadOptions) => {
      try {
        await configFileApi.fetchConfigYaml();
      } catch (err: unknown) {
        const errorMessage =
          getManagementAccessBlockedMessage(err) ??
          (err instanceof Error ? err.message : t('notification.refresh_failed'));
        setError((prev) => prev || errorMessage);
        if (options?.throwOnError) {
          throw err;
        }
      }
    },
    [t]
  );

  const loadFiles = useCallback(
    async (options?: RefreshLoadOptions) => {
      const showBlockingLoading = !filesLoadedRef.current;
      if (showBlockingLoading) {
        setLoading(true);
      }
      setError('');
      try {
        const data = await authFilesApi.list();
        filesLoadedRef.current = true;
        setFiles(data?.files || []);
      } catch (err: unknown) {
        const errorMessage =
          getManagementAccessBlockedMessage(err) ??
          (err instanceof Error ? err.message : t('notification.refresh_failed'));
        setError(errorMessage);
        if (options?.throwOnError) {
          throw err;
        }
      } finally {
        if (showBlockingLoading || !filesLoadedRef.current) {
          setLoading(false);
        }
      }
    },
    [t]
  );

  const handleHeaderRefresh = useCallback(async () => {
    if (refreshInFlightRef.current) {
      refreshQueuedRef.current = true;
      return refreshInFlightRef.current;
    }

    const task = (async () => {
      do {
        refreshQueuedRef.current = false;
        const result = await runAccountRefreshSteps(
          [
            { id: 'auth-files', run: () => loadFiles({ throwOnError: true }) },
            { id: 'config', run: () => loadConfig({ throwOnError: true }) },
          ],
          (message) => {
            setError(message);
            showNotification(message, 'error');
          }
        );
        if (result.stopped) {
          refreshQueuedRef.current = false;
          break;
        }
      } while (refreshQueuedRef.current);
    })();

    refreshInFlightRef.current = task.finally(() => {
      refreshInFlightRef.current = null;
    });
    return refreshInFlightRef.current;
  }, [loadConfig, loadFiles, showNotification]);

  const scheduleHeaderRefresh = useCallback(() => {
    if (refreshScheduleRef.current !== null) {
      return;
    }

    refreshScheduleRef.current = window.setTimeout(() => {
      refreshScheduleRef.current = null;
      void handleHeaderRefresh();
    }, 50);
  }, [handleHeaderRefresh]);

  useDesktopDataChanged(
    ['quota', 'auth-files', 'providers'],
    () => {
      scheduleHeaderRefresh();
    },
    connectionStatus === 'connected'
  );

  useEffect(() => {
    const timer = window.setTimeout(() => {
      void handleHeaderRefresh();
    }, 0);

    return () => window.clearTimeout(timer);
  }, [handleHeaderRefresh]);

  useEffect(
    () => () => {
      if (refreshScheduleRef.current !== null) {
        window.clearTimeout(refreshScheduleRef.current);
        refreshScheduleRef.current = null;
      }
    },
    []
  );

  return (
    <div className={styles.container}>
      <div className={styles.pageHeader}>
        <h1 className={styles.pageTitle}>{t('quota_management.title')}</h1>
        <p className={styles.description}>{t('quota_management.description')}</p>
      </div>

      {error && <div className={styles.errorBox}>{error}</div>}

      <QuotaSection
        config={CODEX_CONFIG}
        files={files}
        loading={loading}
        disabled={disableControls}
      />
    </div>
  );
}
