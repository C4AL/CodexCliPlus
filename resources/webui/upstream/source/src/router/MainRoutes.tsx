import { Suspense, lazy, useEffect, type ComponentType, type ReactElement } from 'react';
import { Navigate, useRoutes, type Location } from 'react-router-dom';
import { LoadingSpinner } from '@/components/ui/LoadingSpinner';

const DashboardPage = lazyPage(() => import('@/pages/DashboardPage'), 'DashboardPage');
const ConsolePage = lazyPage(() => import('@/pages/ConsolePage'), 'ConsolePage');
const AiProvidersPage = lazyPage(() => import('@/pages/AiProvidersPage'), 'AiProvidersPage');
const AiProvidersCodexEditPage = lazyPage(
  () => import('@/pages/AiProvidersCodexEditPage'),
  'AiProvidersCodexEditPage'
);
const AuthFilesPage = lazyPage(() => import('@/pages/AuthFilesPage'), 'AuthFilesPage');
const AuthFilesOAuthExcludedEditPage = lazyPage(
  () => import('@/pages/AuthFilesOAuthExcludedEditPage'),
  'AuthFilesOAuthExcludedEditPage'
);
const AuthFilesOAuthModelAliasEditPage = lazyPage(
  () => import('@/pages/AuthFilesOAuthModelAliasEditPage'),
  'AuthFilesOAuthModelAliasEditPage'
);
const QuotaPage = lazyPage(() => import('@/pages/QuotaPage'), 'QuotaPage');
const UsagePage = lazyPage(() => import('@/pages/UsagePage'), 'UsagePage');
const ConfigPage = lazyPage(() => import('@/pages/ConfigPage'), 'ConfigPage');
const LogsPage = lazyPage(() => import('@/pages/LogsPage'), 'LogsPage');
const SystemPage = lazyPage(() => import('@/pages/SystemPage'), 'SystemPage');

function lazyPage<TModule extends Record<string, unknown>>(
  loader: () => Promise<TModule>,
  exportName: keyof TModule
) {
  return lazy(async () => ({ default: (await loader())[exportName] as ComponentType }));
}

function RouteFallback() {
  return (
    <div aria-busy="true" style={{ display: 'grid', minHeight: 220, placeItems: 'center' }}>
      <LoadingSpinner size={28} />
    </div>
  );
}

function RoutePerformanceMarker({ name, children }: { name: string; children: ReactElement }) {
  useEffect(() => {
    if (typeof performance === 'undefined') {
      return;
    }

    const markName = `ccp-route-rendered:${name}`;
    performance.mark(markName);
    const entryMark = 'ccp-entry-start';
    if (performance.getEntriesByName(entryMark).length > 0) {
      performance.measure(`ccp-entry-to-route:${name}`, entryMark, markName);
    }
  }, [name]);

  return children;
}

const route = (name: string, element: ReactElement) => (
  <RoutePerformanceMarker name={name}>
    <Suspense fallback={<RouteFallback />}>{element}</Suspense>
  </RoutePerformanceMarker>
);

const dashboardRoute = (
  <RoutePerformanceMarker name="dashboard">
    <Suspense fallback={<RouteFallback />}>
      <DashboardPage />
    </Suspense>
  </RoutePerformanceMarker>
);

const mainRoutes = [
  { path: '/', element: dashboardRoute },
  { path: '/dashboard', element: dashboardRoute },
  { path: '/dashboard/overview', element: route('console-overview', <ConsolePage />) },
  { path: '/console', element: route('console', <ConsolePage />) },
  { path: '/settings', element: <Navigate to="/config" replace /> },
  { path: '/api-keys', element: <Navigate to="/config" replace /> },
  {
    path: '/ai-providers/codex/new',
    element: route('ai-providers-codex-edit', <AiProvidersCodexEditPage />),
  },
  {
    path: '/ai-providers/codex/:index',
    element: route('ai-providers-codex-edit', <AiProvidersCodexEditPage />),
  },
  { path: '/ai-providers', element: route('ai-providers', <AiProvidersPage />) },
  { path: '/ai-providers/*', element: <Navigate to="/ai-providers" replace /> },
  { path: '/auth-files', element: route('auth-files', <AuthFilesPage />) },
  {
    path: '/auth-files/oauth-excluded',
    element: route('auth-files-oauth-excluded', <AuthFilesOAuthExcludedEditPage />),
  },
  {
    path: '/auth-files/oauth-model-alias',
    element: route('auth-files-oauth-model-alias', <AuthFilesOAuthModelAliasEditPage />),
  },
  { path: '/oauth', element: <Navigate to="/ai-providers" replace /> },
  { path: '/quota', element: route('quota', <QuotaPage />) },
  { path: '/usage', element: route('usage', <UsagePage />) },
  { path: '/config', element: route('config', <ConfigPage />) },
  { path: '/logs', element: route('logs', <LogsPage />) },
  { path: '/system', element: route('system', <SystemPage />) },
  { path: '*', element: <Navigate to="/" replace /> },
];

export function MainRoutes({ location }: { location?: Location }) {
  return useRoutes(mainRoutes, location);
}
