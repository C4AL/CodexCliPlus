import type { NotificationType } from '@/types';

export interface DesktopBootstrapPayload {
  desktopMode: boolean;
  apiBase: string;
  desktopSessionId: string;
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

export interface LocalDependencyRepairProgress {
  actionId: string;
  phase: string;
  message: string;
  commandLine?: string | null;
  recentOutput: string[];
  logPath?: string | null;
  updatedAt: string;
  exitCode?: number | null;
}

export interface LocalDependencyRepairResponse {
  result: LocalDependencyRepairResult;
  snapshot?: LocalDependencySnapshot | null;
}

export interface DesktopShellState {
  connectionStatus: DesktopConnectionStatus;
  apiBase: string;
  sidebarCollapsed: boolean;
  pathname: string;
}

export interface DesktopDataChangedEvent {
  scopes: string[];
  sequence: number;
}

export interface DesktopManagementFile {
  fieldName: string;
  fileName: string;
  contentType: string;
  contentBase64: string;
}

export interface DesktopManagementRequest {
  method: string;
  path: string;
  body?: string;
  contentType?: string;
  accept?: string;
  files?: DesktopManagementFile[];
  fields?: Record<string, string>;
}

export interface DesktopManagementResponse {
  status: number;
  body: string;
  metadata?: unknown;
}

export type CodexRouteMode = 'official' | 'cpa' | 'unknown';
export type CodexRouteTargetKind = 'official' | 'managed-cpa' | 'third-party-cpa' | 'unknown';

export interface CodexRouteTarget {
  id: string;
  mode: CodexRouteMode;
  kind: CodexRouteTargetKind;
  label: string;
  baseUrl?: string | null;
  port?: number | null;
  profileName?: string | null;
  providerName?: string | null;
  isCurrent: boolean;
  canSwitch: boolean;
  statusMessage?: string | null;
}

export interface CodexRouteState {
  currentMode: CodexRouteMode;
  targetMode?: Exclude<CodexRouteMode, 'unknown'> | null;
  currentTargetId?: string | null;
  currentLabel: string;
  targets: CodexRouteTarget[];
  configPath: string;
  authPath: string;
  canSwitch: boolean;
  statusMessage: string;
}

export interface CodexRouteSwitchResponse {
  state: CodexRouteState;
  configBackupPath?: string | null;
  authBackupPath?: string | null;
  officialAuthBackupPath?: string | null;
}

export type CodexUserFileId = 'config' | 'auth';
export type CodexUserFileLanguage = 'toml' | 'json';

export interface CodexUserFileValidation {
  isValid: boolean;
  message: string;
}

export interface CodexUserFileSnapshot {
  fileId: CodexUserFileId;
  path: string;
  content: string;
  language: CodexUserFileLanguage;
  exists: boolean;
  lastWriteTimeUtc?: string | null;
  sizeBytes: number;
  validation: CodexUserFileValidation;
}

export interface CodexUserFileBackupResult {
  fileId: CodexUserFileId;
  path: string;
  backupPath: string;
  snapshot: CodexUserFileSnapshot;
}

export interface CodexUserFileSaveResult {
  fileId: CodexUserFileId;
  path: string;
  backupPath?: string | null;
  snapshot: CodexUserFileSnapshot;
}

interface CodexUserFileBridgeResponse {
  files?: CodexUserFileSnapshot[];
  file?: CodexUserFileSnapshot;
  validation?: CodexUserFileValidation;
  result?: CodexUserFileBackupResult | CodexUserFileSaveResult;
}

export type DesktopShellCommand =
  | {
      type: 'setTheme';
      theme: DesktopTheme;
      resolvedTheme?: DesktopResolvedTheme;
      transitionMs?: number;
    }
  | { type: 'toggleSidebarCollapsed'; collapsed?: boolean }
  | { type: 'navigate'; path: string }
  | { type: 'clearUsageStats' }
  | { type: 'refreshUsage' };

interface DesktopBridge {
  isDesktopMode?: () => boolean;
  getBootstrap?: () => DesktopBootstrapPayload | null;
  consumeBootstrap?: () => DesktopBootstrapPayload | null;
  openExternal?: (url: string) => void;
  requestNativeLogin?: (message?: string) => void;
  shellStateChanged?: (state: DesktopShellState) => void;
  showShellNotification?: (message: string, type?: NotificationType) => boolean | void;
  importAccountConfig?: (mode?: string) => void;
  exportAccountConfig?: (mode?: string) => void;
  importSacPackage?: () => void;
  exportSacPackage?: () => void;
  clearUsageStats?: () => void;
  usageStatsRefreshed?: () => void;
  checkDesktopUpdate?: () => void;
  applyDesktopUpdate?: () => void;
  requestCodexRouteState?: (requestId: string) => boolean | void;
  switchCodexRoute?: (targetId: string, requestId: string) => boolean | void;
  requestCodexUserFiles?: (requestId: string) => boolean | void;
  readCodexUserFile?: (fileId: CodexUserFileId, requestId: string) => boolean | void;
  validateCodexUserFile?: (
    fileId: CodexUserFileId,
    content: string,
    requestId: string
  ) => boolean | void;
  backupCodexUserFile?: (fileId: CodexUserFileId, requestId: string) => boolean | void;
  saveCodexUserFile?: (
    fileId: CodexUserFileId,
    content: string,
    expectedLastWriteTimeUtc: string | null | undefined,
    requestId: string
  ) => boolean | void;
  managementRequest?: (request: DesktopManagementRequest & { requestId: string }) => void;
  requestLocalDependencySnapshot?: (requestId: string) => boolean | void;
  runLocalDependencyRepair?: (actionId: string, requestId: string) => boolean | void;
}

declare global {
  interface Window {
    __CODEXCLIPLUS_DESKTOP_BRIDGE__?: DesktopBridge;
    chrome?: {
      webview?: {
        addEventListener?: (type: 'message', listener: (event: MessageEvent) => void) => void;
        postMessage?: (message: unknown) => void;
      };
    };
  }
}

let desktopBootstrapCache: DesktopBootstrapPayload | null | undefined;
let desktopCommandListenerReady = false;
const desktopCommandListeners = new Set<(command: DesktopShellCommand) => void>();
const desktopDataChangedListeners = new Set<(event: DesktopDataChangedEvent) => void>();
const pendingManagementRequests = new Map<
  string,
  {
    resolve: (value: DesktopManagementResponse) => void;
    reject: (reason?: unknown) => void;
    timer: ReturnType<typeof window.setTimeout>;
  }
>();
const pendingLocalDependencyRequests = new Map<
  string,
  {
    resolve: (value: unknown) => void;
    reject: (reason?: unknown) => void;
    timer: ReturnType<typeof window.setTimeout>;
    onProgress?: (progress: LocalDependencyRepairProgress) => void;
  }
>();
const pendingCodexRouteRequests = new Map<
  string,
  {
    resolve: (value: CodexRouteSwitchResponse) => void;
    reject: (reason?: unknown) => void;
    timer: ReturnType<typeof window.setTimeout>;
  }
>();
const pendingCodexUserFileRequests = new Map<
  string,
  {
    resolve: (value: CodexUserFileBridgeResponse) => void;
    reject: (reason?: unknown) => void;
    timer: ReturnType<typeof window.setTimeout>;
  }
>();
const MANAGEMENT_REQUEST_TIMEOUT_MS = 120_000;
const LOCAL_DEPENDENCY_SNAPSHOT_TIMEOUT_MS = 2_000;
const LOCAL_DEPENDENCY_REPAIR_TIMEOUT_MS = 35 * 60_000;
const CODEX_ROUTE_REQUEST_TIMEOUT_MS = 30_000;
const CODEX_USER_FILE_REQUEST_TIMEOUT_MS = 30_000;
let localDependencySnapshotInFlight: Promise<LocalDependencySnapshot> | null = null;

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
  const desktopSessionId =
    typeof payload.desktopSessionId === 'string' ? payload.desktopSessionId.trim() : '';
  if (!apiBase || !desktopSessionId) {
    return null;
  }

  return {
    desktopMode: true,
    apiBase,
    desktopSessionId,
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

function normalizeTransitionMilliseconds(value: unknown): number | undefined {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    return undefined;
  }

  return Math.max(0, Math.min(1_000, Math.round(value)));
}

function normalizeNotificationType(type: unknown): NotificationType {
  return type === 'success' || type === 'warning' || type === 'error' ? type : 'info';
}

function normalizeCommand(command: unknown): DesktopShellCommand | null {
  if (!command || typeof command !== 'object') {
    return null;
  }

  const record = command as Record<string, unknown>;
  if (record.type === 'setTheme') {
    return {
      type: 'setTheme',
      theme: normalizeDesktopTheme(record.theme),
      resolvedTheme: normalizeResolvedTheme(record.resolvedTheme),
      transitionMs: normalizeTransitionMilliseconds(record.transitionMs),
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

function normalizeLocalDependencyRepairProgress(
  value: unknown
): LocalDependencyRepairProgress | null {
  if (!isRecord(value)) return null;
  const actionId = typeof value.actionId === 'string' ? value.actionId : '';
  const phase = typeof value.phase === 'string' ? value.phase : '';
  const message = typeof value.message === 'string' ? value.message : '';
  if (!actionId || !phase || !message) return null;
  return {
    actionId,
    phase,
    message,
    commandLine: typeof value.commandLine === 'string' ? value.commandLine : null,
    recentOutput: Array.isArray(value.recentOutput)
      ? value.recentOutput.filter((line): line is string => typeof line === 'string')
      : [],
    logPath: typeof value.logPath === 'string' ? value.logPath : null,
    updatedAt: typeof value.updatedAt === 'string' ? value.updatedAt : '',
    exitCode: typeof value.exitCode === 'number' ? value.exitCode : null,
  };
}

function normalizeCodexRouteMode(value: unknown): CodexRouteMode {
  return value === 'official' || value === 'cpa' ? value : 'unknown';
}

function normalizeCodexRouteTargetKind(value: unknown): CodexRouteTargetKind {
  return value === 'official' || value === 'managed-cpa' || value === 'third-party-cpa'
    ? value
    : 'unknown';
}

function normalizeCodexRouteTarget(value: unknown): Exclude<CodexRouteMode, 'unknown'> | null {
  return value === 'official' || value === 'cpa' ? value : null;
}

function normalizeCodexRouteTargetId(value: unknown): string {
  return typeof value === 'string' ? value.trim() : '';
}

function normalizeCodexRouteTargets(value: unknown): CodexRouteTarget[] {
  if (!Array.isArray(value)) return [];
  return value
    .map((target) => {
      if (!isRecord(target)) return null;
      const id = normalizeCodexRouteTargetId(target.id);
      const label = typeof target.label === 'string' ? target.label.trim() : '';
      if (!id || !label) return null;
      const normalizedTarget: CodexRouteTarget = {
        id,
        mode: normalizeCodexRouteMode(target.mode),
        kind: normalizeCodexRouteTargetKind(target.kind),
        label,
        baseUrl: typeof target.baseUrl === 'string' ? target.baseUrl : null,
        port: typeof target.port === 'number' ? target.port : null,
        profileName: typeof target.profileName === 'string' ? target.profileName : null,
        providerName: typeof target.providerName === 'string' ? target.providerName : null,
        isCurrent: target.isCurrent === true,
        canSwitch: target.canSwitch === true,
        statusMessage: typeof target.statusMessage === 'string' ? target.statusMessage : null,
      };
      return normalizedTarget;
    })
    .filter((target): target is CodexRouteTarget => target !== null);
}

function normalizeCodexRouteState(value: unknown): CodexRouteState | null {
  if (!isRecord(value)) return null;
  const targets = normalizeCodexRouteTargets(value.targets);
  return {
    currentMode: normalizeCodexRouteMode(value.currentMode),
    targetMode: normalizeCodexRouteTarget(value.targetMode),
    currentTargetId:
      typeof value.currentTargetId === 'string' && value.currentTargetId.trim()
        ? value.currentTargetId.trim()
        : (targets.find((target) => target.isCurrent)?.id ?? null),
    currentLabel:
      typeof value.currentLabel === 'string' && value.currentLabel.trim()
        ? value.currentLabel.trim()
        : (targets.find((target) => target.isCurrent)?.label ?? ''),
    targets,
    configPath: typeof value.configPath === 'string' ? value.configPath : '',
    authPath: typeof value.authPath === 'string' ? value.authPath : '',
    canSwitch: value.canSwitch === true,
    statusMessage: typeof value.statusMessage === 'string' ? value.statusMessage : '',
  };
}

function normalizeCodexUserFileId(value: unknown): CodexUserFileId | null {
  return value === 'config' || value === 'auth' ? value : null;
}

function normalizeCodexUserFileLanguage(value: unknown): CodexUserFileLanguage {
  return value === 'json' ? 'json' : 'toml';
}

function normalizeCodexUserFileValidation(value: unknown): CodexUserFileValidation | null {
  if (!isRecord(value)) return null;
  return {
    isValid: value.isValid === true,
    message: typeof value.message === 'string' ? value.message : '',
  };
}

function normalizeCodexUserFileSnapshot(value: unknown): CodexUserFileSnapshot | null {
  if (!isRecord(value)) return null;
  const fileId = normalizeCodexUserFileId(value.fileId);
  const validation = normalizeCodexUserFileValidation(value.validation);
  if (!fileId || !validation) return null;
  const sizeBytes =
    typeof value.sizeBytes === 'number' && Number.isFinite(value.sizeBytes)
      ? Math.max(0, Math.round(value.sizeBytes))
      : 0;
  return {
    fileId,
    path: typeof value.path === 'string' ? value.path : '',
    content: typeof value.content === 'string' ? value.content : '',
    language: normalizeCodexUserFileLanguage(value.language),
    exists: value.exists === true,
    lastWriteTimeUtc: typeof value.lastWriteTimeUtc === 'string' ? value.lastWriteTimeUtc : null,
    sizeBytes,
    validation,
  };
}

function normalizeCodexUserFileSnapshots(value: unknown): CodexUserFileSnapshot[] {
  if (!Array.isArray(value)) return [];
  return value
    .map((item) => normalizeCodexUserFileSnapshot(item))
    .filter((item): item is CodexUserFileSnapshot => item !== null);
}

function normalizeCodexUserFileBackupResult(value: unknown): CodexUserFileBackupResult | null {
  if (!isRecord(value)) return null;
  const fileId = normalizeCodexUserFileId(value.fileId);
  const snapshot = normalizeCodexUserFileSnapshot(value.snapshot);
  if (!fileId || !snapshot) return null;
  return {
    fileId,
    path: typeof value.path === 'string' ? value.path : '',
    backupPath: typeof value.backupPath === 'string' ? value.backupPath : '',
    snapshot,
  };
}

function normalizeCodexUserFileSaveResult(value: unknown): CodexUserFileSaveResult | null {
  if (!isRecord(value)) return null;
  const fileId = normalizeCodexUserFileId(value.fileId);
  const snapshot = normalizeCodexUserFileSnapshot(value.snapshot);
  if (!fileId || !snapshot) return null;
  return {
    fileId,
    path: typeof value.path === 'string' ? value.path : '',
    backupPath: typeof value.backupPath === 'string' ? value.backupPath : null,
    snapshot,
  };
}

function normalizeCodexUserFileResponse(message: Record<string, unknown>): CodexUserFileBridgeResponse {
  const files = normalizeCodexUserFileSnapshots(message.files);
  const file = normalizeCodexUserFileSnapshot(message.file);
  const validation = normalizeCodexUserFileValidation(message.validation);
  const saveResult = normalizeCodexUserFileSaveResult(message.result);
  const backupResult = saveResult ? null : normalizeCodexUserFileBackupResult(message.result);

  return {
    ...(files.length > 0 ? { files } : {}),
    ...(file ? { file } : {}),
    ...(validation ? { validation } : {}),
    ...(saveResult || backupResult ? { result: saveResult ?? backupResult ?? undefined } : {}),
  };
}

function settleLocalDependencyRequest(
  requestId: unknown,
  value: unknown,
  error?: unknown
): boolean {
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

function normalizeDataChangedEvent(message: unknown): DesktopDataChangedEvent | null {
  if (!isRecord(message) || message.type !== 'dataChanged') return null;
  const scopes = Array.isArray(message.scopes)
    ? message.scopes.map((scope) => (typeof scope === 'string' ? scope.trim() : '')).filter(Boolean)
    : [];
  const sequence =
    typeof message.sequence === 'number' && Number.isFinite(message.sequence)
      ? Math.max(0, Math.round(message.sequence))
      : 0;
  if (scopes.length === 0) return null;
  return { scopes: Array.from(new Set(scopes)), sequence };
}

function handleDataChangedMessage(message: unknown): boolean {
  const event = normalizeDataChangedEvent(message);
  if (!event) return false;
  desktopDataChangedListeners.forEach((listener) => listener(event));
  if (typeof window !== 'undefined') {
    window.dispatchEvent(new CustomEvent('codexcliplus:dataChanged', { detail: event }));
  }
  return true;
}

function handleManagementResponseMessage(message: unknown): boolean {
  if (!isRecord(message) || message.type !== 'managementResponse') return false;
  const requestId = typeof message.requestId === 'string' ? message.requestId : '';
  if (!requestId) return true;
  const pending = pendingManagementRequests.get(requestId);
  if (!pending) return true;
  pendingManagementRequests.delete(requestId);
  window.clearTimeout(pending.timer);

  const status =
    typeof message.status === 'number' && Number.isFinite(message.status)
      ? Math.round(message.status)
      : 0;
  const body = typeof message.body === 'string' ? message.body : '';
  if (message.ok === true) {
    pending.resolve({ status, body, metadata: message.metadata });
    return true;
  }

  const error = new Error(
    typeof message.error === 'string' && message.error.trim()
      ? message.error
      : '桌面管理代理请求失败'
  ) as Error & { status?: number; data?: unknown; body?: string };
  error.status = status;
  error.body = body;
  try {
    error.data = body ? JSON.parse(body) : undefined;
  } catch {
    error.data = body;
  }
  pending.reject(error);
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

  if (message.type === 'localDependencyRepairProgress') {
    const requestId = typeof message.requestId === 'string' ? message.requestId : '';
    if (!requestId) return true;
    const pending = pendingLocalDependencyRequests.get(requestId);
    if (!pending) return true;
    const progress = normalizeLocalDependencyRepairProgress(message.progress);
    if (progress) {
      pending.onProgress?.(progress);
    }
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

function handleCodexRouteMessage(message: unknown): boolean {
  if (!isRecord(message) || message.type !== 'codexRouteResponse') return false;
  const requestId = typeof message.requestId === 'string' ? message.requestId : '';
  if (!requestId) return true;
  const pending = pendingCodexRouteRequests.get(requestId);
  if (!pending) return true;
  pendingCodexRouteRequests.delete(requestId);
  window.clearTimeout(pending.timer);

  const state = normalizeCodexRouteState(message.state);
  if (message.ok === true && state) {
    pending.resolve({
      state,
      configBackupPath:
        typeof message.configBackupPath === 'string' ? message.configBackupPath : null,
      authBackupPath: typeof message.authBackupPath === 'string' ? message.authBackupPath : null,
      officialAuthBackupPath:
        typeof message.officialAuthBackupPath === 'string' ? message.officialAuthBackupPath : null,
    });
    return true;
  }

  pending.reject(
    new Error(
      typeof message.error === 'string' && message.error.trim()
        ? message.error
        : 'Codex 路由切换失败'
    )
  );
  return true;
}

function handleCodexUserFileMessage(message: unknown): boolean {
  if (!isRecord(message) || message.type !== 'codexUserFileResponse') return false;
  const requestId = typeof message.requestId === 'string' ? message.requestId : '';
  if (!requestId) return true;
  const pending = pendingCodexUserFileRequests.get(requestId);
  if (!pending) return true;
  pendingCodexUserFileRequests.delete(requestId);
  window.clearTimeout(pending.timer);

  if (message.ok === true) {
    pending.resolve(normalizeCodexUserFileResponse(message));
    return true;
  }

  pending.reject(
    new Error(
      typeof message.error === 'string' && message.error.trim()
        ? message.error
        : 'Codex 用户配置操作失败'
    )
  );
  return true;
}

function registerLocalDependencyRequest<T>(
  requestId: string,
  timeoutMs: number,
  onProgress?: (progress: LocalDependencyRepairProgress) => void
): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    const timer = window.setTimeout(() => {
      pendingLocalDependencyRequests.delete(requestId);
      reject(new Error('桌面端响应超时'));
    }, timeoutMs);
    pendingLocalDependencyRequests.set(requestId, {
      resolve: (value) => resolve(value as T),
      reject,
      timer,
      onProgress,
    });
  });
}

function registerCodexRouteRequest(requestId: string): Promise<CodexRouteSwitchResponse> {
  return new Promise<CodexRouteSwitchResponse>((resolve, reject) => {
    const timer = window.setTimeout(() => {
      pendingCodexRouteRequests.delete(requestId);
      reject(new Error('Codex 路由响应超时'));
    }, CODEX_ROUTE_REQUEST_TIMEOUT_MS);
    pendingCodexRouteRequests.set(requestId, { resolve, reject, timer });
  });
}

function registerCodexUserFileRequest(
  requestId: string
): Promise<CodexUserFileBridgeResponse> {
  return new Promise<CodexUserFileBridgeResponse>((resolve, reject) => {
    const timer = window.setTimeout(() => {
      pendingCodexUserFileRequests.delete(requestId);
      reject(new Error('Codex 用户配置响应超时'));
    }, CODEX_USER_FILE_REQUEST_TIMEOUT_MS);
    pendingCodexUserFileRequests.set(requestId, { resolve, reject, timer });
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
    if (handleCodexUserFileMessage(event.data)) {
      return;
    }

    if (handleCodexRouteMessage(event.data)) {
      return;
    }

    if (handleManagementResponseMessage(event.data)) {
      return;
    }

    if (handleLocalDependencyMessage(event.data)) {
      return;
    }

    if (handleDataChangedMessage(event.data)) {
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
  if (!bridge) {
    return null;
  }

  if (typeof bridge.getBootstrap !== 'function' && typeof bridge.consumeBootstrap !== 'function') {
    return null;
  }

  try {
    const rawPayload =
      typeof bridge.getBootstrap === 'function'
        ? bridge.getBootstrap()
        : bridge.consumeBootstrap?.();
    const payload = normalizePayload(rawPayload);
    if (payload) {
      desktopBootstrapCache = payload;
    }
    return payload;
  } catch (error) {
    console.warn('Failed to read desktop bootstrap payload.', error);
    return null;
  }
}

export function isDesktopMode(): boolean {
  const bridge = getBridge();
  if (typeof bridge?.isDesktopMode === 'function') {
    return bridge.isDesktopMode() === true;
  }

  return typeof window !== 'undefined' && typeof window.chrome?.webview?.postMessage === 'function';
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
      sidebarCollapsed: state.sidebarCollapsed === true,
      pathname: typeof state.pathname === 'string' ? state.pathname : '/',
    });
    return true;
  } catch (error) {
    console.warn('Failed to send desktop shell state.', error);
    return false;
  }
}

export function showShellNotification(message: string, type: NotificationType = 'info'): boolean {
  const normalizedMessage = typeof message === 'string' ? message.trim() : '';
  if (!normalizedMessage) {
    return false;
  }

  const bridge = getBridge();
  if (typeof bridge?.showShellNotification !== 'function') {
    return false;
  }

  try {
    return (
      bridge.showShellNotification(normalizedMessage, normalizeNotificationType(type)) !== false
    );
  } catch (error) {
    console.warn('Failed to show desktop shell notification.', error);
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

export function importAccountConfigInDesktopShell(mode: 'json' | 'config' = 'json'): boolean {
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

export function requestCodexRouteState(): Promise<CodexRouteState> {
  const bridge = getBridge();
  if (typeof bridge?.requestCodexRouteState !== 'function') {
    return Promise.reject(new Error('Codex 路由切换需要桌面模式'));
  }

  ensureDesktopCommandListener();
  const requestId = createDesktopRequestId('codex-route');
  const request = registerCodexRouteRequest(requestId).then((response) => response.state);
  try {
    const posted = bridge.requestCodexRouteState(requestId);
    if (posted === false) {
      throw new Error('桌面桥接通道未就绪，请重新打开桌面应用后再切换。');
    }
  } catch (error) {
    const pending = pendingCodexRouteRequests.get(requestId);
    if (pending) window.clearTimeout(pending.timer);
    pendingCodexRouteRequests.delete(requestId);
    return Promise.reject(error);
  }
  return request;
}

export function switchCodexRoute(targetId: string): Promise<CodexRouteSwitchResponse> {
  const bridge = getBridge();
  if (typeof bridge?.switchCodexRoute !== 'function') {
    return Promise.reject(new Error('Codex 路由切换需要桌面模式'));
  }

  const normalizedTarget = normalizeCodexRouteTargetId(targetId);
  if (!normalizedTarget) {
    return Promise.reject(new Error('目标路由无效'));
  }

  ensureDesktopCommandListener();
  const requestId = createDesktopRequestId('codex-route-switch');
  const request = registerCodexRouteRequest(requestId);
  try {
    const posted = bridge.switchCodexRoute(normalizedTarget, requestId);
    if (posted === false) {
      throw new Error('桌面桥接通道未就绪，请重新打开桌面应用后再切换。');
    }
  } catch (error) {
    const pending = pendingCodexRouteRequests.get(requestId);
    if (pending) window.clearTimeout(pending.timer);
    pendingCodexRouteRequests.delete(requestId);
    return Promise.reject(error);
  }
  return request;
}

export function requestCodexUserFiles(): Promise<CodexUserFileSnapshot[]> {
  const bridge = getBridge();
  if (typeof bridge?.requestCodexUserFiles !== 'function') {
    return Promise.reject(new Error('需要桌面端桥接'));
  }

  ensureDesktopCommandListener();
  const requestId = createDesktopRequestId('codex-user-files');
  const request = registerCodexUserFileRequest(requestId).then((response) => response.files ?? []);
  try {
    const posted = bridge.requestCodexUserFiles(requestId);
    if (posted === false) {
      throw new Error('桌面桥接通道未就绪，请重新打开桌面应用后再同步。');
    }
  } catch (error) {
    const pending = pendingCodexUserFileRequests.get(requestId);
    if (pending) window.clearTimeout(pending.timer);
    pendingCodexUserFileRequests.delete(requestId);
    return Promise.reject(error);
  }
  return request;
}

export function readCodexUserFile(fileId: CodexUserFileId): Promise<CodexUserFileSnapshot> {
  const bridge = getBridge();
  if (typeof bridge?.readCodexUserFile !== 'function') {
    return Promise.reject(new Error('需要桌面端桥接'));
  }

  ensureDesktopCommandListener();
  const requestId = createDesktopRequestId('codex-user-file-read');
  const request = registerCodexUserFileRequest(requestId).then((response) => {
    if (!response.file) throw new Error('Codex 用户配置读取结果无效');
    return response.file;
  });
  try {
    const posted = bridge.readCodexUserFile(fileId, requestId);
    if (posted === false) {
      throw new Error('桌面桥接通道未就绪，请重新打开桌面应用后再读取。');
    }
  } catch (error) {
    const pending = pendingCodexUserFileRequests.get(requestId);
    if (pending) window.clearTimeout(pending.timer);
    pendingCodexUserFileRequests.delete(requestId);
    return Promise.reject(error);
  }
  return request;
}

export function validateCodexUserFile(
  fileId: CodexUserFileId,
  content: string
): Promise<CodexUserFileValidation> {
  const bridge = getBridge();
  if (typeof bridge?.validateCodexUserFile !== 'function') {
    return Promise.reject(new Error('需要桌面端桥接'));
  }

  ensureDesktopCommandListener();
  const requestId = createDesktopRequestId('codex-user-file-validate');
  const request = registerCodexUserFileRequest(requestId).then((response) => {
    if (!response.validation) throw new Error('Codex 用户配置校验结果无效');
    return response.validation;
  });
  try {
    const posted = bridge.validateCodexUserFile(fileId, content, requestId);
    if (posted === false) {
      throw new Error('桌面桥接通道未就绪，请重新打开桌面应用后再校验。');
    }
  } catch (error) {
    const pending = pendingCodexUserFileRequests.get(requestId);
    if (pending) window.clearTimeout(pending.timer);
    pendingCodexUserFileRequests.delete(requestId);
    return Promise.reject(error);
  }
  return request;
}

export function backupCodexUserFile(fileId: CodexUserFileId): Promise<CodexUserFileBackupResult> {
  const bridge = getBridge();
  if (typeof bridge?.backupCodexUserFile !== 'function') {
    return Promise.reject(new Error('需要桌面端桥接'));
  }

  ensureDesktopCommandListener();
  const requestId = createDesktopRequestId('codex-user-file-backup');
  const request = registerCodexUserFileRequest(requestId).then((response) => {
    const result = response.result;
    if (!result || !('backupPath' in result) || !result.backupPath) {
      throw new Error('Codex 用户配置备份结果无效');
    }
    return result as CodexUserFileBackupResult;
  });
  try {
    const posted = bridge.backupCodexUserFile(fileId, requestId);
    if (posted === false) {
      throw new Error('桌面桥接通道未就绪，请重新打开桌面应用后再备份。');
    }
  } catch (error) {
    const pending = pendingCodexUserFileRequests.get(requestId);
    if (pending) window.clearTimeout(pending.timer);
    pendingCodexUserFileRequests.delete(requestId);
    return Promise.reject(error);
  }
  return request;
}

export function saveCodexUserFile(
  fileId: CodexUserFileId,
  content: string,
  expectedLastWriteTimeUtc?: string | null
): Promise<CodexUserFileSaveResult> {
  const bridge = getBridge();
  if (typeof bridge?.saveCodexUserFile !== 'function') {
    return Promise.reject(new Error('需要桌面端桥接'));
  }

  ensureDesktopCommandListener();
  const requestId = createDesktopRequestId('codex-user-file-save');
  const request = registerCodexUserFileRequest(requestId).then((response) => {
    const result = response.result;
    if (!result) throw new Error('Codex 用户配置保存结果无效');
    return result as CodexUserFileSaveResult;
  });
  try {
    const posted = bridge.saveCodexUserFile(
      fileId,
      content,
      expectedLastWriteTimeUtc ?? null,
      requestId
    );
    if (posted === false) {
      throw new Error('桌面桥接通道未就绪，请重新打开桌面应用后再保存。');
    }
  } catch (error) {
    const pending = pendingCodexUserFileRequests.get(requestId);
    if (pending) window.clearTimeout(pending.timer);
    pendingCodexUserFileRequests.delete(requestId);
    return Promise.reject(error);
  }
  return request;
}

export function requestLocalDependencySnapshot(): Promise<LocalDependencySnapshot> {
  if (localDependencySnapshotInFlight) {
    return localDependencySnapshotInFlight;
  }

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
  const inFlight = request.finally(() => {
    if (localDependencySnapshotInFlight === inFlight) {
      localDependencySnapshotInFlight = null;
    }
  });
  localDependencySnapshotInFlight = inFlight;
  try {
    const posted = bridge.requestLocalDependencySnapshot(requestId);
    if (posted === false) {
      throw new Error('桌面桥接通道未就绪，请重新打开桌面应用后再检测。');
    }
  } catch (error) {
    const pending = pendingLocalDependencyRequests.get(requestId);
    if (pending) window.clearTimeout(pending.timer);
    pendingLocalDependencyRequests.delete(requestId);
    localDependencySnapshotInFlight = null;
    return Promise.reject(error);
  }
  return inFlight;
}

export function runLocalDependencyRepair(
  actionId: string,
  onProgress?: (progress: LocalDependencyRepairProgress) => void
): Promise<LocalDependencyRepairResponse> {
  const bridge = getBridge();
  if (typeof bridge?.runLocalDependencyRepair !== 'function') {
    return Promise.reject(new Error('本地环境修复需要桌面模式'));
  }

  ensureDesktopCommandListener();
  const requestId = createDesktopRequestId('local-env-repair');
  const request = registerLocalDependencyRequest<LocalDependencyRepairResponse>(
    requestId,
    LOCAL_DEPENDENCY_REPAIR_TIMEOUT_MS,
    onProgress
  );
  try {
    const posted = bridge.runLocalDependencyRepair(actionId, requestId);
    if (posted === false) {
      throw new Error('桌面桥接通道未就绪，请重新打开桌面应用后再修复。');
    }
  } catch (error) {
    const pending = pendingLocalDependencyRequests.get(requestId);
    if (pending) window.clearTimeout(pending.timer);
    pendingLocalDependencyRequests.delete(requestId);
    return Promise.reject(error);
  }
  return request;
}

export function sendDesktopManagementRequest(
  request: DesktopManagementRequest
): Promise<DesktopManagementResponse> {
  const bridge = getBridge();
  if (typeof bridge?.managementRequest !== 'function') {
    return Promise.reject(new Error('桌面管理代理需要桌面模式'));
  }

  ensureDesktopCommandListener();
  const requestId = createDesktopRequestId('management');
  const pending = new Promise<DesktopManagementResponse>((resolve, reject) => {
    const timer = window.setTimeout(() => {
      pendingManagementRequests.delete(requestId);
      reject(new Error('桌面管理代理响应超时'));
    }, MANAGEMENT_REQUEST_TIMEOUT_MS);
    pendingManagementRequests.set(requestId, { resolve, reject, timer });
  });

  try {
    bridge.managementRequest({
      ...request,
      requestId,
      method: request.method || 'GET',
      path: request.path || '/',
    });
  } catch (error) {
    const current = pendingManagementRequests.get(requestId);
    if (current) window.clearTimeout(current.timer);
    pendingManagementRequests.delete(requestId);
    return Promise.reject(error);
  }

  return pending;
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

export function subscribeDesktopDataChanged(
  listener: (event: DesktopDataChangedEvent) => void
): () => void {
  ensureDesktopCommandListener();
  desktopDataChangedListeners.add(listener);
  return () => {
    desktopDataChangedListeners.delete(listener);
  };
}
