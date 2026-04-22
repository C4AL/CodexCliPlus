import crypto from "node:crypto";
import http from "node:http";
import { WebSocketServer } from "ws";
import { getBridgeHttpBaseUrl, loadConfig } from "./lib/config.mjs";

const HEARTBEAT_INTERVAL_MS = 15000;
const STALE_EXTENSION_MS = HEARTBEAT_INTERVAL_MS * 3;
const HELLO_TIMEOUT_MS = 10000;
const CLOSE_REASON_LIMIT = 120;
const WS_OPEN = 1;

const config = loadConfig();

if (!config.authToken) {
  console.error("Edge Control bridge is not configured. Run install.ps1 first.");
  process.exit(1);
}

const state = {
  bridgeStartedAt: new Date().toISOString(),
  bridgeSessionId: crypto.randomUUID(),
  extension: null,
  lastExtension: null,
  pending: new Map(),
  stats: {
    totalConnections: 0,
    totalDisconnects: 0,
    totalReplacements: 0,
    totalCommands: 0,
    totalRejectedCommands: 0,
    totalCommandTimeouts: 0,
    totalStaleDisconnects: 0,
    totalHandshakeTimeouts: 0,
    totalHandshakeFailures: 0,
  },
};

const server = http.createServer(async (req, res) => {
  try {
    if (!isLocalRequest(req)) {
      respondJson(res, 403, { ok: false, error: "Only localhost clients are allowed." });
      return;
    }

    if (req.url === "/api/status" && req.method === "GET") {
      if (!authenticateHttp(req)) {
        respondJson(res, 401, { ok: false, error: "Invalid token." });
        return;
      }

      reapUnhealthyExtension("status-check");
      respondJson(res, 200, buildStatusPayload());
      return;
    }

    if (req.url === "/api/command" && req.method === "POST") {
      if (!authenticateHttp(req)) {
        respondJson(res, 401, { ok: false, error: "Invalid token." });
        return;
      }

      const body = await readJsonBody(req);
      if (!body?.command) {
        respondJson(res, 400, { ok: false, error: "Missing command." });
        return;
      }

      reapUnhealthyExtension("before-command");
      if (!isExtensionHealthy(state.extension)) {
        state.stats.totalRejectedCommands += 1;
        respondJson(res, 503, { ok: false, error: buildExtensionUnavailableMessage() });
        return;
      }

      const result = await sendCommand(body.command, body.args || {}, Number(body.timeoutMs) || 15000);
      respondJson(res, 200, { ok: true, result });
      return;
    }

    respondJson(res, 404, { ok: false, error: "Not found." });
  } catch (error) {
    respondJson(res, 500, { ok: false, error: error?.message || String(error) });
  }
});

const wss = new WebSocketServer({ noServer: true });

server.on("upgrade", (req, socket, head) => {
  try {
    const url = new URL(req.url, `http://${req.headers.host}`);
    if (!isLocalRequest(req)) {
      socket.write("HTTP/1.1 403 Forbidden\r\n\r\n");
      socket.destroy();
      return;
    }
    if (url.pathname !== (config.bridgePath || "/bridge")) {
      socket.write("HTTP/1.1 404 Not Found\r\n\r\n");
      socket.destroy();
      return;
    }
    if (url.searchParams.get("token") !== config.authToken) {
      socket.write("HTTP/1.1 401 Unauthorized\r\n\r\n");
      socket.destroy();
      return;
    }
    if (url.searchParams.get("role") !== "edge-extension") {
      socket.write("HTTP/1.1 400 Bad Request\r\n\r\n");
      socket.destroy();
      return;
    }

    wss.handleUpgrade(req, socket, head, (ws) => {
      wss.emit("connection", ws, req, url);
    });
  } catch {
    socket.destroy();
  }
});

wss.on("connection", (ws, _req, url) => {
  const replacedExtension = state.extension;
  const extension = createExtensionState(ws, url);

  state.extension = extension;
  state.stats.totalConnections += 1;

  if (replacedExtension) {
    state.stats.totalReplacements += 1;
    retireExtension(replacedExtension, {
      closeCode: 4000,
      closeReason: "Replaced by newer extension connection",
      rejectMessage: "Edge extension was replaced by a newer connection.",
      disconnectReason: "replaced",
    });
  }

  startExtensionHelloTimeout(extension);
  startExtensionHeartbeat(extension);

  ws.on("message", (buffer) => {
    handleExtensionMessage(extension, buffer);
  });

  ws.on("close", (code, reasonBuffer) => {
    extension.closeCode = code ?? null;
    extension.closeReason = decodeCloseReason(reasonBuffer);
    retireExtension(extension, {
      closeSocket: false,
      rejectMessage: buildDisconnectMessage(extension),
      disconnectReason: extension.closeReason || "socket-closed",
    });
  });

  ws.on("error", (error) => {
    extension.lastErrorAt = new Date().toISOString();
    extension.errorMessage = error?.message || String(error);
    safeCloseSocket(ws, 1011, "Socket error");
  });
});

server.listen(config.port, config.host, () => {
  console.log(`Edge Control bridge listening on ${getBridgeHttpBaseUrl(config)}`);
});

function createExtensionState(ws, url) {
  const connectedAt = new Date().toISOString();
  return {
    socket: ws,
    sessionId: crypto.randomUUID(),
    connectedAt,
    lastSeenAt: connectedAt,
    lastHelloAt: null,
    lastHelloAckSentAt: null,
    lastPingAt: null,
    lastPongAt: null,
    lastBridgePingAt: null,
    lastCommandAt: null,
    lastResponseAt: null,
    lastErrorAt: null,
    errorMessage: null,
    readyAt: null,
    helloTimeoutAt: null,
    closeCode: null,
    closeReason: null,
    disconnectReason: null,
    retiredAt: null,
    helloTimeoutTimer: null,
    heartbeatTimer: null,
    meta: {
      runtimeId: url.searchParams.get("runtimeId") || null,
      version: url.searchParams.get("version") || null,
    },
  };
}

function authenticateHttp(req) {
  return req.headers["x-edge-control-token"] === config.authToken;
}

function isLocalRequest(req) {
  const address = req.socket.remoteAddress || "";
  return address === "127.0.0.1" || address === "::1" || address === "::ffff:127.0.0.1";
}

function readJsonBody(req) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    req.on("data", (chunk) => chunks.push(chunk));
    req.on("end", () => {
      if (chunks.length === 0) {
        resolve({});
        return;
      }
      try {
        resolve(JSON.parse(Buffer.concat(chunks).toString("utf8")));
      } catch (error) {
        reject(error);
      }
    });
    req.on("error", reject);
  });
}

function respondJson(res, statusCode, payload) {
  res.writeHead(statusCode, {
    "Content-Type": "application/json; charset=utf-8",
    "Cache-Control": "no-store",
  });
  res.end(JSON.stringify(payload));
}

function buildStatusPayload() {
  const currentExtension = serializeExtension(state.extension);
  return {
    ok: true,
    bridge: {
      baseUrl: getBridgeHttpBaseUrl(config),
      startedAt: state.bridgeStartedAt,
      sessionId: state.bridgeSessionId,
      connectedExtension: Boolean(currentExtension),
      readyExtension: currentExtension?.ready ?? false,
      healthyExtension: currentExtension?.healthy ?? false,
      connectionState: getBridgeConnectionState(currentExtension),
      heartbeatIntervalMs: HEARTBEAT_INTERVAL_MS,
      staleAfterMs: STALE_EXTENSION_MS,
      helloTimeoutMs: HELLO_TIMEOUT_MS,
      pendingRequests: state.pending.size,
      pendingBySession: countPendingBySession(),
      lastDisconnectSummary: summarizeDisconnect(state.lastExtension),
      stats: state.stats,
    },
    extension: currentExtension,
    lastExtension: state.lastExtension,
  };
}

function countPendingBySession() {
  const counts = {};
  for (const pending of state.pending.values()) {
    const key = pending.extensionSessionId || "unknown";
    counts[key] = (counts[key] || 0) + 1;
  }
  return counts;
}

function serializeExtension(extension) {
  if (!extension) {
    return null;
  }

  return {
    sessionId: extension.sessionId,
    connectedAt: extension.connectedAt,
    readyAt: extension.readyAt,
    retiredAt: extension.retiredAt,
    lastSeenAt: extension.lastSeenAt,
    lastSeenAgeMs: getAgeMs(extension.lastSeenAt),
    lastHelloAt: extension.lastHelloAt,
    lastHelloAckSentAt: extension.lastHelloAckSentAt,
    lastPingAt: extension.lastPingAt,
    lastPongAt: extension.lastPongAt,
    lastBridgePingAt: extension.lastBridgePingAt,
    lastCommandAt: extension.lastCommandAt,
    lastResponseAt: extension.lastResponseAt,
    lastErrorAt: extension.lastErrorAt,
    errorMessage: extension.errorMessage,
    helloTimeoutAt: extension.helloTimeoutAt,
    helloTimeoutRemainingMs: getRemainingMs(extension.helloTimeoutAt),
    closeCode: extension.closeCode,
    closeReason: extension.closeReason,
    disconnectReason: extension.disconnectReason,
    ready: isExtensionReady(extension),
    handshakePending: isExtensionHandshakePending(extension),
    healthy: isExtensionHealthy(extension),
    stale: isExtensionStale(extension),
    socketState: getSocketState(extension.socket),
    meta: extension.meta,
  };
}

function getAgeMs(timestamp) {
  if (!timestamp) {
    return null;
  }
  const value = Date.parse(timestamp);
  if (!Number.isFinite(value)) {
    return null;
  }
  return Math.max(0, Date.now() - value);
}

function getRemainingMs(timestamp) {
  if (!timestamp) {
    return null;
  }
  const value = Date.parse(timestamp);
  if (!Number.isFinite(value)) {
    return null;
  }
  return Math.max(0, value - Date.now());
}

function getSocketState(socket) {
  if (!socket) {
    return "closed";
  }

  switch (socket.readyState) {
    case 0:
      return "connecting";
    case 1:
      return "open";
    case 2:
      return "closing";
    default:
      return "closed";
  }
}

function isExtensionStale(extension) {
  if (!extension || extension.retiredAt) {
    return false;
  }

  const lastSeenValue = Date.parse(extension.lastSeenAt || extension.connectedAt);
  if (!Number.isFinite(lastSeenValue)) {
    return true;
  }

  return Date.now() - lastSeenValue > STALE_EXTENSION_MS;
}

function isExtensionReady(extension) {
  return Boolean(extension && !extension.retiredAt && extension.readyAt);
}

function isExtensionHandshakePending(extension) {
  return Boolean(extension && !extension.retiredAt && !extension.readyAt);
}

function isExtensionHandshakeTimedOut(extension) {
  if (!isExtensionHandshakePending(extension)) {
    return false;
  }

  const timeoutValue = Date.parse(extension.helloTimeoutAt || "");
  if (!Number.isFinite(timeoutValue)) {
    return false;
  }

  return Date.now() >= timeoutValue;
}

function isExtensionHealthy(extension) {
  return Boolean(
    extension
      && !extension.retiredAt
      && isExtensionReady(extension)
      && extension.socket
      && extension.socket.readyState === WS_OPEN
      && !isExtensionStale(extension),
  );
}

function getBridgeConnectionState(currentExtension) {
  if (!currentExtension) {
    return "disconnected";
  }
  if (currentExtension.healthy) {
    return "healthy";
  }
  if (currentExtension.handshakePending) {
    return "handshake-pending";
  }
  if (currentExtension.ready) {
    return "connected-unhealthy";
  }
  return "connected";
}

function summarizeDisconnect(extension) {
  if (!extension) {
    return null;
  }

  const parts = [];
  if (typeof extension.closeCode === "number") {
    parts.push(`code ${extension.closeCode}`);
  }
  if (extension.closeReason) {
    parts.push(extension.closeReason);
  }
  if (extension.disconnectReason) {
    parts.push(extension.disconnectReason);
  }

  return parts.length ? parts.join(", ") : "disconnected";
}

function composeDisconnectReason(source, reason) {
  if (!source || source === reason) {
    return reason;
  }
  return `${source}:${reason}`;
}

function reapUnhealthyExtension(source) {
  const extension = state.extension;
  if (!extension) {
    return;
  }

  if (isExtensionHandshakeTimedOut(extension)) {
    state.stats.totalHandshakeTimeouts += 1;
    retireExtension(extension, {
      closeCode: 4003,
      closeReason: "Handshake timeout",
      rejectMessage: "Edge extension handshake timed out.",
      disconnectReason: composeDisconnectReason(source, "handshake-timeout"),
    });
    return;
  }

  if (!isExtensionStale(extension)) {
    return;
  }

  state.stats.totalStaleDisconnects += 1;
  retireExtension(extension, {
    closeCode: 4001,
    closeReason: "Heartbeat timeout",
    rejectMessage: "Edge extension heartbeat timed out.",
    disconnectReason: composeDisconnectReason(source, "heartbeat-timeout"),
  });
}

function startExtensionHelloTimeout(extension) {
  stopExtensionHelloTimeout(extension);
  if (!extension || extension.retiredAt || extension.readyAt) {
    return;
  }

  extension.helloTimeoutAt = new Date(Date.now() + HELLO_TIMEOUT_MS).toISOString();
  extension.helloTimeoutTimer = setTimeout(() => {
    if (!extension.retiredAt && !extension.readyAt) {
      state.stats.totalHandshakeTimeouts += 1;
      retireExtension(extension, {
        closeCode: 4003,
        closeReason: "Handshake timeout",
        rejectMessage: "Edge extension handshake timed out.",
        disconnectReason: "handshake-timeout",
      });
    }
  }, HELLO_TIMEOUT_MS);
}

function stopExtensionHelloTimeout(extension) {
  if (extension?.helloTimeoutTimer) {
    clearTimeout(extension.helloTimeoutTimer);
    extension.helloTimeoutTimer = null;
  }
  if (extension) {
    extension.helloTimeoutAt = null;
  }
}

function startExtensionHeartbeat(extension) {
  stopExtensionHeartbeat(extension);
  extension.heartbeatTimer = setInterval(() => {
    if (extension.retiredAt) {
      stopExtensionHeartbeat(extension);
      return;
    }

    if (!extension.socket || extension.socket.readyState !== WS_OPEN) {
      retireExtension(extension, {
        closeSocket: false,
        rejectMessage: buildDisconnectMessage(extension),
        disconnectReason: "socket-not-open",
      });
      return;
    }

    if (isExtensionHandshakeTimedOut(extension)) {
      state.stats.totalHandshakeTimeouts += 1;
      retireExtension(extension, {
        closeCode: 4003,
        closeReason: "Handshake timeout",
        rejectMessage: "Edge extension handshake timed out.",
        disconnectReason: "heartbeat:handshake-timeout",
      });
      return;
    }

    if (isExtensionStale(extension)) {
      state.stats.totalStaleDisconnects += 1;
      retireExtension(extension, {
        closeCode: 4001,
        closeReason: "Heartbeat timeout",
        rejectMessage: "Edge extension heartbeat timed out.",
        disconnectReason: "heartbeat:heartbeat-timeout",
      });
      return;
    }

    extension.lastBridgePingAt = new Date().toISOString();
    if (!trySendToExtension(extension, {
      type: "ping",
      pingId: crypto.randomUUID(),
      bridgeSessionId: state.bridgeSessionId,
      extensionSessionId: extension.sessionId,
      sentAt: extension.lastBridgePingAt,
      heartbeatIntervalMs: HEARTBEAT_INTERVAL_MS,
      staleAfterMs: STALE_EXTENSION_MS,
    })) {
      retireExtension(extension, {
        closeCode: 1011,
        closeReason: "Heartbeat send failed",
        rejectMessage: "Edge extension heartbeat send failed.",
        disconnectReason: "heartbeat-send-failed",
      });
    }
  }, HEARTBEAT_INTERVAL_MS);
}

function stopExtensionHeartbeat(extension) {
  if (extension?.heartbeatTimer) {
    clearInterval(extension.heartbeatTimer);
    extension.heartbeatTimer = null;
  }
}

function handleExtensionMessage(extension, buffer) {
  try {
    const message = JSON.parse(buffer.toString("utf8"));
    extension.lastSeenAt = new Date().toISOString();

    if (message.type === "hello") {
      extension.lastHelloAt = extension.lastSeenAt;
      extension.readyAt = extension.readyAt || extension.lastSeenAt;
      stopExtensionHelloTimeout(extension);
      extension.meta = {
        ...extension.meta,
        ...(message.extension || {}),
        bridgeConnectReason: message.bridgeConnectReason || null,
        extensionHeartbeatIntervalMs: message.heartbeatIntervalMs || null,
      };

      extension.lastHelloAckSentAt = new Date().toISOString();
      if (!trySendToExtension(extension, {
        type: "hello_ack",
        bridgeSessionId: state.bridgeSessionId,
        extensionSessionId: extension.sessionId,
        connectedAt: extension.connectedAt,
        readyAt: extension.readyAt,
        heartbeatIntervalMs: HEARTBEAT_INTERVAL_MS,
        staleAfterMs: STALE_EXTENSION_MS,
        helloTimeoutMs: HELLO_TIMEOUT_MS,
      })) {
        state.stats.totalHandshakeFailures += 1;
        retireExtension(extension, {
          closeCode: 1011,
          closeReason: "hello_ack send failed",
          rejectMessage: "Edge extension disconnected during handshake.",
          disconnectReason: "handshake-send-failed",
        });
      }
      return;
    }

    if (message.type === "ping") {
      extension.lastPingAt = extension.lastSeenAt;
      if (!trySendToExtension(extension, {
        type: "pong",
        pingId: message.pingId || null,
        bridgeSessionId: state.bridgeSessionId,
        extensionSessionId: extension.sessionId,
        sentAt: extension.lastPingAt,
      })) {
        retireExtension(extension, {
          closeCode: 1011,
          closeReason: "pong send failed",
          rejectMessage: "Edge extension heartbeat response failed.",
          disconnectReason: "pong-send-failed",
        });
      }
      return;
    }

    if (message.type === "pong") {
      extension.lastPongAt = extension.lastSeenAt;
      return;
    }

    if (message.type === "response" && message.requestId) {
      const pending = state.pending.get(message.requestId);
      if (!pending) {
        return;
      }
      if (pending.extensionSessionId !== extension.sessionId) {
        return;
      }

      clearTimeout(pending.timeout);
      state.pending.delete(message.requestId);
      extension.lastResponseAt = extension.lastSeenAt;
      if (message.ok) {
        pending.resolve(message.result);
      } else {
        pending.reject(new Error(message.error?.message || "Unknown extension error"));
      }
    }
  } catch {
    // Ignore malformed messages from the extension.
  }
}

function sendCommand(command, args, timeoutMs) {
  return new Promise((resolve, reject) => {
    reapUnhealthyExtension("send-command");
    const extension = state.extension;
    if (!isExtensionHealthy(extension)) {
      reject(new Error(buildExtensionUnavailableMessage()));
      return;
    }

    const requestId = crypto.randomUUID();
    const boundedTimeoutMs = Number.isFinite(timeoutMs) && timeoutMs > 0 ? timeoutMs : 15000;
    const timeout = setTimeout(() => {
      const pending = state.pending.get(requestId);
      if (!pending) {
        return;
      }

      state.stats.totalCommandTimeouts += 1;
      state.pending.delete(requestId);
      pending.reject(new Error(`Timed out waiting for Edge extension response to ${command}.`));
    }, boundedTimeoutMs);

    state.pending.set(requestId, {
      resolve,
      reject,
      timeout,
      createdAt: new Date().toISOString(),
      command,
      extensionSessionId: extension.sessionId,
    });

    state.stats.totalCommands += 1;
    extension.lastCommandAt = new Date().toISOString();

    try {
      sendToExtension(extension, {
        type: "command",
        requestId,
        command,
        args,
        bridgeSessionId: state.bridgeSessionId,
        extensionSessionId: extension.sessionId,
        sentAt: extension.lastCommandAt,
      });
    } catch (error) {
      clearTimeout(timeout);
      state.pending.delete(requestId);
      reject(error);
    }
  });
}

function retireExtension(extension, options = {}) {
  if (!extension || extension.retiredAt) {
    return;
  }

  stopExtensionHelloTimeout(extension);
  stopExtensionHeartbeat(extension);

  extension.retiredAt = new Date().toISOString();
  extension.disconnectReason = options.disconnectReason || extension.disconnectReason || null;

  if (typeof options.closeCode === "number" && extension.closeCode == null) {
    extension.closeCode = options.closeCode;
  }

  if (options.closeReason && !extension.closeReason) {
    extension.closeReason = options.closeReason;
  }

  if (state.extension === extension) {
    state.extension = null;
  }

  state.stats.totalDisconnects += 1;
  state.lastExtension = serializeExtension(extension);
  failPendingForSession(extension.sessionId, options.rejectMessage || "Edge extension disconnected.");

  if (options.closeSocket !== false) {
    safeCloseSocket(extension.socket, extension.closeCode ?? 1000, extension.closeReason || "Closing connection");
  }
}

function failPendingForSession(extensionSessionId, message) {
  for (const [requestId, pending] of state.pending.entries()) {
    if (pending.extensionSessionId !== extensionSessionId) {
      continue;
    }

    clearTimeout(pending.timeout);
    pending.reject(new Error(message));
    state.pending.delete(requestId);
  }
}

function sendToExtension(extension, payload) {
  if (!extension?.socket || extension.socket.readyState !== WS_OPEN) {
    throw new Error("Edge extension socket is not open.");
  }

  extension.socket.send(JSON.stringify(payload));
}

function trySendToExtension(extension, payload) {
  try {
    sendToExtension(extension, payload);
    return true;
  } catch (error) {
    extension.lastErrorAt = new Date().toISOString();
    extension.errorMessage = error?.message || String(error);
    return false;
  }
}

function safeCloseSocket(socket, code, reason) {
  if (!socket || socket.readyState >= 2) {
    return;
  }

  try {
    socket.close(code, truncateCloseReason(reason));
  } catch {
    try {
      socket.close();
    } catch {
      // Best effort close.
    }
  }
}

function truncateCloseReason(reason) {
  return String(reason || "").slice(0, CLOSE_REASON_LIMIT);
}

function decodeCloseReason(reasonBuffer) {
  if (typeof reasonBuffer === "string") {
    return reasonBuffer;
  }
  if (Buffer.isBuffer(reasonBuffer)) {
    return reasonBuffer.toString("utf8");
  }
  return reasonBuffer ? String(reasonBuffer) : null;
}

function buildExtensionUnavailableMessage() {
  const extension = state.extension;
  if (!extension) {
    const lastDisconnect = summarizeDisconnect(state.lastExtension);
    return lastDisconnect
      ? `Edge extension is not connected. Last disconnect: ${lastDisconnect}.`
      : "Edge extension is not connected.";
  }

  if (isExtensionHandshakePending(extension)) {
    const remainingMs = getRemainingMs(extension.helloTimeoutAt);
    if (remainingMs != null) {
      return `Edge extension is connected but handshake is still pending. Hello timeout in ${remainingMs}ms.`;
    }
    return "Edge extension is connected but handshake is still pending.";
  }

  if (isExtensionStale(extension)) {
    return "Edge extension is connected but heartbeat is stale.";
  }

  const socketState = getSocketState(extension.socket);
  return `Edge extension is connected but not healthy. Socket state: ${socketState}.`;
}

function buildDisconnectMessage(extension) {
  if (!extension) {
    return "Edge extension disconnected.";
  }

  const closeDetails = [];
  if (typeof extension.closeCode === "number") {
    closeDetails.push(`code ${extension.closeCode}`);
  }
  if (extension.closeReason) {
    closeDetails.push(extension.closeReason);
  }

  if (closeDetails.length === 0) {
    return "Edge extension disconnected.";
  }

  return `Edge extension disconnected (${closeDetails.join(", ")}).`;
}
