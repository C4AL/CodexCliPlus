import { Suspense, lazy, useEffect, type ComponentType } from 'react';
import { Navigate, Outlet, RouterProvider, createHashRouter } from 'react-router-dom';
import { ConfirmationModal } from '@/components/common/ConfirmationModal';
import { MainLayout } from '@/components/layout/MainLayout';
import { ProtectedRoute } from '@/router/ProtectedRoute';
import { useLanguageStore, useThemeStore } from '@/stores';
import { isDesktopMode } from '@/desktop/bridge';
import { LoadingSpinner } from '@/components/ui/LoadingSpinner';

const LoginPage = lazyPage(() => import('@/pages/LoginPage'), 'LoginPage');

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

function RootShell() {
  return (
    <>
      <ConfirmationModal />
      <Outlet />
    </>
  );
}

const desktopMode = isDesktopMode();

const router = createHashRouter([
  {
    element: <RootShell />,
    children: [
      desktopMode
        ? { path: '/login', element: <Navigate to="/" replace /> }
        : {
            path: '/login',
            element: (
              <Suspense fallback={<RouteFallback />}>
                <LoginPage />
              </Suspense>
            ),
          },
      {
        path: '/*',
        element: (
          <ProtectedRoute>
            <MainLayout />
          </ProtectedRoute>
        ),
      },
    ],
  },
]);

function App() {
  const initializeTheme = useThemeStore((state) => state.initializeTheme);
  const language = useLanguageStore((state) => state.language);
  const setLanguage = useLanguageStore((state) => state.setLanguage);

  useEffect(() => {
    const cleanupTheme = initializeTheme();
    return cleanupTheme;
  }, [initializeTheme]);

  useEffect(() => {
    setLanguage(language);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []); // 仅用于首屏同步 i18n 语言

  useEffect(() => {
    document.documentElement.lang = language;
  }, [language]);

  useEffect(() => {
    if (typeof performance === 'undefined') {
      return;
    }

    performance.mark('ccp-app-mounted');
    if (performance.getEntriesByName('ccp-entry-start').length > 0) {
      performance.measure('ccp-entry-to-app-mounted', 'ccp-entry-start', 'ccp-app-mounted');
    }
  }, []);

  return <RouterProvider router={router} />;
}

export default App;
