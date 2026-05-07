import { afterEach, describe, expect, it, vi } from 'vitest';
import type { DesktopBootstrapPayload } from '@/desktop/bridge';

const bootstrap: DesktopBootstrapPayload = {
  desktopMode: true,
  apiBase: 'http://127.0.0.1:15345',
  desktopSessionId: 'desktop-session-1',
  theme: 'auto',
  resolvedTheme: 'light',
};

async function loadBridge() {
  vi.resetModules();
  return import('@/desktop/bridge');
}

describe('desktop bootstrap bridge', () => {
  afterEach(() => {
    Reflect.deleteProperty(window, '__CODEXCLIPLUS_DESKTOP_BRIDGE__');
    Reflect.deleteProperty(window, 'chrome');
    vi.resetModules();
    vi.restoreAllMocks();
  });

  it('detects desktop mode from the WebView2 host bridge before the custom bridge is injected', async () => {
    Object.assign(window, {
      chrome: {
        webview: {
          postMessage: vi.fn(),
        },
      },
    });

    const bridge = await loadBridge();

    expect(bridge.isDesktopMode()).toBe(true);
  });

  it('does not permanently cache a missing custom bridge before bootstrap injection', async () => {
    const bridge = await loadBridge();

    expect(bridge.getDesktopBootstrap()).toBeNull();

    const consumeBootstrap = vi.fn(() => bootstrap);
    Object.assign(window, {
      __CODEXCLIPLUS_DESKTOP_BRIDGE__: {
        isDesktopMode: () => true,
        consumeBootstrap,
      },
    });

    expect(bridge.getDesktopBootstrap()).toMatchObject(bootstrap);
    expect(bridge.getDesktopBootstrap()).toMatchObject(bootstrap);
    expect(consumeBootstrap).toHaveBeenCalledTimes(1);
  });

  it('retries when the custom bridge reports that bootstrap is temporarily unavailable', async () => {
    const consumeBootstrap = vi.fn().mockReturnValueOnce(null).mockReturnValueOnce(bootstrap);
    Object.assign(window, {
      __CODEXCLIPLUS_DESKTOP_BRIDGE__: {
        isDesktopMode: () => true,
        consumeBootstrap,
      },
    });

    const bridge = await loadBridge();

    expect(bridge.getDesktopBootstrap()).toBeNull();
    expect(bridge.getDesktopBootstrap()).toMatchObject(bootstrap);
    expect(bridge.getDesktopBootstrap()).toMatchObject(bootstrap);
    expect(consumeBootstrap).toHaveBeenCalledTimes(2);
  });
});
