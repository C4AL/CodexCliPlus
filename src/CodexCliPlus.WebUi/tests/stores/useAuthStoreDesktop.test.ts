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
  const getBootstrap = vi.fn(() => bootstrap);
  const consumeBootstrap = vi.fn(() => ({
    ...bootstrap,
    apiBase: 'http://127.0.0.1:19999',
  }));
  Object.assign(window, {
    __CODEXCLIPLUS_DESKTOP_BRIDGE__: {
      isDesktopMode: () => true,
      getBootstrap,
      consumeBootstrap,
    },
  });

  const [{ useAuthStore }, { useConfigStore }, { obfuscatedStorage }] = await Promise.all([
    import('@/stores/useAuthStore'),
    import('@/stores/useConfigStore'),
    import('@/services/storage/secureStorage'),
  ]);
  return { useAuthStore, useConfigStore, obfuscatedStorage, getBootstrap, consumeBootstrap };
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

async function loadBrowserAuthStore() {
  vi.resetModules();
  Reflect.deleteProperty(window, '__CODEXCLIPLUS_DESKTOP_BRIDGE__');
  Reflect.deleteProperty(window, 'chrome');

  const [{ useAuthStore }, { useConfigStore }, { obfuscatedStorage }] = await Promise.all([
    import('@/stores/useAuthStore'),
    import('@/stores/useConfigStore'),
    import('@/services/storage/secureStorage'),
  ]);
  return { useAuthStore, useConfigStore, obfuscatedStorage };
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
    localStorage.setItem('apiBase', '"http://127.0.0.1:19999"');
    localStorage.setItem('apiUrl', '"http://127.0.0.1:19999"');
    localStorage.setItem('managementKey', '"legacy-secret"');
    const { useAuthStore, useConfigStore, obfuscatedStorage, getBootstrap, consumeBootstrap } =
      await loadDesktopAuthStore();
    localStorage.setItem('codexcliplus-auth', '"legacy-session"');
    const fetchConfig = vi.fn().mockRejectedValue(new Error('不应阻塞桌面首屏'));
    useConfigStore.setState({ fetchConfig });

    await expect(useAuthStore.getState().restoreSession()).resolves.toBe(true);

    expect(getBootstrap).toHaveBeenCalledTimes(1);
    expect(consumeBootstrap).not.toHaveBeenCalled();
    expect(fetchConfig).not.toHaveBeenCalled();
    expect(useAuthStore.getState()).toMatchObject({
      isAuthenticated: true,
      apiBase: bootstrap.apiBase,
      managementKey: '',
      desktopSessionId: bootstrap.desktopSessionId,
      connectionStatus: 'connected',
    });
    expect(localStorage.getItem('isLoggedIn')).toBeNull();
    expect(localStorage.getItem('apiBase')).toBeNull();
    expect(localStorage.getItem('apiUrl')).toBeNull();
    expect(localStorage.getItem('managementKey')).toBeNull();
    const persisted = obfuscatedStorage.getItem<{ state?: Record<string, unknown> }>(
      'codexcliplus-auth'
    );
    expect(persisted?.state).not.toHaveProperty('apiBase');
    expect(persisted?.state).not.toHaveProperty('managementKey');
  });

  it('restores after another desktop module has already read bootstrap', async () => {
    const { useAuthStore, useConfigStore, getBootstrap } = await loadDesktopAuthStore();
    const { getDesktopBootstrap } = await import('@/desktop/bridge');
    const fetchConfig = vi.fn().mockRejectedValue(new Error('不应阻塞桌面首屏'));
    useConfigStore.setState({ fetchConfig });

    expect(getDesktopBootstrap()).toMatchObject(bootstrap);
    await expect(useAuthStore.getState().restoreSession()).resolves.toBe(true);

    expect(getBootstrap).toHaveBeenCalledTimes(1);
    expect(fetchConfig).not.toHaveBeenCalled();
    expect(useAuthStore.getState()).toMatchObject({
      isAuthenticated: true,
      apiBase: bootstrap.apiBase,
      managementKey: '',
      desktopSessionId: bootstrap.desktopSessionId,
      connectionStatus: 'connected',
    });
  });

  it('retries after the desktop bootstrap bridge is injected late', async () => {
    const { useAuthStore, useConfigStore } = await loadDesktopAuthStoreBeforeBridgeInjection();
    const fetchConfig = vi.fn().mockRejectedValue(new Error('不应阻塞桌面首屏'));
    useConfigStore.setState({ fetchConfig });

    await expect(useAuthStore.getState().restoreSession()).resolves.toBe(false);

    const getBootstrap = vi.fn(() => bootstrap);
    Object.assign(window, {
      __CODEXCLIPLUS_DESKTOP_BRIDGE__: {
        isDesktopMode: () => true,
        getBootstrap,
      },
    });

    await expect(useAuthStore.getState().restoreSession()).resolves.toBe(true);

    expect(getBootstrap).toHaveBeenCalledTimes(1);
    expect(fetchConfig).not.toHaveBeenCalled();
    expect(useAuthStore.getState()).toMatchObject({
      isAuthenticated: true,
      apiBase: bootstrap.apiBase,
      managementKey: '',
      desktopSessionId: bootstrap.desktopSessionId,
      connectionStatus: 'connected',
    });
  });

  it('can retry restore when bootstrap becomes available after a failed desktop attempt', async () => {
    vi.resetModules();
    const getBootstrap = vi.fn().mockReturnValueOnce(null).mockReturnValueOnce(bootstrap);
    Object.assign(window, {
      __CODEXCLIPLUS_DESKTOP_BRIDGE__: {
        isDesktopMode: () => true,
        getBootstrap,
      },
    });

    const [{ useAuthStore }, { useConfigStore }] = await Promise.all([
      import('@/stores/useAuthStore'),
      import('@/stores/useConfigStore'),
    ]);
    const fetchConfig = vi.fn().mockRejectedValue(new Error('不应阻塞桌面首屏'));
    useConfigStore.setState({ fetchConfig });

    await expect(useAuthStore.getState().restoreSession()).resolves.toBe(false);
    await expect(useAuthStore.getState().restoreSession()).resolves.toBe(true);

    expect(getBootstrap).toHaveBeenCalledTimes(2);
    expect(fetchConfig).not.toHaveBeenCalled();
    expect(useAuthStore.getState()).toMatchObject({
      isAuthenticated: true,
      apiBase: bootstrap.apiBase,
      managementKey: '',
      desktopSessionId: bootstrap.desktopSessionId,
      connectionStatus: 'connected',
    });
  });

  it('does not restore or submit browser management sessions outside desktop mode', async () => {
    const { useAuthStore, useConfigStore, obfuscatedStorage } = await loadBrowserAuthStore();
    const fetchConfig = vi.fn().mockRejectedValue(new Error('不应校验浏览器会话'));
    useConfigStore.setState({ fetchConfig });
    localStorage.setItem('isLoggedIn', 'true');
    localStorage.setItem('apiBase', '"http://127.0.0.1:15345"');
    localStorage.setItem('managementKey', '"legacy-secret"');
    localStorage.setItem('codexcliplus-auth', '"legacy-session"');

    await expect(useAuthStore.getState().restoreSession()).resolves.toBe(false);

    expect(fetchConfig).not.toHaveBeenCalled();
    expect(useAuthStore.getState()).toMatchObject({
      isAuthenticated: false,
      apiBase: '',
      managementKey: '',
      desktopSessionId: '',
      rememberPassword: false,
      connectionStatus: 'disconnected',
    });
    expect(localStorage.getItem('isLoggedIn')).toBeNull();
    expect(localStorage.getItem('apiBase')).toBeNull();
    expect(localStorage.getItem('managementKey')).toBeNull();
    expect(localStorage.getItem('codexcliplus-auth') || '').not.toContain('legacy-secret');
    const persisted = obfuscatedStorage.getItem<{ state?: Record<string, unknown> }>(
      'codexcliplus-auth'
    );
    expect(persisted?.state).not.toHaveProperty('apiBase');
    expect(persisted?.state).not.toHaveProperty('managementKey');

    await expect(
      useAuthStore.getState().login({
        apiBase: 'http://127.0.0.1:15345',
        managementKey: 'legacy-secret',
        rememberPassword: true,
      })
    ).rejects.toThrow('管理界面只能在桌面应用内打开。');
    await expect(useAuthStore.getState().checkAuth()).resolves.toBe(false);
    expect(fetchConfig).not.toHaveBeenCalled();
  });
});
