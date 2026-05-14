import { beforeEach, describe, expect, it, vi } from 'vitest';

const authState = vi.hoisted(() => ({
  apiBase: 'http://127.0.0.1:15345',
  managementKey: 'secret',
}));

vi.mock('@/stores/useAuthStore', () => ({
  useAuthStore: {
    getState: () => authState,
  },
}));

vi.mock('@/services/api/logs', () => ({
  logsApi: {
    fetchLogs: vi.fn(),
    fetchErrorLogs: vi.fn(),
  },
}));

vi.mock('@/services/api/usage', () => ({
  usageApi: {
    getUsage: vi.fn(),
    getApiKeyUsage: vi.fn(),
  },
}));

import { logsApi } from '@/services/api/logs';
import { usageApi } from '@/services/api/usage';
import {
  requestLogsRefresh,
  requestUsageRefresh,
  resetLogStreamScheduler,
  resetUsageRefreshScheduler,
  subscribeLogStream,
} from '@/services/refresh';

const mockedLogsApi = vi.mocked(logsApi);
const mockedUsageApi = vi.mocked(usageApi);

describe('log refresh scheduler', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    resetLogStreamScheduler();
    resetUsageRefreshScheduler();
    mockedUsageApi.getUsage.mockResolvedValue({ usage: { total_requests: 1 } });
    mockedUsageApi.getApiKeyUsage.mockResolvedValue({});
    mockedLogsApi.fetchLogs.mockResolvedValue({
      lines: ['2026-05-02T00:00:00Z INFO POST /v1/responses 200 123ms'],
      'line-count': 1,
      'latest-timestamp': 100,
    });
  });

  it('reuses one /logs request across subscribers and coalesces the usage refresh it signals', async () => {
    const firstSubscriber = vi.fn();
    const secondSubscriber = vi.fn();
    const unsubscribeFirst = subscribeLogStream((event) => {
      firstSubscriber(event);
      void requestUsageRefresh('test:log-signal', { force: true });
    });
    const unsubscribeSecond = subscribeLogStream(secondSubscriber);

    try {
      const first = requestLogsRefresh('test:first', { mode: 'incremental' });
      const second = requestLogsRefresh('test:second', { mode: 'incremental' });
      await Promise.all([first, second]);

      await Promise.resolve();

      expect(mockedLogsApi.fetchLogs).toHaveBeenCalledTimes(1);
      expect(firstSubscriber).toHaveBeenCalledTimes(1);
      expect(secondSubscriber).toHaveBeenCalledTimes(1);
      expect(mockedUsageApi.getUsage).toHaveBeenCalledTimes(1);
    } finally {
      unsubscribeFirst();
      unsubscribeSecond();
    }
  });
});
