import { logsApi, type ErrorLogsResponse, type LogsQuery, type LogsResponse } from '@/services/api/logs';
import { useAuthStore } from '@/stores/useAuthStore';

export type LogRefreshMode = 'full' | 'incremental';

export interface LogStreamEvent {
  scopeKey: string;
  mode: LogRefreshMode;
  response: LogsResponse;
  lines: string[];
  latestTimestamp: number;
}

export interface LogsRefreshOptions {
  mode?: LogRefreshMode;
  after?: number;
  scopeKey?: string;
}

interface IncrementalRequest {
  after: number;
  promise: Promise<LogStreamEvent>;
}

interface LogScopeState {
  latestTimestamp: number;
  fullInFlight: Promise<LogStreamEvent> | null;
  incrementalInFlight: IncrementalRequest | null;
  errorLogsInFlight: Promise<ErrorLogsResponse> | null;
}

type LogStreamSubscriber = (event: LogStreamEvent) => void;

const logScopes = new Map<string, LogScopeState>();
const logSubscribers = new Set<LogStreamSubscriber>();

const getLogScopeKey = () => {
  const { apiBase = '', managementKey = '' } = useAuthStore.getState();
  return `${apiBase}::${managementKey}`;
};

const getLogScopeState = (scopeKey: string): LogScopeState => {
  const existing = logScopes.get(scopeKey);
  if (existing) {
    return existing;
  }

  const next: LogScopeState = {
    latestTimestamp: 0,
    fullInFlight: null,
    incrementalInFlight: null,
    errorLogsInFlight: null,
  };
  logScopes.set(scopeKey, next);
  return next;
};

const publishLogStreamEvent = (event: LogStreamEvent) => {
  logSubscribers.forEach((subscriber) => subscriber(event));
};

const normalizeLatestTimestamp = (response: LogsResponse): number => {
  const value = Number(response?.['latest-timestamp']);
  return Number.isFinite(value) && value > 0 ? value : 0;
};

const buildLogEvent = (
  scopeKey: string,
  mode: LogRefreshMode,
  response: LogsResponse
): LogStreamEvent => {
  const lines = Array.isArray(response.lines) ? response.lines : [];
  return {
    scopeKey,
    mode,
    response,
    lines,
    latestTimestamp: normalizeLatestTimestamp(response),
  };
};

const startLogsRequest = (
  scopeKey: string,
  state: LogScopeState,
  mode: LogRefreshMode,
  params: LogsQuery,
  after: number
): Promise<LogStreamEvent> => {
  const request = logsApi
    .fetchLogs(params)
    .then((response) => {
      const event = buildLogEvent(scopeKey, mode, response);
      if (event.latestTimestamp > 0) {
        state.latestTimestamp = event.latestTimestamp;
      }
      publishLogStreamEvent(event);
      return event;
    })
    .finally(() => {
      if (mode === 'full' && state.fullInFlight === request) {
        state.fullInFlight = null;
      }
      if (
        mode === 'incremental' &&
        state.incrementalInFlight?.after === after &&
        state.incrementalInFlight.promise === request
      ) {
        state.incrementalInFlight = null;
      }
    });

  if (mode === 'full') {
    state.fullInFlight = request;
  } else {
    state.incrementalInFlight = { after, promise: request };
  }

  return request;
};

export const subscribeLogStream = (subscriber: LogStreamSubscriber) => {
  logSubscribers.add(subscriber);
  return () => {
    logSubscribers.delete(subscriber);
  };
};

export const requestLogsRefresh = (
  _reason: string,
  options: LogsRefreshOptions = {}
): Promise<LogStreamEvent> => {
  const scopeKey = options.scopeKey ?? getLogScopeKey();
  const state = getLogScopeState(scopeKey);
  const mode = options.mode ?? 'incremental';

  if (state.fullInFlight) {
    return state.fullInFlight;
  }

  if (mode === 'full') {
    return startLogsRequest(scopeKey, state, 'full', {}, 0);
  }

  const after = Math.max(0, Number(options.after ?? state.latestTimestamp) || 0);
  if (state.incrementalInFlight && state.incrementalInFlight.after === after) {
    return state.incrementalInFlight.promise;
  }

  const params = after > 0 ? { after } : {};
  return startLogsRequest(scopeKey, state, 'incremental', params, after);
};

export const requestErrorLogsRefresh = (
  _reason: string,
  options: { scopeKey?: string } = {}
): Promise<ErrorLogsResponse> => {
  const scopeKey = options.scopeKey ?? getLogScopeKey();
  const state = getLogScopeState(scopeKey);
  if (state.errorLogsInFlight) {
    return state.errorLogsInFlight;
  }

  const request = logsApi.fetchErrorLogs().finally(() => {
    if (state.errorLogsInFlight === request) {
      state.errorLogsInFlight = null;
    }
  });
  state.errorLogsInFlight = request;
  return request;
};

export const resetLogStreamScheduler = (scopeKey?: string) => {
  if (scopeKey) {
    logScopes.delete(scopeKey);
    return;
  }

  logScopes.clear();
};
