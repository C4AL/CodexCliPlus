import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

type WebViewListener = (event: MessageEvent) => void;

const sampleSnapshot = {
  checkedAt: '2026-05-02T00:00:00.000Z',
  readinessScore: 100,
  summary: '本地 Codex 环境已就绪。',
  items: [],
  repairCapabilities: [],
};

async function loadBridge(actions: {
  requestSnapshot?: (requestId: string) => boolean | void;
  runRepair?: (actionId: string, requestId: string) => boolean | void;
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
      requestLocalDependencySnapshot: actions.requestSnapshot,
      runLocalDependencyRepair: actions.runRepair,
    },
  });

  const bridge = await import('@/desktop/bridge');
  return {
    requestLocalDependencySnapshot: bridge.requestLocalDependencySnapshot,
    runLocalDependencyRepair: bridge.runLocalDependencyRepair,
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
    const bridge = await loadBridge({
      requestSnapshot: (requestId) => {
        requestIds.push(requestId);
        return true;
      },
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
    const bridge = await loadBridge({ requestSnapshot: () => true });

    const request = bridge.requestLocalDependencySnapshot();
    const rejected = expect(request).rejects.toThrow('桌面端响应超时');
    await vi.advanceTimersByTimeAsync(2_000);

    await rejected;
  });

  it('normalizes winget repair item and capability without schema changes', async () => {
    const requestIds: string[] = [];
    const bridge = await loadBridge({
      requestSnapshot: (requestId) => {
        requestIds.push(requestId);
        return true;
      },
    });

    const request = bridge.requestLocalDependencySnapshot();
    bridge.emit({
      type: 'localDependencySnapshot',
      requestId: requestIds[0],
      snapshot: {
        ...sampleSnapshot,
        items: [
          {
            id: 'winget',
            name: 'winget',
            status: 'warning',
            severity: 'repairTool',
            detail: '未找到 winget。',
            recommendation: '修复 winget 后可使用内置安装动作。',
            repairActionId: 'repair-winget',
          },
        ],
        repairCapabilities: [
          {
            actionId: 'repair-winget',
            name: '修复 winget',
            isAvailable: true,
            requiresElevation: true,
            isOptional: false,
            detail: '将通过 Microsoft.WinGet.Client 修复 winget。',
          },
        ],
      },
    });

    await expect(request).resolves.toMatchObject({
      items: [expect.objectContaining({ id: 'winget', repairActionId: 'repair-winget' })],
      repairCapabilities: [
        expect.objectContaining({ actionId: 'repair-winget', isAvailable: true }),
      ],
    });
  });

  it('delivers repair progress without resolving before final result', async () => {
    const repairRequestIds: string[] = [];
    const bridge = await loadBridge({
      requestSnapshot: () => true,
      runRepair: (_actionId, requestId) => {
        repairRequestIds.push(requestId);
        return true;
      },
    });
    const progressEvents: unknown[] = [];

    let settled = false;
    const request = bridge
      .runLocalDependencyRepair('repair-user-path', (progress) => {
        progressEvents.push(progress);
      })
      .then((response) => {
        settled = true;
        return response;
      });

    expect(repairRequestIds).toHaveLength(1);
    bridge.emit({
      type: 'localDependencyRepairStarted',
      requestId: repairRequestIds[0],
      actionId: 'repair-user-path',
    });
    bridge.emit({
      type: 'localDependencyRepairProgress',
      requestId: repairRequestIds[0],
      progress: {
        actionId: 'repair-user-path',
        phase: 'commandRunning',
        message: '正在执行命令。',
        commandLine: 'winget install OpenJS.NodeJS.LTS',
        recentOutput: ['准备安装'],
        logPath: 'C:\\logs\\local-environment-repair.log',
        updatedAt: '2026-05-02T00:00:01.000Z',
        exitCode: null,
      },
    });

    await Promise.resolve();
    expect(settled).toBe(false);
    expect(progressEvents).toEqual([
      expect.objectContaining({
        actionId: 'repair-user-path',
        phase: 'commandRunning',
        commandLine: 'winget install OpenJS.NodeJS.LTS',
        recentOutput: ['准备安装'],
      }),
    ]);

    bridge.emit({
      type: 'localDependencyRepairResult',
      requestId: repairRequestIds[0],
      result: {
        actionId: 'repair-user-path',
        succeeded: true,
        exitCode: 0,
        summary: '用户 PATH 已修复。',
        detail: '已补齐 1 个安全目录。',
        logPath: 'C:\\logs\\local-environment-repair.log',
      },
      snapshot: sampleSnapshot,
    });

    await expect(request).resolves.toMatchObject({
      result: { succeeded: true, summary: '用户 PATH 已修复。' },
      snapshot: { readinessScore: 100 },
    });
  });

  it('passes required environment repair action through the existing repair channel', async () => {
    const repairCalls: Array<{ actionId: string; requestId: string }> = [];
    const bridge = await loadBridge({
      runRepair: (actionId, requestId) => {
        repairCalls.push({ actionId, requestId });
        return true;
      },
    });

    const request = bridge.runLocalDependencyRepair('repair-required-env-install-latest-codex');

    expect(repairCalls).toHaveLength(1);
    expect(repairCalls[0].actionId).toBe('repair-required-env-install-latest-codex');
    bridge.emit({
      type: 'localDependencyRepairResult',
      requestId: repairCalls[0].requestId,
      result: {
        actionId: 'repair-required-env-install-latest-codex',
        succeeded: true,
        exitCode: 0,
        summary: '一键修复并安装最新 Codex 已完成。',
        detail: '已完成。',
      },
      snapshot: sampleSnapshot,
    });

    await expect(request).resolves.toMatchObject({
      result: {
        actionId: 'repair-required-env-install-latest-codex',
        succeeded: true,
      },
      snapshot: { readinessScore: 100 },
    });
  });
});
