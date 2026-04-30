import React, { useEffect, useMemo, useState, useCallback } from 'react';
import { Navigate, useNavigate, useLocation } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { SelectionCheckbox } from '@/components/ui/SelectionCheckbox';
import { IconEye, IconEyeOff } from '@/components/ui/icons';
import { useAuthStore, useNotificationStore } from '@/stores';
import { detectApiBaseFromLocation, normalizeApiBase } from '@/utils/connection';
import { LOGO_JPEG_URL } from '@/assets/logo';
import { getDesktopBootstrap, isDesktopMode } from '@/desktop/bridge';
import type { ApiError } from '@/types';
import styles from './LoginPage.module.scss';

/**
 * 将 API 错误转换为本地化的用户友好消息
 */
type RedirectState = { from?: { pathname?: string } };

function getLocalizedErrorMessage(error: unknown, t: (key: string) => string): string {
  const apiError = error as Partial<ApiError>;
  const status = typeof apiError.status === 'number' ? apiError.status : undefined;
  const code = typeof apiError.code === 'string' ? apiError.code : undefined;
  const message =
    error instanceof Error
      ? error.message
      : typeof apiError.message === 'string'
        ? apiError.message
        : typeof error === 'string'
          ? error
          : '';

  // 根据 HTTP 状态码判断
  if (status === 401) {
    return t('login.error_unauthorized');
  }
  if (status === 403) {
    return t('login.error_forbidden');
  }
  if (status === 404) {
    return t('login.error_not_found');
  }
  if (status && status >= 500) {
    return t('login.error_server');
  }

  // 根据 axios 错误码判断
  if (code === 'ECONNABORTED' || message.toLowerCase().includes('timeout')) {
    return t('login.error_timeout');
  }
  if (code === 'ERR_NETWORK' || message.toLowerCase().includes('network error')) {
    return t('login.error_network');
  }
  if (code === 'ERR_CERT_AUTHORITY_INVALID' || message.toLowerCase().includes('certificate')) {
    return t('login.error_ssl');
  }

  // 检查 CORS 错误
  if (message.toLowerCase().includes('cors') || message.toLowerCase().includes('cross-origin')) {
    return t('login.error_cors');
  }

  // 默认错误消息
  return t('login.error_invalid');
}

export function LoginPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const location = useLocation();
  const { showNotification } = useNotificationStore();
  const isAuthenticated = useAuthStore((state) => state.isAuthenticated);
  const login = useAuthStore((state) => state.login);
  const restoreSession = useAuthStore((state) => state.restoreSession);
  const storedBase = useAuthStore((state) => state.apiBase);
  const storedKey = useAuthStore((state) => state.managementKey);
  const storedRememberPassword = useAuthStore((state) => state.rememberPassword);

  const [apiBase, setApiBase] = useState('');
  const [managementKey, setManagementKey] = useState('');
  const [showCustomBase, setShowCustomBase] = useState(false);
  const [showKey, setShowKey] = useState(false);
  const [rememberPassword, setRememberPassword] = useState(false);
  const [loading, setLoading] = useState(false);
  const [autoLoading, setAutoLoading] = useState(true);
  const [autoLoginSuccess, setAutoLoginSuccess] = useState(false);
  const [error, setError] = useState('');

  const desktopMode = useMemo(() => isDesktopMode(), []);
  const detectedBase = useMemo(() => detectApiBaseFromLocation(), []);
  const activeApiBase = apiBase || detectedBase;
  const connectionModeLabel = desktopMode
    ? t('login.connection_desktop_bridge')
    : t('login.connection_browser');
  const autoLoginLabel = autoLoading
    ? t('login.auto_login_checking')
    : autoLoginSuccess
      ? t('login.auto_login_success')
      : storedRememberPassword
        ? t('login.auto_login_saved')
        : t('login.auto_login_manual');

  useEffect(() => {
    const init = async () => {
      try {
        const autoLoggedIn = await restoreSession();
        if (autoLoggedIn) {
          setAutoLoginSuccess(true);
          // 延迟跳转，让用户看到成功动画
          setTimeout(() => {
            const redirect = (location.state as RedirectState | null)?.from?.pathname || '/';
            navigate(redirect, { replace: true });
          }, 1500);
        } else {
          const latestAuthState = useAuthStore.getState();
          const desktopBootstrap = desktopMode ? getDesktopBootstrap() : null;
          const fallbackKey = latestAuthState.managementKey || storedKey || '';

          setApiBase(desktopBootstrap?.apiBase || latestAuthState.apiBase || storedBase || detectedBase);
          setManagementKey(desktopBootstrap?.managementKey || fallbackKey);
          setRememberPassword(
            desktopMode
              ? false
              : latestAuthState.rememberPassword || storedRememberPassword || Boolean(fallbackKey)
          );
        }
      } finally {
        if (!autoLoginSuccess) {
          setAutoLoading(false);
        }
      }
    };

    init();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const handleSubmit = useCallback(async () => {
    const desktopBootstrap = desktopMode ? getDesktopBootstrap() : null;
    const keyToUse = desktopMode
      ? desktopBootstrap?.managementKey || managementKey
      : managementKey.trim();

    if (!keyToUse.trim()) {
      setError(t('login.error_required'));
      return;
    }

    const baseToUse = desktopMode
      ? normalizeApiBase(desktopBootstrap?.apiBase || apiBase || detectedBase)
      : apiBase
        ? normalizeApiBase(apiBase)
        : detectedBase;
    setLoading(true);
    setError('');
    try {
      await login({
        apiBase: baseToUse,
        managementKey: keyToUse.trim(),
        rememberPassword: desktopMode ? false : rememberPassword
      });
      showNotification(t('common.connected_status'), 'success');
      navigate('/', { replace: true });
    } catch (err: unknown) {
      const message = getLocalizedErrorMessage(err, t);
      setError(message);
      showNotification(`${t('notification.login_failed')}: ${message}`, 'error');
    } finally {
      setLoading(false);
    }
  }, [
    apiBase,
    desktopMode,
    detectedBase,
    login,
    managementKey,
    navigate,
    rememberPassword,
    showNotification,
    t
  ]);

  const handleSubmitKeyDown = useCallback(
    (event: React.KeyboardEvent) => {
      if (event.key === 'Enter' && !loading) {
        event.preventDefault();
        handleSubmit();
      }
    },
    [loading, handleSubmit]
  );

  const handleClearDraft = useCallback(() => {
    setManagementKey('');
    setError('');
  }, []);

  const handleUseDetectedBase = useCallback(() => {
    setApiBase(detectedBase);
    setShowCustomBase(false);
    setError('');
  }, [detectedBase]);

  if (isAuthenticated && !autoLoading && !autoLoginSuccess) {
    const redirect = (location.state as RedirectState | null)?.from?.pathname || '/';
    return <Navigate to={redirect} replace />;
  }

  // 显示启动动画（自动登录中或自动登录成功）
  const showSplash = autoLoading || autoLoginSuccess;

  return (
    <div className={styles.container}>
      {/* 左侧品牌展示区 */}
      <div className={styles.brandPanel}>
        <div className={styles.brandContent}>
          <img src={LOGO_JPEG_URL} alt="CodexCliPlus" className={styles.brandLogo} />
          <span className={styles.brandWord}>CodexCliPlus</span>
          <div className={styles.brandStatus}>
            <span>{connectionModeLabel}</span>
            <strong>{activeApiBase || '-'}</strong>
          </div>
        </div>
      </div>

      {/* 右侧功能交互区 */}
      <div className={styles.formPanel}>
        {showSplash ? (
          /* 启动动画 */
          <div className={styles.splashContent}>
            <img src={LOGO_JPEG_URL} alt="CodexCliPlus" className={styles.splashLogo} />
            <h1 className={styles.splashTitle}>{t('splash.title')}</h1>
            <p className={styles.splashSubtitle}>{t('splash.subtitle')}</p>
            <div className={styles.splashLoader}>
              <div className={styles.splashLoaderBar} />
            </div>
          </div>
        ) : (
          /* 登录表单 */
          <div className={styles.formContent}>
            {/* Logo */}
            <img src={LOGO_JPEG_URL} alt="Logo" className={styles.logo} />

            {/* 登录表单卡片 */}
            <div className={styles.loginCard}>
              <div className={styles.loginHeader}>
                <div className={styles.titleRow}>
                  <div className={styles.title}>{t('title.login')}</div>
                </div>
                <div className={styles.subtitle}>
                  {desktopMode ? t('login.desktop_subtitle') : t('login.subtitle')}
                </div>
              </div>

              <div className={styles.connectionBox}>
                <div className={styles.connectionHeader}>
                  <div>
                    <div className={styles.label}>{t('login.connection_current')}</div>
                    <div className={styles.value}>{activeApiBase}</div>
                  </div>
                  <span className={styles.connectionBadge}>{connectionModeLabel}</span>
                </div>
                <div className={styles.hint}>
                  {desktopMode ? t('login.desktop_connection_hint') : t('login.connection_auto_hint')}
                </div>
              </div>

              <div className={styles.statusGrid}>
                <div>
                  <span>{t('login.status_key')}</span>
                  <strong>
                    {managementKey.trim() ? t('login.status_key_ready') : t('login.status_key_empty')}
                  </strong>
                </div>
                <div>
                  <span>{t('login.status_auto_login')}</span>
                  <strong>{autoLoginLabel}</strong>
                </div>
              </div>

              {!desktopMode && (
                <>
                  <div className={styles.toggleAdvanced}>
                    <SelectionCheckbox
                      checked={showCustomBase}
                      onChange={setShowCustomBase}
                      ariaLabel={t('login.custom_connection_label')}
                      label={t('login.custom_connection_label')}
                      labelClassName={styles.toggleLabel}
                    />
                  </div>

                  {showCustomBase && (
                    <Input
                      label={t('login.custom_connection_label')}
                      placeholder={t('login.custom_connection_placeholder')}
                      value={apiBase}
                      onChange={(e) => setApiBase(e.target.value)}
                      hint={t('login.custom_connection_hint')}
                    />
                  )}
                </>
              )}

              <Input
                autoFocus
                label={t('login.management_key_label')}
                placeholder={t('login.management_key_placeholder')}
                type={showKey ? 'text' : 'password'}
                value={managementKey}
                onChange={(e) => setManagementKey(e.target.value)}
                onKeyDown={handleSubmitKeyDown}
                rightElement={
                  <button
                    type="button"
                    className="btn btn-ghost btn-sm"
                    onClick={() => setShowKey((prev) => !prev)}
                    aria-label={
                      showKey
                        ? t('login.hide_key', { defaultValue: '隐藏密钥' })
                        : t('login.show_key', { defaultValue: '显示密钥' })
                    }
                    title={
                      showKey
                        ? t('login.hide_key', { defaultValue: '隐藏密钥' })
                        : t('login.show_key', { defaultValue: '显示密钥' })
                    }
                  >
                    {showKey ? <IconEyeOff size={16} /> : <IconEye size={16} />}
                  </button>
                }
              />

              <div className={styles.toggleAdvanced}>
                <SelectionCheckbox
                  checked={desktopMode ? false : rememberPassword}
                  disabled={desktopMode}
                  onChange={setRememberPassword}
                  ariaLabel={t('login.remember_password_label')}
                  label={
                    desktopMode
                      ? t('login.remember_desktop_managed')
                      : t('login.remember_password_label')
                  }
                  labelClassName={styles.toggleLabel}
                />
              </div>

              <Button fullWidth onClick={handleSubmit} loading={loading}>
                {loading
                  ? t('login.submitting')
                  : desktopMode
                    ? t('login.desktop_retry_button')
                    : t('login.submit_button')}
              </Button>

              <div className={styles.recoveryActions}>
                <button type="button" onClick={handleClearDraft}>
                  {t('login.clear_key')}
                </button>
                {!desktopMode && (
                  <button type="button" onClick={handleUseDetectedBase}>
                    {t('login.use_detected_address')}
                  </button>
                )}
              </div>

              {error && <div className={styles.errorBox}>{error}</div>}
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
