import { afterEach, describe, expect, it, vi } from 'vitest';

type WebViewListener = (event: MessageEvent) => void;

const sampleRouteState = {
  currentMode: 'official',
  targetMode: 'official',
  currentTargetId: 'official',
  currentLabel: '官方 Codex',
  targets: [
    {
      id: 'official',
      mode: 'official',
      kind: 'official',
      label: '官方 Codex',
      isCurrent: true,
      canSwitch: true,
    },
  ],
  configPath: 'C:\\Users\\user\\.codex\\config.toml',
  authPath: 'C:\\Users\\user\\.codex\\auth.json',
  canSwitch: true,
  statusMessage: '已连接官方 Codex。',
};

const sampleUserFile = {
  fileId: 'config',
  path: 'C:\\Users\\user\\.codex\\config.toml',
  content: 'model = "gpt-5"',
  language: 'toml',
  exists: true,
  lastWriteTimeUtc: '2026-05-09T00:00:00.000Z',
  sizeBytes: 15,
  validation: {
    isValid: true,
    message: '配置有效。',
  },
};

async function loadBridge(actions: {
  requestRouteState?: (requestId: string) => boolean | void;
  requestUserFiles?: (requestId: string) => boolean | void;
}) {
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
      requestCodexRouteState: actions.requestRouteState,
      requestCodexUserFiles: actions.requestUserFiles,
    },
  });

  const bridge = await import('@/api/desktopBridge');
  return {
    requestCodexRouteState: bridge.requestCodexRouteState,
    requestCodexUserFiles: bridge.requestCodexUserFiles,
    emit: (data: unknown) => listener?.({ data } as MessageEvent),
  };
}

describe('Codex desktop bridge', () => {
  afterEach(() => {
    vi.useRealTimers();
    Reflect.deleteProperty(window, 'chrome');
    Reflect.deleteProperty(window, '__CODEXCLIPLUS_DESKTOP_BRIDGE__');
    vi.resetModules();
  });

  it('reuses one in-flight route state request for repeated callers', async () => {
    const requestIds: string[] = [];
    const bridge = await loadBridge({
      requestRouteState: (requestId) => {
        requestIds.push(requestId);
        return true;
      },
    });

    const first = bridge.requestCodexRouteState();
    const second = bridge.requestCodexRouteState();

    expect(second).toBe(first);
    expect(requestIds).toHaveLength(1);

    bridge.emit({
      type: 'codexRouteResponse',
      requestId: requestIds[0],
      ok: true,
      state: sampleRouteState,
    });

    await expect(first).resolves.toMatchObject({ currentLabel: '官方 Codex' });
    await expect(second).resolves.toMatchObject({ statusMessage: sampleRouteState.statusMessage });

    const third = bridge.requestCodexRouteState();
    expect(requestIds).toHaveLength(2);
    bridge.emit({
      type: 'codexRouteResponse',
      requestId: requestIds[1],
      ok: true,
      state: {
        ...sampleRouteState,
        currentLabel: 'Codex CLI Plus',
      },
    });

    await expect(third).resolves.toMatchObject({ currentLabel: 'Codex CLI Plus' });
  });

  it('reuses one in-flight user file list request for repeated callers', async () => {
    const requestIds: string[] = [];
    const bridge = await loadBridge({
      requestUserFiles: (requestId) => {
        requestIds.push(requestId);
        return true;
      },
    });

    const first = bridge.requestCodexUserFiles();
    const second = bridge.requestCodexUserFiles();

    expect(second).toBe(first);
    expect(requestIds).toHaveLength(1);

    bridge.emit({
      type: 'codexUserFileResponse',
      requestId: requestIds[0],
      ok: true,
      files: [sampleUserFile],
    });

    await expect(first).resolves.toEqual([expect.objectContaining({ fileId: 'config' })]);
    await expect(second).resolves.toEqual([
      expect.objectContaining({ path: sampleUserFile.path }),
    ]);

    const third = bridge.requestCodexUserFiles();
    expect(requestIds).toHaveLength(2);
    bridge.emit({
      type: 'codexUserFileResponse',
      requestId: requestIds[1],
      ok: true,
      files: [
        {
          ...sampleUserFile,
          fileId: 'auth',
          path: 'C:\\Users\\user\\.codex\\auth.json',
          language: 'json',
        },
      ],
    });

    await expect(third).resolves.toEqual([expect.objectContaining({ fileId: 'auth' })]);
  });

  it('clears route state in-flight tracking when the desktop bridge rejects posting', async () => {
    const requestIds: string[] = [];
    const bridge = await loadBridge({
      requestRouteState: vi
        .fn((requestId: string) => {
          requestIds.push(requestId);
          return true;
        })
        .mockImplementationOnce((requestId: string) => {
          requestIds.push(requestId);
          return false;
        }),
    });

    await expect(bridge.requestCodexRouteState()).rejects.toThrow('桌面桥接通道未就绪');

    const retry = bridge.requestCodexRouteState();
    expect(requestIds).toHaveLength(2);
    bridge.emit({
      type: 'codexRouteResponse',
      requestId: requestIds[1],
      ok: true,
      state: sampleRouteState,
    });

    await expect(retry).resolves.toMatchObject({ currentTargetId: 'official' });
  });

  it('clears user file list in-flight tracking when the desktop bridge throws', async () => {
    const requestIds: string[] = [];
    const bridge = await loadBridge({
      requestUserFiles: vi
        .fn((requestId: string) => {
          requestIds.push(requestId);
          return true;
        })
        .mockImplementationOnce((requestId: string) => {
          requestIds.push(requestId);
          throw new Error('bridge unavailable');
        }),
    });

    await expect(bridge.requestCodexUserFiles()).rejects.toThrow('bridge unavailable');

    const retry = bridge.requestCodexUserFiles();
    expect(requestIds).toHaveLength(2);
    bridge.emit({
      type: 'codexUserFileResponse',
      requestId: requestIds[1],
      ok: true,
      files: [sampleUserFile],
    });

    await expect(retry).resolves.toEqual([expect.objectContaining({ fileId: 'config' })]);
  });
});
