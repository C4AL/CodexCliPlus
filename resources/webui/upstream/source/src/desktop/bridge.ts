export interface DesktopBootstrapPayload {
  desktopMode: boolean;
  apiBase: string;
  managementKey: string;
  theme?: DesktopTheme;
  resolvedTheme?: DesktopResolvedTheme;
  sidebarCollapsed?: boolean;
}

export type DesktopTheme = 'auto' | 'white' | 'dark';
export type DesktopResolvedTheme = 'light' | 'dark';
export type DesktopConnectionStatus = 'connected' | 'connecting' | 'disconnected' | 'error';
export type LocalDependencyStatus =
  | 'ready'
  | 'warning'
  | 'missing'
  | 'error'
  | 'optionalUnavailable'
  | 'repairing';
export type LocalDependencySeverity = 'required' | 'optional' | 'repairTool';

export interface LocalDependencyItem {
  id: string;
  name: string;
  status: LocalDependencyStatus;
  severity: LocalDependencySeverity;
  version?: string | null;
  path?: string | null;
  detail: string;
  recommendation: string;
  repairActionId?: string | null;
}

export interface LocalDependencyRepairCapability {
  actionId: string;
  name: string;
  isAvailable: boolean;
  requiresElevation: boolean;
  isOptional: boolean;
  detail: string;
}

export interface LocalDependencySnapshot {
  checkedAt: string;
  readinessScore: number;
  summary: string;
  items: LocalDependencyItem[];
  repairCapabilities: LocalDependencyRepairCapability[];
}

export interface LocalDependencyRepairResult {
  actionId: string;
  succeeded: boolean;
  exitCode?: number | null;
  summary: string;
  detail: string;
  logPath?: string | null;
}

export interface LocalDependencyRepairResponse {
  result: LocalDependencyRepairResult;
  snapshot?: LocalDependencySnapshot | null;
}

export interface DesktopShellState {
  connectionStatus: DesktopConnectionStatus;
  apiBase: string;
  theme: DesktopTheme;
  resolvedTheme: DesktopResolvedTheme;
  sidebarCollapsed: boolean;
  pathname: string;
}

export type DesktopShellCommand =
  | { type: 'refreshAll' }
  | { type: 'setTheme'; theme: DesktopTheme; resolvedTheme?: DesktopResolvedTheme }
  | { type: 'toggleSidebarCollapsed'; collapsed?: boolean }
  | { type: 'navigate'; path: string }
  | { type: 'clearUsageStats' }
  | { type: 'refreshUsage' };

interface DesktopBridge {
  isDesktopMode?: () => boolean;
  consumeBootstrap?: () => DesktopBootstrapPayload | null;
  openExternal?: (url: string) => void;
  requestNativeLogin?: (message?: string) => void;
  shellStateChanged?: (state: DesktopShellState) => void;
  importAccountConfig?: (mode?: string) => void;
  exportAccountConfig?: (mode?: string) => void;
  importSacPackage?: () => void;
  exportSacPackage?: () => void;
  clearUsageStats?: () => void;
  usageStatsRefreshed?: () => void;
  checkDesktopUpdate?: () => void;
  applyDesktopUpdate?: () => void;
  requestLocalDependencySnapshot?: (requestId: string) => void;
  runLocalDependencyRepair?: (actionId: string, requestId: string) => void;
}

declare global {
  interface Window {
    __CODEXCLIPLUS_DESKTOP_BRIDGE__?: DesktopBridge;
    chrome?: {
      webview?: {
        addEventListener?: (type: 'message', listener: (event: MessageEvent) => void) => void;
      };
    };
  }
}

let desktopBootstrapCache: DesktopBootstrapPayload | null | undefined;
let desktopCommandListenerReady = false;
const desktopCommandListeners = new Set<(command: DesktopShellCommand) => void>();
const pendingLocalDependencyRequests = new Map<
  string,
  {
    resolve: (value: unknown) => void;
    reject: (reason?: unknown) => void;
    timer: ReturnType<typeof window.setTimeout>;
  }
>();
const LOCAL_DEPENDENCY_SNAPSHOT_TIMEOUT_MS = 30_000;
const LOCAL_DEPENDENCY_REPAIR_TIMEOUT_MS = 35 * 60_000;

function getBridge(): DesktopBridge | null {
  if (typeof window === 'undefined') {
    return null;
  }

  return window.__CODEXCLIPLUS_DESKTOP_BRIDGE__ ?? null;
}

function normalizePayload(
  payload: DesktopBootstrapPayload | null | undefined
): DesktopBootstrapPayload | null {
  if (!payload || payload.desktopMode !== true) {
    return null;
  }

  const apiBase = typeof payload.apiBase === 'string' ? payload.apiBase.trim() : '';
  const managementKey =
    typeof payload.managementKey === 'string' ? payload.managementKey.trim() : '';
  if (!apiBase || !managementKey) {
    return null;
  }

  return {
    desktopMode: true,
    apiBase,
    managementKey,
    theme: normalizeDesktopTheme(payload.theme),
    resolvedTheme: normalizeResolvedTheme(payload.resolvedTheme),
    sidebarCollapsed: payload.sidebarCollapsed === true,
  };
}

function normalizeDesktopTheme(theme: unknown): DesktopTheme {
  return theme === 'dark' || theme === 'white' || theme === 'light'
    ? theme === 'light'
      ? 'white'
      : theme
    : 'auto';
}

function normalizeResolvedTheme(theme: unknown): DesktopResolvedTheme {
  return theme === 'dark' ? 'dark' : 'light';
}

function normalizeCommand(command: unknown): DesktopShellCommand | null {
  if (!command || typeof command !== 'object') {
    return null;
  }

  const record = command as Record<string, unknown>;
  if (record.type === 'refreshAll') {
    return { type: 'refreshAll' };
  }

  if (record.type === 'setTheme') {
    return {
      type: 'setTheme',
      theme: normalizeDesktopTheme(record.theme),
      resolvedTheme: normalizeResolvedTheme(record.resolvedTheme),
    };
  }

  if (record.type === 'toggleSidebarCollapsed') {
    return {
      type: 'toggleSidebarCollapsed',
      collapsed: typeof record.collapsed === 'boolean' ? record.collapsed : undefined,
    };
  }

  if (record.type === 'navigate') {
    const path = typeof record.path === 'string' ? record.path.trim() : '';
    return {
      type: 'navigate',
      path: path || '/',
    };
  }

  if (record.type === 'clearUsageStats') {
    return { type: 'clearUsageStats' };
  }

  if (record.type === 'refreshUsage') {
    return { type: 'refreshUsage' };
  }

  return null;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object';
}

function createDesktopRequestId(prefix: string): string {
  const random =
    typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
      ? crypto.randomUUID()
      : `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
  return `${prefix}-${random}`;
}

function normalizeLocalDependencySnapshot(value: unknown): LocalDependencySnapshot | null {
  if (!isRecord(value)) return null;
  const items = Array.isArray(value.items)
    ? value.items.filter(isRecord).map((item) => ({
        id: typeof item.id === 'string' ? item.id : '',
        name: typeof item.name === 'string' ? item.name : '',
        status: normalizeLocalDependencyStatus(item.status),
        severity: normalizeLocalDependencySeverity(item.severity),
        version: typeof item.version === 'string' ? item.version : null,
        path: typeof item.path === 'string' ? item.path : null,
        detail: typeof item.detail === 'string' ? item.detail : '',
        recommendation: typeof item.recommendation === 'string' ? item.recommendation : '',
        repairActionId: typeof item.repairActionId === 'string' ? item.repairActionId : null,
      }))
    : [];
  const repairCapabilities = Array.isArray(value.repairCapabilities)
    ? value.repairCapabilities.filter(isRecord).map((capability) => ({
        actionId: typeof capability.actionId === 'string' ? capability.actionId : '',
        name: typeof capability.name === 'string' ? capability.name : '',
        isAvailable: capability.isAvailable === true,
        requiresElevation: capability.requiresElevation !== false,
        isOptional: capability.isOptional === true,
        detail: typeof capability.detail === 'string' ? capability.detail : '',
      }))
    : [];

  return {
    checkedAt: typeof value.checkedAt === 'string' ? value.checkedAt : '',
    readinessScore:
      typeof value.readinessScore === 'number' && Number.isFinite(value.readinessScore)
        ? Math.max(0, Math.min(100, Math.round(value.readinessScore)))
        : 0,
    summary: typeof value.summary === 'string' ? value.summary : '',
    items,
    repairCapabilities,
  };
}

function normalizeLocalDependencyStatus(value: unknown): LocalDependencyStatus {
  return value === 'ready' ||
    value === 'warning' ||
    value === 'missing' ||
    value === 'error' ||
    value === 'optionalUnavailable' ||
    value === 'repairing'
    ? value
    : 'error';
}

function normalizeLocalDependencySeverity(value: unknown): LocalDependencySeverity {
  return value === 'optional' || value === 'repairTool' ? value : 'required';
}

function normalizeLocalDependencyRepairResult(value: unknown): LocalDependencyRepairResult | null {
  if (!isRecord(value)) return null;
  return {
    actionId: typeof value.actionId === 'string' ? value.actionId : '',
    succeeded: value.succeeded === true,
    exitCode: typeof value.exitCode === 'number' ? value.exitCode : null,
    summary: typeof value.summary === 'string' ? value.summary : '',
    detail: typeof value.detail === 'string' ? value.detail : '',
    logPath: typeof value.logPath === 'string' ? value.logPath : null,
  };
}

function settleLocalDependencyRequest(requestId: unknown, value: unknown, error?: unknown): boolean {
  if (typeof requestId !== 'string') return false;
  const pending = pendingLocalDependencyRequests.get(requestId);
  if (!pending) return true;
  pendingLocalDependencyRequests.delete(requestId);
  window.clearTimeout(pending.timer);
  if (error) {
    pending.reject(new Error(typeof error === 'string' ? error : '本地环境检测失败'));
  } else {
    pending.resolve(value);
  }
  return true;
}

function handleLocalDependencyMessage(message: unknown): boolean {
  if (!isRecord(message) || typeof message.type !== 'string') return false;

  if (message.type === 'localDependencySnapshot') {
    const snapshot = normalizeLocalDependencySnapshot(message.snapshot);
    return settleLocalDependencyRequest(
      message.requestId,
      snapshot,
      message.error || (!snapshot ? '本地环境检测结果无效' : undefined)
    );
  }

  if (message.type === 'localDependencyRepairStarted') {
    return true;
  }

  if (message.type === 'localDependencyRepairResult') {
    const result = normalizeLocalDependencyRepairResult(message.result);
    const snapshot = normalizeLocalDependencySnapshot(message.snapshot);
    return settleLocalDependencyRequest(
      message.requestId,
      { result, snapshot },
      message.error || (!result ? '本地环境修复结果无效' : undefined)
    );
  }

  return false;
}

function registerLocalDependencyRequest<T>(requestId: string, timeoutMs: number): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    const timer = window.setTimeout(() => {
      pendingLocalDependencyRequests.delete(requestId);
      reject(new Error('桌面端响应超时'));
    }, timeoutMs);
    pendingLocalDependencyRequests.set(requestId, {
      resolve: (value) => resolve(value as T),
      reject,
      timer,
    });
  });
}

function ensureDesktopCommandListener() {
  if (desktopCommandListenerReady || typeof window === 'undefined') {
    return;
  }

  const webview = window.chrome?.webview;
  if (!webview || typeof webview.addEventListener !== 'function') {
    return;
  }

  desktopCommandListenerReady = true;
  webview.addEventListener('message', (event: MessageEvent) => {
    if (handleLocalDependencyMessage(event.data)) {
      return;
    }

    const command = normalizeCommand(event.data);
    if (!command) {
      return;
    }

    desktopCommandListeners.forEach((listener) => listener(command));
  });
}

export function getDesktopBootstrap(): DesktopBootstrapPayload | null {
  if (desktopBootstrapCache !== undefined) {
    return desktopBootstrapCache;
  }

  const bridge = getBridge();
  if (typeof bridge?.consumeBootstrap !== 'function') {
    desktopBootstrapCache = null;
    return desktopBootstrapCache;
  }

  try {
    desktopBootstrapCache = normalizePayload(bridge.consumeBootstrap());
    return desktopBootstrapCache;
  } catch (error) {
    console.warn('Failed to read desktop bootstrap payload.', error);
    desktopBootstrapCache = null;
    return desktopBootstrapCache;
  }
}

export function isDesktopMode(): boolean {
  const bridge = getBridge();
  return typeof bridge?.isDesktopMode === 'function' ? bridge.isDesktopMode() === true : false;
}

export function consumeDesktopBootstrap(): DesktopBootstrapPayload | null {
  return getDesktopBootstrap();
}

export function openExternalInDesktopShell(url: string): boolean {
  const bridge = getBridge();
  if (typeof bridge?.openExternal !== 'function') {
    return false;
  }

  try {
    bridge.openExternal(url);
    return true;
  } catch (error) {
    console.warn('Failed to open external url through desktop shell.', error);
    return false;
  }
}

export function requestNativeLogin(message?: string): boolean {
  const bridge = getBridge();
  if (typeof bridge?.requestNativeLogin !== 'function') {
    return false;
  }

  try {
    bridge.requestNativeLogin(message);
    return true;
  } catch (error) {
    console.warn('Failed to request native desktop login.', error);
    return false;
  }
}

export function sendShellStateChanged(state: DesktopShellState): boolean {
  const bridge = getBridge();
  if (typeof bridge?.shellStateChanged !== 'function') {
    return false;
  }

  try {
    bridge.shellStateChanged({
      connectionStatus: state.connectionStatus,
      apiBase: state.apiBase,
      theme: normalizeDesktopTheme(state.theme),
      resolvedTheme: normalizeResolvedTheme(state.resolvedTheme),
      sidebarCollapsed: state.sidebarCollapsed === true,
      pathname: typeof state.pathname === 'string' ? state.pathname : '/',
    });
    return true;
  } catch (error) {
    console.warn('Failed to send desktop shell state.', error);
    return false;
  }
}

function invokeDesktopBridgeAction(name: keyof DesktopBridge, ...args: string[]): boolean {
  const bridge = getBridge();
  const action = bridge?.[name];
  if (typeof action !== 'function') {
    return false;
  }

  try {
    (action as (...values: string[]) => void)(...args);
    return true;
  } catch (error) {
    console.warn(`Failed to invoke desktop bridge action '${String(name)}'.`, error);
    return false;
  }
}

export function importAccountConfigInDesktopShell(mode: 'json' | 'cpa' = 'json'): boolean {
  return invokeDesktopBridgeAction('importAccountConfig', mode);
}

export function exportAccountConfigInDesktopShell(mode: 'json' | 'sac' = 'json'): boolean {
  return invokeDesktopBridgeAction('exportAccountConfig', mode);
}

export function importSacPackageInDesktopShell(): boolean {
  return invokeDesktopBridgeAction('importSacPackage');
}

export function exportSacPackageInDesktopShell(): boolean {
  return invokeDesktopBridgeAction('exportSacPackage');
}

export function clearUsageStatsInDesktopShell(): boolean {
  return invokeDesktopBridgeAction('clearUsageStats');
}

export function notifyUsageStatsRefreshedInDesktopShell(): boolean {
  return invokeDesktopBridgeAction('usageStatsRefreshed');
}

export function checkDesktopUpdateInDesktopShell(): boolean {
  return invokeDesktopBridgeAction('checkDesktopUpdate');
}

export function applyDesktopUpdateInDesktopShell(): boolean {
  return invokeDesktopBridgeAction('applyDesktopUpdate');
}

export function requestLocalDependencySnapshot(): Promise<LocalDependencySnapshot> {
  const bridge = getBridge();
  if (typeof bridge?.requestLocalDependencySnapshot !== 'function') {
    return Promise.reject(new Error('本地环境检测需要桌面模式'));
  }

  ensureDesktopCommandListener();
  const requestId = createDesktopRequestId('local-env');
  const request = registerLocalDependencyRequest<LocalDependencySnapshot>(
    requestId,
    LOCAL_DEPENDENCY_SNAPSHOT_TIMEOUT_MS
  );
  try {
    bridge.requestLocalDependencySnapshot(requestId);
  } catch (error) {
    const pending = pendingLocalDependencyRequests.get(requestId);
    if (pending) window.clearTimeout(pending.timer);
    pendingLocalDependencyRequests.delete(requestId);
    return Promise.reject(error);
  }
  return request;
}

export function runLocalDependencyRepair(
  actionId: string
): Promise<LocalDependencyRepairResponse> {
  const bridge = getBridge();
  if (typeof bridge?.runLocalDependencyRepair !== 'function') {
    return Promise.reject(new Error('本地环境修复需要桌面模式'));
  }

  ensureDesktopCommandListener();
  const requestId = createDesktopRequestId('local-env-repair');
  const request = registerLocalDependencyRequest<LocalDependencyRepairResponse>(
    requestId,
    LOCAL_DEPENDENCY_REPAIR_TIMEOUT_MS
  );
  try {
    bridge.runLocalDependencyRepair(actionId, requestId);
  } catch (error) {
    const pending = pendingLocalDependencyRequests.get(requestId);
    if (pending) window.clearTimeout(pending.timer);
    pendingLocalDependencyRequests.delete(requestId);
    return Promise.reject(error);
  }
  return request;
}

export function subscribeDesktopShellCommand(
  listener: (command: DesktopShellCommand) => void
): () => void {
  ensureDesktopCommandListener();
  desktopCommandListeners.add(listener);
  return () => {
    desktopCommandListeners.delete(listener);
  };
}
