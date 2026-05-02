import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const bootstrap = {
  desktopMode: true,
  apiBase: 'http://127.0.0.1:15345',
  managementKey: 'desktop-secret',
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

describe('useAuthStore desktop restore', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  afterEach(() => {
    Reflect.deleteProperty(window, '__CODEXCLIPLUS_DESKTOP_BRIDGE__');
    vi.resetModules();
    vi.restoreAllMocks();
  });

  it('trusts the desktop bootstrap without blocking on config fetch', async () => {
    const { useAuthStore, useConfigStore } = await loadDesktopAuthStore();
    const fetchConfig = vi.fn().mockRejectedValue(new Error('不应阻塞桌面首屏'));
    useConfigStore.setState({ fetchConfig });

    await expect(useAuthStore.getState().restoreSession()).resolves.toBe(true);

    expect(fetchConfig).not.toHaveBeenCalled();
    expect(useAuthStore.getState()).toMatchObject({
      isAuthenticated: true,
      apiBase: bootstrap.apiBase,
      managementKey: bootstrap.managementKey,
      connectionStatus: 'connected',
    });
  });
});
