import { usageApi } from '@/services/api/usage';
import { useAuthStore } from '@/stores/useAuthStore';

const USAGE_SHORT_THROTTLE_MS = 1_500;

export interface UsageRefreshResult {
  scopeKey: string;
  response: Record<string, unknown>;
  apiKeyUsage: Record<string, unknown> | null;
  refreshedAt: number;
}

export interface UsageRefreshOptions {
  force?: boolean;
  scopeKey?: string;
}

interface UsageScopeState {
  inFlight: Promise<UsageRefreshResult> | null;
  lastCompletedAt: number;
  lastResult: UsageRefreshResult | null;
  trailingPromise: Promise<UsageRefreshResult> | null;
}

const usageScopes = new Map<string, UsageScopeState>();

const delay = (ms: number) => new Promise<void>((resolve) => window.setTimeout(resolve, ms));

export const getRefreshScopeKey = () => {
  const { apiBase = '', managementKey = '' } = useAuthStore.getState();
  return `${apiBase}::${managementKey}`;
};

const getUsageScopeState = (scopeKey: string): UsageScopeState => {
  const existing = usageScopes.get(scopeKey);
  if (existing) {
    return existing;
  }

  const next: UsageScopeState = {
    inFlight: null,
    lastCompletedAt: 0,
    lastResult: null,
    trailingPromise: null,
  };
  usageScopes.set(scopeKey, next);
  return next;
};

const startUsageRequest = (
  scopeKey: string,
  state: UsageScopeState
): Promise<UsageRefreshResult> => {
  const request = Promise.all([usageApi.getUsage(), usageApi.getApiKeyUsage().catch(() => null)])
    .then(([response, apiKeyUsage]) => {
      const result = {
        scopeKey,
        response,
        apiKeyUsage:
          apiKeyUsage && typeof apiKeyUsage === 'object'
            ? (apiKeyUsage as Record<string, unknown>)
            : null,
        refreshedAt: Date.now(),
      } satisfies UsageRefreshResult;
      state.lastCompletedAt = result.refreshedAt;
      state.lastResult = result;
      return result;
    })
    .finally(() => {
      if (state.inFlight === request) {
        state.inFlight = null;
      }
    });

  state.inFlight = request;
  return request;
};

const scheduleTrailingUsageRequest = (
  scopeKey: string,
  state: UsageScopeState
): Promise<UsageRefreshResult> => {
  if (state.trailingPromise) {
    return state.trailingPromise;
  }

  const trailing = (async () => {
    if (state.inFlight) {
      await state.inFlight.catch(() => undefined);
    }

    const elapsed = Date.now() - state.lastCompletedAt;
    if (state.lastCompletedAt > 0 && elapsed < USAGE_SHORT_THROTTLE_MS) {
      await delay(USAGE_SHORT_THROTTLE_MS - elapsed);
    }

    if (state.inFlight) {
      return state.inFlight;
    }

    return startUsageRequest(scopeKey, state);
  })().finally(() => {
    if (state.trailingPromise === trailing) {
      state.trailingPromise = null;
    }
  });

  state.trailingPromise = trailing;
  return trailing;
};

export const requestUsageRefresh = (
  _reason: string,
  options: UsageRefreshOptions = {}
): Promise<UsageRefreshResult> => {
  const scopeKey = options.scopeKey ?? getRefreshScopeKey();
  const state = getUsageScopeState(scopeKey);

  if (state.inFlight) {
    return state.inFlight;
  }

  const now = Date.now();
  const elapsed = now - state.lastCompletedAt;
  const inShortThrottle =
    state.lastCompletedAt > 0 && elapsed >= 0 && elapsed < USAGE_SHORT_THROTTLE_MS;

  if (inShortThrottle) {
    if (options.force) {
      return scheduleTrailingUsageRequest(scopeKey, state);
    }

    if (state.lastResult) {
      return Promise.resolve(state.lastResult);
    }
  }

  return startUsageRequest(scopeKey, state);
};

export const resetUsageRefreshScheduler = (scopeKey?: string) => {
  if (scopeKey) {
    usageScopes.delete(scopeKey);
    return;
  }

  usageScopes.clear();
};
