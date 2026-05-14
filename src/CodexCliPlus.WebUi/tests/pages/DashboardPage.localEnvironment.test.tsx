import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
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
  useAuthStore: (selector: (state: typeof storeState.auth) => unknown) => selector(storeState.auth),
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
    {
      id: 'npm',
      name: 'npm',
      status: 'missing',
      severity: 'required',
      version: null,
      path: null,
      detail: '未找到 npm。',
      recommendation: '安装 Node.js LTS 后 npm 会同步恢复。',
      repairActionId: 'install-node-npm',
    },
  ],
  repairCapabilities: [
    {
      actionId: 'repair-required-env-install-latest-codex',
      name: '一键修复',
      isAvailable: true,
      requiresElevation: true,
      isOptional: false,
      detail: '将一次提权修复 winget、Node.js/npm、Codex CLI 和用户 PATH，不处理 WSL。',
    },
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
    {
      ...snapshot.items[1],
      status: 'ready',
      detail: 'npm 已就绪。',
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

function getDependencyRow(name: string) {
  const row = screen.getByText(name).closest('[class*="localDependencyItem"]');
  expect(row).not.toBeNull();
  return row as HTMLElement;
}

function getNodeNpmRow() {
  return getDependencyRow('Node.js / npm');
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

  it('automatically requests a local environment snapshot on mount', async () => {
    renderDashboard();

    await waitFor(() => {
      expect(screen.getByText('dashboard.system_overview')).toBeInTheDocument();
      expect(bridgeMocks.requestLocalDependencySnapshot).toHaveBeenCalledTimes(1);
    });
  });

  it('shows the automatically refreshed latest snapshot when the local environment card is opened', async () => {
    renderDashboard();

    await waitFor(() => {
      expect(bridgeMocks.requestLocalDependencySnapshot).toHaveBeenCalledTimes(1);
    });
    fireEvent.click(getLocalEnvironmentButton());

    await waitFor(() => {
      expect(screen.getByText(snapshot.summary)).toBeInTheDocument();
    });
  });

  it('merges Node.js and npm into one display item with one repair action', async () => {
    renderDashboard();

    fireEvent.click(getLocalEnvironmentButton());
    await screen.findByText(snapshot.summary);
    const row = getNodeNpmRow();

    expect(within(row).getByText('Node.js / npm')).toBeInTheDocument();
    expect(
      within(row).getByText((content) =>
        content.includes('Node.js：未找到 Node.js。；npm：未找到 npm。')
      )
    ).toBeInTheDocument();
    expect(
      within(row).getByText((content) =>
        content.includes('Node.js：安装 Node.js LTS。；npm：安装 Node.js LTS 后 npm 会同步恢复。')
      )
    ).toBeInTheDocument();
    const repairButtons = screen.getAllByRole('button', {
      name: 'dashboard.local_environment_repair',
    });
    expect(repairButtons).toHaveLength(1);
    expect(repairButtons[0].className).toContain('localEnvironmentRepairButton');
    expect(repairButtons[0].className).toContain('btn-secondary');
    expect(repairButtons[0].className).not.toContain('btn-primary');
    const requiredRepairButton = screen.getByRole('button', {
      name: 'dashboard.local_environment_required_repair',
    });
    expect(requiredRepairButton.className).toContain('localEnvironmentRepairButton');
    expect(requiredRepairButton.className).toContain('btn-secondary');
    expect(requiredRepairButton.className).not.toContain('btn-primary');
  });

  it('places ready local environment items first but keeps them collapsed by default', async () => {
    bridgeMocks.requestLocalDependencySnapshot.mockResolvedValue({
      ...snapshot,
      summary: '本地环境混合状态。',
      items: [
        {
          id: 'powershell',
          name: 'PowerShell',
          status: 'ready',
          severity: 'required',
          version: '7.5.0',
          path: 'C:\\Program Files\\PowerShell\\7\\pwsh.exe',
          detail: 'PowerShell 已就绪。',
          recommendation: '',
          repairActionId: null,
        },
        {
          id: 'codex-cli',
          name: 'Codex CLI',
          status: 'missing',
          severity: 'required',
          version: null,
          path: null,
          detail: '未找到 Codex CLI。',
          recommendation: '安装最新 Codex。',
          repairActionId: 'install-codex-cli',
        },
      ],
      repairCapabilities: [
        snapshot.repairCapabilities[0],
        {
          actionId: 'install-codex-cli',
          name: '安装 Codex CLI',
          isAvailable: true,
          requiresElevation: false,
          isOptional: false,
          detail: '',
        },
      ],
    });
    renderDashboard();

    fireEvent.click(getLocalEnvironmentButton());
    await screen.findByText('本地环境混合状态。');

    const readyToggle = screen.getByRole('button', {
      name: /dashboard\.local_environment_ready_group_title/,
    });
    const codexRow = getDependencyRow('Codex CLI');

    expect(screen.queryByText('PowerShell')).not.toBeInTheDocument();
    expect(readyToggle.getAttribute('aria-expanded')).toBe('false');
    expect(
      readyToggle.compareDocumentPosition(codexRow) & Node.DOCUMENT_POSITION_FOLLOWING
    ).toBeTruthy();

    fireEvent.click(readyToggle);

    expect(readyToggle.getAttribute('aria-expanded')).toBe('true');
    expect(getDependencyRow('PowerShell')).toBeInTheDocument();
  });

  it('clears a previous bridge error before retrying', async () => {
    bridgeMocks.requestLocalDependencySnapshot
      .mockRejectedValueOnce(new Error('旧错误'))
      .mockResolvedValueOnce(snapshot);
    renderDashboard();

    fireEvent.click(getLocalEnvironmentButton());

    await screen.findByText('旧错误');
    fireEvent.click(
      screen.getByRole('button', { name: 'dashboard.local_environment_bridge_retry' })
    );

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
    bridgeMocks.requestLocalDependencySnapshot
      .mockResolvedValueOnce(snapshot)
      .mockResolvedValue(repairedSnapshot);
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
    let confirmResult: unknown;
    await act(async () => {
      confirmResult = confirmation.onConfirm();
      await Promise.resolve();
    });

    expect(confirmResult).toBeUndefined();
    expect(bridgeMocks.runLocalDependencyRepair).toHaveBeenCalledWith(
      'install-node-npm',
      expect.any(Function)
    );
    const progressBar = screen.getByRole('progressbar');
    expect(progressBar).toHaveAttribute('aria-valuenow', '58');
    expect(progressBar).toHaveAttribute('aria-busy', 'true');
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
      expect(storeState.notifications.showNotification).not.toHaveBeenCalled();
    });
  });

  it('shows repair failure detail in the progress panel', async () => {
    bridgeMocks.runLocalDependencyRepair.mockResolvedValue({
      result: {
        actionId: 'install-node-npm',
        succeeded: false,
        exitCode: -1978335217,
        summary: '安装 Node.js LTS 和 npm 失败。',
        detail:
          '退出码 -1978335217（0x8A15000F）：winget 源数据缺失或索引未就绪，无法读取安装包清单。',
        logPath: 'C:\\logs\\local-environment-repair.log',
      },
      snapshot,
    });
    renderDashboard();

    fireEvent.click(getLocalEnvironmentButton());
    await screen.findByText(snapshot.summary);
    fireEvent.click(screen.getByRole('button', { name: 'dashboard.local_environment_repair' }));

    const confirmation = storeState.notifications.showConfirmation.mock.calls[0][0];
    await act(async () => {
      confirmation.onConfirm();
      await Promise.resolve();
    });

    await waitFor(() => {
      expect(screen.getByText('dashboard.local_environment_repair_detail')).toBeInTheDocument();
      expect(
        screen.getByText(
          '退出码 -1978335217（0x8A15000F）：winget 源数据缺失或索引未就绪，无法读取安装包清单。'
        )
      ).toBeInTheDocument();
      expect(storeState.notifications.showNotification).not.toHaveBeenCalled();
    });
  });

  it('runs required environment and latest Codex repair from the panel header', async () => {
    bridgeMocks.runLocalDependencyRepair.mockResolvedValue({
      result: {
        actionId: 'repair-required-env-install-latest-codex',
        succeeded: true,
        exitCode: 0,
        summary: '一键修复并安装最新 Codex 已完成。',
        detail: '已完成。',
        logPath: 'C:\\logs\\local-environment-repair.log',
      },
      snapshot: repairedSnapshot,
    });
    renderDashboard();

    fireEvent.click(getLocalEnvironmentButton());
    await screen.findByText(snapshot.summary);
    fireEvent.click(
      screen.getByRole('button', {
        name: 'dashboard.local_environment_required_repair',
      })
    );

    expect(storeState.notifications.showConfirmation).toHaveBeenCalledTimes(1);
    const confirmation = storeState.notifications.showConfirmation.mock.calls[0][0];
    await act(async () => {
      confirmation.onConfirm();
      await Promise.resolve();
    });

    expect(bridgeMocks.runLocalDependencyRepair).toHaveBeenCalledWith(
      'repair-required-env-install-latest-codex',
      expect.any(Function)
    );
    await waitFor(() => {
      expect(screen.getByText(repairedSnapshot.summary)).toBeInTheDocument();
      expect(storeState.notifications.showNotification).not.toHaveBeenCalled();
    });
  });

  it('offers bundled offline fallback after required repair network failure', async () => {
    bridgeMocks.runLocalDependencyRepair
      .mockResolvedValueOnce({
        result: {
          actionId: 'repair-required-env-install-latest-codex',
          succeeded: false,
          exitCode: null,
          summary: '解析 Node.js LTS 安装包失败。',
          detail: 'DNS failed',
          failureKind: 'network',
          recommendedFallbackActionId: 'repair-required-env-install-bundled-codex',
          logPath: 'C:\\logs\\local-environment-repair.log',
        },
        snapshot,
      })
      .mockResolvedValueOnce({
        result: {
          actionId: 'repair-required-env-install-bundled-codex',
          succeeded: true,
          exitCode: 0,
          summary: '已使用内置离线包临时安装本地环境。',
          detail: '之后会提示升级。',
          logPath: 'C:\\logs\\local-environment-repair.log',
        },
        snapshot: repairedSnapshot,
      });
    renderDashboard();

    fireEvent.click(getLocalEnvironmentButton());
    await screen.findByText(snapshot.summary);
    fireEvent.click(
      screen.getByRole('button', {
        name: 'dashboard.local_environment_required_repair',
      })
    );

    const firstConfirmation = storeState.notifications.showConfirmation.mock.calls[0][0];
    await act(async () => {
      firstConfirmation.onConfirm();
      await Promise.resolve();
    });

    await waitFor(() => {
      expect(storeState.notifications.showConfirmation).toHaveBeenCalledTimes(2);
    });
    const fallbackConfirmation = storeState.notifications.showConfirmation.mock.calls[1][0];
    expect(fallbackConfirmation.title).toBe('dashboard.local_environment_offline_fallback_title');
    expect(fallbackConfirmation.confirmText).toBe(
      'dashboard.local_environment_offline_fallback_confirm'
    );

    await act(async () => {
      fallbackConfirmation.onConfirm();
      await Promise.resolve();
    });

    expect(bridgeMocks.runLocalDependencyRepair).toHaveBeenNthCalledWith(
      1,
      'repair-required-env-install-latest-codex',
      expect.any(Function)
    );
    expect(bridgeMocks.runLocalDependencyRepair).toHaveBeenNthCalledWith(
      2,
      'repair-required-env-install-bundled-codex',
      expect.any(Function)
    );
  });

  it('does not offer bundled offline fallback after non-network required repair failure', async () => {
    bridgeMocks.runLocalDependencyRepair.mockResolvedValue({
      result: {
        actionId: 'repair-required-env-install-latest-codex',
        succeeded: false,
        exitCode: 1603,
        summary: '安装 Node.js LTS 和 npm 失败。',
        detail: 'msiexec failed',
        logPath: 'C:\\logs\\local-environment-repair.log',
      },
      snapshot,
    });
    renderDashboard();

    fireEvent.click(getLocalEnvironmentButton());
    await screen.findByText(snapshot.summary);
    fireEvent.click(
      screen.getByRole('button', {
        name: 'dashboard.local_environment_required_repair',
      })
    );

    const firstConfirmation = storeState.notifications.showConfirmation.mock.calls[0][0];
    await act(async () => {
      firstConfirmation.onConfirm();
      await Promise.resolve();
    });

    await waitFor(() => {
      expect(screen.getByText('dashboard.local_environment_repair_detail')).toBeInTheDocument();
    });
    expect(storeState.notifications.showConfirmation).toHaveBeenCalledTimes(1);
  });

  it('shows required environment repair progress in the panel header', async () => {
    bridgeMocks.runLocalDependencyRepair.mockImplementation((_actionId, onProgress) => {
      onProgress({
        actionId: 'repair-required-env-install-latest-codex',
        phase: 'commandRunning',
        message: '正在安装最新 Codex。',
        commandLine: 'cmd.exe /d /c npm install -g @openai/codex@latest',
        recentOutput: ['updated latest codex'],
        logPath: 'C:\\logs\\local-environment-repair.log',
        updatedAt: '2026-05-02T00:00:01.000Z',
        exitCode: null,
      });
      return new Promise(() => {});
    });
    renderDashboard();

    fireEvent.click(getLocalEnvironmentButton());
    await screen.findByText(snapshot.summary);
    fireEvent.click(
      screen.getByRole('button', {
        name: 'dashboard.local_environment_required_repair',
      })
    );
    const confirmation = storeState.notifications.showConfirmation.mock.calls[0][0];
    await act(async () => {
      confirmation.onConfirm();
      await Promise.resolve();
    });

    expect(
      screen.getByRole('button', {
        name: 'dashboard.local_environment_required_repairing_button',
      })
    ).toBeDisabled();
    expect(
      screen.getByText('cmd.exe /d /c npm install -g @openai/codex@latest')
    ).toBeInTheDocument();
    expect(
      screen.getByText((content) => content.includes('updated latest codex'))
    ).toBeInTheDocument();
  });

  it('disables required environment repair when the capability is unavailable', async () => {
    bridgeMocks.requestLocalDependencySnapshot.mockResolvedValue({
      ...snapshot,
      repairCapabilities: [
        {
          actionId: 'repair-required-env-install-latest-codex',
          name: '一键修复',
          isAvailable: false,
          requiresElevation: true,
          isOptional: false,
          detail: '需要 winget、PowerShell 或 npm 至少一项可用。',
        },
      ],
    });
    renderDashboard();

    fireEvent.click(getLocalEnvironmentButton());
    await screen.findByText(snapshot.summary);

    expect(
      screen.getByRole('button', {
        name: 'dashboard.local_environment_required_repair',
      })
    ).toBeDisabled();
    expect(screen.getByText('需要 winget、PowerShell 或 npm 至少一项可用。')).toBeInTheDocument();
  });

  it('keeps shared Node.js and npm repair progress attached to the merged item', async () => {
    bridgeMocks.runLocalDependencyRepair.mockImplementation(() => new Promise(() => {}));
    renderDashboard();

    fireEvent.click(getLocalEnvironmentButton());
    await screen.findByText(snapshot.summary);
    const nodeNpmRow = getNodeNpmRow();
    fireEvent.click(
      within(nodeNpmRow).getByRole('button', { name: 'dashboard.local_environment_repair' })
    );

    const confirmation = storeState.notifications.showConfirmation.mock.calls[0][0];
    let confirmResult: unknown;
    await act(async () => {
      confirmResult = confirmation.onConfirm();
      await Promise.resolve();
    });

    expect(confirmResult).toBeUndefined();
    expect(bridgeMocks.runLocalDependencyRepair).toHaveBeenCalledWith(
      'install-node-npm',
      expect.any(Function)
    );
    expect(
      within(nodeNpmRow).getByRole('button', {
        name: 'dashboard.local_environment_repairing_button',
      })
    ).toBeDisabled();
    expect(
      screen.getAllByText('dashboard.local_environment_repair_progress_starting')
    ).toHaveLength(1);
    expect(
      within(nodeNpmRow).getByText('dashboard.local_environment_repair_progress_starting')
    ).toBeInTheDocument();
  });

  it('shows an error notification and clears repair loading when repair rejects', async () => {
    let rejectRepair: ((reason?: unknown) => void) | null = null;
    bridgeMocks.runLocalDependencyRepair.mockImplementation(
      () =>
        new Promise((_resolve, reject) => {
          rejectRepair = reject;
        })
    );
    renderDashboard();

    fireEvent.click(getLocalEnvironmentButton());
    await screen.findByText(snapshot.summary);
    const nodeNpmRow = getNodeNpmRow();
    fireEvent.click(screen.getByRole('button', { name: 'dashboard.local_environment_repair' }));

    const confirmation = storeState.notifications.showConfirmation.mock.calls[0][0];
    let confirmResult: unknown;
    await act(async () => {
      confirmResult = confirmation.onConfirm();
      await Promise.resolve();
    });

    expect(confirmResult).toBeUndefined();
    expect(
      within(nodeNpmRow).getByRole('button', {
        name: 'dashboard.local_environment_repairing_button',
      })
    ).toBeDisabled();
    expect(
      within(nodeNpmRow).getByText('dashboard.local_environment_repair_progress_starting')
    ).toBeInTheDocument();

    await act(async () => {
      rejectRepair?.(new Error('用户取消了管理员授权。'));
    });

    await waitFor(() => {
      expect(storeState.notifications.showNotification).toHaveBeenCalledWith(
        '用户取消了管理员授权。',
        'error'
      );
      expect(
        within(nodeNpmRow).getByRole('button', { name: 'dashboard.local_environment_repair' })
      ).not.toBeDisabled();
      expect(
        within(nodeNpmRow).getByText('dashboard.local_environment_repair_phase_failed')
      ).toBeInTheDocument();
      expect(within(nodeNpmRow).getByText('用户取消了管理员授权。')).toBeInTheDocument();
    });
  });

  it('runs winget repair action from the local environment item', async () => {
    const wingetSnapshot: LocalDependencySnapshot = {
      ...snapshot,
      items: [
        {
          id: 'winget',
          name: 'winget',
          status: 'warning',
          severity: 'repairTool',
          version: null,
          path: null,
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
    };
    bridgeMocks.requestLocalDependencySnapshot.mockResolvedValue(wingetSnapshot);
    bridgeMocks.runLocalDependencyRepair.mockResolvedValue({
      result: {
        actionId: 'repair-winget',
        succeeded: true,
        exitCode: 0,
        summary: 'winget 修复已完成。',
        detail: '请重新检测。',
        logPath: 'C:\\logs\\local-environment-repair.log',
      },
      snapshot: {
        ...wingetSnapshot,
        readinessScore: 100,
        summary: '本地环境已刷新。',
      },
    });
    renderDashboard();

    fireEvent.click(getLocalEnvironmentButton());
    await screen.findByText(wingetSnapshot.summary);
    fireEvent.click(screen.getByRole('button', { name: 'dashboard.local_environment_repair' }));

    const confirmation = storeState.notifications.showConfirmation.mock.calls[0][0];
    let confirmResult: unknown;
    await act(async () => {
      confirmResult = confirmation.onConfirm();
      await Promise.resolve();
    });

    expect(confirmResult).toBeUndefined();
    expect(bridgeMocks.runLocalDependencyRepair).toHaveBeenCalledWith(
      'repair-winget',
      expect.any(Function)
    );
  });
});
