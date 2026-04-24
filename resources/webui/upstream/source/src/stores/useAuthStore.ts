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
import { detectApiBaseFromLocation, normalizeApiBase } from '@/utils/connection';

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

export const useAuthStore = create<AuthStoreState>()(
  persist(
    (set, get) => ({
      isAuthenticated: false,
      apiBase: '',
      managementKey: '',
      rememberPassword: false,
      serverVersion: null,
      serverBuildDate: null,
      connectionStatus: 'disconnected',
      connectionError: null,

      restoreSession: () => {
        if (restoreSessionPromise) {
          return restoreSessionPromise;
        }

        restoreSessionPromise = (async () => {
          const desktopMode = isDesktopMode();
          const desktopBootstrap = consumeDesktopBootstrap();
          const currentState = get();

          if (desktopMode) {
            const resolvedBase = normalizeApiBase(
              desktopBootstrap?.apiBase || currentState.apiBase || detectApiBaseFromLocation()
            );

            set({
              apiBase: resolvedBase,
              managementKey: '',
              rememberPassword: false
            });
            apiClient.setConfig({ apiBase: resolvedBase, managementKey: '' });

            if (desktopBootstrap) {
              try {
                await get().login({
                  apiBase: desktopBootstrap.apiBase,
                  managementKey: desktopBootstrap.managementKey,
                  rememberPassword: false
                });
                return true;
              } catch (error) {
                console.warn('Desktop bootstrap auto login failed:', error);
                return false;
              }
            }

            return false;
          }

          obfuscatedStorage.migratePlaintextKeys(['apiBase', 'apiUrl', 'managementKey']);

          const wasLoggedIn = localStorage.getItem('isLoggedIn') === 'true';
          const legacyBase =
            obfuscatedStorage.getItem<string>('apiBase') ||
            obfuscatedStorage.getItem<string>('apiUrl', { encrypt: true });
          const legacyKey = obfuscatedStorage.getItem<string>('managementKey');

          const { apiBase, managementKey, rememberPassword } = get();
          const resolvedBase = normalizeApiBase(apiBase || legacyBase || detectApiBaseFromLocation());
          const resolvedKey = managementKey || legacyKey || '';
          const resolvedRememberPassword = rememberPassword || Boolean(managementKey) || Boolean(legacyKey);

          set({
            apiBase: resolvedBase,
            managementKey: resolvedKey,
            rememberPassword: resolvedRememberPassword
          });
          apiClient.setConfig({ apiBase: resolvedBase, managementKey: resolvedKey });

          if (wasLoggedIn && resolvedBase && resolvedKey) {
            try {
              await get().login({
                apiBase: resolvedBase,
                managementKey: resolvedKey,
                rememberPassword: resolvedRememberPassword
              });
              return true;
            } catch (error) {
              console.warn('Auto login failed:', error);
              return false;
            }
          }

          return false;
        })();

        return restoreSessionPromise;
      },

      login: async (credentials) => {
        const apiBase = normalizeApiBase(credentials.apiBase);
        const managementKey = credentials.managementKey.trim();
        const rememberPassword = isDesktopMode()
          ? false
          : credentials.rememberPassword ?? get().rememberPassword ?? false;

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
            rememberPassword,
            connectionStatus: 'connected',
            connectionError: null
          });

          if (!isDesktopMode() && rememberPassword) {
            localStorage.setItem('isLoggedIn', 'true');
          } else {
            localStorage.removeItem('isLoggedIn');
          }
        } catch (error: unknown) {
          const message =
            error instanceof Error
              ? error.message
              : typeof error === 'string'
                ? error
                : 'Connection failed';
          set({
            connectionStatus: 'error',
            connectionError: message || 'Connection failed'
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
          serverVersion: null,
          serverBuildDate: null,
          connectionStatus: 'disconnected',
          connectionError: null
        });
        localStorage.removeItem('isLoggedIn');
      },

      checkAuth: async () => {
        const { managementKey, apiBase } = get();

        if (!managementKey || !apiBase) {
          return false;
        }

        try {
          apiClient.setConfig({ apiBase, managementKey });
          await useConfigStore.getState().fetchConfig();

          set({
            isAuthenticated: true,
            connectionStatus: 'connected'
          });

          return true;
        } catch {
          set({
            isAuthenticated: false,
            connectionStatus: 'error'
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
      partialize: (state) => ({
        apiBase: state.apiBase,
        ...(!isDesktopMode() && state.rememberPassword ? { managementKey: state.managementKey } : {}),
        rememberPassword: isDesktopMode() ? false : state.rememberPassword,
        serverVersion: state.serverVersion,
        serverBuildDate: state.serverBuildDate
      })
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
