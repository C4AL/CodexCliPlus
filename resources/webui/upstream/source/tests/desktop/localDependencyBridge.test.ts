import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

type WebViewListener = (event: MessageEvent) => void;

const sampleSnapshot = {
  checkedAt: '2026-05-02T00:00:00.000Z',
  readinessScore: 100,
  summary: '本地 Codex 环境已就绪。',
  items: [],
  repairCapabilities: [],
};

async function loadBridge(requestSnapshot: (requestId: string) => boolean | void) {
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
    __CODEXCLIPLUS_DESKTOP_BRIDGE__: {
      isDesktopMode: () => true,
      requestLocalDependencySnapshot: requestSnapshot,
    },
  });

  const bridge = await import('@/desktop/bridge');
  return {
    requestLocalDependencySnapshot: bridge.requestLocalDependencySnapshot,
    emit: (data: unknown) => listener?.({ data } as MessageEvent),
  };
}

describe('local dependency desktop bridge', () => {
  beforeEach(() => {
    vi.useRealTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
    Reflect.deleteProperty(window, 'chrome');
    Reflect.deleteProperty(window, '__CODEXCLIPLUS_DESKTOP_BRIDGE__');
    vi.resetModules();
  });

  it('reuses one in-flight snapshot request for repeated callers', async () => {
    const requestIds: string[] = [];
    const bridge = await loadBridge((requestId) => {
      requestIds.push(requestId);
      return true;
    });

    const first = bridge.requestLocalDependencySnapshot();
    const second = bridge.requestLocalDependencySnapshot();

    expect(second).toBe(first);
    expect(requestIds).toHaveLength(1);

    bridge.emit({
      type: 'localDependencySnapshot',
      requestId: requestIds[0],
      snapshot: sampleSnapshot,
    });

    await expect(first).resolves.toMatchObject({ readinessScore: 100 });
    await expect(second).resolves.toMatchObject({ summary: sampleSnapshot.summary });
  });

  it('uses a two second timeout for missing desktop responses', async () => {
    vi.useFakeTimers();
    const bridge = await loadBridge(() => true);

    const request = bridge.requestLocalDependencySnapshot();
    const rejected = expect(request).rejects.toThrow('桌面端响应超时');
    await vi.advanceTimersByTimeAsync(2_000);

    await rejected;
  });
});
