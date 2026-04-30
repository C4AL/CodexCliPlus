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
              let bootstrap = payload;

              const postHostMessage = (message) => {
                if (window.chrome?.webview && typeof window.chrome.webview.postMessage === 'function') {
                  window.chrome.webview.postMessage(message);
                }
              };

              const bridge = {
                isDesktopMode: () => true,
                consumeBootstrap: () => {
                  if (!bootstrap) {
                    return null;
                  }

                  const current = { ...bootstrap };
                  bootstrap = null;
                  return current;
                },
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
                    theme: typeof normalized.theme === 'string' ? normalized.theme : 'auto',
                    resolvedTheme: typeof normalized.resolvedTheme === 'string' ? normalized.resolvedTheme : 'light',
                    sidebarCollapsed: normalized.sidebarCollapsed === true,
                    pathname: typeof normalized.pathname === 'string' ? normalized.pathname : '/'
                  });
                },
                importAccountConfig: (mode) => postHostMessage({ type: 'importAccountConfig', mode: typeof mode === 'string' ? mode : 'json' }),
                exportAccountConfig: (mode) => postHostMessage({ type: 'exportAccountConfig', mode: typeof mode === 'string' ? mode : 'json' }),
                importSacPackage: () => postHostMessage({ type: 'importSacPackage' }),
                exportSacPackage: () => postHostMessage({ type: 'exportSacPackage' }),
                clearUsageStats: () => postHostMessage({ type: 'clearUsageStats' }),
                usageStatsRefreshed: () => postHostMessage({ type: 'usageStatsRefreshed' }),
                checkDesktopUpdate: () => postHostMessage({ type: 'checkDesktopUpdate' }),
                applyDesktopUpdate: () => postHostMessage({ type: 'applyDesktopUpdate' })
              };

              Object.defineProperty(window, '__CODEXCLIPLUS_DESKTOP_BRIDGE__', {
                configurable: true,
                value: bridge
              });

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
