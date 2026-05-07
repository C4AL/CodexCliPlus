import { create } from 'zustand';
import { persist, createJSONStorage } from 'zustand/middleware';
import type { AuthState, LoginCredentials, ConnectionStatus } from '@/types';
import { consumeDesktopBootstrap, isDesktopMode } from '@/desktop/bridge';
import { STORAGE_KEY_AUTH } from '@/utils/constants';
import { obfuscatedStorage } from '@/services/storage/secureStorage';
import { apiClient } from '@/services/api/client';
import { useConfigStore } from './useConfigStore';
import { useUsageStatsStore } from './useUsageStatsStore';
import { useModelsStore } from './useModelsStore';
import { normalizeApiBase } from '@/utils/connection';

interface AuthStoreState extends AuthState {
  connectionStatus: ConnectionStatus;
  connectionError: string | null;
  login: (credentials: LoginCredentials) => Promise<void>;
  logout: () => void;
  checkAuth: () => Promise<boolean>;
  restoreSession: () => Promise<boolean>;
  updateServerVersion: (version: string | null, buildDate?: string | null) => void;
  updateConnectionStatus: (status: ConnectionStatus, error?: string | null) => void;
}

let restoreSessionPromise: Promise<boolean> | null = null;

function clearBrowserManagementSession() {
  localStorage.removeItem('isLoggedIn');
  obfuscatedStorage.removeItem('apiBase');
  obfuscatedStorage.removeItem('apiUrl');
  obfuscatedStorage.removeItem('managementKey');
  obfuscatedStorage.removeItem(STORAGE_KEY_AUTH);
}

export const useAuthStore = create<AuthStoreState>()(
  persist(
    (set, get) => ({
      isAuthenticated: false,
      apiBase: '',
      managementKey: '',
      desktopSessionId: '',
      rememberPassword: false,
      serverVersion: null,
      serverBuildDate: null,
      connectionStatus: 'disconnected',
      connectionError: null,

      restoreSession: () => {
        if (restoreSessionPromise) {
          return restoreSessionPromise;
        }

        let resetRestoreSessionPromise = false;
        const nextRestoreSessionPromise = (async () => {
          const desktopMode = isDesktopMode();
          const desktopBootstrap = consumeDesktopBootstrap();

          if (desktopMode) {
            if (!desktopBootstrap) {
              resetRestoreSessionPromise = true;
              set({
                isAuthenticated: false,
                apiBase: '',
                managementKey: '',
                desktopSessionId: '',
                rememberPassword: false,
                connectionStatus: 'disconnected'
              });
              return false;
            }

            const resolvedBase = normalizeApiBase(desktopBootstrap.apiBase);
            const desktopSessionId = desktopBootstrap.desktopSessionId;

            set({
              apiBase: resolvedBase,
              managementKey: '',
              desktopSessionId,
              rememberPassword: false,
              isAuthenticated: true,
              connectionStatus: 'connected',
              connectionError: null
            });
            localStorage.removeItem('isLoggedIn');
            obfuscatedStorage.removeItem('managementKey');
            apiClient.setConfig({ apiBase: resolvedBase, managementKey: '' });
            return true;
          }

          resetRestoreSessionPromise = true;
          clearBrowserManagementSession();
          set({
            isAuthenticated: false,
            apiBase: '',
            managementKey: '',
            desktopSessionId: '',
            rememberPassword: false,
            connectionStatus: 'disconnected',
            connectionError: null
          });
          apiClient.setConfig({ apiBase: '', managementKey: '' });
          return false;
        })().finally(() => {
          if (resetRestoreSessionPromise && restoreSessionPromise === nextRestoreSessionPromise) {
            restoreSessionPromise = null;
          }
        });

        restoreSessionPromise = nextRestoreSessionPromise;
        return restoreSessionPromise;
      },

      login: async (credentials) => {
        const desktopMode = isDesktopMode();
        if (!desktopMode) {
          clearBrowserManagementSession();
          set({
            isAuthenticated: false,
            apiBase: '',
            managementKey: '',
            desktopSessionId: '',
            rememberPassword: false,
            connectionStatus: 'disconnected',
            connectionError: null
          });
          throw new Error('管理界面只能在桌面应用内打开。');
        }

        const apiBase = normalizeApiBase(credentials.apiBase);
        const managementKey = '';
        const rememberPassword = false;

        try {
          set({ connectionStatus: 'connecting' });
          useModelsStore.getState().clearCache();

          apiClient.setConfig({
            apiBase,
            managementKey
          });

          await useConfigStore.getState().fetchConfig(undefined, true);

          set({
            isAuthenticated: true,
            apiBase,
            managementKey,
            desktopSessionId: get().desktopSessionId,
            rememberPassword,
            connectionStatus: 'connected',
            connectionError: null
          });

          localStorage.removeItem('isLoggedIn');
        } catch (error: unknown) {
          const message =
            error instanceof Error
              ? error.message
              : typeof error === 'string'
                ? error
                : '连接失败';
          set({
            connectionStatus: 'error',
            connectionError: message || '连接失败'
          });
          throw error;
        }
      },

      logout: () => {
        restoreSessionPromise = null;
        useConfigStore.getState().clearCache();
        useUsageStatsStore.getState().clearUsageStats();
        useModelsStore.getState().clearCache();
        set({
          isAuthenticated: false,
          apiBase: '',
          managementKey: '',
          desktopSessionId: '',
          serverVersion: null,
          serverBuildDate: null,
          connectionStatus: 'disconnected',
          connectionError: null
        });
        localStorage.removeItem('isLoggedIn');
      },

      checkAuth: async () => {
        const { apiBase } = get();
        const desktopMode = isDesktopMode();

        if (!desktopMode) {
          clearBrowserManagementSession();
          set({
            isAuthenticated: false,
            apiBase: '',
            managementKey: '',
            desktopSessionId: '',
            rememberPassword: false,
            connectionStatus: 'disconnected',
            connectionError: null
          });
          return false;
        }

        if (!apiBase) {
          return false;
        }

        try {
          apiClient.setConfig({ apiBase, managementKey: '' });
          await useConfigStore.getState().fetchConfig();

          set({
            isAuthenticated: true,
            connectionStatus: 'connected'
          });

          return true;
        } catch (error: unknown) {
          const message =
            error instanceof Error
              ? error.message
              : typeof error === 'string'
                ? error
                : '连接失败';
          set({
            isAuthenticated: false,
            connectionStatus: 'error',
            connectionError: message || '连接失败'
          });
          return false;
        }
      },

      updateServerVersion: (version, buildDate) => {
        set({ serverVersion: version || null, serverBuildDate: buildDate || null });
      },

      updateConnectionStatus: (status, error = null) => {
        set({
          connectionStatus: status,
          connectionError: error
        });
      }
    }),
    {
      name: STORAGE_KEY_AUTH,
      storage: createJSONStorage(() => ({
        getItem: (name) => {
          const data = obfuscatedStorage.getItem<AuthStoreState>(name);
          return data ? JSON.stringify(data) : null;
        },
        setItem: (name, value) => {
          obfuscatedStorage.setItem(name, JSON.parse(value));
        },
        removeItem: (name) => {
          obfuscatedStorage.removeItem(name);
        }
      })),
      partialize: (state) =>
        isDesktopMode()
          ? {
              rememberPassword: false,
              desktopSessionId: state.desktopSessionId,
              serverVersion: state.serverVersion,
              serverBuildDate: state.serverBuildDate
            }
          : {
              rememberPassword: false,
              serverVersion: state.serverVersion,
              serverBuildDate: state.serverBuildDate
            }
    }
  )
);

if (typeof window !== 'undefined') {
  window.addEventListener('unauthorized', () => {
    useAuthStore.getState().logout();
  });

  window.addEventListener(
    'server-version-update',
    ((event: CustomEvent) => {
      const detail = event.detail || {};
      useAuthStore.getState().updateServerVersion(detail.version || null, detail.buildDate || null);
    }) as EventListener
  );
}
