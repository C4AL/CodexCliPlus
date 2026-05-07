import { afterEach, describe, expect, it, vi } from 'vitest';
import type { DesktopManagementRequest, DesktopManagementResponse } from '@/desktop/bridge';

type PostedManagementRequest = DesktopManagementRequest & { requestId: string };

async function loadApiClient() {
  vi.resetModules();
  return import('@/services/api/client');
}

function installDesktopManagementProxy(
  respond: (request: PostedManagementRequest) => Partial<DesktopManagementResponse> & { ok?: boolean }
) {
  const listeners: Array<(event: MessageEvent) => void> = [];
  const managementRequest = vi.fn((request: PostedManagementRequest) => {
    const response = respond(request);
    listeners.forEach((listener) =>
      listener({
        data: {
          type: 'managementResponse',
          requestId: request.requestId,
          ok: response.ok ?? true,
          status: response.status ?? 200,
          body: response.body ?? '',
          metadata: response.metadata,
        },
      } as MessageEvent)
    );
  });

  Object.assign(window, {
    chrome: {
      webview: {
        addEventListener: vi.fn((_type: 'message', listener: (event: MessageEvent) => void) => {
          listeners.push(listener);
        }),
        postMessage: vi.fn(),
      },
    },
    __CODEXCLIPLUS_DESKTOP_BRIDGE__: {
      isDesktopMode: () => true,
      managementRequest,
    },
  });

  return { managementRequest };
}

describe('apiClient desktop management proxy', () => {
  afterEach(() => {
    Reflect.deleteProperty(window, '__CODEXCLIPLUS_DESKTOP_BRIDGE__');
    Reflect.deleteProperty(window, 'chrome');
    vi.resetModules();
    vi.restoreAllMocks();
  });

  it('routes desktop GET requests through the bridge without a management key', async () => {
    const { managementRequest } = installDesktopManagementProxy(() => ({
      body: '{"ok":true}',
      metadata: { version: '6.9.31' },
    }));
    const { apiClient } = await loadApiClient();

    apiClient.setConfig({ apiBase: 'http://127.0.0.1:15345', managementKey: '' });

    await expect(apiClient.get('/config', { params: { source: 'dashboard' } })).resolves.toEqual({
      ok: true,
    });

    const request = managementRequest.mock.calls[0]?.[0];
    expect(request).toMatchObject({
      method: 'GET',
      path: '/config?source=dashboard',
      accept: 'application/json',
    });
    expect(request && 'managementKey' in request).toBe(false);
    expect(request && 'authorization' in request).toBe(false);
  });

  it('preserves blob downloads from desktop GET responses', async () => {
    installDesktopManagementProxy(() => ({
      body: '{"token":"saved"}',
    }));
    const { apiClient } = await loadApiClient();

    apiClient.setConfig({ apiBase: 'http://127.0.0.1:15345', managementKey: '' });
    const response = await apiClient.getRaw('/auth-files/download?name=codex.json', {
      responseType: 'blob',
    });

    expect(response.status).toBe(200);
    expect(response.data).toBeInstanceOf(Blob);
    await expect((response.data as Blob).text()).resolves.toBe('{"token":"saved"}');
  });

  it('preserves text responses and request bodies through requestRaw', async () => {
    const { managementRequest } = installDesktopManagementProxy((request) => ({
      body: `saved:${request.body}`,
    }));
    const { apiClient } = await loadApiClient();

    apiClient.setConfig({ apiBase: 'http://127.0.0.1:15345', managementKey: '' });
    const response = await apiClient.requestRaw({
      method: 'PUT',
      url: '/config.yaml',
      data: 'port: 15345',
      responseType: 'text',
      headers: {
        Accept: 'text/plain',
        'Content-Type': 'application/yaml',
      },
    });

    expect(response.data).toBe('saved:port: 15345');
    expect(managementRequest.mock.calls[0]?.[0]).toMatchObject({
      method: 'PUT',
      path: '/config.yaml',
      body: 'port: 15345',
      contentType: 'application/yaml',
      accept: 'text/plain',
    });
  });
});
