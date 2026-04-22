import { capturePageSignals } from "../cdp-helpers.mjs";
import { normalizeWhitespace, sleep, toAbsoluteUrl, truncate, uniqueBy } from "../crawl-utils.mjs";

const NETWORK_RESOURCE_HINT_PATTERN = /(api|graphql|comment|review|rating|feedback|discussion|thread|conversation|reply|post|story)/i;
const NETWORK_MIME_HINT_PATTERN = /(json|javascript|graphql|text\/plain)/i;

function detectSearchProvider(url) {
  try {
    const hostname = new URL(url).hostname.toLowerCase();
    if (/^(.+\.)?google\./i.test(hostname)) {
      return "google";
    }
    if (/^(.+\.)?bing\.com$/i.test(hostname)) {
      return "bing";
    }
    if (/^(.+\.)?baidu\.com$/i.test(hostname)) {
      return "baidu";
    }
    if (/^(.+\.)?duckduckgo\.com$/i.test(hostname)) {
      return "duckduckgo";
    }
  } catch {
    return null;
  }

  return null;
}

function decodeGoogleTarget(rawUrl) {
  try {
    const parsed = new URL(rawUrl);
    if (!/^(.+\.)?google\./i.test(parsed.hostname) || parsed.pathname !== "/url") {
      return rawUrl;
    }
    return parsed.searchParams.get("q") || parsed.searchParams.get("url") || rawUrl;
  } catch {
    return rawUrl;
  }
}

function decodeBingTarget(rawUrl) {
  try {
    const parsed = new URL(rawUrl);
    const encodedTarget = parsed.searchParams.get("u");
    if (!encodedTarget) {
      return rawUrl;
    }

    const base64Payload = encodedTarget.startsWith("a1")
      ? encodedTarget.slice(2)
      : encodedTarget;
    const decoded = Buffer.from(base64Payload, "base64").toString("utf8");
    if (decoded.startsWith("http://") || decoded.startsWith("https://")) {
      return decoded;
    }
    if (decoded.startsWith("/")) {
      return new URL(decoded, parsed.origin).toString();
    }
    return rawUrl;
  } catch {
    return rawUrl;
  }
}

function decodeDuckDuckGoTarget(rawUrl) {
  try {
    const parsed = new URL(rawUrl);
    if (!/^(.+\.)?duckduckgo\.com$/i.test(parsed.hostname) || parsed.pathname !== "/l/") {
      return rawUrl;
    }
    return parsed.searchParams.get("uddg") || rawUrl;
  } catch {
    return rawUrl;
  }
}

function normalizeListingUrl(rawUrl, pageUrl) {
  const provider = detectSearchProvider(pageUrl);
  const absolute = toAbsoluteUrl(rawUrl, pageUrl) || rawUrl;

  switch (provider) {
    case "google":
      return decodeGoogleTarget(absolute);
    case "bing":
      return decodeBingTarget(absolute);
    case "duckduckgo":
      return decodeDuckDuckGoTarget(absolute);
    default:
      return absolute;
  }
}

function isLikelySearchResultsUrl(candidateUrl, pageUrl) {
  try {
    const candidate = new URL(candidateUrl);
    const source = new URL(pageUrl);
    const sameHost = candidate.hostname.toLowerCase() === source.hostname.toLowerCase();
    if (!sameHost) {
      return false;
    }

    return /\/(search|news\/search)\b/i.test(candidate.pathname);
  } catch {
    return false;
  }
}

function safeJsonParse(value) {
  try {
    return JSON.parse(value);
  } catch {
    return null;
  }
}

function extractBalancedJsonSegment(value) {
  const source = String(value || "");
  const start = source.search(/[\[{]/);
  if (start < 0) {
    return null;
  }

  const stack = [];
  let quote = null;
  let escaped = false;

  for (let index = start; index < source.length; index += 1) {
    const character = source[index];

    if (quote) {
      if (escaped) {
        escaped = false;
      } else if (character === "\\") {
        escaped = true;
      } else if (character === quote) {
        quote = null;
      }
      continue;
    }

    if (character === "\"" || character === "'") {
      quote = character;
      continue;
    }

    if (character === "{" || character === "[") {
      stack.push(character);
      continue;
    }

    if (character === "}" || character === "]") {
      const last = stack[stack.length - 1];
      if ((character === "}" && last === "{") || (character === "]" && last === "[")) {
        stack.pop();
        if (stack.length === 0) {
          return source.slice(start, index + 1);
        }
      }
    }
  }

  return null;
}

function parseNdjsonPayload(value) {
  const lines = String(value || "")
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean);
  if (lines.length <= 1) {
    return null;
  }

  const parsed = lines
    .map((line) => safeJsonParse(line))
    .filter((line) => line !== null);

  return parsed.length >= 2 ? parsed : null;
}

function parseStructuredPayload(value, depth = 0) {
  if (depth > 3 || typeof value !== "string") {
    return null;
  }

  const trimmed = value.trim();
  if (!trimmed) {
    return null;
  }

  const direct = safeJsonParse(trimmed);
  if (direct !== null) {
    if (typeof direct === "string" && direct.trim() && direct.trim() !== trimmed) {
      return parseStructuredPayload(direct, depth + 1) || direct;
    }
    return direct;
  }

  const ndjson = parseNdjsonPayload(trimmed);
  if (ndjson) {
    return ndjson;
  }

  const jsonpMatch = trimmed.match(/^[\w$.]+\(([\s\S]+)\);?$/);
  if (jsonpMatch) {
    const parsed = parseStructuredPayload(jsonpMatch[1], depth + 1);
    if (parsed !== null) {
      return parsed;
    }
  }

  const assignmentIndex = trimmed.indexOf("=");
  if (assignmentIndex > 0 && assignmentIndex < 160) {
    const parsed = parseStructuredPayload(trimmed.slice(assignmentIndex + 1).replace(/;+\s*$/, ""), depth + 1);
    if (parsed !== null) {
      return parsed;
    }
  }

  const balanced = extractBalancedJsonSegment(trimmed);
  if (balanced && balanced !== trimmed) {
    const parsed = safeJsonParse(balanced) ?? parseStructuredPayload(balanced, depth + 1);
    if (parsed !== null) {
      return parsed;
    }
  }

  return null;
}

function normalizeResourceHint(value, baseUrl) {
  const text = String(value || "").trim();
  if (!text) {
    return null;
  }

  try {
    const parsed = new URL(text, baseUrl);
    return `${parsed.pathname || ""}${parsed.search || ""}` || parsed.toString();
  } catch {
    return text;
  }
}

function collectPriorityResourceHints(pageSignals, pageUrl, maxHints = 12) {
  const hints = [];
  for (const candidate of pageSignals?.network?.apiCandidates || []) {
    if (candidate?.url) {
      hints.push(candidate.url);
    }
    if (candidate?.pathname) {
      hints.push(candidate.pathname);
    }
  }

  return uniqueBy(
    hints
      .map((hint) => normalizeResourceHint(hint, pageUrl))
      .filter(Boolean),
    (hint) => hint
  ).slice(0, Math.max(1, Number(maxHints) || 12));
}

function firstNonEmpty(values) {
  for (const value of values) {
    const normalized = normalizeWhitespace(value);
    if (normalized) {
      return normalized;
    }
  }
  return null;
}

function readEntityName(value) {
  if (!value) {
    return null;
  }
  if (typeof value === "string") {
    return normalizeWhitespace(value);
  }
  if (Array.isArray(value)) {
    return firstNonEmpty(value.map(readEntityName));
  }
  if (typeof value === "object") {
    return firstNonEmpty([
      value.name,
      value.fullName,
      value.displayName,
      value.username,
      value.handle,
      value.screenName,
    ]);
  }
  return null;
}

function walkObject(root, visit, maxNodes = 8000) {
  const stack = [root];
  let visited = 0;

  while (stack.length > 0 && visited < maxNodes) {
    const node = stack.pop();
    if (!node || typeof node !== "object") {
      continue;
    }

    visited += 1;
    visit(node);

    if (Array.isArray(node)) {
      for (let index = Math.min(node.length, 50) - 1; index >= 0; index -= 1) {
        const child = node[index];
        if (child && typeof child === "object") {
          stack.push(child);
        }
      }
    } else {
      const values = Object.values(node).slice(0, 50);
      for (let index = values.length - 1; index >= 0; index -= 1) {
        const child = values[index];
        if (child && typeof child === "object") {
          stack.push(child);
        }
      }
    }
  }
}

function buildCommentKey(comment) {
  return comment.commentId || `${comment.author || ""}::${comment.text || ""}::${comment.publishedText || ""}`;
}

function normalizeExtractedComment(comment, source, baseUrl) {
  const normalized = {
    commentId: normalizeWhitespace(comment.commentId) || null,
    author: normalizeWhitespace(comment.author) || null,
    authorUrl: toAbsoluteUrl(comment.authorUrl, baseUrl),
    text: truncate(normalizeWhitespace(comment.text), 4000) || null,
    publishedText: normalizeWhitespace(comment.publishedText) || null,
    likeCountText: normalizeWhitespace(comment.likeCountText) || null,
    replyCount: Number.isFinite(Number(comment.replyCount)) ? Number(comment.replyCount) : 0,
    rating: normalizeWhitespace(comment.rating) || null,
    source,
  };

  return normalized.text ? normalized : null;
}

function extractCommentLikeNode(node, source, baseUrl) {
  const typeText = normalizeWhitespace([
    node["@type"],
    node.type,
    node.kind,
    node.__typename,
  ].filter(Boolean).join(" ")).toLowerCase();
  const keyText = Object.keys(node).join(" ").toLowerCase();
  const author = firstNonEmpty([
    readEntityName(node.author),
    readEntityName(node.user),
    readEntityName(node.creator),
    readEntityName(node.owner),
    readEntityName(node.account),
  ]);
  const body = firstNonEmpty([
    node.commentBody,
    node.reviewBody,
    node.contentText,
    node.body,
    node.text,
    node.message,
    node.content,
    node.description,
  ]);
  const publishedText = firstNonEmpty([
    node.datePublished,
    node.createdAt,
    node.publishedAt,
    node.updatedAt,
    node.timestamp,
    node.time,
  ]);
  const likeCountText = firstNonEmpty([
    node.likeCount,
    node.likes,
    node.voteCount,
    node.score,
  ]);
  const rating = firstNonEmpty([
    node.reviewRating?.ratingValue,
    node.ratingValue,
    node.rating,
    node.score,
  ]);
  const replyCount = firstNonEmpty([
    node.replyCount,
    node.childrenCommentCount,
    node.numReplies,
    node.totalReplies,
  ]);
  const commentId = firstNonEmpty([
    node.commentId,
    node.reviewId,
    node.id,
    node.uuid,
  ]);

  const looksCommentish =
    /comment|review|discussionforumposting|answer|reply|post/i.test(typeText)
    || /comment|review|reply|author|rating|published|created/.test(keyText)
    || Boolean(node.commentId || node.reviewBody || node.commentBody);

  if (!looksCommentish || !body) {
    return null;
  }

  return normalizeExtractedComment({
    commentId,
    author,
    authorUrl: node.author?.url || node.user?.url || null,
    text: body,
    publishedText,
    likeCountText,
    replyCount,
    rating,
  }, source, baseUrl);
}

function scoreNetworkResource(resource) {
  let score = 0;
  if (resource.type === "Fetch" || resource.type === "XHR") {
    score += 10;
  }
  if (NETWORK_RESOURCE_HINT_PATTERN.test(resource.url || "")) {
    score += 8;
  }
  if (NETWORK_MIME_HINT_PATTERN.test(resource.mimeType || "")) {
    score += 5;
  }
  if (typeof resource.content === "string" && resource.content.length > 0) {
    score += 4;
  }
  return score;
}

function normalizeLoggedNetworkResource(entry) {
  if (!entry?.url) {
    return null;
  }

  return {
    requestId: entry.requestId || null,
    url: entry.url,
    type: entry.resourceType || entry.type || null,
    mimeType: entry.mimeType || null,
    status: entry.status ?? null,
    method: entry.method || null,
    encodedDataLength: entry.encodedDataLength ?? null,
    content: typeof entry.content === "string" ? entry.content : null,
  };
}

function mergeNetworkResources(...groups) {
  return uniqueBy(
    groups
      .flat()
      .map((resource) => resource || null)
      .filter(Boolean),
    (resource) => resource.requestId || `${resource.url || ""}::${resource.type || ""}::${resource.status ?? ""}`
  );
}

function mergeCountMaps(...maps) {
  const merged = {};
  for (const map of maps) {
    if (!map || typeof map !== "object") {
      continue;
    }
    for (const [key, value] of Object.entries(map)) {
      if (!key) {
        continue;
      }
      merged[key] = (merged[key] || 0) + (Number(value) || 0);
    }
  }
  return merged;
}

async function collectIncrementalNetworkResponses(context, {
  mark,
  priorityResourceHints = [],
  maxEntries,
  attempts = 3,
  delayMs = 250,
} = {}) {
  if (!mark || typeof context.network?.getResponsesSinceMark !== "function") {
    return [];
  }

  let merged = [];
  const mergeEntries = (entries) => {
    merged = mergeNetworkResources(
      merged,
      (entries || []).map((entry) => normalizeLoggedNetworkResource(entry)).filter(Boolean)
    );
    return merged;
  };

  for (let attempt = 0; attempt < Math.max(1, attempts); attempt += 1) {
    const entries = await context.network.getResponsesSinceMark(mark, {
      resourceTypes: ["XHR", "Fetch"],
      urlIncludes: priorityResourceHints.length ? priorityResourceHints : undefined,
      maxEntries,
      includeBodies: true,
    }).catch(() => []);
    const current = mergeEntries(entries);
    if (current.length > 0) {
      return current;
    }
    if (attempt < attempts - 1) {
      await sleep(delayMs);
    }
  }

  for (let attempt = 0; attempt < Math.max(1, attempts); attempt += 1) {
    const entries = await context.network.getResponsesSinceMark(mark, {
      resourceTypes: ["XHR", "Fetch"],
      maxEntries,
      includeBodies: true,
    }).catch(() => []);
    const current = mergeEntries(entries);
    if (current.length > 0) {
      return current;
    }
    if (attempt < attempts - 1) {
      await sleep(delayMs);
    }
  }

  return mergeNetworkResources(merged);
}

function extractCommentsFromNetworkResources(resources, baseUrl, maxComments) {
  const selectedResources = (resources || [])
    .filter((resource) => typeof resource.content === "string" && resource.content.trim())
    .sort((left, right) => scoreNetworkResource(right) - scoreNetworkResource(left));
  const comments = [];
  const seen = new Set();

  for (const resource of selectedResources) {
    if (comments.length >= maxComments) {
      break;
    }

    const payload = parseStructuredPayload(resource.content);
    if (!payload || typeof payload !== "object") {
      continue;
    }

    walkObject(payload, (node) => {
      if (comments.length >= maxComments) {
        return;
      }
      const comment = extractCommentLikeNode(node, `network:${resource.type || "resource"}`, baseUrl);
      if (!comment) {
        return;
      }

      const key = buildCommentKey(comment);
      if (!key || seen.has(key)) {
        return;
      }
      seen.add(key);
      comments.push(comment);
    });
  }

  return comments.slice(0, maxComments);
}

function summarizeStructuredNetworkPayload(resource) {
  const payload = parseStructuredPayload(resource?.content);
  if (!payload || typeof payload !== "object") {
    return null;
  }

  const entityTypes = new Set();
  const sampleTitles = [];
  const topLevelKeys = Array.isArray(payload) ? [] : Object.keys(payload).slice(0, 16);
  let commentLikeCount = 0;
  let entityLikeCount = 0;
  let nodeCount = 0;

  walkObject(payload, (node) => {
    nodeCount += 1;

    const typeText = normalizeWhitespace([
      node?.["@type"],
      node?.type,
      node?.kind,
      node?.__typename,
    ].filter(Boolean).join(" "));
    if (typeText) {
      entityLikeCount += 1;
      entityTypes.add(typeText);
      if (/comment|review|reply/i.test(typeText)) {
        commentLikeCount += 1;
      }
    }

    const keyText = typeof node === "object" ? Object.keys(node).join(" ").toLowerCase() : "";
    if (/comment|review|reply/.test(keyText)) {
      commentLikeCount += 1;
    }

    const title = firstNonEmpty([
      node?.headline,
      node?.title,
      node?.name,
      node?.description,
    ]);
    if (title && sampleTitles.length < 4) {
      sampleTitles.push(truncate(title, 180));
    }
  }, 4000);

  return {
    url: resource.url,
    type: resource.type || null,
    mimeType: resource.mimeType || null,
    payloadKind: Array.isArray(payload) ? "array" : "object",
    arrayLength: Array.isArray(payload) ? payload.length : null,
    topLevelKeys,
    entityTypes: Array.from(entityTypes).slice(0, 12),
    sampleTitles: uniqueBy(sampleTitles, (value) => value).slice(0, 4),
    commentLikeCount,
    entityLikeCount,
    nodeCount,
    contentLength: typeof resource.content === "string" ? resource.content.length : 0,
  };
}

function extractStructuredPayloadsFromNetworkResources(resources, maxPayloads) {
  return uniqueBy(
    (resources || [])
      .map((resource) => summarizeStructuredNetworkPayload(resource))
      .filter(Boolean)
      .sort((left, right) => (
        (right.commentLikeCount || 0) - (left.commentLikeCount || 0)
        || (right.entityLikeCount || 0) - (left.entityLikeCount || 0)
        || (right.contentLength || 0) - (left.contentLength || 0)
      )),
    (payload) => payload.url
  ).slice(0, Math.max(1, Number(maxPayloads) || 8));
}

function extractGenericListingPage(options = {}) {
  const maxItems = Math.max(1, Number(options.maxItems) || 20);

  function text(value) {
    return String(value ?? "").replace(/\s+/g, " ").trim();
  }

  function uniqueItemsBy(items, keySelector) {
    const output = [];
    const seen = new Set();
    for (const item of items) {
      const key = keySelector(item);
      if (!key || seen.has(key)) {
        continue;
      }
      seen.add(key);
      output.push(item);
    }
    return output;
  }

  function toAbsolute(value) {
    try {
      return new URL(value, location.href).toString();
    } catch {
      return null;
    }
  }

  function readSnippet(node) {
    if (!node) {
      return null;
    }

    const snippetNode = node.querySelector?.(
      ".b_caption, .b_snippet, [data-content-feature='1'], .VwiC3b, .MUxGbd, .c-abstract, .result-op, .result__snippet, .snippet"
    );

    return text(
      snippetNode?.innerText ||
      node.querySelector?.("p")?.innerText ||
      node.innerText ||
      ""
    ) || null;
  }

  function shouldIgnoreLink(node, rawHref, title) {
    if (!rawHref || !title) {
      return true;
    }

    const lowered = String(rawHref).trim().toLowerCase();
    if (
      lowered.startsWith("#") ||
      lowered.startsWith("javascript:") ||
      lowered.startsWith("mailto:") ||
      lowered.startsWith("tel:") ||
      lowered.startsWith("data:")
    ) {
      return true;
    }

    if (node.closest?.("nav, header, footer, aside")) {
      return true;
    }

    return /^(sign in|login|home|next|previous|more|settings|privacy|terms)$/i.test(title);
  }

  function parseJson(value) {
    try {
      return JSON.parse(value);
    } catch {
      return null;
    }
  }

  function pushItem(output, seenUrls, item) {
    if (!item?.url || !item?.title || seenUrls.has(item.url)) {
      return;
    }
    seenUrls.add(item.url);
    output.push(item);
  }

  function buildProviderItems(provider) {
    const items = [];
    const seenUrls = new Set();
    const strategies = [];

    if (provider === "google") {
      strategies.push("google-dom");
      const cards = Array.from(document.querySelectorAll("div.g, div[data-snc], a h3"));
      for (const card of cards) {
        const container = card.closest ? card.closest("div.g, div[data-snc], a") || card : card;
        const link = container.matches?.("a[href]") ? container : container.querySelector?.("a[href]");
        const title = text(link?.querySelector?.("h3")?.innerText || link?.innerText || container.querySelector?.("h3")?.innerText || "");
        const url = toAbsolute(link?.getAttribute?.("href") || link?.href);
        if (shouldIgnoreLink(link || container, url, title)) {
          continue;
        }
        pushItem(items, seenUrls, {
          kind: "search-result",
          rank: items.length + 1,
          url,
          title,
          snippet: readSnippet(container),
        });
        if (items.length >= maxItems) {
          break;
        }
      }
    } else if (provider === "bing") {
      strategies.push("bing-dom");
      const cards = Array.from(document.querySelectorAll("li.b_algo, .b_ans, .b_nwsAns, li.b_pag"));
      for (const card of cards) {
        const link = card.querySelector("h2 a[href], a[href]");
        const title = text(link?.innerText || link?.getAttribute?.("title") || "");
        const url = toAbsolute(link?.getAttribute?.("href") || link?.href);
        if (shouldIgnoreLink(link || card, url, title)) {
          continue;
        }
        pushItem(items, seenUrls, {
          kind: "search-result",
          rank: items.length + 1,
          url,
          title,
          snippet: readSnippet(card),
        });
        if (items.length >= maxItems) {
          break;
        }
      }
    } else if (provider === "baidu") {
      strategies.push("baidu-dom");
      const cards = Array.from(document.querySelectorAll("div.result, div.c-container, div.c-result, div.result-op"));
      for (const card of cards) {
        const link = card.querySelector("a[href]");
        const title = text(link?.innerText || link?.getAttribute?.("title") || "");
        const url = toAbsolute(link?.getAttribute?.("href") || link?.href);
        if (shouldIgnoreLink(link || card, url, title)) {
          continue;
        }
        pushItem(items, seenUrls, {
          kind: "search-result",
          rank: items.length + 1,
          url,
          title,
          snippet: readSnippet(card),
        });
        if (items.length >= maxItems) {
          break;
        }
      }
    } else if (provider === "duckduckgo") {
      strategies.push("duckduckgo-dom");
      const cards = Array.from(document.querySelectorAll("[data-testid='result'], .result"));
      for (const card of cards) {
        const link = card.querySelector("a[data-testid='result-title-a'], a.result__a, a[href]");
        const title = text(link?.innerText || "");
        const url = toAbsolute(link?.getAttribute?.("href") || link?.href);
        if (shouldIgnoreLink(link || card, url, title)) {
          continue;
        }
        pushItem(items, seenUrls, {
          kind: "search-result",
          rank: items.length + 1,
          url,
          title,
          snippet: readSnippet(card),
        });
        if (items.length >= maxItems) {
          break;
        }
      }
    }

    return {
      items,
      strategies,
    };
  }

  function buildJsonLdItems() {
    const items = [];
    const seenUrls = new Set();

    for (const script of Array.from(document.querySelectorAll("script[type='application/ld+json']")).slice(0, 12)) {
      const payload = parseJson(script.textContent || "");
      const entries = Array.isArray(payload) ? payload : payload ? [payload] : [];

      for (const entry of entries) {
        const queue = [entry];
        while (queue.length > 0 && items.length < maxItems) {
          const node = queue.shift();
          if (!node || typeof node !== "object") {
            continue;
          }

          const itemListElement = Array.isArray(node.itemListElement) ? node.itemListElement : [];
          for (const listItem of itemListElement) {
            const item = listItem?.item || listItem;
            const url = toAbsolute(item?.url || listItem?.url);
            const title = text(item?.name || item?.headline || listItem?.name || "");
            if (!url || !title || seenUrls.has(url)) {
              continue;
            }
            seenUrls.add(url);
            items.push({
              kind: "structured-result",
              rank: Number(listItem?.position) || items.length + 1,
              url,
              title,
              snippet: text(item?.description || item?.headline || "") || null,
            });
            if (items.length >= maxItems) {
              break;
            }
          }

          if (Array.isArray(node["@graph"])) {
            queue.push(...node["@graph"]);
          }
          for (const value of Object.values(node).slice(0, 24)) {
            if (value && typeof value === "object") {
              queue.push(value);
            }
          }
        }
      }
    }

    return items;
  }

  function buildFallbackItems() {
    const items = [];
    const seenUrls = new Set();

    for (const link of Array.from(document.querySelectorAll("a[href]"))) {
      const rawHref = link.getAttribute("href") || link.href;
      const url = toAbsolute(rawHref);
      const title = text(link.innerText || link.textContent || link.getAttribute("aria-label") || link.getAttribute("title") || "");

      if (shouldIgnoreLink(link, rawHref, title)) {
        continue;
      }

      pushItem(items, seenUrls, {
        kind: "link",
        rank: items.length + 1,
        url,
        title,
        snippet: readSnippet(link.closest("article, section, li, div") || link),
      });

      if (items.length >= maxItems) {
        break;
      }
    }

    return items;
  }

  const provider = (() => {
    const host = location.hostname.toLowerCase();
    if (/^(.+\.)?google\./i.test(host)) {
      return "google";
    }
    if (/^(.+\.)?bing\.com$/i.test(host)) {
      return "bing";
    }
    if (/^(.+\.)?baidu\.com$/i.test(host)) {
      return "baidu";
    }
    if (/^(.+\.)?duckduckgo\.com$/i.test(host)) {
      return "duckduckgo";
    }
    return null;
  })();

  function detectProviderHealth(itemCount = 0) {
    const combined = [
      location.href,
      document.title,
      text(document.body?.innerText || "").slice(0, 4000),
    ].join("\n").toLowerCase();
    const signals = [];

    if (/captcha|recaptcha|verify (?:that )?you(?:['\u2019])?re human|security check|human verification/.test(combined)) {
      signals.push("captcha");
    }
    if (/before you continue|consent|privacy reminder|agree to the use of cookies|consent\.google\./.test(combined)) {
      signals.push("consent");
    }
    if (/unusual traffic|automated queries|sorry\/index|challenge|why did this happen\?/.test(combined)) {
      signals.push("challenge");
    }

    const uniqueSignals = Array.from(new Set(signals));
    const status = uniqueSignals.includes("captcha")
      ? "captcha"
      : uniqueSignals.includes("consent")
        ? "consent"
        : uniqueSignals.length
          ? "challenge"
          : itemCount > 0
            ? "ok"
            : provider
              ? "empty"
              : "unknown";

    return {
      status,
      signals: uniqueSignals,
      reason: uniqueSignals.join(", ") || null,
    };
  }

  const initialProviderHealth = detectProviderHealth();
  if (provider && ["captcha", "consent", "challenge"].includes(initialProviderHealth.status)) {
    return {
      page: {
        url: location.href,
        title: document.title,
        provider,
      },
      items: [],
      meta: {
        source: "provider-block",
        provider,
        strategies: [],
        providerHealth: initialProviderHealth,
      },
    };
  }

  const providerItems = provider ? buildProviderItems(provider) : { items: [], strategies: [] };
  const jsonLdItems = buildJsonLdItems();
  const fallbackItems = buildFallbackItems();
  const items = uniqueItemsBy(
    [
      ...providerItems.items,
      ...jsonLdItems,
      ...fallbackItems,
    ],
    (item) => item.url
  ).slice(0, maxItems);
  const providerHealth = detectProviderHealth(items.length);

  return {
    page: {
      url: location.href,
      title: document.title,
      provider,
    },
    items,
    meta: {
      source: providerItems.strategies.length ? providerItems.strategies.join("+") : "generic-dom",
      provider,
      strategies: [
        ...providerItems.strategies,
        ...(jsonLdItems.length ? ["jsonld"] : []),
        ...(fallbackItems.length ? ["fallback-links"] : []),
      ],
      providerHealth,
    },
  };
}

async function extractGenericDetailPage(options = {}) {
  const maxBodyTextLength = Math.max(1000, Number(options.maxBodyTextLength) || 16000);
  const maxLinks = Math.max(1, Number(options.maxLinks) || 60);
  const maxComments = Math.max(0, Number(options.maxComments) || 0);
  const maxCommentBatches = Math.max(0, Number(options.maxCommentBatches) || 0);
  const maxEmbeddedJsonChars = Math.max(10000, Number(options.maxEmbeddedJsonChars) || 200000);

  function text(value) {
    return String(value ?? "").replace(/\s+/g, " ").trim();
  }

  function truncateText(value, limit) {
    const normalized = String(value ?? "");
    return normalized.length <= limit ? normalized : `${normalized.slice(0, limit)}...[truncated]`;
  }

  function toAbsolute(value) {
    try {
      return new URL(value, location.href).toString();
    } catch {
      return null;
    }
  }

  function parseJson(value) {
    try {
      return JSON.parse(value);
    } catch {
      return null;
    }
  }

  function firstNonEmpty(values) {
    for (const value of values) {
      const normalized = text(value);
      if (normalized) {
        return normalized;
      }
    }
    return null;
  }

  function uniqueItemsBy(items, keySelector) {
    const output = [];
    const seen = new Set();
    for (const item of items) {
      const key = keySelector(item);
      if (!key || seen.has(key)) {
        continue;
      }
      seen.add(key);
      output.push(item);
    }
    return output;
  }

  function wait(ms) {
    return new Promise((resolve) => setTimeout(resolve, ms));
  }

  function searchProviderForCurrentPage() {
    const host = location.hostname.toLowerCase();
    if (/^(.+\.)?google\./i.test(host)) {
      return "google";
    }
    if (/^(.+\.)?bing\.com$/i.test(host)) {
      return "bing";
    }
    if (/^(.+\.)?baidu\.com$/i.test(host)) {
      return "baidu";
    }
    if (/^(.+\.)?duckduckgo\.com$/i.test(host)) {
      return "duckduckgo";
    }
    return null;
  }

  function readEntityName(value) {
    if (!value) {
      return null;
    }
    if (typeof value === "string") {
      return text(value);
    }
    if (Array.isArray(value)) {
      return firstNonEmpty(value.map(readEntityName));
    }
    if (typeof value === "object") {
      return firstNonEmpty([
        value.name,
        value.fullName,
        value.displayName,
        value.username,
        value.handle,
        value.screenName,
      ]);
    }
    return null;
  }

  function walk(root, visit, maxNodes = 5000) {
    const stack = [root];
    let visited = 0;

    while (stack.length > 0 && visited < maxNodes) {
      const node = stack.pop();
      if (!node || typeof node !== "object") {
        continue;
      }

      visited += 1;
      visit(node);

      if (Array.isArray(node)) {
        for (let index = Math.min(node.length, 40) - 1; index >= 0; index -= 1) {
          const child = node[index];
          if (child && typeof child === "object") {
            stack.push(child);
          }
        }
      } else {
        const entries = Object.entries(node).slice(0, 40);
        for (let index = entries.length - 1; index >= 0; index -= 1) {
          const value = entries[index][1];
          if (value && typeof value === "object") {
            stack.push(value);
          }
        }
      }
    }
  }

  function buildCommentKey(comment) {
    return comment.commentId || `${comment.author || ""}::${comment.text || ""}::${comment.publishedText || ""}`;
  }

  function normalizeComment(comment, source) {
    const normalized = {
      commentId: text(comment.commentId) || null,
      author: text(comment.author) || null,
      authorUrl: toAbsolute(comment.authorUrl),
      text: truncateText(text(comment.text), 4000) || null,
      publishedText: text(comment.publishedText) || null,
      likeCountText: text(comment.likeCountText) || null,
      replyCount: Number.isFinite(Number(comment.replyCount)) ? Number(comment.replyCount) : 0,
      rating: text(comment.rating) || null,
      source,
    };

    if (!normalized.text) {
      return null;
    }

    return normalized;
  }

  function collectJsonLdBlocks() {
    const blocks = [];

    for (const script of Array.from(document.querySelectorAll("script[type='application/ld+json']")).slice(0, 16)) {
      const raw = String(script.textContent || "").trim();
      if (!raw || raw.length > maxEmbeddedJsonChars) {
        continue;
      }

      const parsed = parseJson(raw);
      if (parsed) {
        blocks.push(parsed);
      }
    }

    return blocks;
  }

  function collectHydrationRoots() {
    const roots = [];
    const keys = [];
    const globals = {
      __NEXT_DATA__: globalThis.__NEXT_DATA__ || null,
      __NUXT__: globalThis.__NUXT__ || null,
      __APOLLO_STATE__: globalThis.__APOLLO_STATE__ || null,
      __INITIAL_STATE__: globalThis.__INITIAL_STATE__ || null,
      __PRELOADED_STATE__: globalThis.__PRELOADED_STATE__ || null,
      __REMIX_CONTEXT__: globalThis.__remixContext || null,
      __EDGE_CONTROL_DATA__: globalThis.__data || null,
      REDUX_STATE: globalThis.REDUX_STATE || null,
    };

    for (const [key, value] of Object.entries(globals)) {
      if (value && typeof value === "object") {
        roots.push(value);
        keys.push(key);
      }
    }

    for (const script of Array.from(document.querySelectorAll("script#__NEXT_DATA__, script[type='application/json'], script[data-state], script[data-hypernova-key]")).slice(0, 8)) {
      const raw = String(script.textContent || "").trim();
      if (!raw || raw.length > maxEmbeddedJsonChars) {
        continue;
      }

      const parsed = parseJson(raw);
      if (parsed && typeof parsed === "object") {
        roots.push(parsed);
        keys.push(script.id || script.getAttribute("data-state") || script.getAttribute("data-hypernova-key") || "inline-json");
      }
    }

    return {
      roots,
      keys: uniqueItemsBy(keys, (value) => value),
    };
  }

  function extractCommentLikeNode(node, source) {
    const typeText = text([
      node["@type"],
      node.type,
      node.kind,
      node.__typename,
    ].filter(Boolean).join(" ")).toLowerCase();
    const keyText = Object.keys(node).join(" ").toLowerCase();
    const author = firstNonEmpty([
      readEntityName(node.author),
      readEntityName(node.user),
      readEntityName(node.creator),
      readEntityName(node.owner),
      readEntityName(node.account),
    ]);
    const body = firstNonEmpty([
      node.commentBody,
      node.reviewBody,
      node.contentText,
      node.body,
      node.text,
      node.message,
      node.content,
      node.description,
    ]);
    const publishedText = firstNonEmpty([
      node.datePublished,
      node.createdAt,
      node.publishedAt,
      node.updatedAt,
      node.timestamp,
      node.time,
    ]);
    const likeCountText = firstNonEmpty([
      node.likeCount,
      node.likes,
      node.voteCount,
      node.score,
    ]);
    const rating = firstNonEmpty([
      node.reviewRating?.ratingValue,
      node.ratingValue,
      node.rating,
      node.score,
    ]);
    const replyCount = firstNonEmpty([
      node.replyCount,
      node.childrenCommentCount,
      node.numReplies,
      node.totalReplies,
    ]);
    const commentId = firstNonEmpty([
      node.commentId,
      node.reviewId,
      node.id,
      node.uuid,
    ]);

    const looksCommentish =
      /comment|review|discussionforumposting|answer|reply|post/i.test(typeText) ||
      /comment|review|reply|author|rating|published|created/.test(keyText) ||
      Boolean(node.commentId || node.reviewBody || node.commentBody);

    if (!looksCommentish || !body) {
      return null;
    }
    if (!author && !publishedText && !rating && !/comment|review|reply/i.test(typeText + keyText)) {
      return null;
    }

    return normalizeComment({
      commentId,
      author,
      authorUrl: readEntityName(node.author?.url) || node.author?.url || node.user?.url || null,
      text: body,
      publishedText,
      likeCountText,
      replyCount,
      rating,
    }, source);
  }

  function collectStructuredComments(jsonLdBlocks, hydration) {
    const comments = [];
    const seen = new Set();
    const roots = [...jsonLdBlocks.map((item) => ({ source: "jsonld", value: item })), ...hydration.roots.map((item, index) => ({
      source: `hydration:${hydration.keys[index] || index}`,
      value: item,
    }))];

    for (const root of roots) {
      if (comments.length >= maxComments) {
        break;
      }

      walk(root.value, (node) => {
        if (comments.length >= maxComments) {
          return;
        }
        const comment = extractCommentLikeNode(node, root.source);
        if (!comment) {
          return;
        }

        const key = buildCommentKey(comment);
        if (!key || seen.has(key)) {
          return;
        }
        seen.add(key);
        comments.push(comment);
      }, 6000);
    }

    return comments;
  }

  function collectDomComments() {
    if (maxComments <= 0) {
      return [];
    }

    const comments = [];
    const seen = new Set();
    const candidates = new Set();
    const selectors = [
      "[itemtype*='Comment']",
      "[itemtype*='Review']",
      "[data-comment-id]",
      "[data-commentid]",
      "[data-testid*='comment']",
      "[data-testid*='review']",
      "article[class*='comment']",
      "li[class*='comment']",
      "div[class*='comment-item']",
      "div[class*='review-item']",
      "[id*='comment'] article",
      "[class*='comments'] article",
      "[class*='reviews'] article",
    ];

    for (const selector of selectors) {
      try {
        for (const node of Array.from(document.querySelectorAll(selector)).slice(0, maxComments * 4)) {
          candidates.add(node);
        }
      } catch {
        // Ignore invalid selector combinations on hostile pages.
      }
    }

    for (const node of Array.from(candidates)) {
      if (comments.length >= maxComments) {
        break;
      }

      const author = firstNonEmpty([
        node.querySelector?.("[itemprop='author']")?.textContent,
        node.querySelector?.("[rel='author']")?.textContent,
        node.querySelector?.("[class*='author'], [data-testid*='author']")?.textContent,
        node.querySelector?.("a[href*='/user/'], a[href*='/profile/'], a[href*='/u/']")?.textContent,
      ]);
      const body = firstNonEmpty([
        node.querySelector?.("[itemprop='reviewBody']")?.textContent,
        node.querySelector?.("[itemprop='text']")?.textContent,
        node.querySelector?.("[class*='content'], [class*='body'], [class*='text']")?.textContent,
        node.querySelector?.("p, blockquote")?.textContent,
        node.textContent,
      ]);
      const publishedText = firstNonEmpty([
        node.querySelector?.("time")?.getAttribute("datetime"),
        node.querySelector?.("time")?.textContent,
        node.querySelector?.("[class*='time'], [class*='date']")?.textContent,
      ]);
      const likeCountText = firstNonEmpty([
        node.querySelector?.("[class*='like'], [data-testid*='like']")?.textContent,
      ]);
      const rating = firstNonEmpty([
        node.querySelector?.("[itemprop='ratingValue']")?.getAttribute("content"),
        node.querySelector?.("[itemprop='ratingValue']")?.textContent,
      ]);
      const comment = normalizeComment({
        commentId: node.getAttribute("data-comment-id") || node.getAttribute("data-commentid") || node.id || null,
        author,
        authorUrl: node.querySelector?.("[itemprop='author'] a, [rel='author'], a[href*='/user/'], a[href*='/profile/']")?.href || null,
        text: body,
        publishedText,
        likeCountText,
        replyCount: null,
        rating,
      }, "dom");

      if (!comment) {
        continue;
      }
      if (!comment.author && !comment.publishedText && !/comment|review/i.test(`${node.id} ${node.className} ${node.getAttribute("itemtype") || ""}`)) {
        continue;
      }

      const key = buildCommentKey(comment);
      if (!key || seen.has(key)) {
        continue;
      }

      seen.add(key);
      comments.push(comment);
    }

    return comments;
  }

  async function nudgeCommentViewport() {
    const root = document.querySelector("#comments, [id*='comment'], [class*='comments'], [class*='reviews']");
    if (root?.scrollIntoView) {
      try {
        root.scrollIntoView({ block: "center" });
      } catch {
        // Ignore scroll issues on hostile layouts.
      }
    }
    window.scrollBy(0, Math.min(window.innerHeight || 900, 900));
    await wait(350);
  }

  function findLoadMoreButtons() {
    const patterns = [
      /load more/i,
      /show more/i,
      /more comments/i,
      /view more/i,
      /\u66f4\u591a\u8bc4\u8bba/,
      /\u52a0\u8f7d\u66f4\u591a/,
      /\u67e5\u770b\u66f4\u591a/,
      /\u5c55\u5f00/,
    ];

    return Array.from(document.querySelectorAll("button, [role='button']"))
      .filter((node) => {
        const label = text(node.innerText || node.textContent || node.getAttribute("aria-label") || "");
        return label && patterns.some((pattern) => pattern.test(label));
      })
      .slice(0, 3);
  }

  async function expandCommentArea() {
    let clicked = 0;

    for (const button of findLoadMoreButtons()) {
      try {
        button.scrollIntoView?.({ block: "center" });
        button.click?.();
        clicked += 1;
      } catch {
        // Best effort expansion only.
      }
    }

    if (clicked > 0) {
      await wait(500);
    }

    return clicked;
  }

  const metaTags = Object.fromEntries(
    Array.from(document.querySelectorAll("meta[name], meta[property]"))
      .map((element) => {
        const key = element.getAttribute("name") || element.getAttribute("property");
        const value = element.getAttribute("content");
        return key && value ? [key, value] : null;
      })
      .filter(Boolean)
  );

  const headings = Array.from(document.querySelectorAll("h1, h2, h3"))
    .map((element) => text(element.innerText || element.textContent || ""))
    .filter(Boolean)
    .slice(0, 20);

  const links = uniqueItemsBy(
    Array.from(document.querySelectorAll("a[href]"))
      .map((element) => ({
        url: toAbsolute(element.getAttribute("href") || element.href),
        text: text(element.innerText || element.textContent || element.getAttribute("title") || "") || null,
      }))
      .filter((item) => item.url),
    (item) => item.url
  ).slice(0, maxLinks);

  const primaryText = firstNonEmpty([
    document.querySelector("article")?.innerText,
    document.querySelector("[itemprop='articleBody']")?.innerText,
    document.querySelector("main")?.innerText,
    document.body?.innerText,
  ]) || "";

  const jsonLdBlocks = collectJsonLdBlocks();
  const hydration = collectHydrationRoots();
  const structuredComments = collectStructuredComments(jsonLdBlocks, hydration);
  let domComments = maxComments > 0 ? collectDomComments() : [];
  let loadMoreClicks = 0;
  let passiveDomCount = domComments.length;
  let interactionBatches = 0;

  if (maxComments > 0 && structuredComments.length + domComments.length < maxComments) {
    let previousCount = domComments.length;

    for (let batch = 0; batch < maxCommentBatches; batch += 1) {
      await nudgeCommentViewport();
      interactionBatches += 1;
      let updatedDomComments = collectDomComments();

      if (updatedDomComments.length > previousCount) {
        domComments = updatedDomComments;
      }

      if (structuredComments.length + domComments.length >= maxComments) {
        break;
      }

      if (updatedDomComments.length <= previousCount && batch < maxCommentBatches - 1) {
        loadMoreClicks += await expandCommentArea();
        updatedDomComments = collectDomComments();
      }

      domComments = updatedDomComments;

      if (structuredComments.length + domComments.length >= maxComments || domComments.length <= previousCount) {
        break;
      }

      previousCount = domComments.length;
    }
  }

  const comments = uniqueItemsBy(
    [...structuredComments, ...domComments],
    (comment) => buildCommentKey(comment)
  ).slice(0, maxComments);

  const commentSourceBreakdown = comments.reduce((accumulator, comment) => {
    const key = comment.source || "unknown";
    accumulator[key] = (accumulator[key] || 0) + 1;
    return accumulator;
  }, {});

  const jsonLdTypes = uniqueItemsBy(
    jsonLdBlocks.flatMap((block) => {
      const types = [];
      walk(block, (node) => {
        const type = text(node?.["@type"]);
        if (type) {
          types.push(type);
        }
      }, 2000);
      return types;
    }),
    (value) => value
  ).slice(0, 20);

  return {
    pageType: "generic-html",
    canonicalUrl: document.querySelector("link[rel='canonical']")?.href || location.href,
    title: document.querySelector("meta[property='og:title']")?.content || document.title,
    description: firstNonEmpty([
      metaTags.description,
      metaTags["og:description"],
      metaTags["twitter:description"],
      document.querySelector("meta[name='description']")?.getAttribute("content"),
      primaryText.slice(0, 280),
    ]),
    bodyText: truncateText(primaryText, maxBodyTextLength),
    headings,
    links,
    comments,
    commentsMeta: {
      fetchedCount: comments.length,
      structuredCount: structuredComments.length,
      passiveDomCount,
      domCount: domComments.length,
      batchesAttempted: maxCommentBatches,
      interactionBatches,
      loadMoreClicks,
      sourceBreakdown: commentSourceBreakdown,
      hydrationKeys: hydration.keys,
    },
    meta: {
      metaTags,
      jsonLdTypes,
      hydrationKeys: hydration.keys,
      searchProvider: searchProviderForCurrentPage(),
    },
  };
}

export const genericHtmlAdapter = {
  id: "generic-html",
  capabilities: {
    search: false,
    detail: true,
    comments: true,
  },

  matchesUrl() {
    return true;
  },

  async extractListings(context) {
    const result = await context.evaluate(extractGenericListingPage, [{
      maxItems: context.job.limits.maxItemsPerSearch,
    }]);

    return {
      ...result,
      items: uniqueBy(
        (result.items || []).map((item) => ({
          ...item,
          url: normalizeListingUrl(item.url, context.target.url),
          canonicalUrl: normalizeListingUrl(item.canonicalUrl || item.url, context.target.url),
          title: normalizeWhitespace(item.title),
          snippet: truncate(normalizeWhitespace(item.snippet), 2000),
        }))
          .filter((item) => item.url && !isLikelySearchResultsUrl(item.url, context.target.url)),
        (item) => item.canonicalUrl || item.url
      ),
    };
  },

  async extractDetail(context) {
    const resolvedPageUrl =
      context.navigation?.ready?.state?.href
      || context.navigation?.ready?.tab?.url
      || context.target.url;
    const pageSignals = typeof context.client?.sendCdp === "function"
      ? await capturePageSignals(context.client, {
          tabId: context.tabId,
          pageUrl: resolvedPageUrl,
          timeoutMs: Math.min(context.job.timeouts.commandMs, context.job.timeouts.evaluateMs),
          maxResourceEntries: Number(context.adapterOptions.maxSignalResources) || 12,
          maxApiCandidates: Number(context.adapterOptions.maxSignalApiCandidates) || 12,
          maxStorageItems: Number(context.adapterOptions.maxSignalStorageItems) || 12,
          maxStorageValueLength: Number(context.adapterOptions.maxSignalStorageValueLength) || 240,
          maxInlineStateBlocks: Number(context.adapterOptions.maxSignalInlineStateBlocks) || 8,
          maxInlineStateChars: Number(context.adapterOptions.maxInlineStateChars) || 1200,
          maxCookieCount: Number(context.adapterOptions.maxSignalCookies) || 12,
          maxIndexedDbDatabases: Number(context.adapterOptions.maxSignalIndexedDbDatabases) || 4,
          maxIndexedDbStores: Number(context.adapterOptions.maxSignalIndexedDbStores) || 8,
          maxCacheNames: Number(context.adapterOptions.maxSignalCacheNames) || 8,
        }).catch(() => null)
      : null;
    const requestedCommentBatches = Math.max(0, Number(context.job.limits.maxCommentBatches) || 0);
    const passiveResult = await context.evaluate(extractGenericDetailPage, [{
      maxBodyTextLength: context.job.limits.maxBodyTextLength,
      maxLinks: context.job.limits.maxLinksPerPage,
      maxComments: context.job.limits.maxCommentsPerPage,
      maxCommentBatches: 0,
      maxEmbeddedJsonChars: context.adapterOptions.maxEmbeddedJsonChars,
    }]);
    const priorityResourceHints = collectPriorityResourceHints(
      pageSignals,
      resolvedPageUrl,
      Number(context.adapterOptions.maxPriorityResourceHints) || 12
    );
    const initialLoggedNetworkResources = typeof context.network?.getResponses === "function"
      ? await context.network.getResponses({
          resourceTypes: ["XHR", "Fetch"],
          urlIncludes: priorityResourceHints,
          maxEntries: Math.max(
            context.job.limits.maxNetworkCandidates,
            context.job.limits.maxNetworkPayloads * 4
          ),
          includeBodies: true,
        }).catch(() => [])
      : [];
    const shouldRunInteractiveCommentPass =
      requestedCommentBatches > 0
      && context.job.limits.maxCommentsPerPage > 0
      && (passiveResult.comments?.length || 0) < context.job.limits.maxCommentsPerPage;
    const interactionNetworkMark = shouldRunInteractiveCommentPass && typeof context.network?.getMark === "function"
      ? await context.network.getMark().catch(() => null)
      : null;
    const interactiveResult = shouldRunInteractiveCommentPass
      ? await context.evaluate(extractGenericDetailPage, [{
          maxBodyTextLength: context.job.limits.maxBodyTextLength,
          maxLinks: context.job.limits.maxLinksPerPage,
          maxComments: context.job.limits.maxCommentsPerPage,
          maxCommentBatches: requestedCommentBatches,
          maxEmbeddedJsonChars: context.adapterOptions.maxEmbeddedJsonChars,
        }]).catch(() => null)
      : null;
    const interactionLoggedNetworkResources = await collectIncrementalNetworkResponses(context, {
      mark: interactionNetworkMark,
      priorityResourceHints,
      maxEntries: Math.max(
        context.job.limits.maxNetworkCandidates,
        context.job.limits.maxNetworkPayloads * 4
      ),
    });
    const normalizedLoggedResources = mergeNetworkResources(
      initialLoggedNetworkResources.map((entry) => normalizeLoggedNetworkResource(entry)).filter(Boolean),
      interactionLoggedNetworkResources.map((entry) => normalizeLoggedNetworkResource(entry)).filter(Boolean)
    );
    const supplementalLoggedResources = !normalizedLoggedResources.length && typeof context.network?.getResponses === "function"
      ? await context.network.getResponses({
          resourceTypes: ["XHR", "Fetch"],
          maxEntries: Math.max(
            context.job.limits.maxNetworkCandidates,
            context.job.limits.maxNetworkPayloads * 4
          ),
          includeBodies: true,
        }).catch(() => [])
      : [];
    const mergedLoggedResources = mergeNetworkResources(
      normalizedLoggedResources,
      supplementalLoggedResources.map((entry) => normalizeLoggedNetworkResource(entry)).filter(Boolean)
    );
    const remainingNetworkCandidateBudget = Math.max(
      0,
      context.job.limits.maxNetworkCandidates - mergedLoggedResources.length
    );
    const remainingNetworkPayloadBudget = Math.max(
      0,
      context.job.limits.maxNetworkPayloads - mergedLoggedResources.filter((resource) => typeof resource.content === "string" && resource.content.length > 0).length
    );
    const fallbackNetworkResources = remainingNetworkCandidateBudget > 0 && typeof context.getPageResources === "function"
      ? await context.getPageResources({
          pageUrl: resolvedPageUrl,
          resourceTypes: ["XHR", "Fetch", "Other"],
          priorityUrlIncludes: priorityResourceHints,
          maxResources: remainingNetworkCandidateBudget,
          maxContentResources: remainingNetworkPayloadBudget,
          maxContentBytes: context.job.limits.maxNetworkPayloadBytes,
        }).catch(() => [])
      : [];
    const networkResources = mergeNetworkResources(
      mergedLoggedResources,
      fallbackNetworkResources
    ).slice(0, context.job.limits.maxNetworkCandidates);
    const networkComments = extractCommentsFromNetworkResources(
      networkResources,
      resolvedPageUrl,
      context.job.limits.maxCommentsPerPage
    );
    const structuredNetworkPayloads = extractStructuredPayloadsFromNetworkResources(
      networkResources,
      Number(context.adapterOptions.maxStructuredNetworkPayloads) || 8
    );
    const result = interactiveResult || passiveResult;
    const mergedComments = uniqueBy(
      [...(passiveResult.comments || []), ...(interactiveResult?.comments || []), ...networkComments],
      (comment) => buildCommentKey(comment)
    ).slice(0, context.job.limits.maxCommentsPerPage);
    const networkContentCount = networkResources.filter((resource) => typeof resource.content === "string" && resource.content.length > 0).length;

    return {
      ...result,
      canonicalUrl: toAbsoluteUrl(result.canonicalUrl || passiveResult.canonicalUrl, resolvedPageUrl) || resolvedPageUrl,
      title: normalizeWhitespace(result.title || passiveResult.title),
      description: truncate(normalizeWhitespace(result.description || passiveResult.description), context.job.limits.maxBodyTextLength),
      bodyText: truncate(normalizeWhitespace(result.bodyText || passiveResult.bodyText), context.job.limits.maxBodyTextLength),
      headings: uniqueBy([...(passiveResult.headings || []), ...(interactiveResult?.headings || [])], (value) => value).slice(0, 20),
      links: uniqueBy([...(passiveResult.links || []), ...(interactiveResult?.links || [])], (item) => item.url || item.text || ""),
      comments: mergedComments.map((comment) => ({
        ...comment,
        author: normalizeWhitespace(comment.author),
        authorUrl: toAbsoluteUrl(comment.authorUrl, resolvedPageUrl),
        text: truncate(normalizeWhitespace(comment.text), 4000),
        publishedText: normalizeWhitespace(comment.publishedText),
        likeCountText: normalizeWhitespace(comment.likeCountText),
        rating: normalizeWhitespace(comment.rating),
      })),
      commentsMeta: {
        ...(passiveResult.commentsMeta || {}),
        ...(interactiveResult?.commentsMeta || {}),
        fetchedCount: mergedComments.length,
        passiveFetchedCount: passiveResult.comments?.length || 0,
        interactiveFetchedCount: interactiveResult?.comments?.length || 0,
        interactiveCommentPass: Boolean(interactiveResult),
        sourceBreakdown: mergeCountMaps(passiveResult.commentsMeta?.sourceBreakdown, interactiveResult?.commentsMeta?.sourceBreakdown),
        networkCandidateCount: networkResources.length,
        networkPayloadCount: networkContentCount,
        networkCommentCount: networkComments.length,
        networkHintCount: priorityResourceHints.length,
        networkResponseCount: mergedLoggedResources.length,
        interactionNetworkResponseCount: interactionLoggedNetworkResources.length,
        silentSignalsCaptured: Boolean(pageSignals),
      },
      meta: {
        ...(passiveResult.meta || {}),
        ...(interactiveResult?.meta || {}),
        network: pageSignals?.network || null,
        performance: pageSignals?.performance || null,
        application: pageSignals?.application || null,
        networkPayloads: structuredNetworkPayloads,
        networkResources: networkResources
          .slice(0, 12)
          .map((resource) => ({
            url: resource.url,
            type: resource.type,
            mimeType: resource.mimeType,
            status: resource.status,
            hasContent: typeof resource.content === "string" && resource.content.length > 0,
          })),
      },
    };
  },
};
