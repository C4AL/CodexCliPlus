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
});
