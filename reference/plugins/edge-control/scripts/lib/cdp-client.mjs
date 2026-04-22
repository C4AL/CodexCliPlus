import { sleep } from "./async.mjs";
import { decodeHtmlEntities, safeJsonParse } from "./text.mjs";

export class EdgeCdpClient {
  constructor(bridgeClient) {
    this.bridge = bridgeClient;
  }

  async listTabs(options = {}) {
    return this.bridge.command("list_tabs", options, { timeoutMs: 20000 });
  }

  async getTab(tabId) {
    const tabs = await this.listTabs();
    return tabs.find((tab) => tab.id === tabId) || null;
  }

  async focusTab(tabId) {
    return this.bridge.command("focus_tab", { tabId }, { timeoutMs: 15000 });
  }

  async navigate(url, options = {}) {
    return this.bridge.command(
      "navigate",
      {
        url,
        tabId: options.tabId,
        createNewTab: options.createNewTab,
        active: options.active,
      },
      { timeoutMs: options.timeoutMs ?? 40000 }
    );
  }

  async closeTab(tabId) {
    return this.bridge.command(
      "send_cdp",
      {
        tabId,
        method: "Page.close",
        params: {},
        detachAfter: true,
      },
      { timeoutMs: 20000, retries: 2 }
    );
  }

  async waitFor(tabId, selector, options = {}) {
    return this.bridge.command(
      "wait_for",
      {
        tabId,
        selector,
        timeoutMs: options.timeoutMs ?? 30000,
        world: options.world ?? "MAIN",
      },
      { timeoutMs: (options.timeoutMs ?? 30000) + 5000 }
    );
  }

  async query(tabId, selector, options = {}) {
    return this.bridge.command(
      "query",
      {
        tabId,
        selector,
        maxResults: options.maxResults,
        maxLength: options.maxLength,
        includeHtml: options.includeHtml,
        world: options.world ?? "MAIN",
      },
      { timeoutMs: options.timeoutMs ?? 20000 }
    );
  }

  async sendCdp(tabId, method, params = {}, options = {}) {
    return this.bridge.command(
      "send_cdp",
      {
        tabId,
        method,
        params,
        detachAfter: options.detachAfter ?? true,
      },
      { timeoutMs: options.timeoutMs ?? 60000, retries: options.retries ?? 4 }
    );
  }

  async evaluate(tabId, expression, options = {}) {
    const result = await this.sendCdp(
      tabId,
      "Runtime.evaluate",
      {
        expression,
        awaitPromise: options.awaitPromise ?? true,
        returnByValue: options.returnByValue ?? true,
        userGesture: options.userGesture ?? true,
      },
      {
        detachAfter: options.detachAfter ?? true,
        timeoutMs: options.timeoutMs ?? 120000,
        retries: options.retries ?? 4,
      }
    );

    return result?.result?.result?.value;
  }

  async evaluateJson(tabId, expression, options = {}) {
    const value = await this.evaluate(tabId, expression, options);
    if (typeof value === "string") {
      return safeJsonParse(decodeHtmlEntities(value), null);
    }
    return value ?? null;
  }

  async waitForPageReady(tabId, options = {}) {
    const timeoutMs = options.timeoutMs ?? 45000;
    const pollIntervalMs = options.pollIntervalMs ?? 500;
    const startedAt = Date.now();

    while (Date.now() - startedAt < timeoutMs) {
      const tab = await this.getTab(tabId);
      if (tab?.url && tab.url !== "about:blank") {
        try {
          const readyState = await this.evaluate(
            tabId,
            "document.readyState",
            {
              timeoutMs: 5000,
              retries: 1,
            }
          );

          if (readyState === "interactive" || readyState === "complete") {
            return {
              tab,
              readyState,
            };
          }
        } catch {
          // Continue polling until the page can execute.
        }
      }

      await sleep(pollIntervalMs);
    }

    const tab = await this.getTab(tabId);
    return {
      tab,
      readyState: null,
    };
  }
}
