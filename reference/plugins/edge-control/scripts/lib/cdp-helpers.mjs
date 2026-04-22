import { sleep } from "./crawl-utils.mjs";

const NON_STICKY_QUERY_PARAMS = new Set([
  "pp",
  "si",
  "feature",
  "sp",
  "persist_gl",
  "persist_hl",
]);

const DEFAULT_BLOCKED_URL_PATTERNS = [
  "*://*.doubleclick.net/*",
  "*://*.googletagmanager.com/*",
  "*://*.google-analytics.com/*",
  "*://*.analytics.google.com/*",
  "*://*.adservice.google.com/*",
  "*://*.googleadservices.com/*",
  "*://*.facebook.com/tr/*",
  "*://*.facebook.net/*",
  "*://*.segment.io/*",
  "*://*.segment.com/*",
  "*://*.amplitude.com/*",
  "*://*.mixpanel.com/*",
  "*://*.clarity.ms/*",
  "*://*.hotjar.com/*",
  "*://*.intercom.io/*",
  "*://*.taboola.com/*",
  "*://*.outbrain.com/*",
  "*://*.criteo.com/*",
  "*://*.png*",
  "*://*.jpg*",
  "*://*.jpeg*",
  "*://*.gif*",
  "*://*.webp*",
  "*://*.avif*",
  "*://*.svg*",
  "*://*.mp4*",
  "*://*.webm*",
  "*://*.m3u8*",
  "*://*.mpd*",
  "*://*.woff*",
  "*://*.woff2*",
  "*://*.ttf*",
  "*://*.otf*",
];

const RESOURCE_PRIORITY_HINT_PATTERN = /(api|graphql|comment|review|rating|feedback|discussion|thread|conversation|reply|post|feed|json|ajax|data)/i;

const QUIET_PAGE_BOOTSTRAP = `(() => {
  if (globalThis.__edgeControlQuietModeInstalled) {
    return;
  }

  globalThis.__edgeControlQuietModeInstalled = true;

  const installStyle = () => {
    if (document.getElementById("__edge_control_quiet_style__")) {
      return;
    }

    const style = document.createElement("style");
    style.id = "__edge_control_quiet_style__";
    style.textContent = [
      "* { animation-duration: 0s !important; animation-delay: 0s !important; transition-duration: 0s !important; scroll-behavior: auto !important; }",
      "video, audio { visibility: hidden !important; pointer-events: none !important; }",
      "html { caret-color: transparent !important; }",
    ].join("\\n");

    (document.head || document.documentElement || document.body)?.appendChild(style);
  };

  const disableDialogs = () => {
    const noop = () => undefined;
    try { window.alert = noop; } catch {}
    try { window.confirm = () => true; } catch {}
    try { window.prompt = () => ""; } catch {}
    try { window.print = noop; } catch {}
  };

  const quietMedia = () => {
    const mediaElements = document.querySelectorAll("video, audio");
    for (const media of mediaElements) {
      try { media.autoplay = false; } catch {}
      try { media.muted = true; } catch {}
      try { media.pause(); } catch {}
      try { media.preload = "none"; } catch {}
    }
  };

  const originalPlay = globalThis.HTMLMediaElement?.prototype?.play;
  if (typeof originalPlay === "function") {
    try {
      globalThis.HTMLMediaElement.prototype.play = function playQuietly() {
        try { this.pause(); } catch {}
        try { this.muted = true; } catch {}
        return Promise.resolve();
      };
    } catch {}
  }

  disableDialogs();
  installStyle();

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", installStyle, { once: true });
    document.addEventListener("DOMContentLoaded", quietMedia, { once: true });
  } else {
    quietMedia();
  }

  new MutationObserver(() => {
    installStyle();
    quietMedia();
  }).observe(document.documentElement || document, {
    subtree: true,
    childList: true,
  });
})()`;

function formatExceptionDetails(details) {
  const text = details?.exception?.description || details?.text || "Runtime.evaluate failed.";
  return `${text}${details?.lineNumber !== undefined ? ` (line ${details.lineNumber})` : ""}`;
}

function buildFunctionExpression(fn, args) {
  return `(async () => {
    const __fn = ${fn.toString()};
    const __args = ${JSON.stringify(args || [])};
    return await __fn(...__args);
  })()`;
}

export function buildEvaluateExpression(sourceOrFunction, args = []) {
  if (typeof sourceOrFunction === "function") {
    return buildFunctionExpression(sourceOrFunction, args);
  }
  if (args.length > 0) {
    throw new Error("cdpEvaluate only accepts args when the source is a function.");
  }
  return String(sourceOrFunction);
}

function urlsLikelyMatch(currentValue, expectedValue) {
  if (!expectedValue) {
    return true;
  }
  if (!currentValue) {
    return false;
  }

  try {
    const current = new URL(currentValue);
    const expected = new URL(expectedValue, current.origin);
    const currentPathVariants = buildComparablePathVariants(current.pathname);
    const expectedPathVariants = buildComparablePathVariants(expected.pathname);

    if (
      current.origin !== expected.origin
      || !currentPathVariants.some((variant) => expectedPathVariants.includes(variant))
    ) {
      return false;
    }

    for (const [key, value] of expected.searchParams.entries()) {
      if (NON_STICKY_QUERY_PARAMS.has(key)) {
        continue;
      }
      if (current.searchParams.get(key) !== value) {
        return false;
      }
    }

    return true;
  } catch {
    return currentValue === expectedValue || currentValue.includes(expectedValue);
  }
}

function buildComparablePathVariants(pathname) {
  const normalized = normalizeComparablePath(pathname);
  const variants = new Set([normalized]);
  const segments = normalized.split("/").filter(Boolean);

  if (segments.length > 1 && /^[a-z]{2,3}(?:[-_][a-zA-Z]{2,8}){0,2}$/.test(segments[0])) {
    variants.add(normalizeComparablePath(`/${segments.slice(1).join("/")}`));
  }

  return Array.from(variants);
}

function normalizeComparablePath(pathname) {
  const value = String(pathname || "/");
  if (value === "/") {
    return value;
  }

  const trimmed = value.replace(/\/+$/, "");
  return trimmed || "/";
}

function uniqStrings(values = []) {
  return Array.from(
    new Set(
      values
        .map((value) => String(value || "").trim())
        .filter(Boolean)
    )
  );
}

function truncateString(value, maxLength = 240) {
  const text = String(value ?? "");
  if (!Number.isFinite(Number(maxLength)) || Number(maxLength) <= 0 || text.length <= Number(maxLength)) {
    return text;
  }
  return `${text.slice(0, Math.max(0, Number(maxLength) - 14))}...[truncated]`;
}

function toFiniteNumber(value, digits = 3) {
  const numeric = Number(value);
  if (!Number.isFinite(numeric)) {
    return null;
  }
  return Number(numeric.toFixed(digits));
}

function countValues(values = []) {
  return values.reduce((accumulator, value) => {
    const key = String(value || "unknown");
    accumulator[key] = (accumulator[key] || 0) + 1;
    return accumulator;
  }, {});
}

function originForUrl(value) {
  try {
    const parsed = new URL(value);
    if (parsed.protocol === "http:" || parsed.protocol === "https:") {
      return parsed.origin;
    }
  } catch {
    return null;
  }

  return null;
}

function looksLikeInterruptionPage({ href, title } = {}) {
  const text = [href, title].map((value) => String(value || "").toLowerCase()).join("\n");
  return /captcha|recaptcha|verify (?:that )?you(?:['\u2019])?re human|unusual traffic|automated queries|sorry\/index|before you continue|consent|challenge|security check/.test(text);
}

function scorePageResource(resource, {
  pageUrl = null,
  priorityUrlIncludes = [],
} = {}) {
  const url = String(resource?.url || "").toLowerCase();
  const mimeType = String(resource?.mimeType || "").toLowerCase();
  const type = String(resource?.type || "").toLowerCase();
  const pageOrigin = originForUrl(pageUrl);
  const resourceOrigin = originForUrl(resource?.url);
  let score = 0;

  if (type === "fetch" || type === "xhr") {
    score += 24;
  } else if (type === "other") {
    score += 8;
  }

  if (RESOURCE_PRIORITY_HINT_PATTERN.test(url)) {
    score += 12;
  }
  if (/graphql/.test(url)) {
    score += 14;
  }
  if (/\.json(?:$|[?#])/.test(url) || /[?&](format|output)=json(?:$|[&#])/.test(url)) {
    score += 10;
  }
  if (/json|javascript|graphql|text\/plain/.test(mimeType)) {
    score += 8;
  }
  if (pageOrigin && resourceOrigin && pageOrigin === resourceOrigin) {
    score += 6;
  }
  if ((priorityUrlIncludes || []).some((pattern) => url.includes(String(pattern || "").toLowerCase()))) {
    score += 20;
  }

  const size = Number(resource?.size);
  if (Number.isFinite(size)) {
    if (size >= 500 && size <= 500000) {
      score += 4;
    } else if (size > 500000) {
      score -= 2;
    }
  }

  return score;
}

async function mapWithConcurrency(items, limit, mapper) {
  const results = new Array(items.length);
  let cursor = 0;
  const workerCount = Math.max(1, Math.min(Number(limit) || 1, items.length || 1));

  await Promise.all(Array.from({ length: workerCount }, async () => {
    while (true) {
      const index = cursor;
      cursor += 1;
      if (index >= items.length) {
        return;
      }
      results[index] = await mapper(items[index], index);
    }
  }));

  return results;
}

function summarizeCookie(cookie) {
  if (!cookie || typeof cookie !== "object") {
    return null;
  }

  return {
    name: truncateString(cookie.name, 80) || null,
    domain: cookie.domain || null,
    path: cookie.path || null,
    expires: toFiniteNumber(cookie.expires, 0),
    size: toFiniteNumber(cookie.size, 0),
    httpOnly: Boolean(cookie.httpOnly),
    secure: Boolean(cookie.secure),
    session: Boolean(cookie.session),
    sameSite: cookie.sameSite || null,
  };
}

function summarizePerformanceMetrics(metrics = []) {
  const summary = {};
  for (const metric of Array.isArray(metrics) ? metrics : []) {
    if (!metric?.name) {
      continue;
    }
    const value = toFiniteNumber(metric.value);
    if (value !== null) {
      summary[metric.name] = value;
    }
  }
  return summary;
}

function summarizeUsageBreakdown(usageBreakdown = []) {
  return (Array.isArray(usageBreakdown) ? usageBreakdown : [])
    .map((entry) => ({
      storageType: entry?.storageType || null,
      usage: toFiniteNumber(entry?.usage, 0),
    }))
    .filter((entry) => entry.storageType);
}

function flattenFrameResources(frameTree, output = []) {
  if (!frameTree) {
    return output;
  }

  const frameId = frameTree.frame?.id || null;
  for (const resource of frameTree.resources || []) {
    output.push({
      frameId,
      url: resource?.url || null,
      type: resource?.type || null,
      mimeType: resource?.mimeType || null,
      size: resource?.contentSize || null,
      lastModified: resource?.lastModified || null,
    });
  }

  for (const child of frameTree.childFrames || []) {
    flattenFrameResources(child, output);
  }

  return output;
}

function decodeResourceContent(payload, maxBytes) {
  const content = payload?.content;
  if (typeof content !== "string" || content.length === 0) {
    return null;
  }

  let decoded = content;
  if (payload?.base64Encoded) {
    decoded = Buffer.from(content, "base64").toString("utf8");
  }

  return decoded.length <= maxBytes
    ? decoded
    : decoded.slice(0, maxBytes) + "...[truncated]";
}

async function bestEffortSendCdp(client, payload) {
  try {
    return await client.sendCdp(payload);
  } catch {
    return null;
  }
}

async function bestEffortCdpResult(client, payload) {
  const response = await bestEffortSendCdp(client, payload);
  return response?.result ?? null;
}

export async function cdpEvaluate(client, {
  tabId,
  expression,
  args = [],
  timeoutMs = 15000,
  awaitPromise = true,
  returnByValue = true,
  allowUnsafeEvalBlockedByCSP = true,
} = {}) {
  const payload = await client.sendCdp({
    tabId,
    method: "Runtime.evaluate",
    params: {
      expression: buildEvaluateExpression(expression, args),
      awaitPromise,
      returnByValue,
      allowUnsafeEvalBlockedByCSP,
      userGesture: false,
    },
    timeoutMs,
  });

  if (payload?.result?.exceptionDetails) {
    throw new Error(formatExceptionDetails(payload.result.exceptionDetails));
  }

  return payload?.result?.result?.value ?? null;
}

export async function prepareTabForCrawl(client, {
  tabId,
  timeoutMs = 15000,
  quiet = true,
  blockHeavyResources = true,
  blockUrlPatterns = [],
  bypassServiceWorker = true,
  injectQuietPageScript = true,
} = {}) {
  if (typeof tabId !== "number") {
    throw new Error("prepareTabForCrawl requires a numeric tabId.");
  }

  const blockedUrls = uniqStrings([
    ...(blockHeavyResources ? DEFAULT_BLOCKED_URL_PATTERNS : []),
    ...blockUrlPatterns,
  ]);

  await bestEffortSendCdp(client, {
    tabId,
    method: "Page.enable",
    params: {},
    timeoutMs,
  });
  await bestEffortSendCdp(client, {
    tabId,
    method: "Runtime.enable",
    params: {},
    timeoutMs,
  });
  await bestEffortSendCdp(client, {
    tabId,
    method: "Network.enable",
    params: {},
    timeoutMs,
  });

  if (bypassServiceWorker) {
    await bestEffortSendCdp(client, {
      tabId,
      method: "Network.setBypassServiceWorker",
      params: { bypass: true },
      timeoutMs,
    });
  }

  if (blockedUrls.length > 0) {
    await bestEffortSendCdp(client, {
      tabId,
      method: "Network.setBlockedURLs",
      params: { urls: blockedUrls },
      timeoutMs,
    });
  }

  if (quiet && injectQuietPageScript) {
    await bestEffortSendCdp(client, {
      tabId,
      method: "Page.addScriptToEvaluateOnNewDocument",
      params: { source: QUIET_PAGE_BOOTSTRAP },
      timeoutMs,
    });
  }

  return {
    tabId,
    quiet,
    blockedUrls,
  };
}

export async function getPageResources(client, {
  tabId,
  timeoutMs = 15000,
  pageUrl = null,
  resourceTypes = [],
  urlIncludes = [],
  priorityUrlIncludes = [],
  maxResources = 50,
  maxContentResources = 0,
  maxContentBytes = 250000,
  maxConcurrency = 4,
} = {}) {
  if (typeof tabId !== "number") {
    throw new Error("getPageResources requires a numeric tabId.");
  }

  const treePayload = await client.sendCdp({
    tabId,
    method: "Page.getResourceTree",
    params: {},
    timeoutMs,
  });

  const typeFilter = new Set(
    (resourceTypes || [])
      .map((value) => String(value || "").trim())
      .filter(Boolean)
  );
  const urlPatterns = uniqStrings(urlIncludes);
  const priorityPatterns = uniqStrings(priorityUrlIncludes);
  const resourceTree = treePayload?.result?.frameTree || null;
  const resources = flattenFrameResources(resourceTree)
    .filter((resource) => resource.url)
    .filter((resource) => (typeFilter.size ? typeFilter.has(resource.type) : true))
    .filter((resource) => (
      urlPatterns.length
        ? urlPatterns.some((pattern) => resource.url.includes(pattern))
        : true
    ));

  const uniqueResources = uniqStrings(resources.map((resource) => `${resource.frameId || ""}::${resource.url}`))
    .map((key) => resources.find((resource) => `${resource.frameId || ""}::${resource.url}` === key))
    .filter(Boolean)
    .map((resource) => ({
      ...resource,
      score: scorePageResource(resource, {
        pageUrl,
        priorityUrlIncludes: priorityPatterns,
      }),
    }))
    .sort((left, right) => (
      (right.score || 0) - (left.score || 0)
      || (Number(right.size) || 0) - (Number(left.size) || 0)
      || String(left.url || "").localeCompare(String(right.url || ""))
    ))
    .slice(0, Math.max(1, Number(maxResources) || 50));

  const contentLimit = Math.max(0, Number(maxContentResources) || 0);
  const contentTargets = uniqueResources
    .filter((resource) => resource.frameId && resource.url)
    .slice(0, contentLimit);

  if (contentTargets.length > 0) {
    await mapWithConcurrency(contentTargets, Math.min(Number(maxConcurrency) || 4, contentTargets.length), async (resource) => {
      const contentPayload = await bestEffortSendCdp(client, {
        tabId,
        method: "Page.getResourceContent",
        params: {
          frameId: resource.frameId,
          url: resource.url,
        },
        timeoutMs,
      });

      const content = decodeResourceContent(contentPayload?.result, Math.max(1024, Number(maxContentBytes) || 250000));
      if (!content) {
        return;
      }

      resource.content = content;
      resource.base64Encoded = Boolean(contentPayload?.result?.base64Encoded);
    });
  }

  return uniqueResources;
}

export async function getNetworkLogEntries(client, {
  tabId,
  timeoutMs = 15000,
  sinceSequence,
  maxEntries = 50,
  includeBodies = true,
  resourceTypes = [],
  urlIncludes = [],
  consume = false,
} = {}) {
  if (typeof tabId !== "number") {
    throw new Error("getNetworkLogEntries requires a numeric tabId.");
  }

  const result = await client.readNetworkLog({
    tabId,
    sinceSequence,
    maxEntries,
    includeBodies,
    resourceTypes,
    urlIncludes,
    consume,
    timeoutMs,
  });

  return Array.isArray(result?.entries) ? result.entries : [];
}

export async function getNetworkLogMark(client, {
  tabId,
  timeoutMs = 15000,
} = {}) {
  if (typeof tabId !== "number") {
    throw new Error("getNetworkLogMark requires a numeric tabId.");
  }

  const result = await client.getNetworkLogMark({
    tabId,
    timeoutMs,
  });

  return result?.mark || null;
}

export async function waitForPageReady(client, {
  tabId,
  timeoutMs = 20000,
  intervalMs = 250,
  acceptInteractive = true,
  expectedUrl = null,
  initialUrl = null,
} = {}) {
  const deadline = Date.now() + timeoutMs;
  const initialHref = initialUrl || null;
  const initialAlreadyMatches = urlsLikelyMatch(initialHref, expectedUrl);
  let stableHits = 0;
  let consecutiveEvaluationFailures = 0;
  let lastHref = null;
  let lastReadyState = null;

  while (Date.now() < deadline) {
    const state = await cdpEvaluate(client, {
      tabId,
      expression: () => ({
        href: location.href,
        title: document.title,
        readyState: document.readyState,
      }),
      timeoutMs: Math.min(timeoutMs, 5000),
    }).catch(() => null);

    if (!state) {
      consecutiveEvaluationFailures += 1;
      if (consecutiveEvaluationFailures >= 2) {
        const tabs = await client.listTabs().catch(() => []);
        const tab = Array.isArray(tabs) ? tabs.find((item) => item.id === tabId) || null : null;
        if (!tab) {
          throw new Error(`Tab ${tabId} is no longer available while waiting for navigation.`);
        }
      }
      await sleep(Math.min(intervalMs, Math.max(50, deadline - Date.now())));
      continue;
    }

    consecutiveEvaluationFailures = 0;

    const readyState = state?.readyState || null;
    const href = state?.href || null;
    lastHref = href;
    lastReadyState = readyState;
    const matchesExpected = urlsLikelyMatch(href, expectedUrl);
    const interruptionReady = looksLikeInterruptionPage({
      href,
      title: state?.title,
    });
    const movedOffInitial = !initialHref || (href && href !== initialHref) || initialAlreadyMatches;
    const stable =
      href &&
      href !== "about:blank" &&
      (matchesExpected || interruptionReady) &&
      movedOffInitial &&
      (
        readyState === "complete" ||
        (acceptInteractive && readyState === "interactive")
      );

    if (stable) {
      stableHits += 1;
    } else {
      stableHits = 0;
    }

    if (stableHits >= (readyState === "complete" ? 1 : 2)) {
      return {
        tab: {
          id: tabId,
          url: href,
          status: readyState === "complete" ? "complete" : "loading",
        },
        state,
      };
    }

    await sleep(Math.min(intervalMs, Math.max(50, deadline - Date.now())));
  }

  throw new Error(
    `Timed out waiting for tab ${tabId} to finish loading. Last state: ${lastReadyState || "unknown"} @ ${lastHref || "unknown"}`
  );
}

export async function navigateAndWait(client, {
  tabId,
  url,
  createNewTab = false,
  active = false,
  timeoutMs = 20000,
} = {}) {
  const currentState = tabId ? await cdpEvaluate(client, {
    tabId,
    expression: () => location.href,
    timeoutMs: Math.min(timeoutMs, 5000),
  }).catch(() => null) : null;
  const navigation = await client.navigate({ tabId, url, createNewTab, active, timeoutMs });
  const resolvedTabId = navigation?.id ?? tabId;
  const ready = await waitForPageReady(client, {
    tabId: resolvedTabId,
    timeoutMs,
    expectedUrl: url,
    initialUrl: currentState,
  });
  return {
    navigation,
    tabId: resolvedTabId,
    ready,
  };
}

export async function scrollPage(client, {
  tabId,
  stepPx = 1600,
  maxSteps = 6,
  idleMs = 250,
  timeoutMs = 15000,
} = {}) {
  return cdpEvaluate(client, {
    tabId,
    timeoutMs,
    expression: async ({ stepPx: innerStepPx, maxSteps: innerMaxSteps, idleMs: innerIdleMs }) => {
      const snapshots = [];
      for (let index = 0; index < innerMaxSteps; index += 1) {
        window.scrollBy(0, innerStepPx);
        await new Promise((resolve) => setTimeout(resolve, innerIdleMs));
        snapshots.push({
          step: index + 1,
          scrollY: window.scrollY,
          height: document.documentElement.scrollHeight,
        });
      }
      return snapshots;
    },
    args: [{ stepPx, maxSteps, idleMs }],
  });
}

async function captureRuntimeSignals(client, {
  tabId,
  timeoutMs,
  maxResourceEntries,
  maxApiCandidates,
  maxStorageItems,
  maxStorageValueLength,
  maxInlineStateBlocks,
  maxInlineStateChars,
} = {}) {
  return cdpEvaluate(client, {
    tabId,
    timeoutMs,
    expression: ({
      maxResourceEntries: innerMaxResourceEntries,
      maxApiCandidates: innerMaxApiCandidates,
      maxStorageItems: innerMaxStorageItems,
      maxStorageValueLength: innerMaxStorageValueLength,
      maxInlineStateBlocks: innerMaxInlineStateBlocks,
      maxInlineStateChars: innerMaxInlineStateChars,
    }) => {
      function text(value) {
        return String(value ?? "");
      }

      function truncate(value, limit) {
        const raw = text(value);
        if (!Number.isFinite(Number(limit)) || Number(limit) <= 0 || raw.length <= Number(limit)) {
          return raw;
        }
        return `${raw.slice(0, Math.max(0, Number(limit) - 14))}...[truncated]`;
      }

      function numeric(value, digits = 3) {
        const parsed = Number(value);
        if (!Number.isFinite(parsed)) {
          return null;
        }
        return Number(parsed.toFixed(digits));
      }

      function firstNonEmpty(values) {
        for (const value of values) {
          const normalized = text(value).trim();
          if (normalized) {
            return normalized;
          }
        }
        return null;
      }

      function countBy(values, selector) {
        return values.reduce((accumulator, entry) => {
          const key = text(selector(entry) || "unknown");
          accumulator[key] = (accumulator[key] || 0) + 1;
          return accumulator;
        }, {});
      }

      function summarizeUrl(value) {
        try {
          const parsed = new URL(value, location.href);
          return {
            url: parsed.toString(),
            origin: parsed.origin,
            hostname: parsed.hostname,
            pathname: parsed.pathname,
            search: parsed.search || null,
          };
        } catch {
          const fallback = text(value).trim();
          return {
            url: fallback || null,
            origin: null,
            hostname: null,
            pathname: null,
            search: null,
          };
        }
      }

      function snapshotStorage(storage) {
        const result = {
          supported: true,
          count: 0,
          keys: [],
          items: [],
        };

        try {
          const totalCount = Number(storage?.length) || 0;
          result.count = totalCount;

          for (let index = 0; index < Math.min(totalCount, innerMaxStorageItems); index += 1) {
            const key = storage.key(index);
            if (!key) {
              continue;
            }

            let rawValue = "";
            try {
              rawValue = text(storage.getItem(key));
            } catch {
              rawValue = "";
            }

            result.keys.push(key);
            result.items.push({
              key,
              length: rawValue.length,
              looksJson: /^[\[{]/.test(rawValue.trim()),
              preview: truncate(rawValue, innerMaxStorageValueLength) || null,
            });
          }
        } catch {
          result.supported = false;
          result.count = null;
        }

        return result;
      }

      function summarizeValue(value, depth = 0) {
        if (value == null) {
          return null;
        }

        if (typeof value === "string") {
          return {
            type: "string",
            length: value.length,
            preview: truncate(value, innerMaxStorageValueLength),
          };
        }

        if (typeof value === "number" || typeof value === "boolean") {
          return {
            type: typeof value,
            value,
          };
        }

        if (typeof value === "function") {
          return {
            type: "function",
          };
        }

        if (Array.isArray(value)) {
          return {
            type: "array",
            length: value.length,
            preview: depth >= 1
              ? []
              : value.slice(0, 4).map((item) => summarizeValue(item, depth + 1)),
          };
        }

        if (typeof value === "object") {
          let keys = [];
          try {
            keys = Object.keys(value);
          } catch {
            keys = [];
          }

          return {
            type: "object",
            size: keys.length,
            keys: keys.slice(0, 12),
          };
        }

        return {
          type: typeof value,
        };
      }

      function collectGlobalState() {
        let keys = [];

        try {
          keys = Object.getOwnPropertyNames(globalThis)
            .filter((key) => /^(__|APP_|NUXT|NEXT|INITIAL|PRELOADED|APOLLO|REDUX|BOOTSTRAP|STATE|DATA|STORE|CACHE)/i.test(key)
              || /(state|data|cache|store|apollo|redux|query|bootstrap)/i.test(key))
            .slice(0, 24);
        } catch {
          keys = [];
        }

        return keys.map((key) => {
          let value = null;
          try {
            value = globalThis[key];
          } catch {
            value = null;
          }

          return {
            key,
            hasCommentSignal: /comment|review|reply/i.test(key),
            summary: summarizeValue(value),
          };
        });
      }

      function collectInlineStateBlocks() {
        const output = [];
        const inlinePatterns = [
          /__NEXT_DATA__/,
          /__NUXT__/,
          /__INITIAL_STATE__/,
          /__PRELOADED_STATE__/,
          /APOLLO_STATE/i,
          /redux/i,
          /graphql/i,
          /bootstrap/i,
          /window\.__/i,
          /self\.__/i,
        ];

        for (const script of Array.from(document.scripts).slice(0, 48)) {
          if (output.length >= innerMaxInlineStateBlocks) {
            break;
          }

          const type = text(script.type).toLowerCase();
          const raw = text(script.textContent || "");
          const trimmed = raw.trim();
          const src = text(script.src || "").trim();
          const looksState =
            script.id === "__NEXT_DATA__"
            || type === "application/json"
            || type === "application/ld+json"
            || script.hasAttribute("data-state")
            || script.hasAttribute("data-hypernova-key")
            || inlinePatterns.some((pattern) => pattern.test(trimmed) || pattern.test(src));

          if (!looksState) {
            continue;
          }

          output.push({
            id: script.id || null,
            type: type || null,
            src: src || null,
            size: trimmed.length || src.length || 0,
            looksJson: /^[\[{]/.test(trimmed),
            stateHint: firstNonEmpty([
              script.id,
              script.getAttribute("data-state"),
              script.getAttribute("data-hypernova-key"),
              script.getAttribute("type"),
            ]),
            preview: trimmed ? truncate(trimmed, innerMaxInlineStateChars) : null,
          });
        }

        return output;
      }

      function classifyResource(entry) {
        const summary = summarizeUrl(entry.name);
        const urlLower = text(summary.url).toLowerCase();
        const initiatorType = text(entry.initiatorType || "other").toLowerCase() || "other";
        const apiLike =
          ["fetch", "xmlhttprequest", "beacon"].includes(initiatorType)
          || /\/(api|graphql|comments?|reviews?|replies|discussion|thread|posts?|feed|ajax|data)\b/.test(urlLower)
          || /\.json(?:$|[?#])/.test(urlLower)
          || /[?&](format|output)=json(?:$|[&#])/.test(urlLower);

        let category = initiatorType;
        if (/graphql/.test(urlLower)) {
          category = "graphql";
        } else if (/comments?|reviews?|replies/.test(urlLower)) {
          category = "comments";
        } else if (/\/api\b/.test(urlLower)) {
          category = "api";
        } else if (/\.json(?:$|[?#])/.test(urlLower)) {
          category = "json";
        }

        return {
          url: summary.url,
          origin: summary.origin,
          hostname: summary.hostname,
          pathname: summary.pathname,
          search: summary.search,
          initiatorType,
          category,
          apiLike,
          duration: numeric(entry.duration),
          startTime: numeric(entry.startTime),
          transferSize: numeric(entry.transferSize, 0),
          encodedBodySize: numeric(entry.encodedBodySize, 0),
          decodedBodySize: numeric(entry.decodedBodySize, 0),
          nextHopProtocol: text(entry.nextHopProtocol) || null,
          renderBlockingStatus: text(entry.renderBlockingStatus) || null,
          deliveryType: text(entry.deliveryType) || null,
        };
      }

      const navigationEntry = performance.getEntriesByType("navigation")[0] || null;
      const paintEntries = performance.getEntriesByType("paint")
        .map((entry) => ({
          name: entry.name || null,
          startTime: numeric(entry.startTime),
          duration: numeric(entry.duration),
        }))
        .filter((entry) => entry.name);

      const resources = performance.getEntriesByType("resource")
        .map((entry) => classifyResource(entry))
        .filter((entry) => entry.url);

      const apiCandidates = resources
        .filter((entry) => entry.apiLike)
        .sort((left, right) => (
          (right.transferSize || right.encodedBodySize || 0) - (left.transferSize || left.encodedBodySize || 0)
          || (right.duration || 0) - (left.duration || 0)
        ))
        .slice(0, innerMaxApiCandidates);

      const largestResources = resources
        .slice()
        .sort((left, right) => (
          (right.transferSize || right.encodedBodySize || 0) - (left.transferSize || left.encodedBodySize || 0)
          || (right.duration || 0) - (left.duration || 0)
        ))
        .slice(0, innerMaxResourceEntries);

      return {
        document: {
          url: location.href,
          title: document.title,
          referrer: document.referrer || null,
          readyState: document.readyState,
          visibilityState: document.visibilityState,
          hidden: Boolean(document.hidden),
          lang: document.documentElement.lang || null,
        },
        performance: {
          navigation: navigationEntry ? {
            type: navigationEntry.type || null,
            duration: numeric(navigationEntry.duration),
            domContentLoadedEventEnd: numeric(navigationEntry.domContentLoadedEventEnd),
            loadEventEnd: numeric(navigationEntry.loadEventEnd),
            responseEnd: numeric(navigationEntry.responseEnd),
            transferSize: numeric(navigationEntry.transferSize, 0),
            encodedBodySize: numeric(navigationEntry.encodedBodySize, 0),
            decodedBodySize: numeric(navigationEntry.decodedBodySize, 0),
            nextHopProtocol: text(navigationEntry.nextHopProtocol) || null,
          } : null,
          paints: paintEntries,
          resourceSummary: {
            total: resources.length,
            sameOriginCount: resources.filter((entry) => entry.origin === location.origin).length,
            apiLikeCount: apiCandidates.length,
            totalTransferSize: resources.reduce((sum, entry) => sum + (entry.transferSize || 0), 0),
            initiatorCounts: resources.reduce((accumulator, entry) => {
              const key = entry.initiatorType || "unknown";
              accumulator[key] = (accumulator[key] || 0) + 1;
              return accumulator;
            }, {}),
            categoryCounts: countBy(resources.filter((entry) => entry.apiLike), (entry) => entry.category),
          },
          apiCandidates,
          largestResources,
        },
        application: {
          storage: {
            localStorage: snapshotStorage(globalThis.localStorage),
            sessionStorage: snapshotStorage(globalThis.sessionStorage),
          },
          serviceWorker: {
            supported: "serviceWorker" in navigator,
            controlled: Boolean(navigator.serviceWorker?.controller),
            controllerUrl: navigator.serviceWorker?.controller?.scriptURL || null,
          },
          manifestLinks: Array.from(document.querySelectorAll("link[rel='manifest']"))
            .map((element) => element.href || element.getAttribute("href") || null)
            .filter(Boolean)
            .slice(0, 4),
          globalState: collectGlobalState(),
          inlineStateBlocks: collectInlineStateBlocks(),
        },
      };
    },
    args: [{
      maxResourceEntries,
      maxApiCandidates,
      maxStorageItems,
      maxStorageValueLength,
      maxInlineStateBlocks,
      maxInlineStateChars,
    }],
  }).catch(() => null);
}

async function captureIndexedDbSnapshot(client, {
  tabId,
  origin,
  timeoutMs,
  maxIndexedDbDatabases,
  maxIndexedDbStores,
} = {}) {
  if (!origin) {
    return null;
  }

  await bestEffortSendCdp(client, {
    tabId,
    method: "IndexedDB.enable",
    params: {},
    timeoutMs,
  });

  const databaseNamesResult = await bestEffortCdpResult(client, {
    tabId,
    method: "IndexedDB.requestDatabaseNames",
    params: { securityOrigin: origin },
    timeoutMs,
  });

  const databaseNames = Array.isArray(databaseNamesResult?.databaseNames)
    ? databaseNamesResult.databaseNames.filter(Boolean)
    : [];
  const limitedNames = databaseNames.slice(0, maxIndexedDbDatabases);
  const databases = await Promise.all(limitedNames.map(async (databaseName) => {
    const databaseResult = await bestEffortCdpResult(client, {
      tabId,
      method: "IndexedDB.requestDatabase",
      params: {
        securityOrigin: origin,
        databaseName,
      },
      timeoutMs,
    });

    const database = databaseResult?.databaseWithObjectStores;
    const objectStores = Array.isArray(database?.objectStores) ? database.objectStores : [];

    return {
      name: database?.name || databaseName,
      version: toFiniteNumber(database?.version, 0),
      objectStoreCount: objectStores.length,
      objectStores: objectStores.slice(0, maxIndexedDbStores).map((store) => ({
        name: store?.name || null,
        autoIncrement: Boolean(store?.autoIncrement),
        keyPath: Array.isArray(store?.keyPath) ? store.keyPath : (store?.keyPath ? [store.keyPath] : []),
        indexes: Array.isArray(store?.indexes)
          ? store.indexes.slice(0, 8).map((index) => ({
            name: index?.name || null,
            keyPath: Array.isArray(index?.keyPath) ? index.keyPath : (index?.keyPath ? [index.keyPath] : []),
            unique: Boolean(index?.unique),
            multiEntry: Boolean(index?.multiEntry),
          }))
          : [],
      })),
    };
  }));

  return {
    origin,
    databaseCount: databaseNames.length,
    truncated: databaseNames.length > limitedNames.length,
    databases: databases.filter(Boolean),
  };
}

async function captureCacheStorageSnapshot(client, {
  tabId,
  origin,
  timeoutMs,
  maxCacheNames,
} = {}) {
  if (!origin) {
    return null;
  }

  const cacheResult = await bestEffortCdpResult(client, {
    tabId,
    method: "CacheStorage.requestCacheNames",
    params: { securityOrigin: origin },
    timeoutMs,
  });

  const caches = Array.isArray(cacheResult?.caches) ? cacheResult.caches : [];
  return {
    origin,
    count: caches.length,
    truncated: caches.length > maxCacheNames,
    caches: caches.slice(0, maxCacheNames).map((cache) => ({
      cacheName: cache?.cacheName || null,
      cacheId: cache?.cacheId || null,
      securityOrigin: cache?.securityOrigin || origin,
    })),
  };
}

export async function capturePageSignals(client, {
  tabId,
  pageUrl = null,
  timeoutMs = 15000,
  maxResourceEntries = 12,
  maxApiCandidates = 12,
  maxStorageItems = 12,
  maxStorageValueLength = 240,
  maxInlineStateBlocks = 8,
  maxInlineStateChars = 1200,
  maxCookieCount = 12,
  maxIndexedDbDatabases = 4,
  maxIndexedDbStores = 8,
  maxCacheNames = 8,
} = {}) {
  if (typeof tabId !== "number") {
    throw new Error("capturePageSignals requires a numeric tabId.");
  }

  const origin = originForUrl(pageUrl);

  await Promise.all([
    bestEffortSendCdp(client, {
      tabId,
      method: "Performance.enable",
      params: {},
      timeoutMs,
    }),
    bestEffortSendCdp(client, {
      tabId,
      method: "Network.enable",
      params: {},
      timeoutMs,
    }),
  ]);

  const [
    runtime,
    performanceResult,
    cookiesResult,
    quotaResult,
    manifestResult,
    indexedDb,
    cacheStorage,
  ] = await Promise.all([
    captureRuntimeSignals(client, {
      tabId,
      timeoutMs,
      maxResourceEntries,
      maxApiCandidates,
      maxStorageItems,
      maxStorageValueLength,
      maxInlineStateBlocks,
      maxInlineStateChars,
    }),
    bestEffortCdpResult(client, {
      tabId,
      method: "Performance.getMetrics",
      params: {},
      timeoutMs,
    }),
    bestEffortCdpResult(client, {
      tabId,
      method: "Network.getCookies",
      params: pageUrl ? { urls: [pageUrl] } : {},
      timeoutMs,
    }),
    origin ? bestEffortCdpResult(client, {
      tabId,
      method: "Storage.getUsageAndQuota",
      params: { origin },
      timeoutMs,
    }) : Promise.resolve(null),
    bestEffortCdpResult(client, {
      tabId,
      method: "Page.getAppManifest",
      params: {},
      timeoutMs,
    }),
    captureIndexedDbSnapshot(client, {
      tabId,
      origin,
      timeoutMs,
      maxIndexedDbDatabases,
      maxIndexedDbStores,
    }),
    captureCacheStorageSnapshot(client, {
      tabId,
      origin,
      timeoutMs,
      maxCacheNames,
    }),
  ]);

  const cookies = Array.isArray(cookiesResult?.cookies)
    ? cookiesResult.cookies.map((cookie) => summarizeCookie(cookie)).filter(Boolean)
    : [];
  const manifestErrors = Array.isArray(manifestResult?.errors)
    ? manifestResult.errors.map((error) => ({
      message: error?.message || error?.error || null,
      critical: Boolean(error?.critical),
      line: toFiniteNumber(error?.line, 0),
      column: toFiniteNumber(error?.column, 0),
    }))
    : [];

  return {
    pageUrl: runtime?.document?.url || pageUrl || null,
    network: {
      cookieCount: cookies.length,
      cookies: cookies.slice(0, maxCookieCount),
      resourceSummary: runtime?.performance?.resourceSummary || null,
      apiCandidates: runtime?.performance?.apiCandidates || [],
      largestResources: runtime?.performance?.largestResources || [],
    },
    performance: {
      metrics: summarizePerformanceMetrics(performanceResult?.metrics),
      navigation: runtime?.performance?.navigation || null,
      paints: runtime?.performance?.paints || [],
    },
    application: {
      quota: quotaResult ? {
        origin,
        usage: toFiniteNumber(quotaResult.usage, 0),
        quota: toFiniteNumber(quotaResult.quota, 0),
        overrideActive: Boolean(quotaResult.overrideActive),
        usageBreakdown: summarizeUsageBreakdown(quotaResult.usageBreakdown),
      } : null,
      manifest: manifestResult ? {
        url: manifestResult.url || null,
        hasData: Boolean(manifestResult.data),
        dataPreview: manifestResult.data ? truncateString(manifestResult.data, maxInlineStateChars) : null,
        errors: manifestErrors,
      } : null,
      storage: runtime?.application?.storage || null,
      serviceWorker: runtime?.application?.serviceWorker || null,
      manifestLinks: runtime?.application?.manifestLinks || [],
      globalState: runtime?.application?.globalState || [],
      inlineStateBlocks: runtime?.application?.inlineStateBlocks || [],
      indexedDb,
      cacheStorage,
    },
  };
}
