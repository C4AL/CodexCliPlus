using System.Text.Encodings.Web;
using System.Text.Json;

using CPAD.Core.Models;

namespace CPAD.Services;

public static class DesktopBridgeScriptFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
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
            }
          };

          Object.defineProperty(window, '__CPAD_DESKTOP_BRIDGE__', {
            configurable: true,
            value: bridge
          });

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
