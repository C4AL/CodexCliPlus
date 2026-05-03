import { beforeEach, describe, expect, it, vi } from 'vitest';

const authState = vi.hoisted(() => ({
  apiBase: 'http://127.0.0.1:15345',
  managementKey: 'secret',
}));

vi.mock('@/services/api/usage', () => ({
  usageApi: {
    getUsage: vi.fn(),
    getApiKeyUsage: vi.fn(),
  },
}));

vi.mock('@/stores/useAuthStore', () => ({
  useAuthStore: {
    getState: () => authState,
  },
}));

vi.mock('@/desktop/bridge', () => ({
  notifyUsageStatsRefreshedInDesktopShell: vi.fn(),
}));

vi.mock('@/utils/usage', () => ({
  collectUsageDetails: vi.fn(() => []),
  computeKeyStatsFromApiKeyUsage: vi.fn(() => ({
    bySource: {},
    byAuthIndex: {},
    statusBySource: {},
  })),
  computeKeyStatsFromDetails: vi.fn(() => ({ bySource: {}, byAuthIndex: {} })),
  mergeKeyStats: vi.fn((preferred, fallback) => ({
    bySource: { ...(fallback.bySource ?? {}), ...(preferred.bySource ?? {}) },
    byAuthIndex: { ...(fallback.byAuthIndex ?? {}), ...(preferred.byAuthIndex ?? {}) },
    statusBySource: { ...(fallback.statusBySource ?? {}), ...(preferred.statusBySource ?? {}) },
  })),
}));

vi.mock('@/i18n', () => ({
  default: {
    t: vi.fn((key: string) => `translated:${key}`),
  },
}));

import { notifyUsageStatsRefreshedInDesktopShell } from '@/desktop/bridge';
import { usageApi } from '@/services/api/usage';
import { useUsageStatsStore } from '@/stores/useUsageStatsStore';

const mockedUsageApi = vi.mocked(usageApi);
const mockedNotifyUsageStatsRefreshed = vi.mocked(notifyUsageStatsRefreshedInDesktopShell);

describe('useUsageStatsStore desktop sync bridge', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    authState.apiBase = 'http://127.0.0.1:15345';
    authState.managementKey = 'secret';
    useUsageStatsStore.getState().clearUsageStats();
    mockedUsageApi.getUsage.mockResolvedValue({
      usage: {
        total_requests: 1,
        apis: {},
      },
    });
    mockedUsageApi.getApiKeyUsage.mockResolvedValue({});
  });

  it('notifies the desktop shell after a successful usage refresh', async () => {
    await useUsageStatsStore.getState().loadUsageStats({ force: true });

    expect(mockedNotifyUsageStatsRefreshed).toHaveBeenCalledTimes(1);
  });

  it('reuses a fresh cached usage snapshot without another desktop notification', async () => {
    await useUsageStatsStore.getState().loadUsageStats({ force: true });
    mockedNotifyUsageStatsRefreshed.mockClear();

    await useUsageStatsStore.getState().loadUsageStats();

    expect(mockedUsageApi.getUsage).toHaveBeenCalledTimes(1);
    expect(mockedUsageApi.getApiKeyUsage).toHaveBeenCalledTimes(1);
    expect(mockedNotifyUsageStatsRefreshed).not.toHaveBeenCalled();
  });

  it('coalesces concurrent usage refreshes for the same connection scope', async () => {
    let resolveUsage: (value: { usage: { total_requests: number } }) => void = () => {};
    mockedUsageApi.getUsage.mockReturnValueOnce(
      new Promise((resolve) => {
        resolveUsage = resolve;
      })
    );

    const firstRefresh = useUsageStatsStore.getState().loadUsageStats({ force: true });
    const secondRefresh = useUsageStatsStore.getState().loadUsageStats({ force: true });
    resolveUsage({ usage: { total_requests: 2 } });
    await Promise.all([firstRefresh, secondRefresh]);

    expect(mockedUsageApi.getUsage).toHaveBeenCalledTimes(1);
    expect(mockedNotifyUsageStatsRefreshed).toHaveBeenCalledTimes(1);
  });

  it('ignores stale in-flight refreshes after the connection scope changes', async () => {
    let resolveStaleUsage: (value: { usage: { total_requests: number } }) => void = () => {};
    mockedUsageApi.getUsage
      .mockReturnValueOnce(
        new Promise((resolve) => {
          resolveStaleUsage = resolve;
        })
      )
      .mockResolvedValueOnce({ usage: { total_requests: 3 } });

    const staleRefresh = useUsageStatsStore.getState().loadUsageStats({ force: true });
    authState.managementKey = 'rotated-secret';
    const currentRefresh = useUsageStatsStore.getState().loadUsageStats({ force: true });

    resolveStaleUsage({ usage: { total_requests: 2 } });
    await Promise.all([staleRefresh, currentRefresh]);

    expect(mockedUsageApi.getUsage).toHaveBeenCalledTimes(2);
    expect(mockedNotifyUsageStatsRefreshed).toHaveBeenCalledTimes(1);
    expect(useUsageStatsStore.getState().scopeKey).toBe('http://127.0.0.1:15345::rotated-secret');
  });

  it('stores and rethrows normalized refresh errors', async () => {
    mockedUsageApi.getUsage.mockRejectedValueOnce('backend unavailable');

    await expect(useUsageStatsStore.getState().loadUsageStats({ force: true })).rejects.toThrow(
      'backend unavailable'
    );

    expect(useUsageStatsStore.getState().loading).toBe(false);
    expect(useUsageStatsStore.getState().error).toBe('backend unavailable');
    expect(mockedNotifyUsageStatsRefreshed).not.toHaveBeenCalled();
  });

  it('does not emit a refresh notification when clearing local store state', () => {
    useUsageStatsStore.getState().clearUsageStats();

    expect(mockedNotifyUsageStatsRefreshed).not.toHaveBeenCalled();
  });
});
