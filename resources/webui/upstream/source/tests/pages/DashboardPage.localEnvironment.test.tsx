import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { LocalDependencySnapshot } from '@/desktop/bridge';

const bridgeMocks = vi.hoisted(() => ({
  desktopMode: true,
  requestLocalDependencySnapshot: vi.fn(),
  runLocalDependencyRepair: vi.fn(),
}));

const storeState = vi.hoisted(() => ({
  auth: {
    connectionStatus: 'connected',
    serverVersion: '6.9.31',
    serverBuildDate: null,
    apiBase: 'http://127.0.0.1:15345',
  },
  config: {
    config: {
      apiKeys: ['sk-test'],
      debug: false,
      usageStatisticsEnabled: true,
      loggingToFile: false,
      requestRetry: 2,
      wsAuth: true,
      routingStrategy: 'round-robin',
      routingSessionAffinity: false,
    },
  },
  models: {
    models: [],
    loading: false,
    fetchModels: vi.fn(),
  },
  notifications: {
    showConfirmation: vi.fn(),
    showNotification: vi.fn(),
  },
}));

vi.mock('react-i18next', () => ({
  useTranslation: () => ({
    t: (key: string, values?: Record<string, unknown>) =>
      values ? `${key}:${JSON.stringify(values)}` : key,
    i18n: { language: 'zh-CN' },
  }),
}));

vi.mock('@/desktop/bridge', async () => {
  const actual = await vi.importActual<typeof import('@/desktop/bridge')>('@/desktop/bridge');
  return {
    ...actual,
    isDesktopMode: () => bridgeMocks.desktopMode,
    requestLocalDependencySnapshot: bridgeMocks.requestLocalDependencySnapshot,
    runLocalDependencyRepair: bridgeMocks.runLocalDependencyRepair,
  };
});

vi.mock('@/hooks/useDesktopDataChanged', () => ({
  useDesktopDataChanged: vi.fn(),
}));

vi.mock('@/stores', () => ({
  useAuthStore: (selector: (state: typeof storeState.auth) => unknown) =>
    selector(storeState.auth),
  useConfigStore: (selector: (state: typeof storeState.config) => unknown) =>
    selector(storeState.config),
  useModelsStore: (selector: (state: typeof storeState.models) => unknown) =>
    selector(storeState.models),
  useNotificationStore: () => storeState.notifications,
}));

vi.mock('@/services/api', () => ({
  apiKeysApi: { list: vi.fn(() => Promise.resolve(['sk-test'])) },
  authFilesApi: { list: vi.fn(() => Promise.resolve({ files: [] })) },
  providersApi: { getCodexConfigs: vi.fn(() => Promise.resolve([])) },
}));

import { DashboardPage } from '@/pages/DashboardPage';

const snapshot: LocalDependencySnapshot = {
  checkedAt: '2026-05-02T00:00:00.000Z',
  readinessScore: 80,
  summary: '本地环境检测完成。',
  items: [
    {
      id: 'node',
      name: 'Node.js',
      status: 'missing',
      severity: 'required',
      version: null,
      path: null,
      detail: '未找到 Node.js。',
      recommendation: '安装 Node.js LTS。',
      repairActionId: 'install-node-npm',
    },
  ],
  repairCapabilities: [
    {
      actionId: 'install-node-npm',
      name: '安装 Node.js LTS 和 npm',
      isAvailable: true,
      requiresElevation: true,
      isOptional: false,
      detail: '',
    },
  ],
};

const repairedSnapshot: LocalDependencySnapshot = {
  ...snapshot,
  readinessScore: 100,
  summary: '本地环境已刷新。',
  items: [
    {
      ...snapshot.items[0],
      status: 'ready',
      detail: 'Node.js 已就绪。',
    },
  ],
};

function renderDashboard() {
  return render(
    <MemoryRouter>
      <DashboardPage />
    </MemoryRouter>
  );
}

function getLocalEnvironmentButton() {
  return screen.getByRole('button', { name: /dashboard\.local_environment/ });
}

describe('DashboardPage local environment loading', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    bridgeMocks.desktopMode = true;
    bridgeMocks.requestLocalDependencySnapshot.mockResolvedValue(snapshot);
    bridgeMocks.runLocalDependencyRepair.mockResolvedValue({
      result: {
        actionId: 'install-node-npm',
        succeeded: true,
        exitCode: 0,
        summary: 'Node.js 安装已完成。',
        detail: '请重新检测。',
        logPath: 'C:\\logs\\local-environment-repair.log',
      },
      snapshot: repairedSnapshot,
    });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('does not request a local environment snapshot on mount', async () => {
    renderDashboard();

    await waitFor(() => {
      expect(screen.getByText('dashboard.system_overview')).toBeInTheDocument();
    });
    expect(bridgeMocks.requestLocalDependencySnapshot).not.toHaveBeenCalled();
  });

  it('requests a fresh snapshot when the local environment card is opened', async () => {
    renderDashboard();

    fireEvent.click(getLocalEnvironmentButton());

    await waitFor(() => {
      expect(bridgeMocks.requestLocalDependencySnapshot).toHaveBeenCalledTimes(1);
      expect(screen.getByText(snapshot.summary)).toBeInTheDocument();
    });
  });

  it('clears a previous bridge error before retrying', async () => {
    bridgeMocks.requestLocalDependencySnapshot
      .mockRejectedValueOnce(new Error('旧错误'))
      .mockResolvedValueOnce(snapshot);
    renderDashboard();

    fireEvent.click(getLocalEnvironmentButton());

    await screen.findByText('旧错误');
    fireEvent.click(screen.getByRole('button', { name: 'dashboard.local_environment_bridge_retry' }));

    await waitFor(() => {
      expect(screen.queryByText('旧错误')).not.toBeInTheDocument();
      expect(screen.getByText(snapshot.summary)).toBeInTheDocument();
    });
    expect(bridgeMocks.requestLocalDependencySnapshot).toHaveBeenCalledTimes(2);
  });

  it('shows repair progress and refreshes the snapshot after repair finishes', async () => {
    let resolveRepair:
      | ((value: Awaited<ReturnType<typeof bridgeMocks.runLocalDependencyRepair>>) => void)
      | null = null;
    bridgeMocks.runLocalDependencyRepair.mockImplementation((_actionId, onProgress) => {
      onProgress({
        actionId: 'install-node-npm',
        phase: 'commandRunning',
        message: '正在执行安装命令。',
        commandLine: 'winget install OpenJS.NodeJS.LTS',
        recentOutput: ['准备安装', '正在下载'],
        logPath: 'C:\\logs\\local-environment-repair.log',
        updatedAt: '2026-05-02T00:00:01.000Z',
        exitCode: null,
      });
      return new Promise((resolve) => {
        resolveRepair = resolve;
      });
    });
    renderDashboard();

    fireEvent.click(getLocalEnvironmentButton());
    await screen.findByText(snapshot.summary);
    fireEvent.click(screen.getByRole('button', { name: 'dashboard.local_environment_repair' }));

    expect(storeState.notifications.showConfirmation).toHaveBeenCalledTimes(1);
    const confirmation = storeState.notifications.showConfirmation.mock.calls[0][0];
    await act(async () => {
      void confirmation.onConfirm();
      await Promise.resolve();
    });

    expect(bridgeMocks.runLocalDependencyRepair).toHaveBeenCalledWith(
      'install-node-npm',
      expect.any(Function)
    );
    expect(screen.getByText('winget install OpenJS.NodeJS.LTS')).toBeInTheDocument();
    expect(screen.getByText((content) => content.includes('正在下载'))).toBeInTheDocument();

    await act(async () => {
      resolveRepair?.({
        result: {
          actionId: 'install-node-npm',
          succeeded: true,
          exitCode: 0,
          summary: 'Node.js 安装已完成。',
          detail: '请重新检测。',
          logPath: 'C:\\logs\\local-environment-repair.log',
        },
        snapshot: repairedSnapshot,
      });
    });

    await waitFor(() => {
      expect(screen.getByText(repairedSnapshot.summary)).toBeInTheDocument();
      expect(storeState.notifications.showNotification).toHaveBeenCalledWith(
        'Node.js 安装已完成。',
        'success'
      );
    });
  });
});
