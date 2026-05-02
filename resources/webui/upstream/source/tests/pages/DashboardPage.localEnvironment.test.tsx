import { fireEvent, render, screen, waitFor } from '@testing-library/react';
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
  readinessScore: 100,
  summary: '本地环境检测完成。',
  items: [],
  repairCapabilities: [],
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
});
