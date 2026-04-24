export interface DesktopBootstrapPayload {
  desktopMode: boolean;
  apiBase: string;
  managementKey: string;
}

interface DesktopBridge {
  isDesktopMode?: () => boolean;
  consumeBootstrap?: () => DesktopBootstrapPayload | null;
  openExternal?: (url: string) => void;
}

declare global {
  interface Window {
    __CPAD_DESKTOP_BRIDGE__?: DesktopBridge;
  }
}

function getBridge(): DesktopBridge | null {
  if (typeof window === 'undefined') {
    return null;
  }

  return window.__CPAD_DESKTOP_BRIDGE__ ?? null;
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

export function isDesktopMode(): boolean {
  const bridge = getBridge();
  return typeof bridge?.isDesktopMode === 'function' ? bridge.isDesktopMode() === true : false;
}

export function consumeDesktopBootstrap(): DesktopBootstrapPayload | null {
  const bridge = getBridge();
  if (typeof bridge?.consumeBootstrap !== 'function') {
    return null;
  }

  try {
    return normalizePayload(bridge.consumeBootstrap());
  } catch (error) {
    console.warn('Failed to consume desktop bootstrap payload.', error);
    return null;
  }
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
