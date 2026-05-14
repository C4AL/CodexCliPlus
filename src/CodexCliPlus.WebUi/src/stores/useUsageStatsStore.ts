import { create } from 'zustand';
import { notifyUsageStatsRefreshedInDesktopShell } from '@/desktop/bridge';
import { requestUsageRefresh, resetUsageRefreshScheduler } from '@/services/refresh';
import { useAuthStore } from '@/stores/useAuthStore';
import { createAuthScopeKey } from '@/utils/authScope';
import {
  collectUsageDetails,
  computeKeyStatsFromApiKeyUsage,
  computeKeyStatsFromDetails,
  mergeKeyStats,
  type KeyStats,
  type UsageDetail,
} from '@/utils/usage';
import i18n from '@/i18n';

export const USAGE_STATS_STALE_TIME_MS = 240_000;

export type LoadUsageStatsOptions = {
  force?: boolean;
  staleTimeMs?: number;
};

type UsageStatsSnapshot = Record<string, unknown>;

type UsageStatsState = {
  usage: UsageStatsSnapshot | null;
  keyStats: KeyStats;
  usageDetails: UsageDetail[];
  loading: boolean;
  error: string | null;
  lastRefreshedAt: number | null;
  scopeKey: string;
  loadUsageStats: (options?: LoadUsageStatsOptions) => Promise<void>;
  clearUsageStats: () => void;
};

const createEmptyKeyStats = (): KeyStats => ({ bySource: {}, byAuthIndex: {} });

let usageRequestToken = 0;

const getErrorMessage = (error: unknown) =>
  error instanceof Error
    ? error.message
    : typeof error === 'string'
      ? error
      : i18n.t('usage_stats.loading_error');

export const useUsageStatsStore = create<UsageStatsState>((set, get) => ({
  usage: null,
  keyStats: createEmptyKeyStats(),
  usageDetails: [],
  loading: false,
  error: null,
  lastRefreshedAt: null,
  scopeKey: '',

  loadUsageStats: async (options = {}) => {
    const force = options.force === true;
    const staleTimeMs = options.staleTimeMs ?? USAGE_STATS_STALE_TIME_MS;
    const scopeKey = createAuthScopeKey(useAuthStore.getState());
    const state = get();
    const scopeChanged = state.scopeKey !== scopeKey;

    // 连接目标变化时，旧请求结果必须失效。
    if (scopeChanged) {
      usageRequestToken += 1;
    }

    const fresh =
      !scopeChanged &&
      state.lastRefreshedAt !== null &&
      Date.now() - state.lastRefreshedAt < staleTimeMs;

    if (!force && fresh) {
      return;
    }

    if (scopeChanged) {
      set({
        usage: null,
        keyStats: createEmptyKeyStats(),
        usageDetails: [],
        error: null,
        lastRefreshedAt: null,
        scopeKey,
      });
    }

    const requestId = (usageRequestToken += 1);
    set({ loading: true, error: null, scopeKey });

    try {
      const result = await requestUsageRefresh(force ? 'force' : 'stale', { force, scopeKey });
      const rawUsage = result.response?.usage ?? result.response;
      const usage =
        rawUsage && typeof rawUsage === 'object' ? (rawUsage as UsageStatsSnapshot) : null;

      if (requestId !== usageRequestToken || result.scopeKey !== scopeKey) return;

      const usageDetails = collectUsageDetails(usage);
      const usageKeyStats = computeKeyStatsFromDetails(usageDetails);
      const apiKeyStats = computeKeyStatsFromApiKeyUsage(result.apiKeyUsage);
      set({
        usage,
        keyStats: mergeKeyStats(apiKeyStats, usageKeyStats),
        usageDetails,
        loading: false,
        error: null,
        lastRefreshedAt: result.refreshedAt,
        scopeKey,
      });
      notifyUsageStatsRefreshedInDesktopShell();
    } catch (error: unknown) {
      if (requestId !== usageRequestToken) return;
      const message = getErrorMessage(error);
      set({
        loading: false,
        error: message,
        scopeKey,
      });
      const wrappedError = new Error(message) as Error & { cause?: unknown };
      wrappedError.cause = error;
      throw wrappedError;
    }
  },

  clearUsageStats: () => {
    usageRequestToken += 1;
    resetUsageRefreshScheduler();
    set({
      usage: null,
      keyStats: createEmptyKeyStats(),
      usageDetails: [],
      loading: false,
      error: null,
      lastRefreshedAt: null,
      scopeKey: '',
    });
  },
}));
