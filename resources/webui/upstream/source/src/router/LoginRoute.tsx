import { Suspense, lazy, type ComponentType } from 'react';
import { Navigate } from 'react-router-dom';
import { LoadingSpinner } from '@/components/ui/LoadingSpinner';
import { isDesktopMode } from '@/desktop/bridge';

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

export function LoginRoute() {
  if (isDesktopMode()) {
    return <Navigate to="/" replace />;
  }

  return (
    <Suspense fallback={<RouteFallback />}>
      <LoginPage />
    </Suspense>
  );
}
