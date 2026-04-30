import {
  ReactNode,
  SVGProps,
  useCallback,
  useEffect,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
  type MutableRefObject,
} from 'react';
import { NavLink, useLocation, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Button } from '@/components/ui/Button';
import { PageTransition } from '@/components/common/PageTransition';
import { MainRoutes } from '@/router/MainRoutes';
import {
  IconSidebarAuthFiles,
  IconSidebarConsole,
  IconSidebarConfig,
  IconSidebarDashboard,
  IconSidebarLogs,
  IconSidebarProviders,
  IconSidebarQuota,
  IconSidebarSystem,
  IconSidebarUsage,
} from '@/components/ui/icons';
import { LOGO_JPEG_URL } from '@/assets/logo';
import {
  getDesktopBootstrap,
  isDesktopMode,
  sendShellStateChanged,
  subscribeDesktopDataChanged,
  subscribeDesktopShellCommand,
} from '@/desktop/bridge';
import {
  useAuthStore,
  useConfigStore,
  useNotificationStore,
  useThemeStore,
  useUsageStatsStore,
} from '@/stores';
import type { Theme } from '@/types';

const sidebarIcons: Record<string, ReactNode> = {
  dashboard: <IconSidebarDashboard size={18} />,
  runtimeOverview: <IconSidebarConsole size={18} />,
  aiProviders: <IconSidebarProviders size={18} />,
  authFiles: <IconSidebarAuthFiles size={18} />,
  quota: <IconSidebarQuota size={18} />,
  usage: <IconSidebarUsage size={18} />,
  config: <IconSidebarConfig size={18} />,
  logs: <IconSidebarLogs size={18} />,
  system: <IconSidebarSystem size={18} />,
};

const DESKTOP_MODE = isDesktopMode();

// Header action icons - smaller size for header buttons
const headerIconProps: SVGProps<SVGSVGElement> = {
  width: 16,
  height: 16,
  viewBox: '0 0 24 24',
  fill: 'none',
  stroke: 'currentColor',
  strokeWidth: 2,
  strokeLinecap: 'round',
  strokeLinejoin: 'round',
  'aria-hidden': 'true',
  focusable: 'false',
};

const headerIcons = {
  menu: (
    <svg {...headerIconProps}>
      <path d="M4 7h16" />
      <path d="M4 12h16" />
      <path d="M4 17h16" />
    </svg>
  ),
  chevronLeft: (
    <svg {...headerIconProps}>
      <path d="m14 18-6-6 6-6" />
    </svg>
  ),
  chevronRight: (
    <svg {...headerIconProps}>
      <path d="m10 6 6 6-6 6" />
    </svg>
  ),
  sun: (
    <svg {...headerIconProps}>
      <circle cx="12" cy="12" r="4" />
      <path d="M12 2v2" />
      <path d="M12 20v2" />
      <path d="m4.93 4.93 1.41 1.41" />
      <path d="m17.66 17.66 1.41 1.41" />
      <path d="M2 12h2" />
      <path d="M20 12h2" />
      <path d="m6.34 17.66-1.41 1.41" />
      <path d="m19.07 4.93-1.41 1.41" />
    </svg>
  ),
  moon: (
    <svg {...headerIconProps}>
      <path d="M12 3a6 6 0 0 0 9 9 9 9 0 1 1-9-9z" />
    </svg>
  ),
  whiteTheme: (
    <svg {...headerIconProps}>
      <circle cx="12" cy="12" r="7" />
      <circle cx="12" cy="12" r="3" fill="currentColor" stroke="none" />
    </svg>
  ),
  autoTheme: (
    <svg {...headerIconProps}>
      <defs>
        <clipPath id="mainLayoutAutoThemeSunLeftHalf">
          <rect x="0" y="0" width="12" height="24" />
        </clipPath>
      </defs>
      <circle cx="12" cy="12" r="4" />
      <circle
        cx="12"
        cy="12"
        r="4"
        clipPath="url(#mainLayoutAutoThemeSunLeftHalf)"
        fill="currentColor"
      />
      <path d="M12 2v2" />
      <path d="M12 20v2" />
      <path d="M4.93 4.93l1.41 1.41" />
      <path d="M17.66 17.66l1.41 1.41" />
      <path d="M2 12h2" />
      <path d="M20 12h2" />
      <path d="M6.34 17.66l-1.41 1.41" />
      <path d="M19.07 4.93l-1.41 1.41" />
    </svg>
  ),
  logout: (
    <svg {...headerIconProps}>
      <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4" />
      <path d="m16 17 5-5-5-5" />
      <path d="M21 12H9" />
    </svg>
  ),
};

const THEME_CARDS: Array<{
  key: Theme;
  labelKey: string;
  colors: { bg: string; card: string; border: string; text: string; textMuted: string };
}> = [
  {
    key: 'auto',
    labelKey: 'theme.auto',
    colors: {
      bg: 'linear-gradient(135deg, #ffffff 0 50%, #111111 50% 100%)',
      card: 'linear-gradient(135deg, #ffffff 0 50%, #1a1a1a 50% 100%)',
      border: '#bdbdbd',
      text: '#2d2a26',
      textMuted: 'linear-gradient(135deg, #c9c9c9 0 50%, #5a5a5a 50% 100%)',
    },
  },
  {
    key: 'white',
    labelKey: 'theme.white',
    colors: {
      bg: '#ffffff',
      card: '#ffffff',
      border: '#e5e5e5',
      text: '#2d2a26',
      textMuted: '#a29c95',
    },
  },
  {
    key: 'dark',
    labelKey: 'theme.dark',
    colors: {
      bg: '#151412',
      card: '#1d1b18',
      border: '#3a3530',
      text: '#f6f4f1',
      textMuted: '#9c958d',
    },
  },
];

const setRootCssVariableIfChanged = (
  name: string,
  value: string,
  lastValueRef: MutableRefObject<string | null>
) => {
  if (lastValueRef.current === value) {
    return;
  }

  document.documentElement.style.setProperty(name, value);
  lastValueRef.current = value;
};

const removeRootCssVariable = (name: string, lastValueRef: MutableRefObject<string | null>) => {
  if (lastValueRef.current === null) {
    return;
  }

  document.documentElement.style.removeProperty(name);
  lastValueRef.current = null;
};

export function MainLayout() {
  const { t } = useTranslation();
  const { showNotification } = useNotificationStore();
  const location = useLocation();
  const navigate = useNavigate();

  const apiBase = useAuthStore((state) => state.apiBase);
  const connectionStatus = useAuthStore((state) => state.connectionStatus);
  const logout = useAuthStore((state) => state.logout);

  const config = useConfigStore((state) => state.config);
  const fetchConfig = useConfigStore((state) => state.fetchConfig);
  const clearCache = useConfigStore((state) => state.clearCache);

  const theme = useThemeStore((state) => state.theme);
  const resolvedTheme = useThemeStore((state) => state.resolvedTheme);
  const setTheme = useThemeStore((state) => state.setTheme);
  const clearUsageStats = useUsageStatsStore((state) => state.clearUsageStats);
  const loadUsageStats = useUsageStatsStore((state) => state.loadUsageStats);

  const desktopMode = DESKTOP_MODE;
  const [desktopBootstrap] = useState(() => getDesktopBootstrap());
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const [sidebarCollapsed, setSidebarCollapsed] = useState(
    () => desktopMode && desktopBootstrap?.sidebarCollapsed === true
  );
  const [themeMenuOpen, setThemeMenuOpen] = useState(false);
  const [brandExpanded, setBrandExpanded] = useState(true);
  const contentRef = useRef<HTMLDivElement | null>(null);
  const themeMenuRef = useRef<HTMLDivElement | null>(null);
  const brandCollapseTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const headerRef = useRef<HTMLElement | null>(null);
  const headerHeightCssValue = useRef<string | null>(null);
  const contentCenterCssValue = useRef<string | null>(null);
  const pendingDataChangedScopes = useRef<Set<string>>(new Set());
  const dataChangedTimer = useRef<ReturnType<typeof window.setTimeout> | null>(null);

  const fullBrandName = t('title.main');
  const abbrBrandName = t('title.abbr');
  const isLogsPage = location.pathname.startsWith('/logs');

  // 将顶栏高度写入 CSS 变量，确保侧栏/内容区计算一致，防止滚动时抖动
  useLayoutEffect(() => {
    if (desktopMode) {
      setRootCssVariableIfChanged('--header-height', '0px', headerHeightCssValue);
      return () => {
        removeRootCssVariable('--header-height', headerHeightCssValue);
      };
    }

    let animationFrame: number | null = null;
    const updateHeaderHeight = () => {
      const height = headerRef.current?.offsetHeight;
      if (height) {
        setRootCssVariableIfChanged('--header-height', `${height}px`, headerHeightCssValue);
      }
    };
    const scheduleHeaderHeightUpdate = () => {
      if (animationFrame !== null) return;
      animationFrame = window.requestAnimationFrame(() => {
        animationFrame = null;
        updateHeaderHeight();
      });
    };

    updateHeaderHeight();

    const resizeObserver =
      typeof ResizeObserver !== 'undefined' && headerRef.current
        ? new ResizeObserver(scheduleHeaderHeightUpdate)
        : null;
    if (resizeObserver && headerRef.current) {
      resizeObserver.observe(headerRef.current);
    }

    window.addEventListener('resize', scheduleHeaderHeightUpdate);

    return () => {
      if (animationFrame !== null) {
        window.cancelAnimationFrame(animationFrame);
      }
      if (resizeObserver) {
        resizeObserver.disconnect();
      }
      window.removeEventListener('resize', scheduleHeaderHeightUpdate);
    };
  }, [desktopMode]);

  // 将主内容区的中心点写入 CSS 变量，供底部浮层（配置面板操作栏、提供商导航）对齐到内容区
  useLayoutEffect(() => {
    let animationFrame: number | null = null;
    const updateContentCenter = () => {
      const el = contentRef.current;
      if (!el) return;
      const rect = el.getBoundingClientRect();
      const centerX = Math.round((rect.left + rect.width / 2) * 100) / 100;
      setRootCssVariableIfChanged('--content-center-x', `${centerX}px`, contentCenterCssValue);
    };
    const scheduleContentCenterUpdate = () => {
      if (animationFrame !== null) return;
      animationFrame = window.requestAnimationFrame(() => {
        animationFrame = null;
        updateContentCenter();
      });
    };

    updateContentCenter();

    const resizeObserver =
      typeof ResizeObserver !== 'undefined' && contentRef.current
        ? new ResizeObserver(scheduleContentCenterUpdate)
        : null;

    if (resizeObserver && contentRef.current) {
      resizeObserver.observe(contentRef.current);
    }

    window.addEventListener('resize', scheduleContentCenterUpdate);

    return () => {
      if (animationFrame !== null) {
        window.cancelAnimationFrame(animationFrame);
      }
      if (resizeObserver) {
        resizeObserver.disconnect();
      }
      window.removeEventListener('resize', scheduleContentCenterUpdate);
      removeRootCssVariable('--content-center-x', contentCenterCssValue);
    };
  }, []);

  // 5秒后自动收起品牌名称
  useEffect(() => {
    brandCollapseTimer.current = setTimeout(() => {
      setBrandExpanded(false);
    }, 5000);

    return () => {
      if (brandCollapseTimer.current) {
        clearTimeout(brandCollapseTimer.current);
      }
    };
  }, []);

  useEffect(() => {
    if (!themeMenuOpen) {
      return;
    }

    const handlePointerDown = (event: MouseEvent) => {
      if (!themeMenuRef.current?.contains(event.target as Node)) {
        setThemeMenuOpen(false);
      }
    };

    const handleEscape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setThemeMenuOpen(false);
      }
    };

    document.addEventListener('mousedown', handlePointerDown);
    document.addEventListener('keydown', handleEscape);

    return () => {
      document.removeEventListener('mousedown', handlePointerDown);
      document.removeEventListener('keydown', handleEscape);
    };
  }, [themeMenuOpen]);

  const handleBrandClick = useCallback(() => {
    if (!brandExpanded) {
      setBrandExpanded(true);
      // 点击展开后，5秒后再次收起
      if (brandCollapseTimer.current) {
        clearTimeout(brandCollapseTimer.current);
      }
      brandCollapseTimer.current = setTimeout(() => {
        setBrandExpanded(false);
      }, 5000);
    }
  }, [brandExpanded]);

  const toggleThemeMenu = useCallback(() => {
    setThemeMenuOpen((prev) => !prev);
  }, []);

  const handleThemeSelect = useCallback(
    (nextTheme: Theme) => {
      setTheme(nextTheme);
      setThemeMenuOpen(false);
    },
    [setTheme]
  );

  useEffect(() => {
    fetchConfig().catch(() => {
      // ignore initial failure; login flow会提示
    });
  }, [fetchConfig]);

  const statusClass =
    connectionStatus === 'connected'
      ? 'success'
      : connectionStatus === 'connecting'
        ? 'warning'
        : connectionStatus === 'error'
          ? 'error'
          : 'muted';

  const navItems = useMemo(
    () => [
      { path: '/', label: t('nav.dashboard'), icon: sidebarIcons.dashboard },
      {
        path: '/dashboard/overview',
        label: t('nav.runtime_overview'),
        icon: sidebarIcons.runtimeOverview,
      },
      { path: '/config', label: t('nav.config_management'), icon: sidebarIcons.config },
      { path: '/ai-providers', label: t('nav.ai_providers'), icon: sidebarIcons.aiProviders },
      { path: '/auth-files', label: t('nav.auth_files'), icon: sidebarIcons.authFiles },
      { path: '/quota', label: t('nav.quota_management'), icon: sidebarIcons.quota },
      { path: '/usage', label: t('nav.usage_stats'), icon: sidebarIcons.usage },
      ...(desktopMode || config?.loggingToFile
        ? [{ path: '/logs', label: t('nav.logs'), icon: sidebarIcons.logs }]
        : []),
      ...(desktopMode
        ? []
        : [{ path: '/system', label: t('nav.system_info'), icon: sidebarIcons.system }]),
    ],
    [config?.loggingToFile, desktopMode, t]
  );
  const navOrder = useMemo(() => navItems.map((item) => item.path), [navItems]);
  const getRouteOrder = useCallback(
    (pathname: string) => {
      const trimmedPath =
        pathname.length > 1 && pathname.endsWith('/') ? pathname.slice(0, -1) : pathname;
      const normalizedPath =
        trimmedPath === '/dashboard'
          ? '/'
          : trimmedPath === '/console'
            ? '/dashboard/overview'
            : trimmedPath;

      const aiProvidersIndex = navOrder.indexOf('/ai-providers');
      if (aiProvidersIndex !== -1) {
        if (normalizedPath === '/ai-providers') return aiProvidersIndex;
        if (normalizedPath.startsWith('/ai-providers/')) {
          if (normalizedPath.startsWith('/ai-providers/codex')) return aiProvidersIndex + 0.1;
          return aiProvidersIndex + 0.05;
        }
      }

      const authFilesIndex = navOrder.indexOf('/auth-files');
      if (authFilesIndex !== -1) {
        if (normalizedPath === '/auth-files') return authFilesIndex;
        if (normalizedPath.startsWith('/auth-files/')) {
          if (normalizedPath.startsWith('/auth-files/oauth-excluded')) return authFilesIndex + 0.1;
          if (normalizedPath.startsWith('/auth-files/oauth-model-alias')) return authFilesIndex + 0.2;
          return authFilesIndex + 0.05;
        }
      }

      const exactIndex = navOrder.indexOf(normalizedPath);
      if (exactIndex !== -1) return exactIndex;
      const nestedIndex = navOrder.findIndex(
        (path) => path !== '/' && normalizedPath.startsWith(`${path}/`)
      );
      return nestedIndex === -1 ? null : nestedIndex;
    },
    [navOrder]
  );

  const getTransitionVariant = useCallback((fromPathname: string, toPathname: string) => {
    const normalize = (pathname: string) => {
      const trimmed =
        pathname.length > 1 && pathname.endsWith('/') ? pathname.slice(0, -1) : pathname;
      if (trimmed === '/dashboard') return '/';
      return trimmed === '/console' ? '/dashboard/overview' : trimmed;
    };

    const from = normalize(fromPathname);
    const to = normalize(toPathname);
    const isAuthFiles = (pathname: string) =>
      pathname === '/auth-files' || pathname.startsWith('/auth-files/');
    const isAiProviders = (pathname: string) =>
      pathname === '/ai-providers' || pathname.startsWith('/ai-providers/');
    if (isAuthFiles(from) && isAuthFiles(to)) return 'ios';
    if (isAiProviders(from) && isAiProviders(to)) return 'ios';
    return 'vertical';
  }, []);

  const applyDesktopDataChanged = useCallback(
    (scopes: string[]) => {
      const scopeSet = new Set(scopes.map((scope) => scope.toLowerCase()));
      if (
        scopeSet.has('config') ||
        scopeSet.has('providers') ||
        scopeSet.has('quota') ||
        scopeSet.has('auth-files') ||
        scopeSet.has('persistence')
      ) {
        clearCache();
        void fetchConfig(undefined, true).catch(() => {});
      }

      if (scopeSet.has('usage') || scopeSet.has('persistence')) {
        void loadUsageStats({ force: true }).catch(() => {});
      }
    },
    [clearCache, fetchConfig, loadUsageStats]
  );

  useEffect(() => {
    if (!desktopMode) {
      return;
    }

    const flushPendingScopes = () => {
      if (dataChangedTimer.current) {
        window.clearTimeout(dataChangedTimer.current);
        dataChangedTimer.current = null;
      }
      const scopes = Array.from(pendingDataChangedScopes.current);
      pendingDataChangedScopes.current.clear();
      if (scopes.length > 0) {
        applyDesktopDataChanged(scopes);
      }
    };

    const scheduleFlush = () => {
      if (document.hidden) return;
      if (dataChangedTimer.current) {
        window.clearTimeout(dataChangedTimer.current);
      }
      dataChangedTimer.current = window.setTimeout(flushPendingScopes, 180);
    };

    const unsubscribe = subscribeDesktopDataChanged((event) => {
      event.scopes.forEach((scope) => pendingDataChangedScopes.current.add(scope));
      scheduleFlush();
    });

    const handleVisible = () => {
      if (!document.hidden) {
        flushPendingScopes();
      }
    };

    document.addEventListener('visibilitychange', handleVisible);
    window.addEventListener('focus', flushPendingScopes);

    return () => {
      unsubscribe();
      if (dataChangedTimer.current) {
        window.clearTimeout(dataChangedTimer.current);
        dataChangedTimer.current = null;
      }
      document.removeEventListener('visibilitychange', handleVisible);
      window.removeEventListener('focus', flushPendingScopes);
    };
  }, [applyDesktopDataChanged, desktopMode]);

  useEffect(() => {
    if (!desktopMode) {
      return;
    }

    return subscribeDesktopShellCommand((command) => {
      if (command.type === 'setTheme') {
        setTheme(command.theme);
        return;
      }

      if (command.type === 'toggleSidebarCollapsed') {
        setSidebarCollapsed((previous) =>
          typeof command.collapsed === 'boolean' ? command.collapsed : !previous
        );
        return;
      }

      if (command.type === 'navigate') {
        navigate(command.path || '/');
        return;
      }

      if (command.type === 'clearUsageStats') {
        clearUsageStats();
        showNotification(t('system_info.clear_usage_success'), 'success');
        return;
      }

      if (command.type === 'refreshUsage') {
        void loadUsageStats({ force: true }).catch(() => {});
      }
    });
  }, [
    clearUsageStats,
    desktopMode,
    loadUsageStats,
    navigate,
    setTheme,
    showNotification,
    t,
  ]);

  useEffect(() => {
    if (!desktopMode) {
      return;
    }

    sendShellStateChanged({
      connectionStatus,
      apiBase,
      theme,
      resolvedTheme,
      sidebarCollapsed,
      pathname: location.pathname,
    });
  }, [
    apiBase,
    connectionStatus,
    desktopMode,
    location.pathname,
    resolvedTheme,
    sidebarCollapsed,
    theme,
  ]);

  return (
    <div className={`app-shell${desktopMode ? ' desktop-shell' : ''}`}>
      {!desktopMode && (
        <header className="main-header" ref={headerRef}>
          <div className="left">
            <button
              className="sidebar-toggle-header"
              onClick={() => setSidebarCollapsed((prev) => !prev)}
              title={
                sidebarCollapsed
                  ? t('sidebar.expand', { defaultValue: '展开' })
                  : t('sidebar.collapse', { defaultValue: '收起' })
              }
            >
              {sidebarCollapsed ? headerIcons.chevronRight : headerIcons.chevronLeft}
            </button>
            <img src={LOGO_JPEG_URL} alt="CodexCliPlus logo" className="brand-logo" />
            <div
              className={`brand-header ${brandExpanded ? 'expanded' : 'collapsed'}`}
              onClick={handleBrandClick}
              title={brandExpanded ? undefined : fullBrandName}
            >
              <span className="brand-full">{fullBrandName}</span>
              <span className="brand-abbr">{abbrBrandName}</span>
            </div>
          </div>

          <div className="right">
            <div className="connection">
              <span className={`status-badge ${statusClass}`}>
                {t(
                  connectionStatus === 'connected'
                    ? 'common.connected_status'
                    : connectionStatus === 'connecting'
                      ? 'common.connecting_status'
                      : 'common.disconnected_status'
                )}
              </span>
              <span className="base">{apiBase || '-'}</span>
            </div>

            <div className="header-actions">
              <Button
                className="mobile-menu-btn"
                variant="ghost"
                size="sm"
                onClick={() => setSidebarOpen((prev) => !prev)}
              >
                {headerIcons.menu}
              </Button>
              <div className={`theme-menu ${themeMenuOpen ? 'open' : ''}`} ref={themeMenuRef}>
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={toggleThemeMenu}
                  title={t('theme.switch')}
                  aria-label={t('theme.switch')}
                  aria-haspopup="menu"
                  aria-expanded={themeMenuOpen}
                >
                  {theme === 'auto'
                    ? headerIcons.autoTheme
                    : theme === 'dark'
                      ? headerIcons.moon
                      : theme === 'white'
                        ? headerIcons.whiteTheme
                        : headerIcons.sun}
                </Button>
                {themeMenuOpen && (
                  <div
                    className="notification entering theme-menu-popover"
                    role="menu"
                    aria-label={t('theme.switch')}
                  >
                    {THEME_CARDS.map((tc) => (
                      <button
                        key={tc.key}
                        type="button"
                        className={`theme-card ${theme === tc.key ? 'active' : ''}`}
                        onClick={() => handleThemeSelect(tc.key)}
                        role="menuitemradio"
                        aria-checked={theme === tc.key}
                      >
                        <div
                          className="theme-card-preview"
                          style={{
                            background: tc.colors.bg,
                            border: `1px solid ${tc.colors.border}`,
                          }}
                        >
                          <div
                            className="theme-card-header"
                            style={{
                              background: tc.colors.card,
                              borderBottom: `1px solid ${tc.colors.border}`,
                            }}
                          />
                          <div className="theme-card-body">
                            <div
                              className="theme-card-sidebar"
                              style={{
                                background: tc.colors.card,
                                borderRight: `1px solid ${tc.colors.border}`,
                              }}
                            />
                            <div
                              className="theme-card-content"
                              style={{ background: tc.colors.bg }}
                            >
                              <div
                                className="theme-card-line"
                                style={{ background: tc.colors.textMuted }}
                              />
                              <div
                                className="theme-card-line short"
                                style={{ background: tc.colors.textMuted }}
                              />
                            </div>
                          </div>
                        </div>
                        <span className="theme-card-label">{t(tc.labelKey)}</span>
                      </button>
                    ))}
                  </div>
                )}
              </div>
              <Button variant="ghost" size="sm" onClick={logout} title={t('header.logout')}>
                {headerIcons.logout}
              </Button>
            </div>
          </div>
        </header>
      )}

      <div className="main-body">
        <button
          type="button"
          className={`sidebar-backdrop ${sidebarOpen ? 'visible' : ''}`}
          onClick={() => setSidebarOpen(false)}
          aria-label={t('common.close')}
          aria-hidden={!sidebarOpen}
          tabIndex={sidebarOpen ? 0 : -1}
        />

        {!desktopMode && (
          <aside
            className={`sidebar ${sidebarOpen ? 'open' : ''} ${sidebarCollapsed ? 'collapsed' : ''}`}
          >
            <div className="nav-section">
              {navItems.map((item) => (
                <NavLink
                  key={item.path}
                  to={item.path}
                  className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}
                  onClick={() => setSidebarOpen(false)}
                  title={sidebarCollapsed ? item.label : undefined}
                >
                  <span className="nav-icon">{item.icon}</span>
                  {!sidebarCollapsed && <span className="nav-label">{item.label}</span>}
                </NavLink>
              ))}
            </div>
          </aside>
        )}

        <div className={`content${isLogsPage ? ' content-logs' : ''}`} ref={contentRef}>
          <main className={`main-content${isLogsPage ? ' main-content-logs' : ''}`}>
            <PageTransition
              render={(location) => <MainRoutes location={location} />}
              getRouteOrder={getRouteOrder}
              getTransitionVariant={getTransitionVariant}
              scrollContainerRef={contentRef}
            />
          </main>
        </div>
      </div>
    </div>
  );
}
