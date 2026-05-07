import type { TFunction } from 'i18next';
import type { QuotaConfig, QuotaStore } from '@/components/quota/quotaConfigs';
import type { AuthFileItem } from '@/types';
import { useAuthStore } from '@/stores/useAuthStore';
import { useQuotaStore } from '@/stores/useQuotaStore';
import { createAuthScopeKey } from '@/utils/authScope';
import { getStatusFromError, resolveCodexChatgptAccountId } from '@/utils/quota';
import { normalizeAuthIndex } from '@/utils/usage';

type QuotaUpdater<T> = T | ((prev: T) => T);

type QuotaResultStatus = 'success' | 'error' | 'skipped';

export interface QuotaRefreshOptions {
  force?: boolean;
  immediate?: boolean;
  debounceMs?: number;
  scopeKey?: string;
  t: TFunction;
}

export interface QuotaRefreshResult<TData> {
  name: string;
  key: string;
  status: QuotaResultStatus;
  data?: TData;
  error?: string;
  errorStatus?: number;
  fromCache?: boolean;
}

interface QuotaResourceState {
  inFlight: Promise<QuotaRefreshResult<unknown>> | null;
  debouncePromise: Promise<QuotaRefreshResult<unknown>> | null;
  lastSuccessAt: number;
  lastData: unknown;
  failureCount: number;
  backoffUntil: number;
  lastError: { message: string; status?: number } | null;
}

interface QueueEntry<T> {
  run: () => Promise<T>;
  resolve: (value: T) => void;
  reject: (reason?: unknown) => void;
}

const CODEX_BACKOFF_MS = [60_000, 120_000, 300_000] as const;
const STANDARD_BACKOFF_MS = [30_000, 60_000, 180_000] as const;

const quotaStates = new Map<string, QuotaResourceState>();
const codexQueue: QueueEntry<unknown>[] = [];
const standardQueue: QueueEntry<unknown>[] = [];

let codexActiveCount = 0;
let standardActiveCount = 0;

const delay = (ms: number) => new Promise<void>((resolve) => window.setTimeout(resolve, ms));

const getQuotaScopeKey = () => {
  return createAuthScopeKey(useAuthStore.getState());
};

const getQuotaState = (key: string): QuotaResourceState => {
  const existing = quotaStates.get(key);
  if (existing) {
    return existing;
  }

  const next: QuotaResourceState = {
    inFlight: null,
    debouncePromise: null,
    lastSuccessAt: 0,
    lastData: undefined,
    failureCount: 0,
    backoffUntil: 0,
    lastError: null,
  };
  quotaStates.set(key, next);
  return next;
};

const getQuotaStrategy = (type: string) =>
  type === 'codex'
    ? {
        debounceMs: 1_500,
        successCooldownMs: 60_000,
        backoffMs: CODEX_BACKOFF_MS,
        concurrency: 'codex' as const,
      }
    : {
        debounceMs: 1_000,
        successCooldownMs: 30_000,
        backoffMs: STANDARD_BACKOFF_MS,
        concurrency: 'standard' as const,
      };

const isRecord = (value: unknown): value is Record<string, unknown> =>
  value !== null && typeof value === 'object';

const readNestedRecord = (value: unknown, key: string): Record<string, unknown> | null => {
  if (!isRecord(value)) return null;
  const nested = value[key];
  return isRecord(nested) ? nested : null;
};

const normalizeIdentityPart = (value: unknown): string | null => {
  if (value === null || value === undefined) return null;
  const text = String(value).trim();
  return text ? text : null;
};

const resolveGenericAccountIdentity = (file: AuthFileItem): string => {
  const metadata = readNestedRecord(file, 'metadata');
  const attributes = readNestedRecord(file, 'attributes');
  const candidates = [
    file.account,
    file.email,
    file.user,
    file.user_id,
    file.userId,
    file.project_id,
    file.projectId,
    metadata?.account,
    metadata?.email,
    metadata?.user,
    metadata?.user_id,
    metadata?.userId,
    metadata?.project_id,
    metadata?.projectId,
    attributes?.account,
    attributes?.email,
    attributes?.user,
    attributes?.user_id,
    attributes?.userId,
    attributes?.project_id,
    attributes?.projectId,
  ];

  for (const candidate of candidates) {
    const normalized = normalizeIdentityPart(candidate);
    if (normalized) {
      return normalized;
    }
  }

  return '';
};

export const buildQuotaResourceKey = <TState, TData>(
  config: QuotaConfig<TState, TData>,
  file: AuthFileItem,
  scopeKey = getQuotaScopeKey()
) => {
  const authIndex = normalizeAuthIndex(file['auth_index'] ?? file.authIndex) ?? '';
  const accountIdentity =
    config.type === 'codex'
      ? (resolveCodexChatgptAccountId(file) ?? '')
      : resolveGenericAccountIdentity(file);
  return [scopeKey, config.type, file.name, authIndex, accountIdentity].join('::');
};

const getErrorMessage = (err: unknown, t: TFunction) =>
  err instanceof Error ? err.message : typeof err === 'string' ? err : t('common.unknown_error');

const getBackoffDelay = (state: QuotaResourceState, backoffMs: readonly number[]) => {
  const index = Math.min(Math.max(state.failureCount - 1, 0), backoffMs.length - 1);
  return backoffMs[index] ?? backoffMs[backoffMs.length - 1] ?? 0;
};

const pumpQueue = (kind: 'codex' | 'standard') => {
  const queue = kind === 'codex' ? codexQueue : standardQueue;
  const maxActive = kind === 'codex' ? 1 : 2;
  const activeCount = kind === 'codex' ? codexActiveCount : standardActiveCount;
  if (activeCount >= maxActive) {
    return;
  }

  const entry = queue.shift();
  if (!entry) {
    return;
  }

  if (kind === 'codex') {
    codexActiveCount += 1;
  } else {
    standardActiveCount += 1;
  }

  entry
    .run()
    .then(entry.resolve, entry.reject)
    .finally(() => {
      if (kind === 'codex') {
        codexActiveCount = Math.max(0, codexActiveCount - 1);
      } else {
        standardActiveCount = Math.max(0, standardActiveCount - 1);
      }
      pumpQueue(kind);
    });
};

const runWithQuotaConcurrency = <T>(
  kind: 'codex' | 'standard',
  run: () => Promise<T>
): Promise<T> =>
  new Promise<T>((resolve, reject) => {
    const entry: QueueEntry<T> = { run, resolve, reject };
    if (kind === 'codex') {
      codexQueue.push(entry as QueueEntry<unknown>);
    } else {
      standardQueue.push(entry as QueueEntry<unknown>);
    }
    pumpQueue(kind);
  });

const getQuotaStoreSetter = <TState, TData>(config: QuotaConfig<TState, TData>) => {
  const store = useQuotaStore.getState() as unknown as QuotaStore;
  const setter = store[config.storeSetter];
  return setter as (updater: QuotaUpdater<Record<string, TState>>) => void;
};

const setQuotaForFile = <TState, TData>(
  config: QuotaConfig<TState, TData>,
  fileName: string,
  nextState: TState
) => {
  const setQuota = getQuotaStoreSetter(config);
  setQuota((prev) => ({
    ...prev,
    [fileName]: nextState,
  }));
};

const startQuotaFetch = <TState, TData>(
  config: QuotaConfig<TState, TData>,
  file: AuthFileItem,
  key: string,
  state: QuotaResourceState,
  options: QuotaRefreshOptions
): Promise<QuotaRefreshResult<TData>> => {
  setQuotaForFile(config, file.name, config.buildLoadingState());

  const strategy = getQuotaStrategy(config.type);
  const request = runWithQuotaConcurrency(strategy.concurrency, async () => {
    try {
      const data = await config.fetchQuota(file, options.t);
      state.lastSuccessAt = Date.now();
      state.lastData = data;
      state.failureCount = 0;
      state.backoffUntil = 0;
      state.lastError = null;
      setQuotaForFile(config, file.name, config.buildSuccessState(data));
      return {
        name: file.name,
        key,
        status: 'success',
        data,
      } satisfies QuotaRefreshResult<TData>;
    } catch (err: unknown) {
      const message = getErrorMessage(err, options.t);
      const errorStatus = getStatusFromError(err);
      state.failureCount += 1;
      state.backoffUntil = Date.now() + getBackoffDelay(state, strategy.backoffMs);
      state.lastError = { message, status: errorStatus };
      setQuotaForFile(config, file.name, config.buildErrorState(message, errorStatus));
      return {
        name: file.name,
        key,
        status: 'error',
        error: message,
        errorStatus,
      } satisfies QuotaRefreshResult<TData>;
    }
  }).finally(() => {
    if (state.inFlight === request) {
      state.inFlight = null;
    }
  });

  state.inFlight = request as Promise<QuotaRefreshResult<unknown>>;
  return request;
};

const requestQuotaFileRefresh = async <TState, TData>(
  config: QuotaConfig<TState, TData>,
  file: AuthFileItem,
  options: QuotaRefreshOptions
): Promise<QuotaRefreshResult<TData>> => {
  const key = buildQuotaResourceKey(config, file, options.scopeKey);
  const state = getQuotaState(key);
  const strategy = getQuotaStrategy(config.type);
  const now = Date.now();

  if (state.inFlight) {
    return state.inFlight as Promise<QuotaRefreshResult<TData>>;
  }

  if (state.debouncePromise) {
    return state.debouncePromise as Promise<QuotaRefreshResult<TData>>;
  }

  if (state.backoffUntil > now) {
    const message = state.lastError?.message ?? options.t('notification.refresh_failed');
    const errorStatus = state.lastError?.status;
    setQuotaForFile(config, file.name, config.buildErrorState(message, errorStatus));
    return {
      name: file.name,
      key,
      status: 'error',
      error: message,
      errorStatus,
      fromCache: true,
    };
  }

  const successFresh =
    state.lastSuccessAt > 0 && now - state.lastSuccessAt < strategy.successCooldownMs;
  if (!options.force && successFresh && state.lastData !== undefined) {
    const data = state.lastData as TData;
    setQuotaForFile(config, file.name, config.buildSuccessState(data));
    return {
      name: file.name,
      key,
      status: 'success',
      data,
      fromCache: true,
    };
  }

  const debounceMs = options.immediate ? 0 : (options.debounceMs ?? strategy.debounceMs);
  if (debounceMs <= 0) {
    return startQuotaFetch(config, file, key, state, options);
  }

  const debounced = delay(debounceMs)
    .then(() => {
      if (state.inFlight) {
        return state.inFlight as Promise<QuotaRefreshResult<TData>>;
      }
      return startQuotaFetch(config, file, key, state, options);
    })
    .finally(() => {
      if (state.debouncePromise === debounced) {
        state.debouncePromise = null;
      }
    });

  state.debouncePromise = debounced as Promise<QuotaRefreshResult<unknown>>;
  return debounced;
};

export const requestQuotaRefresh = async <TState, TData>(
  config: QuotaConfig<TState, TData>,
  files: AuthFileItem[],
  options: QuotaRefreshOptions
): Promise<Array<QuotaRefreshResult<TData>>> => {
  const targets = files.filter((file) => config.filterFn(file));
  if (targets.length === 0) {
    return [];
  }

  if (config.type === 'codex') {
    const results: Array<QuotaRefreshResult<TData>> = [];
    for (const file of targets) {
      results.push(await requestQuotaFileRefresh(config, file, options));
    }

    return results;
  }

  return Promise.all(targets.map((file) => requestQuotaFileRefresh(config, file, options)));
};

export const resetQuotaRefreshScheduler = () => {
  quotaStates.clear();
  codexQueue.length = 0;
  standardQueue.length = 0;
  codexActiveCount = 0;
  standardActiveCount = 0;
};
