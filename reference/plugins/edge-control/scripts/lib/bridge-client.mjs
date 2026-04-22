import { sleep } from "./async.mjs";
import { getBridgeHttpBaseUrl, loadConfig } from "./config.mjs";

function isRetryableBridgeMessage(message) {
  return /extension is not connected|timed out|unreachable|503|socket/i.test(
    String(message || "")
  );
}

export class EdgeBridgeClient {
  constructor(options = {}) {
    this.config = options.config || loadConfig();
    this.baseUrl = options.baseUrl || getBridgeHttpBaseUrl(this.config);
    this.defaultRetries = options.defaultRetries ?? 4;
    this.defaultRetryDelayMs = options.defaultRetryDelayMs ?? 500;
  }

  async request(path, options = {}) {
    if (!this.config.authToken) {
      throw new Error("Edge Control is not configured. Run install.ps1 first.");
    }

    let response;
    try {
      response = await fetch(`${this.baseUrl}${path}`, {
        ...options,
        headers: {
          "Content-Type": "application/json",
          "x-edge-control-token": this.config.authToken,
          ...(options.headers || {}),
        },
      });
    } catch (error) {
      throw new Error(
        `Edge Control bridge is unreachable at ${this.baseUrl}. ${error?.message || String(error)}`
      );
    }

    const payload = await response.json();
    if (!response.ok || payload.ok === false) {
      throw new Error(payload.error || `Bridge request failed with status ${response.status}.`);
    }
    return payload;
  }

  async getStatus() {
    return this.request("/api/status", { method: "GET" });
  }

  async waitForExtension(options = {}) {
    const timeoutMs = options.timeoutMs ?? 30000;
    const pollIntervalMs = options.pollIntervalMs ?? 500;
    const startedAt = Date.now();

    while (Date.now() - startedAt < timeoutMs) {
      const status = await this.getStatus();
      if (status.bridge?.connectedExtension) {
        return status;
      }
      await sleep(pollIntervalMs);
    }

    throw new Error(`Edge extension did not connect within ${timeoutMs}ms.`);
  }

  async command(command, args = {}, options = {}) {
    const retries = options.retries ?? this.defaultRetries;
    const timeoutMs = options.timeoutMs ?? 15000;
    const retryDelayMs = options.retryDelayMs ?? this.defaultRetryDelayMs;

    let lastError;
    for (let attempt = 0; attempt <= retries; attempt += 1) {
      try {
        const payload = await this.request("/api/command", {
          method: "POST",
          body: JSON.stringify({ command, args, timeoutMs }),
        });
        return payload.result;
      } catch (error) {
        lastError = error;
        if (
          attempt >= retries ||
          !isRetryableBridgeMessage(error?.message || String(error))
        ) {
          throw error;
        }

        await sleep(retryDelayMs * (attempt + 1));
      }
    }

    throw lastError;
  }
}
