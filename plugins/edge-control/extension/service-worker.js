importScripts("config.local.js");

const DEFAULT_BRIDGE_URL = "ws://127.0.0.1:47173/bridge";
const RECONNECT_BASE_MS = 1000;
const RECONNECT_MAX_MS = 15000;
const CONNECT_TIMEOUT_MS = 10000;
const HEARTBEAT_INTERVAL_MS = 15000;
const HEARTBEAT_STALE_MS = HEARTBEAT_INTERVAL_MS * 3;
const HELLO_RETRY_MS = 2000;
const HELLO_ACK_TIMEOUT_MS = 10000;
const KEEPALIVE_ALARM = "edge-control-bridge-keepalive";
const DEBUGGER_VERSION = "1.3";
const CLOSE_REASON_LIMIT = 120;
const DEFAULT_NETWORK_LOG_MAX_ENTRIES = 160;
const DEFAULT_NETWORK_LOG_MAX_BODIES = 24;
const DEFAULT_NETWORK_LOG_MAX_BODY_BYTES = 240000;
const DEFAULT_NETWORK_LOG_RESOURCE_TYPES = ["XHR", "Fetch"];
const NETWORK_BODY_MIME_PATTERN = /(json|javascript|graphql|text\/plain|text\/html|xml)/i;
const NETWORK_BODY_URL_PATTERN = /(api|graphql|comment|review|reply|discussion|thread|post|feed|ajax|json|data)/i;

let bridgeSocket = null;
let reconnectTimer = null;
let connectTimeoutTimer = null;
let heartbeatTimer = null;
let helloTimer = null;
let activeSocketGeneration = 0;
let warnedMissingAuthToken = false;
let runtimeReloadTimer = null;
let runtimeReloadScheduledAt = null;
const attachedTabs = new Set();
const debuggerAttachPromises = new Map();
const networkLogStates = new Map();

const connectionState = {
  bridgeSessionId: null,
  bridgeConnectionId: null,
  socketGeneration: 0,
  reconnectAttempt: 0,
  lastConnectStartedAt: null,
  lastOpenAt: null,
  lastCloseAt: null,
  lastCloseCode: null,
  lastCloseReason: null,
  lastErrorAt: null,
  lastMessageAt: null,
  lastHeartbeatSentAt: null,
  lastHeartbeatAckAt: null,
  lastHelloSentAt: null,
  lastHelloAckAt: null,
  lastHelloReason: null,
  lastHandshakeTimeoutAt: null,
  lastReconnectScheduledAt: null,
};

chrome.runtime.onInstalled.addListener(() => {
  ensureAlarm();
  connectBridge("installed");
});

chrome.runtime.onStartup.addListener(() => {
  ensureAlarm();
  connectBridge("startup");
});

chrome.alarms.onAlarm.addListener((alarm) => {
  if (alarm.name === KEEPALIVE_ALARM) {
    connectBridge("alarm");
  }
});

chrome.tabs.onRemoved.addListener((tabId) => {
  attachedTabs.delete(tabId);
  debuggerAttachPromises.delete(tabId);
  networkLogStates.delete(tabId);
});

chrome.debugger.onDetach.addListener((source) => {
  if (typeof source.tabId === "number") {
    attachedTabs.delete(source.tabId);
    debuggerAttachPromises.delete(source.tabId);
    networkLogStates.delete(source.tabId);
  }
});

chrome.debugger.onEvent.addListener((source, method, params) => {
  void handleDebuggerEvent(source, method, params);
});

chrome.runtime.onSuspend.addListener(() => {
  stopHelloLoop();
  stopHeartbeat();
  clearConnectTimeoutTimer();
});

ensureAlarm();
connectBridge("bootstrap");

function ensureAlarm() {
  chrome.alarms.create(KEEPALIVE_ALARM, { periodInMinutes: 1 });
}

function scheduleRuntimeReload(delayMs = 300) {
  const boundedDelayMs = toFiniteInt(delayMs, 300, 0);
  if (runtimeReloadTimer) {
    clearTimeout(runtimeReloadTimer);
  }

  runtimeReloadScheduledAt = toIsoNow();
  runtimeReloadTimer = setTimeout(() => {
    runtimeReloadTimer = null;
    runtimeReloadScheduledAt = null;
    try {
      chrome.runtime.reload();
    } catch (error) {
      console.error("Edge Control: runtime reload failed.", error);
    }
  }, boundedDelayMs);

  return {
    scheduledAt: runtimeReloadScheduledAt,
    delayMs: boundedDelayMs,
  };
}

function getConfig() {
  const config = globalThis.EDGE_CONTROL_CONFIG || {};
  return {
    bridgeUrl: config.bridgeUrl || DEFAULT_BRIDGE_URL,
    authToken: config.authToken || null,
  };
}

function connectBridge(reason = "manual") {
  const { bridgeUrl, authToken } = getConfig();
  if (!authToken) {
    if (!warnedMissingAuthToken) {
      warnedMissingAuthToken = true;
      console.warn("Edge Control: missing auth token; run install.ps1.");
    }
    return;
  }

  warnedMissingAuthToken = false;

  if (bridgeSocket && (bridgeSocket.readyState === WebSocket.OPEN || bridgeSocket.readyState === WebSocket.CONNECTING)) {
    if (bridgeSocket.readyState === WebSocket.OPEN) {
      if (isHelloAckTimedOut()) {
        connectionState.lastErrorAt = toIsoNow();
        connectionState.lastHandshakeTimeoutAt = connectionState.lastErrorAt;
        safeCloseSocket(bridgeSocket, 4003, `Hello ack timeout after ${HELLO_ACK_TIMEOUT_MS}ms`);
        return;
      }

      if (!connectionState.lastHelloAckAt && !helloTimer) {
        startHelloLoop(activeSocketGeneration, reason);
      }

      const lastBridgeActivityAt = getLastBridgeActivityAt();
      if (lastBridgeActivityAt && Date.now() - Date.parse(lastBridgeActivityAt) > HEARTBEAT_STALE_MS) {
        connectionState.lastErrorAt = toIsoNow();
        safeCloseSocket(bridgeSocket, 4002, "Heartbeat timeout");
      }
    }
    return;
  }

  clearReconnectTimer();
  clearConnectTimeoutTimer();

  const socketGeneration = ++activeSocketGeneration;
  connectionState.socketGeneration = socketGeneration;
  connectionState.lastConnectStartedAt = toIsoNow();
  connectionState.lastCloseCode = null;
  connectionState.lastCloseReason = null;
  connectionState.bridgeSessionId = null;
  connectionState.bridgeConnectionId = null;
  connectionState.lastHeartbeatAckAt = null;
  connectionState.lastHelloAckAt = null;
  connectionState.lastHelloReason = null;
  connectionState.lastHandshakeTimeoutAt = null;

  const url = new URL(bridgeUrl);
  url.searchParams.set("role", "edge-extension");
  url.searchParams.set("token", authToken);
  url.searchParams.set("runtimeId", chrome.runtime.id);
  url.searchParams.set("version", chrome.runtime.getManifest().version);

  const socket = new WebSocket(url.toString());
  bridgeSocket = socket;

  connectTimeoutTimer = setTimeout(() => {
    if (bridgeSocket === socket && socket.readyState === WebSocket.CONNECTING) {
      connectionState.lastErrorAt = toIsoNow();
      safeCloseSocket(socket, 4008, `Connect timeout after ${CONNECT_TIMEOUT_MS}ms`);
    }
  }, CONNECT_TIMEOUT_MS);

  socket.addEventListener("open", () => {
    if (!isCurrentSocket(socket, socketGeneration)) {
      safeCloseSocket(socket, 4000, "Superseded connection");
      return;
    }

    clearConnectTimeoutTimer();
    connectionState.reconnectAttempt = 0;
    connectionState.lastOpenAt = toIsoNow();
    startHeartbeat(socketGeneration);
    startHelloLoop(socketGeneration, reason);
  });

  socket.addEventListener("message", async (event) => {
    if (!isCurrentSocket(socket, socketGeneration)) {
      return;
    }

    const receivedAt = toIsoNow();
    connectionState.lastMessageAt = receivedAt;

    try {
      const message = JSON.parse(event.data);

      if (message.type === "hello_ack") {
        connectionState.bridgeSessionId = message.bridgeSessionId || null;
        connectionState.bridgeConnectionId = message.extensionSessionId || null;
        connectionState.lastHelloAckAt = receivedAt;
        stopHelloLoop();
        return;
      }

      if (message.type === "ping") {
        connectionState.lastHeartbeatAckAt = receivedAt;
        sendSocketMessage(socket, {
          type: "pong",
          receivedPingId: message.pingId || null,
          bridgeSessionId: connectionState.bridgeSessionId,
          extensionSessionId: connectionState.bridgeConnectionId,
          sentAt: receivedAt,
        });
        return;
      }

      if (message.type === "pong") {
        connectionState.lastHeartbeatAckAt = receivedAt;
        return;
      }

      if (message.type !== "command") {
        return;
      }

      const startedAt = Date.now();

      try {
        const result = await runCommand(message.command, message.args || {});
        if (!isCurrentSocket(socket, socketGeneration)) {
          return;
        }
        sendSocketMessage(socket, {
          type: "response",
          requestId: message.requestId,
          ok: true,
          result,
          meta: {
            handledAt: toIsoNow(),
            durationMs: Date.now() - startedAt,
            bridgeSessionId: connectionState.bridgeSessionId,
            extensionSessionId: connectionState.bridgeConnectionId,
            socketGeneration,
          },
        });
      } catch (error) {
        if (!isCurrentSocket(socket, socketGeneration)) {
          return;
        }
        sendSocketMessage(socket, {
          type: "response",
          requestId: message.requestId,
          ok: false,
          error: serializeError(error),
          meta: {
            handledAt: toIsoNow(),
            durationMs: Date.now() - startedAt,
            bridgeSessionId: connectionState.bridgeSessionId,
            extensionSessionId: connectionState.bridgeConnectionId,
            socketGeneration,
          },
        });
      }
    } catch (error) {
      sendSocketMessage(socket, {
        type: "response",
        requestId: safeRequestId(event.data),
        ok: false,
        error: serializeError(error),
        meta: {
          handledAt: receivedAt,
          bridgeSessionId: connectionState.bridgeSessionId,
          extensionSessionId: connectionState.bridgeConnectionId,
          socketGeneration,
        },
      });
    }
  });

  socket.addEventListener("close", (event) => {
    if (!isCurrentSocket(socket, socketGeneration)) {
      return;
    }

    clearConnectTimeoutTimer();
    bridgeSocket = null;
    stopHelloLoop();
    stopHeartbeat();
    connectionState.lastCloseAt = toIsoNow();
    connectionState.lastCloseCode = event.code ?? null;
    connectionState.lastCloseReason = event.reason || null;
    scheduleReconnect();
  });

  socket.addEventListener("error", () => {
    if (isCurrentSocket(socket, socketGeneration)) {
      connectionState.lastErrorAt = toIsoNow();
    }
    safeCloseSocket(socket, 1011, "Socket error");
  });
}

function isCurrentSocket(socket, socketGeneration) {
  return bridgeSocket === socket && activeSocketGeneration === socketGeneration;
}

function safeRequestId(rawMessage) {
  try {
    const message = JSON.parse(rawMessage);
    return message.requestId || null;
  } catch {
    return null;
  }
}

function isHelloAckTimedOut() {
  if (!bridgeSocket || bridgeSocket.readyState !== WebSocket.OPEN || connectionState.lastHelloAckAt) {
    return false;
  }

  const openedAt = Date.parse(connectionState.lastOpenAt || connectionState.lastConnectStartedAt || "");
  if (!Number.isFinite(openedAt)) {
    return false;
  }

  return Date.now() - openedAt >= HELLO_ACK_TIMEOUT_MS;
}

async function sendHello(socket, socketGeneration, reason) {
  if (!isCurrentSocket(socket, socketGeneration) || socket.readyState !== WebSocket.OPEN) {
    return false;
  }

  const activeTab = await getCurrentTab().catch(() => null);
  if (!isCurrentSocket(socket, socketGeneration) || socket.readyState !== WebSocket.OPEN) {
    return false;
  }

  connectionState.lastHelloSentAt = toIsoNow();
  connectionState.lastHelloReason = reason;
  return sendSocketMessage(socket, {
    type: "hello",
    bridgeConnectReason: reason,
    heartbeatIntervalMs: HEARTBEAT_INTERVAL_MS,
    extension: {
      runtimeId: chrome.runtime.id,
      version: chrome.runtime.getManifest().version,
      activeTabId: activeTab?.id ?? null,
    },
  });
}

function startHelloLoop(socketGeneration, reason) {
  stopHelloLoop();
  const run = async () => {
    if (socketGeneration !== activeSocketGeneration) {
      return;
    }

    const socket = bridgeSocket;
    if (!socket || socket.readyState !== WebSocket.OPEN) {
      return;
    }

    if (connectionState.lastHelloAckAt) {
      stopHelloLoop();
      return;
    }

    if (isHelloAckTimedOut()) {
      connectionState.lastErrorAt = toIsoNow();
      connectionState.lastHandshakeTimeoutAt = connectionState.lastErrorAt;
      safeCloseSocket(socket, 4003, `Hello ack timeout after ${HELLO_ACK_TIMEOUT_MS}ms`);
      return;
    }

    await sendHello(socket, socketGeneration, reason);

    if (!connectionState.lastHelloAckAt && isCurrentSocket(socket, socketGeneration) && socket.readyState === WebSocket.OPEN) {
      helloTimer = setTimeout(() => {
        helloTimer = null;
        void run();
      }, HELLO_RETRY_MS);
    }
  };

  void run();
}

function stopHelloLoop() {
  if (helloTimer) {
    clearTimeout(helloTimer);
    helloTimer = null;
  }
}

function startHeartbeat(socketGeneration) {
  stopHeartbeat();
  heartbeatTimer = setInterval(() => {
    if (socketGeneration !== activeSocketGeneration) {
      stopHeartbeat();
      return;
    }

    if (!bridgeSocket || bridgeSocket.readyState !== WebSocket.OPEN) {
      return;
    }

    const lastBridgeActivityAt = getLastBridgeActivityAt();
    if (lastBridgeActivityAt && Date.now() - Date.parse(lastBridgeActivityAt) > HEARTBEAT_STALE_MS) {
      connectionState.lastErrorAt = toIsoNow();
      safeCloseSocket(bridgeSocket, 4002, "Heartbeat timeout");
      return;
    }

    connectionState.lastHeartbeatSentAt = toIsoNow();
    sendBridgeMessage({
      type: "ping",
      bridgeSessionId: connectionState.bridgeSessionId,
      extensionSessionId: connectionState.bridgeConnectionId,
      sentAt: connectionState.lastHeartbeatSentAt,
      socketGeneration,
    });
  }, HEARTBEAT_INTERVAL_MS);
}

function stopHeartbeat() {
  if (heartbeatTimer) {
    clearInterval(heartbeatTimer);
    heartbeatTimer = null;
  }
}

function getLastBridgeActivityAt() {
  return mostRecentTimestamp([
    connectionState.lastHeartbeatAckAt,
    connectionState.lastMessageAt,
    connectionState.lastHelloAckAt,
    connectionState.lastOpenAt,
  ]);
}

function mostRecentTimestamp(values) {
  const timestamps = values
    .filter(Boolean)
    .map((value) => Date.parse(value))
    .filter((value) => Number.isFinite(value));

  if (timestamps.length === 0) {
    return null;
  }

  return new Date(Math.max(...timestamps)).toISOString();
}

function scheduleReconnect() {
  if (reconnectTimer) {
    return;
  }

  connectionState.reconnectAttempt += 1;
  const exponentialDelay = Math.min(RECONNECT_BASE_MS * (2 ** (connectionState.reconnectAttempt - 1)), RECONNECT_MAX_MS);
  const jitterMs = Math.floor(Math.random() * Math.min(500, Math.max(100, exponentialDelay / 4)));
  const delayMs = exponentialDelay + jitterMs;
  connectionState.lastReconnectScheduledAt = toIsoNow();

  reconnectTimer = setTimeout(() => {
    reconnectTimer = null;
    connectBridge("reconnect");
  }, delayMs);
}

function clearReconnectTimer() {
  if (reconnectTimer) {
    clearTimeout(reconnectTimer);
    reconnectTimer = null;
  }
  connectionState.lastReconnectScheduledAt = null;
}

function clearConnectTimeoutTimer() {
  if (connectTimeoutTimer) {
    clearTimeout(connectTimeoutTimer);
    connectTimeoutTimer = null;
  }
}

function safeCloseSocket(socket, code, reason) {
  if (!socket || socket.readyState === WebSocket.CLOSED || socket.readyState === WebSocket.CLOSING) {
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

function sendBridgeMessage(payload) {
  return sendSocketMessage(bridgeSocket, payload);
}

function sendSocketMessage(socket, payload) {
  if (!socket || socket.readyState !== WebSocket.OPEN) {
    return false;
  }

  try {
    socket.send(JSON.stringify(payload));
    return true;
  } catch {
    return false;
  }
}

function serializeError(error) {
  return {
    message: error?.message || String(error),
    stack: error?.stack || null,
  };
}

function toIsoNow() {
  return new Date().toISOString();
}

async function getCurrentTab() {
  const tabs = await chrome.tabs.query({ active: true, lastFocusedWindow: true });
  return tabs[0] || null;
}

async function resolveTabId(tabId) {
  if (typeof tabId === "number") {
    return tabId;
  }
  const currentTab = await getCurrentTab();
  if (!currentTab?.id) {
    throw new Error("No active Edge tab is available.");
  }
  return currentTab.id;
}

async function focusTab(tabId) {
  const tab = await chrome.tabs.get(tabId);
  await chrome.tabs.update(tabId, { active: true });
  if (typeof tab.windowId === "number") {
    await chrome.windows.update(tab.windowId, { focused: true });
  }
  return summarizeTab(await chrome.tabs.get(tabId));
}

function summarizeTab(tab) {
  return {
    id: tab.id,
    windowId: tab.windowId,
    title: tab.title,
    url: tab.url,
    status: tab.status,
    active: tab.active,
    audible: tab.audible,
    discarded: tab.discarded,
    pinned: tab.pinned,
  };
}

function toFiniteInt(value, fallback, minimum = 0) {
  const numeric = Number(value);
  if (!Number.isFinite(numeric)) {
    return fallback;
  }
  return Math.max(minimum, Math.trunc(numeric));
}

function normalizeNetworkResourceTypes(values = []) {
  const normalized = Array.from(new Set(
    (Array.isArray(values) ? values : [])
      .map((value) => String(value || "").trim())
      .filter(Boolean)
  ));

  return normalized.length ? normalized : [...DEFAULT_NETWORK_LOG_RESOURCE_TYPES];
}

function normalizeStringList(values = []) {
  return Array.from(new Set(
    (Array.isArray(values) ? values : [])
      .map((value) => String(value || "").trim())
      .filter(Boolean)
  ));
}

function decodeBodyContent(payload, maxBytes) {
  const content = payload?.body;
  if (typeof content !== "string" || !content.length) {
    return null;
  }

  let decoded = content;
  if (payload?.base64Encoded) {
    decoded = atob(content);
  }

  const limit = Math.max(1024, toFiniteInt(maxBytes, DEFAULT_NETWORK_LOG_MAX_BODY_BYTES, 1024));
  return decoded.length <= limit
    ? decoded
    : `${decoded.slice(0, Math.max(0, limit - 14))}...[truncated]`;
}

function createNetworkLogState(tabId, options = {}) {
  return {
    tabId,
    enabled: true,
    startedAt: toIsoNow(),
    updatedAt: toIsoNow(),
    maxEntries: toFiniteInt(options.maxEntries, DEFAULT_NETWORK_LOG_MAX_ENTRIES, 1),
    maxBodies: toFiniteInt(options.maxBodies, DEFAULT_NETWORK_LOG_MAX_BODIES, 0),
    maxBodyBytes: toFiniteInt(options.maxBodyBytes, DEFAULT_NETWORK_LOG_MAX_BODY_BYTES, 1024),
    captureBodies: options.captureBodies !== false,
    resourceTypes: new Set(normalizeNetworkResourceTypes(options.resourceTypes)),
    bodyUrlIncludes: normalizeStringList(options.bodyUrlIncludes),
    urlIncludes: normalizeStringList(options.urlIncludes),
    entries: [],
    requests: new Map(),
    nextSequence: 1,
    bodyCount: 0,
    stats: {
      totalEvents: 0,
      totalStored: 0,
      totalBodies: 0,
      totalDropped: 0,
      totalFailed: 0,
      totalRead: 0,
      totalCleared: 0,
    },
  };
}

function ensureNetworkLogState(tabId, options = {}) {
  let state = networkLogStates.get(tabId);
  if (!state) {
    state = createNetworkLogState(tabId, options);
    networkLogStates.set(tabId, state);
    return state;
  }

  state.enabled = true;
  state.updatedAt = toIsoNow();
  state.maxEntries = toFiniteInt(options.maxEntries, state.maxEntries, 1);
  state.maxBodies = toFiniteInt(options.maxBodies, state.maxBodies, 0);
  state.maxBodyBytes = toFiniteInt(options.maxBodyBytes, state.maxBodyBytes, 1024);
  state.captureBodies = options.captureBodies !== false;
  state.resourceTypes = new Set(normalizeNetworkResourceTypes(options.resourceTypes?.length ? options.resourceTypes : Array.from(state.resourceTypes)));
  state.bodyUrlIncludes = normalizeStringList(options.bodyUrlIncludes?.length ? options.bodyUrlIncludes : state.bodyUrlIncludes);
  state.urlIncludes = normalizeStringList(options.urlIncludes?.length ? options.urlIncludes : state.urlIncludes);
  return state;
}

function clearNetworkLogState(state) {
  if (!state) {
    return;
  }
  state.entries = [];
  state.requests.clear();
  state.nextSequence = 1;
  state.bodyCount = 0;
  state.updatedAt = toIsoNow();
  state.stats.totalCleared += 1;
}

function buildNetworkLogMark(state) {
  if (!state) {
    return {
      lastSequence: 0,
      nextSequence: 1,
      updatedAt: null,
    };
  }

  return {
    lastSequence: Math.max(0, state.nextSequence - 1),
    nextSequence: state.nextSequence,
    updatedAt: state.updatedAt,
  };
}

function summarizeNetworkLogState(state, { entryCount = null } = {}) {
  if (!state) {
    return null;
  }

  const mark = buildNetworkLogMark(state);

  return {
    tabId: state.tabId,
    enabled: Boolean(state.enabled),
    startedAt: state.startedAt,
    updatedAt: state.updatedAt,
    maxEntries: state.maxEntries,
    maxBodies: state.maxBodies,
    maxBodyBytes: state.maxBodyBytes,
    captureBodies: Boolean(state.captureBodies),
    resourceTypes: Array.from(state.resourceTypes),
    bodyUrlIncludes: state.bodyUrlIncludes.slice(0, 16),
    urlIncludes: state.urlIncludes.slice(0, 16),
    bufferedEntries: entryCount ?? state.entries.length,
    bufferedBodyEntries: state.bodyCount,
    pendingRequests: state.requests.size,
    lastSequence: mark.lastSequence,
    nextSequence: mark.nextSequence,
    stats: { ...state.stats },
  };
}

async function ensureDebuggerAttached(tabId) {
  const target = { tabId };
  if (!attachedTabs.has(tabId)) {
    let attachPromise = debuggerAttachPromises.get(tabId);
    if (!attachPromise) {
      attachPromise = chrome.debugger.attach(target, DEBUGGER_VERSION)
        .then(() => {
          attachedTabs.add(tabId);
        })
        .finally(() => {
          debuggerAttachPromises.delete(tabId);
        });
      debuggerAttachPromises.set(tabId, attachPromise);
    }
    await attachPromise;
  }
  return target;
}

function shouldTrackNetworkRequest(state, request) {
  if (!state?.enabled || !request?.url) {
    return false;
  }

  const resourceType = String(request.resourceType || request.type || "").trim();
  if (state.resourceTypes.size && resourceType && !state.resourceTypes.has(resourceType)) {
    return false;
  }

  if (state.urlIncludes.length) {
    return state.urlIncludes.some((pattern) => request.url.includes(pattern));
  }

  return true;
}

function shouldCaptureNetworkBody(state, request) {
  if (!state?.captureBodies || !request || request.failed) {
    return false;
  }

  const mimeType = String(request.mimeType || "").toLowerCase();
  const url = String(request.url || "").toLowerCase();
  if (state.maxBodies === 0 || (state.maxBodies > 0 && state.bodyCount >= state.maxBodies)) {
    return false;
  }
  if (state.bodyUrlIncludes.length) {
    return state.bodyUrlIncludes.some((pattern) => url.includes(String(pattern || "").toLowerCase()));
  }
  return NETWORK_BODY_MIME_PATTERN.test(mimeType) || NETWORK_BODY_URL_PATTERN.test(url);
}

function pruneNetworkLogEntries(state) {
  while (state.entries.length > state.maxEntries) {
    const dropped = state.entries.shift();
    if (dropped?.content && state.bodyCount > 0) {
      state.bodyCount -= 1;
    }
    state.stats.totalDropped += 1;
  }
}

function pushNetworkLogEntry(state, request) {
  state.entries.push({
    sequence: state.nextSequence++,
    requestId: request.requestId,
    url: request.url,
    method: request.method || null,
    resourceType: request.resourceType || request.type || null,
    mimeType: request.mimeType || null,
    status: request.status ?? null,
    statusText: request.statusText || null,
    startedAt: request.startedAt || null,
    responseReceivedAt: request.responseReceivedAt || null,
    completedAt: request.completedAt || null,
    fromDiskCache: Boolean(request.fromDiskCache),
    fromServiceWorker: Boolean(request.fromServiceWorker),
    protocol: request.protocol || null,
    initiatorType: request.initiatorType || null,
    encodedDataLength: request.encodedDataLength ?? null,
    failed: Boolean(request.failed),
    errorText: request.errorText || null,
    content: request.content || null,
    bodyCaptured: Boolean(request.content),
    bodyCaptureError: request.bodyCaptureError || null,
  });
  state.updatedAt = toIsoNow();
  state.stats.totalStored += 1;
  if (request.content) {
    state.bodyCount += 1;
    state.stats.totalBodies += 1;
  }
  if (request.failed) {
    state.stats.totalFailed += 1;
  }
  pruneNetworkLogEntries(state);
}

async function finalizeNetworkRequest(tabId, requestId, patch = {}) {
  const state = networkLogStates.get(tabId);
  if (!state) {
    return;
  }

  const request = state.requests.get(requestId);
  if (!request) {
    return;
  }

  Object.assign(request, patch);
  request.completedAt = request.completedAt || toIsoNow();

  if (shouldTrackNetworkRequest(state, request) && shouldCaptureNetworkBody(state, request)) {
    try {
      const payload = await chrome.debugger.sendCommand({ tabId }, "Network.getResponseBody", { requestId });
      request.content = decodeBodyContent(payload, state.maxBodyBytes);
    } catch (error) {
      request.bodyCaptureError = error?.message || String(error);
    }
  }

  if (shouldTrackNetworkRequest(state, request)) {
    pushNetworkLogEntry(state, request);
  }

  state.requests.delete(requestId);
}

async function handleDebuggerEvent(source, method, params) {
  const tabId = source?.tabId;
  if (typeof tabId !== "number") {
    return;
  }

  const state = networkLogStates.get(tabId);
  if (!state?.enabled) {
    return;
  }

  state.stats.totalEvents += 1;

  if (method === "Network.requestWillBeSent") {
    const requestId = params?.requestId;
    if (!requestId) {
      return;
    }

    const existing = state.requests.get(requestId) || {};
    state.requests.set(requestId, {
      ...existing,
      requestId,
      url: params?.request?.url || existing.url || null,
      method: params?.request?.method || existing.method || null,
      resourceType: params?.type || existing.resourceType || null,
      initiatorType: params?.initiator?.type || existing.initiatorType || null,
      startedAt: existing.startedAt || toIsoNow(),
    });
    return;
  }

  if (method === "Network.responseReceived") {
    const requestId = params?.requestId;
    if (!requestId) {
      return;
    }

    const existing = state.requests.get(requestId) || { requestId, startedAt: toIsoNow() };
    const response = params?.response || {};
    state.requests.set(requestId, {
      ...existing,
      url: response.url || existing.url || null,
      resourceType: params?.type || existing.resourceType || null,
      mimeType: response.mimeType || existing.mimeType || null,
      status: response.status ?? existing.status ?? null,
      statusText: response.statusText || existing.statusText || null,
      protocol: response.protocol || existing.protocol || null,
      fromDiskCache: Boolean(response.fromDiskCache),
      fromServiceWorker: Boolean(response.fromServiceWorker),
      responseReceivedAt: toIsoNow(),
    });
    return;
  }

  if (method === "Network.loadingFinished") {
    const requestId = params?.requestId;
    if (!requestId) {
      return;
    }

    await finalizeNetworkRequest(tabId, requestId, {
      encodedDataLength: params?.encodedDataLength ?? null,
      failed: false,
    });
    return;
  }

  if (method === "Network.loadingFailed") {
    const requestId = params?.requestId;
    if (!requestId) {
      return;
    }

    await finalizeNetworkRequest(tabId, requestId, {
      failed: true,
      errorText: params?.errorText || null,
      encodedDataLength: params?.encodedDataLength ?? null,
    });
  }
}

async function startNetworkLog(tabId, options = {}) {
  const target = await ensureDebuggerAttached(tabId);
  const state = ensureNetworkLogState(tabId, options);
  if (options.clear !== false) {
    clearNetworkLogState(state);
  }

  await chrome.debugger.sendCommand(target, "Network.enable", {});
  return summarizeNetworkLogState(state);
}

function readNetworkLog(tabId, options = {}) {
  const state = networkLogStates.get(tabId);
  if (!state) {
    return {
      tabId,
      entries: [],
      meta: null,
    };
  }

  const resourceTypes = new Set(normalizeStringList(options.resourceTypes));
  const urlIncludes = normalizeStringList(options.urlIncludes);
  const sinceSequence = toFiniteInt(options.sinceSequence, 0, 0);
  const maxEntries = toFiniteInt(options.maxEntries, state.entries.length || state.maxEntries, 1);
  const includeBodies = options.includeBodies !== false;

  let entries = state.entries
    .filter((entry) => entry.sequence > sinceSequence)
    .filter((entry) => (resourceTypes.size ? resourceTypes.has(String(entry.resourceType || "")) : true))
    .filter((entry) => (
      urlIncludes.length
        ? urlIncludes.some((pattern) => String(entry.url || "").includes(pattern))
        : true
    ));

  if (entries.length > maxEntries) {
    entries = entries.slice(entries.length - maxEntries);
  }

  const sequenceSet = new Set(entries.map((entry) => entry.sequence));
  const output = entries.map((entry) => includeBodies ? { ...entry } : { ...entry, content: null });
  if (options.consume === true && sequenceSet.size) {
    let removedBodies = 0;
    for (const entry of state.entries) {
      if (sequenceSet.has(entry.sequence) && entry.content) {
        removedBodies += 1;
      }
    }
    state.entries = state.entries.filter((entry) => !sequenceSet.has(entry.sequence));
    state.bodyCount = Math.max(0, state.bodyCount - removedBodies);
    state.updatedAt = toIsoNow();
  }
  state.stats.totalRead += output.length;

  return {
    tabId,
    entries: output,
    meta: summarizeNetworkLogState(state, { entryCount: output.length }),
  };
}

function getNetworkLogMark(tabId) {
  const state = networkLogStates.get(tabId);
  return {
    tabId,
    mark: buildNetworkLogMark(state),
    meta: summarizeNetworkLogState(state, { entryCount: 0 }),
  };
}

async function stopNetworkLog(tabId, options = {}) {
  const state = networkLogStates.get(tabId);
  if (!state) {
    return {
      tabId,
      stopped: false,
      meta: null,
    };
  }

  state.enabled = false;
  const summary = summarizeNetworkLogState(state);
  if (options.clear === true) {
    networkLogStates.delete(tabId);
  }

  if (options.detachIfIdle === true && attachedTabs.has(tabId)) {
    try {
      await chrome.debugger.detach({ tabId });
    } catch {
      // Best effort detach.
    }
    attachedTabs.delete(tabId);
    networkLogStates.delete(tabId);
  }

  return {
    tabId,
    stopped: true,
    meta: summary,
  };
}

async function sendCdp(tabId, method, params, detachAfter) {
  const target = await ensureDebuggerAttached(tabId);

  try {
    return await chrome.debugger.sendCommand(target, method, params || {});
  } finally {
    if (detachAfter && !networkLogStates.get(tabId)?.enabled) {
      try {
        await chrome.debugger.detach(target);
      } catch {
        // Best effort detach.
      }
      attachedTabs.delete(tabId);
    }
  }
}

async function executeDomCommand(tabId, command, args) {
  const injectionResults = await chrome.scripting.executeScript({
    target: { tabId, allFrames: Boolean(args.allFrames) },
    world: args.world === "MAIN" ? "MAIN" : "ISOLATED",
    func: injectedDomCommand,
    args: [command, args],
  });

  return injectionResults.map((item) => ({
    frameId: item.frameId,
    result: item.result,
  }));
}

function getSocketState() {
  if (!bridgeSocket) {
    return "closed";
  }

  switch (bridgeSocket.readyState) {
    case WebSocket.CONNECTING:
      return "connecting";
    case WebSocket.OPEN:
      return "open";
    case WebSocket.CLOSING:
      return "closing";
    default:
      return "closed";
  }
}

async function runCommand(command, args) {
  switch (command) {
    case "get_status": {
      const currentTab = await getCurrentTab();
      const { bridgeUrl } = getConfig();
      return {
        runtimeId: chrome.runtime.id,
        version: chrome.runtime.getManifest().version,
        runtimeReloadPending: Boolean(runtimeReloadTimer),
        runtimeReloadScheduledAt,
        connected: bridgeSocket?.readyState === WebSocket.OPEN,
        activeTabId: currentTab?.id ?? null,
        debuggerAttachedTabs: Array.from(attachedTabs.values()),
        networkLogTabs: Array.from(networkLogStates.values()).map((state) => summarizeNetworkLogState(state)),
        bridge: {
          url: bridgeUrl,
          sessionId: connectionState.bridgeSessionId,
          extensionSessionId: connectionState.bridgeConnectionId,
          socketGeneration: connectionState.socketGeneration,
          socketState: getSocketState(),
          handshakeReady: Boolean(connectionState.lastHelloAckAt),
          reconnectAttempt: connectionState.reconnectAttempt,
          heartbeatIntervalMs: HEARTBEAT_INTERVAL_MS,
          heartbeatStaleMs: HEARTBEAT_STALE_MS,
          helloRetryMs: HELLO_RETRY_MS,
          helloAckTimeoutMs: HELLO_ACK_TIMEOUT_MS,
          reconnectScheduled: Boolean(reconnectTimer),
          lastConnectStartedAt: connectionState.lastConnectStartedAt,
          lastOpenAt: connectionState.lastOpenAt,
          lastCloseAt: connectionState.lastCloseAt,
          lastCloseCode: connectionState.lastCloseCode,
          lastCloseReason: connectionState.lastCloseReason,
          lastErrorAt: connectionState.lastErrorAt,
          lastHandshakeTimeoutAt: connectionState.lastHandshakeTimeoutAt,
          lastMessageAt: connectionState.lastMessageAt,
          lastHeartbeatSentAt: connectionState.lastHeartbeatSentAt,
          lastHeartbeatAckAt: connectionState.lastHeartbeatAckAt,
          lastHelloSentAt: connectionState.lastHelloSentAt,
          lastHelloAckAt: connectionState.lastHelloAckAt,
          lastHelloReason: connectionState.lastHelloReason,
          lastReconnectScheduledAt: connectionState.lastReconnectScheduledAt,
          lastBridgeActivityAt: getLastBridgeActivityAt(),
        },
      };
    }
    case "list_tabs": {
      const queryInfo = {};
      if (args.currentWindow === true) {
        queryInfo.currentWindow = true;
      }
      if (typeof args.windowId === "number") {
        queryInfo.windowId = args.windowId;
      }
      if (args.activeOnly === true) {
        queryInfo.active = true;
      }
      const tabs = await chrome.tabs.query(queryInfo);
      return tabs.map(summarizeTab);
    }
    case "focus_tab": {
      const tabId = await resolveTabId(args.tabId);
      return focusTab(tabId);
    }
    case "navigate": {
      if (!args.url) {
        throw new Error("navigate requires args.url.");
      }
      if (args.createNewTab) {
        const tab = await chrome.tabs.create({
          url: args.url,
          active: args.active !== false,
          ...(typeof args.windowId === "number" ? { windowId: args.windowId } : {}),
        });
        return summarizeTab(tab);
      }
      const tabId = await resolveTabId(args.tabId);
      const tab = await chrome.tabs.update(tabId, { url: args.url });
      return summarizeTab(tab);
    }
    case "reload": {
      const tabId = await resolveTabId(args.tabId);
      await chrome.tabs.reload(tabId, { bypassCache: Boolean(args.bypassCache) });
      return { tabId, reloaded: true };
    }
    case "reload_extension": {
      const scheduled = scheduleRuntimeReload(args.delayMs);
      return {
        reloading: true,
        runtimeId: chrome.runtime.id,
        version: chrome.runtime.getManifest().version,
        ...scheduled,
      };
    }
    case "start_network_log": {
      const tabId = await resolveTabId(args.tabId);
      const meta = await startNetworkLog(tabId, args);
      return { tabId, meta };
    }
    case "read_network_log": {
      const tabId = await resolveTabId(args.tabId);
      return readNetworkLog(tabId, args);
    }
    case "get_network_log_mark": {
      const tabId = await resolveTabId(args.tabId);
      return getNetworkLogMark(tabId);
    }
    case "stop_network_log": {
      const tabId = await resolveTabId(args.tabId);
      return stopNetworkLog(tabId, args);
    }
    case "click":
    case "type":
    case "press_key":
    case "wait_for":
    case "get_dom":
    case "query":
    case "eval": {
      const tabId = await resolveTabId(args.tabId);
      const result = await executeDomCommand(tabId, command, args);
      return {
        tabId,
        frames: result,
      };
    }
    case "send_cdp": {
      const tabId = await resolveTabId(args.tabId);
      if (!args.method) {
        throw new Error("send_cdp requires args.method.");
      }
      const result = await sendCdp(tabId, args.method, args.params || {}, Boolean(args.detachAfter));
      return { tabId, method: args.method, result };
    }
    default:
      throw new Error(`Unsupported command: ${command}`);
  }
}

function injectedDomCommand(command, args) {
  const MAX_DEFAULT = 12000;

  function normalizeText(value) {
    return String(value || "").replace(/\s+/g, " ").trim();
  }

  function truncate(value, maxLength) {
    const limit = Number(maxLength) > 0 ? Number(maxLength) : MAX_DEFAULT;
    const text = String(value ?? "");
    if (text.length <= limit) {
      return text;
    }
    return text.slice(0, limit) + "...[truncated]";
  }

  function describeElement(element) {
    if (!element) {
      return null;
    }
    const rect = typeof element.getBoundingClientRect === "function" ? element.getBoundingClientRect() : null;
    return {
      tag: element.tagName,
      id: element.id || null,
      className: element.className || null,
      name: element.getAttribute?.("name") || null,
      type: element.getAttribute?.("type") || null,
      value: "value" in element ? element.value : null,
      text: truncate(normalizeText(element.innerText || element.textContent || ""), args.maxLength),
      outerHTML: args.includeHtml === false ? null : truncate(element.outerHTML || "", args.maxLength),
      rect: rect ? {
        x: rect.x,
        y: rect.y,
        width: rect.width,
        height: rect.height,
      } : null,
    };
  }

  function resolveByText(targetText) {
    const wanted = normalizeText(targetText).toLowerCase();
    if (!wanted) {
      return null;
    }
    const elements = Array.from(document.querySelectorAll("body *"));
    return elements.find((node) => normalizeText(node.innerText || node.textContent || "").toLowerCase().includes(wanted)) || null;
  }

  function resolveTarget() {
    if (args.activeElement) {
      return document.activeElement;
    }
    if (args.selector) {
      const list = Array.from(document.querySelectorAll(args.selector));
      const index = Number.isInteger(args.index) ? args.index : 0;
      return list[index] || null;
    }
    if (args.xpath) {
      const result = document.evaluate(args.xpath, document, null, XPathResult.FIRST_ORDERED_NODE_TYPE, null);
      return result.singleNodeValue;
    }
    if (args.text) {
      return resolveByText(args.text);
    }
    return null;
  }

  function emitInputEvents(element) {
    element.dispatchEvent(new Event("input", { bubbles: true, cancelable: true }));
    element.dispatchEvent(new Event("change", { bubbles: true, cancelable: true }));
  }

  async function waitForTarget(timeoutMs) {
    const timeout = Number(timeoutMs) > 0 ? Number(timeoutMs) : 5000;
    const start = Date.now();
    while (Date.now() - start < timeout) {
      const target = resolveTarget();
      if (target) {
        return target;
      }
      await new Promise((resolve) => setTimeout(resolve, 100));
    }
    return null;
  }

  async function handle() {
    if (command === "query") {
      if (!args.selector) {
        throw new Error("query requires selector.");
      }
      const nodes = Array.from(document.querySelectorAll(args.selector));
      const maxResults = Number(args.maxResults) > 0 ? Number(args.maxResults) : 20;
      return {
        count: nodes.length,
        matches: nodes.slice(0, maxResults).map(describeElement),
      };
    }

    if (command === "get_dom") {
      const target = resolveTarget();
      if (target) {
        return describeElement(target);
      }
      return {
        title: document.title,
        url: location.href,
        text: args.includeText === false ? null : truncate(document.body?.innerText || "", args.maxLength),
        outerHTML: args.includeHtml === false ? null : truncate(document.documentElement.outerHTML || "", args.maxLength),
      };
    }

    if (command === "wait_for") {
      const target = await waitForTarget(args.timeoutMs);
      return {
        found: Boolean(target),
        element: describeElement(target),
      };
    }

    const target = resolveTarget();

    if (!target && command !== "eval" && command !== "press_key") {
      throw new Error("Target element was not found.");
    }

    if (command === "click") {
      target.scrollIntoView?.({ block: "center", inline: "center" });
      target.focus?.();
      target.click?.();
      return {
        clicked: true,
        element: describeElement(target),
      };
    }

    if (command === "type") {
      const value = String(args.text ?? "");
      target.scrollIntoView?.({ block: "center", inline: "center" });
      target.focus?.();

      if (target.isContentEditable) {
        if (args.clearFirst !== false) {
          target.textContent = "";
        }
        target.textContent += value;
      } else if ("value" in target) {
        target.value = args.clearFirst === false ? String(target.value || "") + value : value;
      } else {
        throw new Error("Target does not support text input.");
      }

      emitInputEvents(target);
      return {
        typed: true,
        element: describeElement(target),
      };
    }

    if (command === "press_key") {
      const key = String(args.key || "");
      const element = target || document.activeElement || document.body;
      element.focus?.();

      ["keydown", "keypress", "keyup"].forEach((eventName) => {
        element.dispatchEvent(new KeyboardEvent(eventName, {
          bubbles: true,
          cancelable: true,
          key,
        }));
      });

      if (key === "Enter" && typeof element.form?.requestSubmit === "function") {
        element.form.requestSubmit();
      }

      return {
        pressed: key,
        element: describeElement(element),
      };
    }

    if (command === "eval") {
      if (!args.expression) {
        throw new Error("eval requires expression.");
      }
      const value = (0, eval)(args.expression);
      const resolved = value && typeof value.then === "function" ? await value : value;
      const text = typeof resolved === "string" ? resolved : JSON.stringify(resolved, null, 2);
      return {
        value: truncate(text, args.maxLength),
      };
    }

    throw new Error(`Unsupported DOM command: ${command}`);
  }

  return handle();
}
