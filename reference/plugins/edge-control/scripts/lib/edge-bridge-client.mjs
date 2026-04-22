import { getBridgeHttpBaseUrl, loadConfig } from "./config.mjs";
import { pickDefined } from "./crawl-utils.mjs";

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function isRetryableBridgeMessage(message) {
  return /bridge is unreachable|extension is not connected|extension is connected but|heartbeat timed out|handshake|503|socket|replaced/i.test(
    String(message || "")
  );
}

function shouldWaitForExtension(message) {
  return /extension is not connected|extension is connected but|heartbeat timed out|handshake|replaced/i.test(
    String(message || "")
  );
}

function summarizeBridgeStatus(status) {
  const bridge = status?.bridge || {};
  const extension = status?.extension || status?.lastExtension || null;

  if (bridge.healthyExtension) {
    return "healthy";
  }

  if (bridge.readyExtension) {
    return "ready";
  }

  if (bridge.connectedExtension) {
    const handshakeState = extension?.handshakePending ? "handshake-pending" : "connected";
    return `${handshakeState} (${extension?.socketState || "unknown-socket"})`;
  }

  if (status?.lastExtension?.disconnectReason || status?.lastExtension?.closeCode != null) {
    const details = [];
    if (typeof status.lastExtension.closeCode === "number") {
      details.push(`code ${status.lastExtension.closeCode}`);
    }
    if (status.lastExtension.disconnectReason) {
      details.push(status.lastExtension.disconnectReason);
    }
    return `disconnected (${details.join(", ")})`;
  }

  return "disconnected";
}

export class EdgeBridgeClient {
  constructor({
    config = loadConfig(),
    fetchImpl = globalThis.fetch,
    defaultRetries = 3,
    defaultRetryDelayMs = 400,
    defaultExtensionWaitMs = 12000,
    defaultStatusPollIntervalMs = 500,
  } = {}) {
    this.config = config;
    this.fetchImpl = fetchImpl;
    this.defaultRetries = defaultRetries;
    this.defaultRetryDelayMs = defaultRetryDelayMs;
    this.defaultExtensionWaitMs = defaultExtensionWaitMs;
    this.defaultStatusPollIntervalMs = defaultStatusPollIntervalMs;
  }

  get baseUrl() {
    return getBridgeHttpBaseUrl(this.config);
  }

  async bridgeFetch(path, options = {}) {
    if (!this.config.authToken) {
      throw new Error("Edge Control is not configured. Run install.ps1 first.");
    }

    let response;
    try {
      response = await this.fetchImpl(`${this.baseUrl}${path}`, {
        ...options,
        headers: {
          "Content-Type": "application/json",
          "x-edge-control-token": this.config.authToken,
          ...(options.headers || {}),
        },
      });
    } catch (error) {
      throw new Error(
        `Edge Control bridge is unreachable at ${this.baseUrl}. Start it with start-host.ps1 and ensure the Edge extension is loaded. Root error: ${error?.message || String(error)}`
      );
    }

    const rawBody = await response.text();
    let payload;
    try {
      payload = rawBody ? JSON.parse(rawBody) : {};
    } catch (error) {
      throw new Error(
        `Bridge request to ${path} returned invalid JSON with status ${response.status}. Root error: ${error?.message || String(error)}`
      );
    }

    if (!response.ok || payload.ok === false) {
      const message = payload.error || `Bridge request failed with status ${response.status}.`;
      const error = new Error(message);
      error.statusCode = response.status;
      error.bridgePayload = payload;
      throw error;
    }
    return payload;
  }

  async status() {
    return this.bridgeFetch("/api/status", { method: "GET" });
  }

  async waitForHealthyExtension({
    timeoutMs = this.defaultExtensionWaitMs,
    pollIntervalMs = this.defaultStatusPollIntervalMs,
  } = {}) {
    const startedAt = Date.now();
    let lastStatus = null;
    let lastError = null;

    while (Date.now() - startedAt < timeoutMs) {
      try {
        lastStatus = await this.status();
        if (lastStatus?.bridge?.healthyExtension) {
          return lastStatus;
        }
      } catch (error) {
        lastError = error;
        if (!isRetryableBridgeMessage(error?.message || String(error))) {
          throw error;
        }
      }

      await sleep(pollIntervalMs);
    }

    const summary = lastStatus ? summarizeBridgeStatus(lastStatus) : "unknown";
    const lastMessage = lastError?.message ? ` Last error: ${lastError.message}` : "";
    throw new Error(`Edge extension did not become healthy within ${timeoutMs}ms. Last bridge status: ${summary}.${lastMessage}`);
  }

  async waitForReloadedExtension({
    previousSessionId = null,
    timeoutMs = this.defaultExtensionWaitMs,
    pollIntervalMs = this.defaultStatusPollIntervalMs,
  } = {}) {
    const startedAt = Date.now();
    let lastStatus = null;
    let lastError = null;
    let sawSessionChange = false;
    let sawDisconnect = false;

    while (Date.now() - startedAt < timeoutMs) {
      try {
        lastStatus = await this.status();
        const currentSessionId = lastStatus?.extension?.sessionId || null;
        if (!lastStatus?.bridge?.connectedExtension) {
          sawDisconnect = true;
        }
        if (previousSessionId && currentSessionId && currentSessionId !== previousSessionId) {
          sawSessionChange = true;
        }
        if (
          lastStatus?.bridge?.healthyExtension
          && (
            !previousSessionId
            || sawSessionChange
            || sawDisconnect
          )
        ) {
          return lastStatus;
        }
      } catch (error) {
        lastError = error;
        if (!isRetryableBridgeMessage(error?.message || String(error))) {
          throw error;
        }
        sawDisconnect = true;
      }

      await sleep(pollIntervalMs);
    }

    const summary = lastStatus ? summarizeBridgeStatus(lastStatus) : "unknown";
    const lastMessage = lastError?.message ? ` Last error: ${lastError.message}` : "";
    throw new Error(`Edge extension did not finish reloading within ${timeoutMs}ms. Last bridge status: ${summary}.${lastMessage}`);
  }

  async command(command, args = {}, timeoutMs = 15000, options = {}) {
    const retries = Number.isInteger(options.retries) ? options.retries : this.defaultRetries;
    const retryDelayMs = Number.isFinite(options.retryDelayMs) ? options.retryDelayMs : this.defaultRetryDelayMs;
    const waitForExtensionMs = Number.isFinite(options.waitForExtensionMs)
      ? options.waitForExtensionMs
      : this.defaultExtensionWaitMs;
    const statusPollIntervalMs = Number.isFinite(options.statusPollIntervalMs)
      ? options.statusPollIntervalMs
      : this.defaultStatusPollIntervalMs;

    let lastError = null;

    for (let attempt = 0; attempt <= retries; attempt += 1) {
      try {
        const payload = await this.bridgeFetch("/api/command", {
          method: "POST",
          body: JSON.stringify({ command, args, timeoutMs }),
        });
        return payload.result;
      } catch (error) {
        lastError = error;
        const message = error?.message || String(error);
        if (attempt >= retries || !isRetryableBridgeMessage(message)) {
          throw error;
        }

        if (shouldWaitForExtension(message)) {
          try {
            await this.waitForHealthyExtension({
              timeoutMs: waitForExtensionMs,
              pollIntervalMs: statusPollIntervalMs,
            });
            continue;
          } catch (waitError) {
            lastError = waitError;
          }
        }

        await sleep(retryDelayMs * (attempt + 1));
      }
    }

    throw lastError || new Error(`Failed to send Edge bridge command ${command}.`);
  }

  async listTabs(options = {}) {
    return this.command("list_tabs", pickDefined(options));
  }

  async navigate({ url, tabId, windowId, createNewTab = false, active = false, timeoutMs = 15000 }) {
    return this.command("navigate", pickDefined({ url, tabId, windowId, createNewTab, active }), timeoutMs);
  }

  async reload({ tabId, bypassCache = false, timeoutMs = 15000 }) {
    return this.command("reload", pickDefined({ tabId, bypassCache }), timeoutMs);
  }

  async sendCdp({ tabId, method, params = {}, detachAfter = false, timeoutMs = 15000 }) {
    return this.command("send_cdp", pickDefined({ tabId, method, params, detachAfter }), timeoutMs);
  }

  async startNetworkLog({
    tabId,
    maxEntries,
    maxBodies,
    maxBodyBytes,
    resourceTypes,
    bodyUrlIncludes,
    urlIncludes,
    captureBodies = true,
    clear = true,
    timeoutMs = 15000,
  } = {}) {
    return this.command("start_network_log", pickDefined({
      tabId,
      maxEntries,
      maxBodies,
      maxBodyBytes,
      resourceTypes,
      bodyUrlIncludes,
      urlIncludes,
      captureBodies,
      clear,
    }), timeoutMs);
  }

  async readNetworkLog({
    tabId,
    sinceSequence,
    maxEntries,
    includeBodies = true,
    resourceTypes,
    urlIncludes,
    consume = false,
    timeoutMs = 15000,
  } = {}) {
    return this.command("read_network_log", pickDefined({
      tabId,
      sinceSequence,
      maxEntries,
      includeBodies,
      resourceTypes,
      urlIncludes,
      consume,
    }), timeoutMs);
  }

  async getNetworkLogMark({
    tabId,
    timeoutMs = 15000,
  } = {}) {
    return this.command("get_network_log_mark", pickDefined({
      tabId,
    }), timeoutMs);
  }

  async reloadExtension({
    delayMs = 300,
    timeoutMs = 5000,
    waitForHealthyMs = 20000,
    pollIntervalMs = this.defaultStatusPollIntervalMs,
  } = {}) {
    const before = await this.status().catch(() => null);
    const previousSessionId = before?.extension?.sessionId || null;
    let request = null;
    let requestError = null;

    try {
      request = await this.command("reload_extension", pickDefined({
        delayMs,
      }), timeoutMs, {
        retries: 0,
      });
    } catch (error) {
      requestError = error;
      if (!isRetryableBridgeMessage(error?.message || String(error))) {
        throw error;
      }
    }

    const after = await this.waitForReloadedExtension({
      previousSessionId,
      timeoutMs: waitForHealthyMs,
      pollIntervalMs,
    });
    const runtime = await this.command("get_status", {}, timeoutMs, {
      retries: 0,
    }).catch(() => null);

    return {
      reloaded: true,
      request,
      requestError: requestError?.message || null,
      before,
      after,
      runtime,
    };
  }

  async stopNetworkLog({
    tabId,
    clear = false,
    detachIfIdle = false,
    timeoutMs = 15000,
  } = {}) {
    return this.command("stop_network_log", pickDefined({
      tabId,
      clear,
      detachIfIdle,
    }), timeoutMs);
  }

  async blankTab(tabId, timeoutMs = 15000) {
    return this.navigate({
      tabId,
      url: "about:blank",
      createNewTab: false,
      active: false,
      timeoutMs,
    });
  }

  async closeTab(tabId, timeoutMs = 5000) {
    return this.sendCdp({
      tabId,
      method: "Page.close",
      params: {},
      detachAfter: true,
      timeoutMs,
    });
  }
}
