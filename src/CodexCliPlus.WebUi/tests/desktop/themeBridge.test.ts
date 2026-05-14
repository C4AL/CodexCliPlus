import { afterEach, describe, expect, it, vi } from 'vitest';

type WebViewListener = (event: MessageEvent) => void;

async function loadBridge() {
  let listener: WebViewListener | null = null;
  vi.resetModules();
  Object.assign(window, {
    chrome: {
      webview: {
        addEventListener: vi.fn((_type: 'message', nextListener: WebViewListener) => {
          listener = nextListener;
        }),
      },
    },
  });

  const bridge = await import('@/desktop/bridge');
  return {
    subscribeDesktopShellCommand: bridge.subscribeDesktopShellCommand,
    emit: (data: unknown) => listener?.({ data } as MessageEvent),
  };
}

describe('desktop theme bridge', () => {
  afterEach(() => {
    Reflect.deleteProperty(window, 'chrome');
    vi.resetModules();
    vi.restoreAllMocks();
  });

  it('preserves resolved theme and transition duration on setTheme commands', async () => {
    const bridge = await loadBridge();
    const commands: unknown[] = [];

    const unsubscribe = bridge.subscribeDesktopShellCommand((command) => {
      commands.push(command);
    });
    bridge.emit({
      type: 'setTheme',
      theme: 'auto',
      resolvedTheme: 'dark',
      transitionMs: 180,
    });

    expect(commands).toEqual([
      {
        type: 'setTheme',
        theme: 'auto',
        resolvedTheme: 'dark',
        transitionMs: 180,
      },
    ]);

    unsubscribe();
  });

  it('sends shell state without feeding theme back to the desktop host', async () => {
    const shellStateChanged = vi.fn();
    Object.assign(window, {
      __CODEXCLIPLUS_DESKTOP_BRIDGE__: {
        shellStateChanged,
      },
    });
    vi.resetModules();
    const { sendShellStateChanged } = await import('@/desktop/bridge');

    expect(
      sendShellStateChanged({
        connectionStatus: 'connected',
        apiBase: 'http://127.0.0.1:15345',
        sidebarCollapsed: true,
        pathname: '/usage',
      })
    ).toBe(true);

    expect(shellStateChanged).toHaveBeenCalledWith({
      connectionStatus: 'connected',
      apiBase: 'http://127.0.0.1:15345',
      sidebarCollapsed: true,
      pathname: '/usage',
    });
  });
});
