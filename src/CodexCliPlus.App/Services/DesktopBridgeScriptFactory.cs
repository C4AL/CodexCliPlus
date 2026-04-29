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
                }
              };

              Object.defineProperty(window, '__CODEXCLIPLUS_DESKTOP_BRIDGE__', {
                configurable: true,
                value: bridge
              });

              let navigationHoverZoneActive = false;
              const updateNavigationHoverZone = (active) => {
                if (navigationHoverZoneActive === active) {
                  return;
                }

                navigationHoverZoneActive = active;
                postHostMessage({ type: 'navigationHoverZone', active });
              };

              window.addEventListener('mousemove', (event) => {
                updateNavigationHoverZone(event.clientX <= window.innerWidth * 0.25);
              }, { passive: true });
              window.addEventListener('mouseleave', () => updateNavigationHoverZone(false));

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
