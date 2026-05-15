import { Navigate } from 'react-router-dom';
import { BrowserManagementBlocked } from '@/screens/router/BrowserManagementBlocked';
import { isDesktopMode } from '@/api/desktopBridge';

export function LoginRoute() {
  if (isDesktopMode()) {
    return <Navigate to="/" replace />;
  }

  return <BrowserManagementBlocked />;
}
