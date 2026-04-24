import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Card } from '@/components/ui/Card';
import { Button } from '@/components/ui/Button';
import { Modal } from '@/components/ui/Modal';
import { ToggleSwitch } from '@/components/ui/ToggleSwitch';
import { IconDownload, IconRefreshCw } from '@/components/ui/icons';
import {
  useAuthStore,
  useConfigStore,
  useNotificationStore,
  useModelsStore,
  useThemeStore,
} from '@/stores';
import { configApi, versionApi } from '@/services/api';
import type { LatestVersionInfo } from '@/services/api/version';
import { apiKeysApi } from '@/services/api/apiKeys';
import { classifyModels } from '@/utils/models';
import { STORAGE_KEY_AUTH } from '@/utils/constants';
import { INLINE_LOGO_JPEG } from '@/assets/logoInline';
import iconGemini from '@/assets/icons/gemini.svg';
import iconClaude from '@/assets/icons/claude.svg';
import iconOpenaiLight from '@/assets/icons/openai-light.svg';
import iconOpenaiDark from '@/assets/icons/openai-dark.svg';
import iconQwen from '@/assets/icons/qwen.svg';
import iconKimiLight from '@/assets/icons/kimi-light.svg';
import iconKimiDark from '@/assets/icons/kimi-dark.svg';
import iconGlm from '@/assets/icons/glm.svg';
import iconGrok from '@/assets/icons/grok.svg';
import iconDeepseek from '@/assets/icons/deepseek.svg';
import iconMinimax from '@/assets/icons/minimax.svg';
import { CPA_UV_OVERLAY_METADATA } from '@/overlay/cpaUvOverlayMetadata';
import styles from './SystemPage.module.scss';

const MODEL_CATEGORY_ICONS: Record<string, string | { light: string; dark: string }> = {
  gpt: { light: iconOpenaiLight, dark: iconOpenaiDark },
  claude: iconClaude,
  gemini: iconGemini,
  qwen: iconQwen,
  kimi: { light: iconKimiLight, dark: iconKimiDark },
  glm: iconGlm,
  grok: iconGrok,
  deepseek: iconDeepseek,
  minimax: iconMinimax,
};

const getIsZh = (language?: string) => language?.toLowerCase().startsWith('zh') === true;

const getErrorMessage = (error: unknown) =>
  error instanceof Error
    ? error.message
    : typeof error === 'string'
      ? error
      : 'Request failed';

const getErrorStatus = (error: unknown) =>
  error !== null &&
  typeof error === 'object' &&
  'status' in error &&
  typeof (error as { status?: unknown }).status === 'number'
    ? ((error as { status?: number }).status ?? undefined)
    : undefined;

export function SystemPage() {
  const { t, i18n } = useTranslation();
  const isZh = getIsZh(i18n.language);
  const dt = useCallback(
    (zh: string, en: string) => (isZh ? zh : en),
    [isZh]
  );

  const { showNotification, showConfirmation } = useNotificationStore();
  const resolvedTheme = useThemeStore((state) => state.resolvedTheme);
  const auth = useAuthStore();
  const config = useConfigStore((state) => state.config);
  const fetchConfig = useConfigStore((state) => state.fetchConfig);
  const clearCache = useConfigStore((state) => state.clearCache);
  const updateConfigValue = useConfigStore((state) => state.updateConfigValue);

  const models = useModelsStore((state) => state.models);
  const modelsLoading = useModelsStore((state) => state.loading);
  const modelsError = useModelsStore((state) => state.error);
  const fetchModelsFromStore = useModelsStore((state) => state.fetchModels);

  const [modelStatus, setModelStatus] = useState<{
    type: 'success' | 'warning' | 'error' | 'muted';
    message: string;
  }>();
  const [requestLogModalOpen, setRequestLogModalOpen] = useState(false);
  const [requestLogDraft, setRequestLogDraft] = useState(false);
  const [requestLogTouched, setRequestLogTouched] = useState(false);
  const [requestLogSaving, setRequestLogSaving] = useState(false);
  const [checkingVersion, setCheckingVersion] = useState(false);
  const [installingUpdate, setInstallingUpdate] = useState(false);
  const [latestInfo, setLatestInfo] = useState<LatestVersionInfo | null>(null);
  const [latestInfoError, setLatestInfoError] = useState<string | null>(null);
  const [lastCheckedAt, setLastCheckedAt] = useState<number | null>(null);

  const apiKeysCache = useRef<string[]>([]);
  const versionTapCount = useRef(0);
  const versionTapTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  const otherLabel = useMemo(
    () => (isZh ? '\u5176\u4ed6' : 'Other'),
    [isZh]
  );
  const groupedModels = useMemo(() => classifyModels(models, { otherLabel }), [models, otherLabel]);
  const requestLogEnabled = config?.requestLog ?? false;
  const requestLogDirty = requestLogDraft !== requestLogEnabled;
  const canEditRequestLog = auth.connectionStatus === 'connected' && Boolean(config);

  const managementVersion = __APP_VERSION__ || t('system_info.version_unknown');
  const apiVersion = auth.serverVersion || t('system_info.version_unknown');
  const buildTime = auth.serverBuildDate
    ? new Date(auth.serverBuildDate).toLocaleString(i18n.language)
    : t('system_info.version_unknown');

  const normalizedLatestInfo = latestInfo;
  const localUpdateSource =
    normalizedLatestInfo?.managementSource ||
    (isZh ? '\u672c\u5730\u7ba1\u7406\u901a\u9053' : CPA_UV_OVERLAY_METADATA.updateSourceLabel);

  const getIconForCategory = (categoryId: string): string | null => {
    const iconEntry = MODEL_CATEGORY_ICONS[categoryId];
    if (!iconEntry) return null;
    if (typeof iconEntry === 'string') return iconEntry;
    return resolvedTheme === 'dark' ? iconEntry.dark : iconEntry.light;
  };

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
    } catch (err) {
      console.warn('Auto loading API keys for models failed:', err);
      return [];
    }
  }, [config?.apiKeys]);

  const fetchModels = async ({ forceRefresh = false }: { forceRefresh?: boolean } = {}) => {
    if (auth.connectionStatus !== 'connected') {
      setModelStatus({
        type: 'warning',
        message: t('notification.connection_required'),
      });
      return;
    }

    if (!auth.apiBase) {
      showNotification(t('notification.connection_required'), 'warning');
      return;
    }

    if (forceRefresh) {
      apiKeysCache.current = [];
    }

    setModelStatus({ type: 'muted', message: t('system_info.models_loading') });
    try {
      const apiKeys = await resolveApiKeysForModels();
      const primaryKey = apiKeys[0];
      const list = await fetchModelsFromStore(auth.apiBase, primaryKey, forceRefresh);
      const hasModels = list.length > 0;
      setModelStatus({
        type: hasModels ? 'success' : 'warning',
        message: hasModels
          ? t('system_info.models_count', { count: list.length })
          : t('system_info.models_empty'),
      });
    } catch (err: unknown) {
      const message = getErrorMessage(err);
      const suffix = message ? `: ${message}` : '';
      const text = `${t('system_info.models_error')}${suffix}`;
      setModelStatus({ type: 'error', message: text });
    }
  };

  const refreshLatestInfo = useCallback(
    async (notifyResult: boolean) => {
      if (auth.connectionStatus !== 'connected') {
        if (notifyResult) {
          showNotification(t('notification.connection_required'), 'warning');
        }
        return;
      }

      setCheckingVersion(true);
      setLatestInfoError(null);
      try {
        const info = await versionApi.checkLatest(auth.serverVersion, managementVersion);
        setLatestInfo(info);
        setLastCheckedAt(Date.now());

        if (notifyResult) {
          if (info.updateAvailable === true) {
            const latestLabel =
              info.managementLatestVersion || info.latestVersion || dt('\u6700\u65b0\u7248\u672c', 'latest version');
            showNotification(
              dt(
                `\u68c0\u6d4b\u5230\u53ef\u7528\u66f4\u65b0\uff1a${latestLabel}`,
                `Update available: ${latestLabel}`
              ),
              'warning'
            );
          } else if (info.updateAvailable === false) {
            showNotification(
              dt('\u5f53\u524d\u5df2\u662f\u6700\u65b0\u7248\u672c', 'Already on the latest version'),
              'success'
            );
          } else {
            showNotification(
              dt('\u5df2\u5237\u65b0\u7248\u672c\u4fe1\u606f', 'Version information refreshed'),
              'success'
            );
          }
        }
      } catch (error: unknown) {
        const message = getErrorMessage(error);
        setLatestInfoError(message);
        if (notifyResult) {
          showNotification(
            `${dt('\u68c0\u67e5\u66f4\u65b0\u5931\u8d25', 'Failed to check for updates')}${message ? `: ${message}` : ''}`,
            'error'
          );
        }
      } finally {
        setCheckingVersion(false);
      }
    },
    [auth.connectionStatus, auth.serverVersion, dt, managementVersion, showNotification, t]
  );

  const performInstallUpdate = useCallback(async () => {
    setInstallingUpdate(true);
    try {
      const result = await versionApi.installLatest();
      const latestLabel = result.latestVersion || normalizedLatestInfo?.managementLatestVersion || normalizedLatestInfo?.latestVersion;

      if (result.status === 'installing') {
        showNotification(
          dt(
            '\u5df2\u63d0\u4ea4\u66f4\u65b0\u5b89\u88c5\u8bf7\u6c42\uff0c\u5b8c\u6210\u540e\u8bf7\u91cd\u542f\u684c\u9762\u5e94\u7528\u3002',
            'The update installation has started. Restart the desktop app after it finishes.'
          ),
          'warning'
        );
      } else if (result.status === 'already-latest') {
        showNotification(
          dt('\u5f53\u524d\u5df2\u662f\u6700\u65b0\u7248\u672c', 'Already on the latest version'),
          'success'
        );
      } else if (result.status === 'install_unsupported') {
        showNotification(
          result.message ||
            dt(
              '\u5f53\u524d\u73af\u5883\u4e0d\u652f\u6301\u81ea\u52a8\u5b89\u88c5\u66f4\u65b0',
              'Automatic update installation is not supported in this environment.'
            ),
          'warning'
        );
      } else {
        showNotification(
          result.message ||
            (latestLabel
              ? dt(`\u5df2\u63d0\u4ea4\u66f4\u65b0\uff1a${latestLabel}`, `Update request sent: ${latestLabel}`)
              : dt('\u5df2\u63d0\u4ea4\u66f4\u65b0\u8bf7\u6c42', 'Update request sent')),
          'success'
        );
      }

      await refreshLatestInfo(false);
    } catch (error: unknown) {
      const status = getErrorStatus(error);
      const message = getErrorMessage(error);
      if ([404, 405, 501].includes(status ?? 0)) {
        const fallbackMessage = dt(
          '\u5f53\u524d\u540e\u7aef\u4e0d\u652f\u6301\u5b89\u88c5\u66f4\u65b0\uff0c\u7cfb\u7edf\u9875\u5df2\u81ea\u52a8\u964d\u7ea7\u4e3a\u53ea\u8bfb\u7248\u672c\u68c0\u67e5\u3002',
          'This backend does not support install-update. The System page has been downgraded to read-only version checks.'
        );
        setLatestInfoError(fallbackMessage);
        showNotification(fallbackMessage, 'warning');
      } else {
        showNotification(
          `${dt('\u5b89\u88c5\u66f4\u65b0\u5931\u8d25', 'Failed to install update')}${message ? `: ${message}` : ''}`,
          'error'
        );
      }
    } finally {
      setInstallingUpdate(false);
    }
  }, [dt, normalizedLatestInfo?.latestVersion, normalizedLatestInfo?.managementLatestVersion, refreshLatestInfo, showNotification]);

  const handleInstallUpdate = useCallback(() => {
    showConfirmation({
      title: dt('\u5b89\u88c5\u66f4\u65b0', 'Install update'),
      message: dt(
        '\u786e\u8ba4\u901a\u8fc7\u672c\u5730\u7ba1\u7406\u901a\u9053\u5b89\u88c5\u6700\u65b0\u7248\u672c\uff1f',
        'Install the latest version through the local management channel?'
      ),
      confirmText: dt('\u7acb\u5373\u5b89\u88c5', 'Install now'),
      onConfirm: () => {
        void performInstallUpdate();
      },
    });
  }, [dt, performInstallUpdate, showConfirmation]);

  const handleClearLoginStorage = () => {
    showConfirmation({
      title: t('system_info.clear_login_title', { defaultValue: 'Clear Login Storage' }),
      message: t('system_info.clear_login_confirm'),
      variant: 'danger',
      confirmText: t('common.confirm'),
      onConfirm: () => {
        auth.logout();
        if (typeof localStorage === 'undefined') return;
        const keysToRemove = [STORAGE_KEY_AUTH, 'isLoggedIn', 'apiBase', 'apiUrl', 'managementKey'];
        keysToRemove.forEach((key) => localStorage.removeItem(key));
        showNotification(t('notification.login_storage_cleared'), 'success');
      },
    });
  };

  const openRequestLogModal = useCallback(() => {
    setRequestLogTouched(false);
    setRequestLogDraft(requestLogEnabled);
    setRequestLogModalOpen(true);
  }, [requestLogEnabled]);

  const handleInfoVersionTap = useCallback(() => {
    versionTapCount.current += 1;
    if (versionTapTimer.current) {
      clearTimeout(versionTapTimer.current);
    }

    if (versionTapCount.current >= 7) {
      versionTapCount.current = 0;
      versionTapTimer.current = null;
      openRequestLogModal();
      return;
    }

    versionTapTimer.current = setTimeout(() => {
      versionTapCount.current = 0;
      versionTapTimer.current = null;
    }, 1500);
  }, [openRequestLogModal]);

  const handleRequestLogClose = useCallback(() => {
    setRequestLogModalOpen(false);
    setRequestLogTouched(false);
  }, []);

  const handleRequestLogSave = async () => {
    if (!canEditRequestLog) return;
    if (!requestLogDirty) {
      setRequestLogModalOpen(false);
      return;
    }

    const previous = requestLogEnabled;
    setRequestLogSaving(true);
    updateConfigValue('request-log', requestLogDraft);

    try {
      await configApi.updateRequestLog(requestLogDraft);
      clearCache('request-log');
      showNotification(t('notification.request_log_updated'), 'success');
      setRequestLogModalOpen(false);
    } catch (error: unknown) {
      const message = getErrorMessage(error);
      updateConfigValue('request-log', previous);
      showNotification(
        `${t('notification.update_failed')}${message ? `: ${message}` : ''}`,
        'error'
      );
    } finally {
      setRequestLogSaving(false);
    }
  };

  useEffect(() => {
    fetchConfig().catch(() => {
      // ignore
    });
  }, [fetchConfig]);

  useEffect(() => {
    if (requestLogModalOpen && !requestLogTouched) {
      setRequestLogDraft(requestLogEnabled);
    }
  }, [requestLogModalOpen, requestLogTouched, requestLogEnabled]);

  useEffect(() => {
    return () => {
      if (versionTapTimer.current) {
        clearTimeout(versionTapTimer.current);
      }
    };
  }, []);

  useEffect(() => {
    fetchModels();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [auth.connectionStatus, auth.apiBase]);

  useEffect(() => {
    if (auth.connectionStatus !== 'connected') {
      return;
    }

    void refreshLatestInfo(false);
  }, [auth.connectionStatus, refreshLatestInfo]);

  const updateSummary = useMemo(() => {
    if (latestInfoError) {
      return {
        type: 'error' as const,
        message: latestInfoError,
      };
    }

    if (!normalizedLatestInfo) {
      return null;
    }

    if (normalizedLatestInfo.updateAvailable === true) {
      return {
        type: 'warning' as const,
        message: dt(
          '\u68c0\u6d4b\u5230\u53ef\u7528\u66f4\u65b0',
          'An update is available'
        ),
      };
    }

    if (normalizedLatestInfo.updateAvailable === false) {
      return {
        type: 'success' as const,
        message: dt(
          '\u5f53\u524d\u5df2\u662f\u6700\u65b0\u7248\u672c',
          'Already on the latest version'
        ),
      };
    }

    return {
      type: 'muted' as const,
      message: dt(
        '\u5df2\u5237\u65b0\u7248\u672c\u4fe1\u606f',
        'Version information refreshed'
      ),
    };
  }, [dt, latestInfoError, normalizedLatestInfo]);

  const installSupported = normalizedLatestInfo?.installSupported === true;
  const serverLatestVersion = normalizedLatestInfo?.latestVersion || dt('\u672a\u77e5', 'Unknown');
  const managementLatestVersion =
    normalizedLatestInfo?.managementLatestVersion ||
    normalizedLatestInfo?.latestVersion ||
    dt('\u672a\u77e5', 'Unknown');
  const installModeLabel = installSupported
    ? dt('\u53ef\u5b89\u88c5', 'Install supported')
    : dt('\u53ea\u8bfb\u68c0\u67e5', 'Read-only check');
  const lastCheckedLabel = lastCheckedAt
    ? new Date(lastCheckedAt).toLocaleString(i18n.language)
    : dt('\u5c1a\u672a\u68c0\u67e5', 'Not checked yet');

  return (
    <div className={styles.container}>
      <h1 className={styles.pageTitle}>{t('system_info.title')}</h1>
      <div className={styles.content}>
        <Card className={styles.aboutCard}>
          <div className={styles.aboutHeader}>
            <img src={INLINE_LOGO_JPEG} alt={CPA_UV_OVERLAY_METADATA.brandName} className={styles.aboutLogo} />
            <div className={styles.aboutTitle}>{CPA_UV_OVERLAY_METADATA.brandName}</div>
          </div>

          <div className={styles.aboutInfoGrid}>
            <button
              type="button"
              className={`${styles.infoTile} ${styles.tapTile}`}
              onClick={handleInfoVersionTap}
            >
              <div className={styles.tileHeader}>
                <div className={styles.tileLabel}>{dt('\u7ba1\u7406\u754c\u9762\u7248\u672c', 'Management UI version')}</div>
              </div>
              <div className={styles.tileValue}>{managementVersion}</div>
              <div className={styles.tileSub}>{CPA_UV_OVERLAY_METADATA.managementVersion}</div>
            </button>

            <div className={styles.infoTile}>
              <div className={styles.tileHeader}>
                <div className={styles.tileLabel}>{t('footer.api_version')}</div>
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  className={styles.tileAction}
                  onClick={() => void refreshLatestInfo(true)}
                  loading={checkingVersion}
                  title={dt('\u68c0\u67e5\u66f4\u65b0', 'Check updates')}
                  aria-label={dt('\u68c0\u67e5\u66f4\u65b0', 'Check updates')}
                >
                  {dt('\u68c0\u67e5\u66f4\u65b0', 'Check updates')}
                </Button>
              </div>
              <div className={styles.tileValue}>{apiVersion}</div>
              <div className={styles.tileSub}>{dt('\u540e\u7aef\u7248\u672c', 'Server version')}</div>
            </div>

            <div className={styles.infoTile}>
              <div className={styles.tileLabel}>{t('footer.build_date')}</div>
              <div className={styles.tileValue}>{buildTime}</div>
            </div>

            <div className={styles.infoTile}>
              <div className={styles.tileLabel}>{t('connection.status')}</div>
              <div className={styles.tileValue}>{t(`common.${auth.connectionStatus}_status`)}</div>
              <div className={styles.tileSub}>{auth.apiBase || '-'}</div>
            </div>
          </div>
        </Card>

        <Card
          title={dt('\u66f4\u65b0', 'Updates')}
          extra={
            <Button
              variant="secondary"
              size="sm"
              onClick={() => void refreshLatestInfo(true)}
              loading={checkingVersion}
            >
              <span className={styles.groupTitle}>
                <IconRefreshCw size={16} />
                <span>{dt('\u5237\u65b0\u72b6\u6001', 'Refresh status')}</span>
              </span>
            </Button>
          }
        >
          <p className={styles.sectionDescription}>
            {dt(
              '\u4f7f\u7528\u672c\u5730\u7ba1\u7406\u901a\u9053\u68c0\u67e5\u684c\u9762\u5e94\u7528\u4e0e\u7ba1\u7406\u754c\u9762\u7684\u7248\u672c\u72b6\u6001\uff0c\u4e0d\u663e\u793a\u5916\u90e8\u4ed3\u5e93\u94fe\u63a5\u3002',
              'Use the local management channel to inspect desktop and management UI versions without exposing external repository links.'
            )}
          </p>

          {updateSummary && (
            <div className={`status-badge ${updateSummary.type}`}>{updateSummary.message}</div>
          )}
          {normalizedLatestInfo?.installNote && (
            <div className="status-badge warning">{normalizedLatestInfo.installNote}</div>
          )}

          <div className={styles.versionInfo}>
            <div className={styles.versionItem}>
              <div className={styles.label}>{dt('\u540e\u7aef\u5f53\u524d', 'Server current')}</div>
              <div className={styles.version}>{apiVersion}</div>
            </div>
            <div className={styles.versionItem}>
              <div className={styles.label}>{dt('\u540e\u7aef\u6700\u65b0', 'Server latest')}</div>
              <div className={styles.version}>{serverLatestVersion}</div>
            </div>
            <div className={styles.versionItem}>
              <div className={styles.label}>{dt('\u7ba1\u7406\u754c\u9762\u5f53\u524d', 'Management UI current')}</div>
              <div className={styles.version}>{managementVersion}</div>
            </div>
            <div className={styles.versionItem}>
              <div className={styles.label}>{dt('\u7ba1\u7406\u754c\u9762\u6700\u65b0', 'Management UI latest')}</div>
              <div className={styles.version}>{managementLatestVersion}</div>
            </div>
            <div className={styles.versionItem}>
              <div className={styles.label}>{dt('\u66f4\u65b0\u6e90', 'Update source')}</div>
              <div className={styles.version}>{localUpdateSource}</div>
              <div className={styles.tileSub}>{CPA_UV_OVERLAY_METADATA.brandingMode}</div>
            </div>
            <div className={styles.versionItem}>
              <div className={styles.label}>{dt('\u66f4\u65b0\u6a21\u5f0f', 'Update mode')}</div>
              <div className={styles.version}>{installModeLabel}</div>
              <div className={styles.tileSub}>
                {dt('\u6700\u8fd1\u68c0\u67e5\uff1a', 'Last checked: ')}
                {lastCheckedLabel}
              </div>
            </div>
          </div>

          <div className={styles.clearLoginActions}>
            <Button
              variant="secondary"
              onClick={() => void refreshLatestInfo(true)}
              loading={checkingVersion}
            >
              {dt('\u91cd\u65b0\u68c0\u67e5', 'Check again')}
            </Button>
            {installSupported && (
              <Button onClick={handleInstallUpdate} loading={installingUpdate}>
                <span className={styles.groupTitle}>
                  <IconDownload size={16} />
                  <span>{dt('\u5b89\u88c5\u6700\u65b0\u7248\u672c', 'Install latest version')}</span>
                </span>
              </Button>
            )}
          </div>
        </Card>

        <Card
          title={t('system_info.models_title')}
          extra={
            <Button
              variant="secondary"
              size="sm"
              onClick={() => fetchModels({ forceRefresh: true })}
              loading={modelsLoading}
            >
              {t('common.refresh')}
            </Button>
          }
        >
          <p className={styles.sectionDescription}>{t('system_info.models_desc')}</p>
          {modelStatus && (
            <div className={`status-badge ${modelStatus.type}`}>{modelStatus.message}</div>
          )}
          {modelsError && <div className="error-box">{modelsError}</div>}
          {modelsLoading ? (
            <div className="hint">{t('common.loading')}</div>
          ) : models.length === 0 ? (
            <div className="hint">{t('system_info.models_empty')}</div>
          ) : (
            <div className="item-list">
              {groupedModels.map((group) => {
                const iconSrc = getIconForCategory(group.id);
                return (
                  <div key={group.id} className="item-row">
                    <div className="item-meta">
                      <div className={styles.groupTitle}>
                        {iconSrc && <img src={iconSrc} alt="" className={styles.groupIcon} />}
                        <span className="item-title">{group.label}</span>
                      </div>
                      <div className="item-subtitle">
                        {t('system_info.models_count', { count: group.items.length })}
                      </div>
                    </div>
                    <div className={styles.modelTags}>
                      {group.items.map((model) => (
                        <span
                          key={`${model.name}-${model.alias ?? 'default'}`}
                          className={styles.modelTag}
                          title={model.description || ''}
                        >
                          <span className={styles.modelName}>{model.name}</span>
                          {model.alias && <span className={styles.modelAlias}>{model.alias}</span>}
                        </span>
                      ))}
                    </div>
                  </div>
                );
              })}
            </div>
          )}
        </Card>

        <Card title={t('system_info.clear_login_title')}>
          <p className={styles.sectionDescription}>{t('system_info.clear_login_desc')}</p>
          <div className={styles.clearLoginActions}>
            <Button variant="danger" onClick={handleClearLoginStorage}>
              {t('system_info.clear_login_button')}
            </Button>
          </div>
        </Card>
      </div>

      <Modal
        open={requestLogModalOpen}
        onClose={handleRequestLogClose}
        title={t('basic_settings.request_log_title')}
        footer={
          <>
            <Button variant="secondary" onClick={handleRequestLogClose} disabled={requestLogSaving}>
              {t('common.cancel')}
            </Button>
            <Button
              onClick={handleRequestLogSave}
              loading={requestLogSaving}
              disabled={!canEditRequestLog || !requestLogDirty}
            >
              {t('common.save')}
            </Button>
          </>
        }
      >
        <div className="request-log-modal">
          <div className="status-badge warning">{t('basic_settings.request_log_warning')}</div>
          <ToggleSwitch
            label={t('basic_settings.request_log_enable')}
            labelPosition="left"
            checked={requestLogDraft}
            disabled={!canEditRequestLog || requestLogSaving}
            onChange={(value) => {
              setRequestLogDraft(value);
              setRequestLogTouched(true);
            }}
          />
        </div>
      </Modal>
    </div>
  );
}
