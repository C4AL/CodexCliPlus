using System.Text.Encodings.Web;
using System.Text.Json;
using CodexCliPlus.Core.Models;

namespace CodexCliPlus.Services;

public static class DesktopBridgeScriptFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string CreateInitializationScript(DesktopBootstrapPayload payload)
    {
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        return $$"""
            (() => {
              const payload = Object.freeze({{payloadJson}});

              const hasHostBridge = () =>
                Boolean(window.chrome?.webview && typeof window.chrome.webview.postMessage === 'function');

              const postHostMessage = (message) => {
                if (!hasHostBridge()) {
                  return false;
                }

                window.chrome.webview.postMessage(message);
                return true;
              };

              const readBootstrap = () => ({ ...payload });

              const bridge = {
                isDesktopMode: () => hasHostBridge(),
                getBootstrap: readBootstrap,
                consumeBootstrap: readBootstrap,
                openExternal: (url) => {
                  if (typeof url === 'string' && url.trim()) {
                    postHostMessage({ type: 'openExternal', url });
                  }
                },
                requestNativeLogin: (message) => {
                  const normalizedMessage = typeof message === 'string' ? message.trim() : '';
                  postHostMessage({
                    type: 'requestNativeLogin',
                    message: normalizedMessage || undefined
                  });
                },
                shellStateChanged: (state) => {
                  const normalized = state && typeof state === 'object' ? state : {};
                  postHostMessage({
                    type: 'shellStateChanged',
                    connectionStatus: typeof normalized.connectionStatus === 'string' ? normalized.connectionStatus : 'disconnected',
                    apiBase: typeof normalized.apiBase === 'string' ? normalized.apiBase : '',
                    sidebarCollapsed: normalized.sidebarCollapsed === true,
                    pathname: typeof normalized.pathname === 'string' ? normalized.pathname : '/'
                  });
                },
                showShellNotification: (message, type) => {
                  const normalizedMessage = typeof message === 'string' ? message.trim() : '';
                  if (!normalizedMessage) {
                    return false;
                  }

                  const normalizedType =
                    type === 'success' || type === 'warning' || type === 'error'
                      ? type
                      : 'info';
                  return postHostMessage({
                    type: 'shellNotification',
                    message: normalizedMessage,
                    notificationType: normalizedType
                  });
                },
                importAccountConfig: (mode) => postHostMessage({ type: 'importAccountConfig', mode: typeof mode === 'string' ? mode : 'json' }),
                exportAccountConfig: (mode) => postHostMessage({ type: 'exportAccountConfig', mode: typeof mode === 'string' ? mode : 'json' }),
                importSacPackage: () => postHostMessage({ type: 'importSacPackage' }),
                exportSacPackage: () => postHostMessage({ type: 'exportSacPackage' }),
                clearUsageStats: () => postHostMessage({ type: 'clearUsageStats' }),
                usageStatsRefreshed: () => postHostMessage({ type: 'usageStatsRefreshed' }),
                checkDesktopUpdate: () => postHostMessage({ type: 'checkDesktopUpdate' }),
                applyDesktopUpdate: () => postHostMessage({ type: 'applyDesktopUpdate' }),
                requestCodexRouteState: (requestId) => postHostMessage({
                  type: 'requestCodexRouteState',
                  requestId: typeof requestId === 'string' ? requestId : undefined
                }),
                switchCodexRoute: (targetId, requestId) => postHostMessage({
                  type: 'switchCodexRoute',
                  targetId: typeof targetId === 'string' ? targetId : '',
                  targetMode: typeof targetId === 'string' ? targetId : '',
                  requestId: typeof requestId === 'string' ? requestId : undefined
                }),
                managementRequest: (request) => {
                  const normalized = request && typeof request === 'object' ? request : {};
                  postHostMessage({
                    type: 'managementRequest',
                    requestId: typeof normalized.requestId === 'string' ? normalized.requestId : undefined,
                    method: typeof normalized.method === 'string' ? normalized.method : 'GET',
                    path: typeof normalized.path === 'string' ? normalized.path : '/',
                    body: typeof normalized.body === 'string' ? normalized.body : undefined,
                    contentType: typeof normalized.contentType === 'string' ? normalized.contentType : 'application/json',
                    accept: typeof normalized.accept === 'string' ? normalized.accept : 'application/json',
                    files: Array.isArray(normalized.files) ? normalized.files : undefined,
                    fields: normalized.fields && typeof normalized.fields === 'object' ? normalized.fields : undefined
                  });
                },
                requestLocalDependencySnapshot: (requestId) => postHostMessage({
                  type: 'requestLocalDependencySnapshot',
                  requestId: typeof requestId === 'string' ? requestId : undefined
                }),
                runLocalDependencyRepair: (actionId, requestId) => postHostMessage({
                  type: 'runLocalDependencyRepair',
                  actionId: typeof actionId === 'string' ? actionId : '',
                  requestId: typeof requestId === 'string' ? requestId : undefined
                })
              };

              Object.defineProperty(window, '__CODEXCLIPLUS_DESKTOP_BRIDGE__', {
                configurable: true,
                value: bridge
              });

              const applyInitialTheme = () => {
                const root = document.documentElement;
                if (!root) {
                  return false;
                }

                const theme = typeof payload.theme === 'string' ? payload.theme : 'auto';
                const resolvedTheme = payload.resolvedTheme === 'dark' ? 'dark' : 'light';
                const dataTheme = theme === 'dark' || (theme === 'auto' && resolvedTheme === 'dark')
                  ? 'dark'
                  : 'white';
                root.setAttribute('data-theme', dataTheme);
                root.style.colorScheme = dataTheme === 'dark' ? 'dark' : 'light';
                return true;
              };

              if (!applyInitialTheme()) {
                window.addEventListener('DOMContentLoaded', () => {
                  applyInitialTheme();
                }, { once: true });
              }

              let navigationHoverZoneActive = false;
              let navigationHoverZoneTimer = null;
              let pointerDown = false;
              let dragStarted = false;
              let lastPointer = { x: 0, y: 0 };

              const isEditableElement = (target) => {
                if (!target || typeof target.closest !== 'function') {
                  return false;
                }

                return Boolean(target.closest('input, textarea, select, [contenteditable="true"], [role="textbox"]'));
              };

              const updateNavigationHoverZone = (active) => {
                if (navigationHoverZoneActive === active) {
                  return;
                }

                navigationHoverZoneActive = active;
                postHostMessage({ type: 'navigationHoverZone', active });
              };

              const clearNavigationHoverTimer = () => {
                if (navigationHoverZoneTimer) {
                  window.clearTimeout(navigationHoverZoneTimer);
                  navigationHoverZoneTimer = null;
                }
              };

              const cancelNavigationHoverIntent = () => {
                clearNavigationHoverTimer();
                updateNavigationHoverZone(false);
              };

              window.addEventListener('mousemove', (event) => {
                const movedFar =
                  Math.abs(event.clientX - lastPointer.x) > 4 ||
                  Math.abs(event.clientY - lastPointer.y) > 4;
                lastPointer = { x: event.clientX, y: event.clientY };
                if (pointerDown && movedFar) {
                  dragStarted = true;
                }

                if (
                  pointerDown ||
                  dragStarted ||
                  isEditableElement(event.target) ||
                  event.clientX > 18
                ) {
                  cancelNavigationHoverIntent();
                  return;
                }

                if (navigationHoverZoneActive || navigationHoverZoneTimer) {
                  return;
                }

                navigationHoverZoneTimer = window.setTimeout(() => {
                  navigationHoverZoneTimer = null;
                  updateNavigationHoverZone(true);
                }, 90);
              }, { passive: true });
              window.addEventListener('mousedown', (event) => {
                pointerDown = true;
                dragStarted = false;
                lastPointer = { x: event.clientX, y: event.clientY };
                cancelNavigationHoverIntent();
              }, { passive: true });
              window.addEventListener('mouseup', () => {
                pointerDown = false;
                dragStarted = false;
              }, { passive: true });
              window.addEventListener('mouseleave', () => cancelNavigationHoverIntent());
              window.addEventListener('blur', () => cancelNavigationHoverIntent());

              const previousOpen = typeof window.open === 'function' ? window.open.bind(window) : null;
              window.open = (url, target, features) => {
                if (typeof url === 'string' && /^https?:\/\//i.test(url)) {
                  bridge.openExternal(url);
                  return null;
                }

                return previousOpen ? previousOpen(url, target, features) : null;
              };
            })();
            """;
    }
}
