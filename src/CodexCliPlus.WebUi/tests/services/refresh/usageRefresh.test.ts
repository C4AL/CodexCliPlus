import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const authState = vi.hoisted(() => ({
  apiBase: 'http://127.0.0.1:15345',
  managementKey: 'secret',
}));

vi.mock('@/stores/useAuthStore', () => ({
  useAuthStore: {
    getState: () => authState,
  },
}));

vi.mock('@/services/api/usage', () => ({
  usageApi: {
    getUsage: vi.fn(),
    getApiKeyUsage: vi.fn(),
  },
}));

import { usageApi } from '@/services/api/usage';
import { requestUsageRefresh, resetUsageRefreshScheduler } from '@/services/refresh';

const mockedUsageApi = vi.mocked(usageApi);

describe('requestUsageRefresh', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-05-02T00:00:00.000Z'));
    vi.clearAllMocks();
    resetUsageRefreshScheduler();
    authState.apiBase = 'http://127.0.0.1:15345';
    authState.managementKey = 'secret';
    mockedUsageApi.getApiKeyUsage.mockResolvedValue({});
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('coalesces force refreshes while a usage request is in flight', async () => {
    let resolveUsage: (value: { usage: { total_requests: number } }) => void = () => {};
    mockedUsageApi.getUsage.mockReturnValueOnce(
      new Promise((resolve) => {
        resolveUsage = resolve;
      })
    );

    const first = requestUsageRefresh('test:first', { force: true });
    const second = requestUsageRefresh('test:second', { force: true });

    expect(mockedUsageApi.getUsage).toHaveBeenCalledTimes(1);
    resolveUsage({ usage: { total_requests: 1 } });

    await expect(Promise.all([first, second])).resolves.toHaveLength(2);
    expect(mockedUsageApi.getUsage).toHaveBeenCalledTimes(1);
  });

  it('merges multiple force refreshes inside the short cooldown into one trailing request', async () => {
    mockedUsageApi.getUsage
      .mockResolvedValueOnce({ usage: { total_requests: 1 } })
      .mockResolvedValueOnce({ usage: { total_requests: 2 } });

    await requestUsageRefresh('test:initial', { force: true });

    const second = requestUsageRefresh('test:second', { force: true });
    const third = requestUsageRefresh('test:third', { force: true });

    expect(mockedUsageApi.getUsage).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(1_500);

    const results = await Promise.all([second, third]);
    expect(results.map((item) => item.response.usage)).toEqual([
      { total_requests: 2 },
      { total_requests: 2 },
    ]);
    expect(mockedUsageApi.getUsage).toHaveBeenCalledTimes(2);
  });
});
