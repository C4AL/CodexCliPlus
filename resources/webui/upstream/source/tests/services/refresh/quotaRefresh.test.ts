import type { TFunction } from 'i18next';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { QuotaConfig } from '@/components/quota/quotaConfigs';
import type { AuthFileItem, CodexQuotaState, CodexQuotaWindow } from '@/types';
import { requestQuotaRefresh, resetQuotaRefreshScheduler } from '@/services/refresh';
import { useQuotaStore } from '@/stores/useQuotaStore';

const authState = vi.hoisted(() => ({
  apiBase: 'http://127.0.0.1:15345',
  managementKey: 'secret',
}));

vi.mock('@/stores/useAuthStore', () => ({
  useAuthStore: {
    getState: () => authState,
  },
}));

const t = ((key: string) => key) as TFunction;
const windows: CodexQuotaWindow[] = [
  {
    id: 'primary',
    label: '5H',
    usedPercent: 10,
    resetLabel: 'soon',
  },
];

const fetchQuota = vi.fn();

const codexConfig: QuotaConfig<
  CodexQuotaState,
  { planType: string | null; windows: CodexQuotaWindow[] }
> = {
  type: 'codex',
  i18nPrefix: 'codex_quota',
  filterFn: (file) => file.type === 'codex',
  fetchQuota,
  storeSelector: (state) => state.codexQuota,
  storeSetter: 'setCodexQuota',
  buildLoadingState: () => ({ status: 'loading', windows: [] }),
  buildSuccessState: (data) => ({
    status: 'success',
    planType: data.planType,
    windows: data.windows,
  }),
  buildErrorState: (message, status) => ({
    status: 'error',
    windows: [],
    error: message,
    errorStatus: status,
  }),
  cardClassName: '',
  controlsClassName: '',
  controlClassName: '',
  gridClassName: '',
  renderQuotaItems: () => null,
};

const codexFile: AuthFileItem = {
  name: 'codex-plus.json',
  type: 'codex',
  authIndex: 1,
};

const secondCodexFile: AuthFileItem = {
  name: 'codex-team.json',
  type: 'codex',
  authIndex: 2,
};

function createDeferred<T>() {
  let resolve: (value: T) => void = () => {};
  const promise = new Promise<T>((innerResolve) => {
    resolve = innerResolve;
  });

  return { promise, resolve };
}

const flushAsyncWork = () => new Promise<void>((resolve) => window.setTimeout(resolve, 0));

describe('quota refresh scheduler', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    resetQuotaRefreshScheduler();
    useQuotaStore.getState().clearQuotaCache();
    fetchQuota.mockResolvedValue({ planType: 'plus', windows });
  });

  it('deduplicates quota page and dashboard refreshes for the same Codex account', async () => {
    const first = requestQuotaRefresh(codexConfig, [codexFile], { t, immediate: true });
    const second = requestQuotaRefresh(codexConfig, [codexFile], { t, immediate: true });

    await Promise.all([first, second]);

    expect(fetchQuota).toHaveBeenCalledTimes(1);
    expect(useQuotaStore.getState().codexQuota[codexFile.name]?.status).toBe('success');
  });

  it('blocks immediate Codex retries while the failure backoff is active', async () => {
    fetchQuota.mockRejectedValueOnce(new Error('quota unavailable'));

    const first = await requestQuotaRefresh(codexConfig, [codexFile], { t, immediate: true });
    const second = await requestQuotaRefresh(codexConfig, [codexFile], {
      t,
      force: true,
      immediate: true,
    });

    expect(first[0]?.status).toBe('error');
    expect(second[0]?.status).toBe('error');
    expect(second[0]?.fromCache).toBe(true);
    expect(fetchQuota).toHaveBeenCalledTimes(1);
  });

  it('does not immediately retry a transient Codex failure', async () => {
    const error = Object.assign(new Error('service unavailable'), { status: 503 });
    fetchQuota.mockRejectedValueOnce(error).mockResolvedValueOnce({ planType: 'plus', windows });

    const result = await requestQuotaRefresh(codexConfig, [codexFile], {
      t,
      force: true,
      immediate: true,
    });

    expect(result[0]?.status).toBe('error');
    expect(result[0]?.errorStatus).toBe(503);
    expect(fetchQuota).toHaveBeenCalledTimes(1);
  });

  it('keeps Codex account refreshes serial across multiple files', async () => {
    const first = createDeferred<{ planType: string | null; windows: CodexQuotaWindow[] }>();
    const second = createDeferred<{ planType: string | null; windows: CodexQuotaWindow[] }>();
    let activeRequests = 0;
    let maxActiveRequests = 0;

    fetchQuota.mockImplementation((file: AuthFileItem) => {
      activeRequests += 1;
      maxActiveRequests = Math.max(maxActiveRequests, activeRequests);
      const deferred = file.name === codexFile.name ? first : second;
      return deferred.promise.finally(() => {
        activeRequests -= 1;
      });
    });

    const refresh = requestQuotaRefresh(codexConfig, [codexFile, secondCodexFile], {
      t,
      force: true,
      immediate: true,
    });

    expect(fetchQuota).toHaveBeenCalledTimes(1);
    first.resolve({ planType: 'plus', windows });
    await flushAsyncWork();
    expect(fetchQuota).toHaveBeenCalledTimes(2);
    second.resolve({ planType: 'team', windows });

    const results = await refresh;

    expect(results.map((result) => result.status)).toEqual(['success', 'success']);
    expect(maxActiveRequests).toBe(1);
  });
});
