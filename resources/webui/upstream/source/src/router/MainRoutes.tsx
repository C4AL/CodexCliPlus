import { Navigate, useRoutes, type Location } from 'react-router-dom';
import { DashboardPage } from '@/pages/DashboardPage';
import { AiProvidersPage } from '@/pages/AiProvidersPage';
import { AiProvidersCodexEditPage } from '@/pages/AiProvidersCodexEditPage';
import { AuthFilesPage } from '@/pages/AuthFilesPage';
import { AuthFilesOAuthExcludedEditPage } from '@/pages/AuthFilesOAuthExcludedEditPage';
import { AuthFilesOAuthModelAliasEditPage } from '@/pages/AuthFilesOAuthModelAliasEditPage';
import { OAuthPage } from '@/pages/OAuthPage';
import { QuotaPage } from '@/pages/QuotaPage';
import { UsagePage } from '@/pages/UsagePage';
import { ConfigPage } from '@/pages/ConfigPage';
import { LogsPage } from '@/pages/LogsPage';
import { SystemPage } from '@/pages/SystemPage';

const mainRoutes = [
  { path: '/', element: <DashboardPage /> },
  { path: '/dashboard', element: <DashboardPage /> },
  { path: '/settings', element: <Navigate to="/config" replace /> },
  { path: '/api-keys', element: <Navigate to="/config" replace /> },
  { path: '/ai-providers/codex/new', element: <AiProvidersCodexEditPage /> },
  { path: '/ai-providers/codex/:index', element: <AiProvidersCodexEditPage /> },
  { path: '/ai-providers', element: <AiProvidersPage /> },
  { path: '/ai-providers/*', element: <Navigate to="/ai-providers" replace /> },
  { path: '/auth-files', element: <AuthFilesPage /> },
  { path: '/auth-files/oauth-excluded', element: <AuthFilesOAuthExcludedEditPage /> },
  { path: '/auth-files/oauth-model-alias', element: <AuthFilesOAuthModelAliasEditPage /> },
  { path: '/oauth', element: <OAuthPage /> },
  { path: '/quota', element: <QuotaPage /> },
  { path: '/usage', element: <UsagePage /> },
  { path: '/config', element: <ConfigPage /> },
  { path: '/logs', element: <LogsPage /> },
  { path: '/system', element: <SystemPage /> },
  { path: '*', element: <Navigate to="/" replace /> },
];

export function MainRoutes({ location }: { location?: Location }) {
  return useRoutes(mainRoutes, location);
}
