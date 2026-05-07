import { Navigate } from 'react-router-dom';
import { BrowserManagementBlocked } from '@/router/BrowserManagementBlocked';
import { isDesktopMode } from '@/desktop/bridge';

export function LoginRoute() {
  if (isDesktopMode()) {
    return <Navigate to="/" replace />;
  }

  return <BrowserManagementBlocked />;
}
