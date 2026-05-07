import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const bootstrap = {
  desktopMode: true,
  apiBase: 'http://127.0.0.1:15345',
  desktopSessionId: 'desktop-session-1',
  theme: 'auto',
  resolvedTheme: 'light',
};

async function loadDesktopAuthStore() {
  vi.resetModules();
  Object.assign(window, {
    __CODEXCLIPLUS_DESKTOP_BRIDGE__: {
      isDesktopMode: () => true,
      consumeBootstrap: vi.fn(() => bootstrap),
    },
  });

  const [{ useAuthStore }, { useConfigStore }] = await Promise.all([
    import('@/stores/useAuthStore'),
    import('@/stores/useConfigStore'),
  ]);
  return { useAuthStore, useConfigStore };
}

async function loadDesktopAuthStoreBeforeBridgeInjection() {
  vi.resetModules();
  Reflect.deleteProperty(window, '__CODEXCLIPLUS_DESKTOP_BRIDGE__');
  Object.assign(window, {
    chrome: {
      webview: {
        postMessage: vi.fn(),
      },
    },
  });

  const [{ useAuthStore }, { useConfigStore }] = await Promise.all([
    import('@/stores/useAuthStore'),
    import('@/stores/useConfigStore'),
  ]);
  return { useAuthStore, useConfigStore };
}

describe('useAuthStore desktop restore', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  afterEach(() => {
    Reflect.deleteProperty(window, '__CODEXCLIPLUS_DESKTOP_BRIDGE__');
    Reflect.deleteProperty(window, 'chrome');
    vi.resetModules();
    vi.restoreAllMocks();
  });

  it('trusts the desktop bootstrap without blocking on config fetch', async () => {
    localStorage.setItem('isLoggedIn', 'true');
    localStorage.setItem('managementKey', '"legacy-secret"');
    const { useAuthStore, useConfigStore } = await loadDesktopAuthStore();
    const fetchConfig = vi.fn().mockRejectedValue(new Error('不应阻塞桌面首屏'));
    useConfigStore.setState({ fetchConfig });

    await expect(useAuthStore.getState().restoreSession()).resolves.toBe(true);

    expect(fetchConfig).not.toHaveBeenCalled();
    expect(useAuthStore.getState()).toMatchObject({
      isAuthenticated: true,
      apiBase: bootstrap.apiBase,
      managementKey: '',
      desktopSessionId: bootstrap.desktopSessionId,
      connectionStatus: 'connected',
    });
    expect(localStorage.getItem('isLoggedIn')).toBeNull();
    expect(localStorage.getItem('managementKey')).toBeNull();
  });

  it('retries after the desktop bootstrap bridge is injected late', async () => {
    const { useAuthStore, useConfigStore } = await loadDesktopAuthStoreBeforeBridgeInjection();
    const fetchConfig = vi.fn().mockRejectedValue(new Error('不应阻塞桌面首屏'));
    useConfigStore.setState({ fetchConfig });

    await expect(useAuthStore.getState().restoreSession()).resolves.toBe(false);

    const consumeBootstrap = vi.fn(() => bootstrap);
    Object.assign(window, {
      __CODEXCLIPLUS_DESKTOP_BRIDGE__: {
        isDesktopMode: () => true,
        consumeBootstrap,
      },
    });

    await expect(useAuthStore.getState().restoreSession()).resolves.toBe(true);

    expect(consumeBootstrap).toHaveBeenCalledTimes(1);
    expect(fetchConfig).not.toHaveBeenCalled();
    expect(useAuthStore.getState()).toMatchObject({
      isAuthenticated: true,
      apiBase: bootstrap.apiBase,
      managementKey: '',
      desktopSessionId: bootstrap.desktopSessionId,
      connectionStatus: 'connected',
    });
  });
});
