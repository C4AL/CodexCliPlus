import { beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('@/services/api', () => ({
  usageApi: {
    getUsage: vi.fn()
  }
}));

vi.mock('@/stores/useAuthStore', () => ({
  useAuthStore: {
    getState: () => ({ apiBase: 'http://127.0.0.1:15345', managementKey: 'secret' })
  }
}));

vi.mock('@/desktop/bridge', () => ({
  notifyUsageStatsRefreshedInDesktopShell: vi.fn()
}));

import { notifyUsageStatsRefreshedInDesktopShell } from '@/desktop/bridge';
import { usageApi } from '@/services/api';
import { useUsageStatsStore } from '@/stores/useUsageStatsStore';

const mockedUsageApi = vi.mocked(usageApi);
const mockedNotifyUsageStatsRefreshed = vi.mocked(notifyUsageStatsRefreshedInDesktopShell);

describe('useUsageStatsStore desktop sync bridge', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useUsageStatsStore.getState().clearUsageStats();
    mockedUsageApi.getUsage.mockResolvedValue({
      usage: {
        total_requests: 1,
        apis: {}
      }
    });
  });

  it('notifies the desktop shell after a successful usage refresh', async () => {
    await useUsageStatsStore.getState().loadUsageStats({ force: true });

    expect(mockedNotifyUsageStatsRefreshed).toHaveBeenCalledTimes(1);
  });

  it('does not emit a refresh notification when clearing local store state', () => {
    useUsageStatsStore.getState().clearUsageStats();

    expect(mockedNotifyUsageStatsRefreshed).not.toHaveBeenCalled();
  });
});
