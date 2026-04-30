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

export function subscribeDesktopShellCommand(
  listener: (command: DesktopShellCommand) => void
): () => void {
  ensureDesktopCommandListener();
  desktopCommandListeners.add(listener);
  return () => {
    desktopCommandListeners.delete(listener);
  };
}
