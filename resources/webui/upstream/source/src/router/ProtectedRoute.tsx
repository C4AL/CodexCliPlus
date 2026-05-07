import { useEffect, useState, type ReactElement } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { useAuthStore } from '@/stores';
import { LoadingSpinner } from '@/components/ui/LoadingSpinner';
import { Button } from '@/components/ui/Button';
import { isDesktopMode, requestNativeLogin } from '@/desktop/bridge';

export function ProtectedRoute({ children }: { children: ReactElement }) {
  const location = useLocation();
  const desktopMode = isDesktopMode();
  const isAuthenticated = useAuthStore((state) => state.isAuthenticated);
  const managementKey = useAuthStore((state) => state.managementKey);
  const apiBase = useAuthStore((state) => state.apiBase);
  const connectionError = useAuthStore((state) => state.connectionError);
  const restoreSession = useAuthStore((state) => state.restoreSession);
  const checkAuth = useAuthStore((state) => state.checkAuth);
  const [checking, setChecking] = useState(() => desktopMode);
  const [retryAttempt, setRetryAttempt] = useState(0);

  useEffect(() => {
    let cancelled = false;

    const tryRestore = async () => {
      if (isAuthenticated) {
        setChecking(false);
        return;
      }

      if (!desktopMode && (!managementKey || !apiBase)) {
        setChecking(false);
        return;
      }

      setChecking(true);
      try {
        const restored = await restoreSession();
        let authorized = restored;
        if (!restored && !desktopMode && managementKey && apiBase) {
          authorized = await checkAuth();
        }

        if (!authorized && desktopMode) {
          return;
        }
      } catch (error) {
        if (desktopMode) {
          console.warn('Desktop authentication check failed:', error);
        }
      } finally {
        if (!cancelled) {
          setChecking(false);
        }
      }
    };
    tryRestore();
    return () => {
      cancelled = true;
    };
  }, [apiBase, checkAuth, desktopMode, isAuthenticated, managementKey, restoreSession, retryAttempt]);

  const desktopAuthMessage = connectionError || '无法通过桌面代理恢复当前管理会话。';

  if (checking) {
    return (
      <div className="main-content">
        <LoadingSpinner />
      </div>
    );
  }

  if (!isAuthenticated && desktopMode) {
    return (
      <div className="main-content">
        <div className="card" style={{ margin: 'auto', maxWidth: 520, width: '100%' }}>
          <div className="card-header">
            <div className="title">桌面会话需要恢复</div>
          </div>
          <p style={{ color: 'var(--text-secondary)', margin: 0 }}>
            无法使用当前桌面会话进入管理界面。
          </p>
          <p
            style={{
              color: 'var(--error-color)',
              margin: '12px 0 0',
              wordBreak: 'break-word'
            }}
          >
            {desktopAuthMessage}
          </p>
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 12, marginTop: 20 }}>
            <Button onClick={() => setRetryAttempt((attempt) => attempt + 1)}>重试</Button>
            <Button variant="secondary" onClick={() => requestNativeLogin(desktopAuthMessage)}>
              返回登录
            </Button>
          </div>
        </div>
      </div>
    );
  }

  if (!isAuthenticated) {
    return <Navigate to="/login" replace state={{ from: location }} />;
  }

  return children;
}
