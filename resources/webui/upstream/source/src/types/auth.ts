export interface LoginCredentials {
  apiBase: string;
  managementKey: string;
  rememberPassword?: boolean;
}

export interface AuthState {
  isAuthenticated: boolean;
  apiBase: string;
  managementKey: string;
  rememberPassword: boolean;
  serverVersion: string | null;
  serverBuildDate: string | null;
}

export type ConnectionStatus = 'connected' | 'disconnected' | 'connecting' | 'error';

export interface ConnectionInfo {
  status: ConnectionStatus;
  lastCheck: Date | null;
  error: string | null;
}

export interface DesktopBootstrapPayload {
  desktopMode: boolean;
  apiBase: string;
  managementKey: string;
}
