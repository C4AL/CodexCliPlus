export interface DesktopBootstrapPayload {
  desktopMode: boolean;
  apiBase: string;
  managementKey: string;
}

interface DesktopBridge {
  isDesktopMode?: () => boolean;
  consumeBootstrap?: () => DesktopBootstrapPayload | null;
  openExternal?: (url: string) => void;
  requestNativeLogin?: (message?: string) => void;
}

declare global {
  interface Window {
    __CODEXCLIPLUS_DESKTOP_BRIDGE__?: DesktopBridge;
  }
}

let desktopBootstrapCache: DesktopBootstrapPayload | null | undefined;

function getBridge(): DesktopBridge | null {
  if (typeof window === 'undefined') {
    return null;
  }

  return window.__CODEXCLIPLUS_DESKTOP_BRIDGE__ ?? null;
}

function normalizePayload(payload: DesktopBootstrapPayload | null | undefined): DesktopBootstrapPayload | null {
  if (!payload || payload.desktopMode !== true) {
    return null;
  }

  const apiBase = typeof payload.apiBase === 'string' ? payload.apiBase.trim() : '';
  const managementKey = typeof payload.managementKey === 'string' ? payload.managementKey.trim() : '';
  if (!apiBase || !managementKey) {
    return null;
  }

  return {
    desktopMode: true,
    apiBase,
    managementKey
  };
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
