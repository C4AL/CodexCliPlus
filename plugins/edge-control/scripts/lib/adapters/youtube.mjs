import { normalizeWhitespace, toAbsoluteUrl, truncate } from "../crawl-utils.mjs";

async function extractYoutubeSearchPage(options = {}) {
  const maxItems = Math.max(1, Number(options.maxItems) || 20);
  const maxPages = Math.max(1, Number(options.maxPages) || 1);
  const query = String(options.query || "");

  function readText(value) {
    if (!value) {
      return "";
    }
    if (typeof value === "string") {
      return value;
    }
    if (typeof value.simpleText === "string") {
      return value.simpleText;
    }
    if (Array.isArray(value.runs)) {
      return value.runs.map((item) => item.text || "").join("");
    }
    return "";
  }

  function normalizeUrl(value) {
    if (!value) {
      return null;
    }
    try {
      return new URL(value, location.origin).toString();
    } catch {
      return null;
    }
  }

  function walk(root, visit) {
    const stack = [{ node: root, path: [] }];
    while (stack.length > 0) {
      const current = stack.pop();
      const { node, path } = current;
      if (!node || typeof node !== "object") {
        continue;
      }
      visit(node, path);
      if (Array.isArray(node)) {
        for (let index = node.length - 1; index >= 0; index -= 1) {
          stack.push({ node: node[index], path: [...path, index] });
        }
      } else {
        for (const [key, value] of Object.entries(node)) {
          stack.push({ node: value, path: [...path, key] });
        }
      }
    }
  }

  function getConfig() {
    return globalThis.ytcfg?.data_ || globalThis.ytcfg?.data || window.ytcfg?.data_ || {};
  }

  function toVideoItem(renderer) {
    const video = renderer.videoRenderer || renderer.reelItemRenderer || null;
    if (!video) {
      return null;
    }

    const videoId = video.videoId || video.navigationEndpoint?.watchEndpoint?.videoId || null;
    const title = readText(video.title);
    const url = normalizeUrl(
      video.navigationEndpoint?.commandMetadata?.webCommandMetadata?.url ||
      (videoId ? `/watch?v=${videoId}` : null)
    );

    if (!title || !url) {
      return null;
    }

    return {
      kind: "video",
      adapterId: "youtube",
      site: "youtube",
      videoId,
      url,
      title,
      snippet: readText(video.descriptionSnippet || video.detailedMetadataSnippets?.[0]?.snippetText) || null,
      channel: readText(video.ownerText || video.longBylineText) || null,
      channelUrl: normalizeUrl(
        video.ownerText?.runs?.[0]?.navigationEndpoint?.commandMetadata?.webCommandMetadata?.url ||
        video.longBylineText?.runs?.[0]?.navigationEndpoint?.commandMetadata?.webCommandMetadata?.url
      ),
      publishedText: readText(video.publishedTimeText) || null,
      durationText: readText(video.lengthText) || null,
      viewCountText: readText(video.viewCountText) || null,
      thumbnailUrl: video.thumbnail?.thumbnails?.slice(-1)[0]?.url || null,
    };
  }

  function collectItems(root, output, seenUrls) {
    walk(root, (node) => {
      const item = toVideoItem(node);
      if (item && !seenUrls.has(item.url)) {
        seenUrls.add(item.url);
        output.push(item);
      }
    });
  }

  function collectContinuationTokens(root) {
    const candidates = [];
    walk(root, (node, path) => {
      const token = node?.continuationEndpoint?.continuationCommand?.token || node?.nextContinuationData?.continuation || null;
      if (token) {
        candidates.push({
          token,
          path: path.join("."),
        });
      }
    });
    return candidates;
  }

  function pickSearchContinuation(root) {
    const candidates = collectContinuationTokens(root);
    const sorted = candidates.sort((left, right) => {
      const leftScore = /secondaryContents|itemSectionRenderer|continuationItemRenderer|search/i.test(left.path) ? 10 : 0;
      const rightScore = /secondaryContents|itemSectionRenderer|continuationItemRenderer|search/i.test(right.path) ? 10 : 0;
      return rightScore - leftScore;
    });
    return sorted[0]?.token || null;
  }

  async function fetchContinuation(token) {
    const config = getConfig();
    if (!config.INNERTUBE_API_KEY || !config.INNERTUBE_CONTEXT || !token) {
      return null;
    }
    const response = await fetch(`${location.origin}/youtubei/v1/search?key=${encodeURIComponent(config.INNERTUBE_API_KEY)}&prettyPrint=false`, {
      method: "POST",
      credentials: "same-origin",
      headers: {
        "content-type": "application/json",
      },
      body: JSON.stringify({
        context: config.INNERTUBE_CONTEXT,
        continuation: token,
      }),
    });
    if (!response.ok) {
      throw new Error(`YouTube search continuation failed with status ${response.status}.`);
    }
    return response.json();
  }

  const initialData = globalThis.ytInitialData || window.ytInitialData || null;
  const seenUrls = new Set();
  const items = [];

  if (initialData) {
    collectItems(initialData, items, seenUrls);
  }

  let continuation = pickSearchContinuation(initialData);
  let pagesFetched = initialData ? 1 : 0;

  while (continuation && pagesFetched < maxPages && items.length < maxItems) {
    const payload = await fetchContinuation(continuation);
    if (!payload) {
      break;
    }
    pagesFetched += 1;
    collectItems(payload, items, seenUrls);
    continuation = pickSearchContinuation(payload);
  }

  if (items.length === 0) {
    const domItems = Array.from(document.querySelectorAll("ytd-video-renderer, ytd-rich-item-renderer"))
      .map((node) => {
        const anchor = node.querySelector("a#video-title");
        if (!anchor?.href) {
          return null;
        }
        return {
          kind: "video",
          adapterId: "youtube",
          site: "youtube",
          videoId: (() => {
            try {
              return new URL(anchor.href).searchParams.get("v");
            } catch {
              return null;
            }
          })(),
          url: anchor.href,
          title: (anchor.textContent || "").replace(/\s+/g, " ").trim(),
          snippet: (node.querySelector("#description-text")?.textContent || "").replace(/\s+/g, " ").trim() || null,
          channel: (node.querySelector("ytd-channel-name")?.textContent || "").replace(/\s+/g, " ").trim() || null,
          channelUrl: node.querySelector("ytd-channel-name a")?.href || null,
          publishedText: (node.querySelector("#metadata-line span:last-child")?.textContent || "").replace(/\s+/g, " ").trim() || null,
          durationText: (node.querySelector("badge-shape")?.textContent || "").replace(/\s+/g, " ").trim() || null,
          viewCountText: (node.querySelector("#metadata-line span:first-child")?.textContent || "").replace(/\s+/g, " ").trim() || null,
          thumbnailUrl: node.querySelector("img")?.src || null,
        };
      })
      .filter(Boolean);

    for (const item of domItems) {
      if (!seenUrls.has(item.url)) {
        seenUrls.add(item.url);
        items.push(item);
      }
      if (items.length >= maxItems) {
        break;
      }
    }
  }

  return {
    page: {
      url: location.href,
      title: document.title,
      query,
    },
    items: items.slice(0, maxItems).map((item, index) => ({
      ...item,
      rank: index + 1,
    })),
    meta: {
      source: initialData ? "ytInitialData+youtubei" : "dom",
      pagesFetched,
      continuationRemaining: Boolean(continuation),
    },
  };
}

async function extractYoutubeWatchPage(options = {}) {
  const maxComments = Math.max(0, Number(options.maxComments) || 0);
  const maxCommentBatches = Math.max(1, Number(options.maxCommentBatches) || 1);
  const maxBodyTextLength = Math.max(1000, Number(options.maxBodyTextLength) || 16000);

  function readText(value) {
    if (!value) {
      return "";
    }
    if (typeof value === "string") {
      return value;
    }
    if (typeof value.simpleText === "string") {
      return value.simpleText;
    }
    if (Array.isArray(value.runs)) {
      return value.runs.map((item) => item.text || "").join("");
    }
    return "";
  }

  function normalizeUrl(value) {
    if (!value) {
      return null;
    }
    try {
      return new URL(value, location.origin).toString();
    } catch {
      return null;
    }
  }

  function walk(root, visit) {
    const stack = [{ node: root, path: [] }];
    while (stack.length > 0) {
      const current = stack.pop();
      const { node, path } = current;
      if (!node || typeof node !== "object") {
        continue;
      }
      visit(node, path);
      if (Array.isArray(node)) {
        for (let index = node.length - 1; index >= 0; index -= 1) {
          stack.push({ node: node[index], path: [...path, index] });
        }
      } else {
        for (const [key, value] of Object.entries(node)) {
          stack.push({ node: value, path: [...path, key] });
        }
      }
    }
  }

  function getConfig() {
    return globalThis.ytcfg?.data_ || globalThis.ytcfg?.data || window.ytcfg?.data_ || {};
  }

  function parseStructuredVideoObject() {
    const scripts = Array.from(document.querySelectorAll('script[type="application/ld+json"]'));
    for (const script of scripts) {
      try {
        const payload = JSON.parse(script.textContent);
        const entries = Array.isArray(payload) ? payload : [payload];
        const match = entries.find((entry) => entry?.["@type"] === "VideoObject");
        if (match) {
          return match;
        }
      } catch {
        // Ignore malformed JSON-LD.
      }
    }
    return null;
  }

  function parseComments(root, seenIds, output) {
    walk(root, (node) => {
      const thread = node?.commentThreadRenderer || null;
      const comment = thread?.comment?.commentRenderer || node?.commentRenderer || null;
      if (!comment) {
        return;
      }
      const commentId = comment.commentId || null;
      const key = commentId || `${readText(comment.authorText)}::${readText(comment.contentText)}::${readText(comment.publishedTimeText)}`;
      if (!key || seenIds.has(key)) {
        return;
      }
      seenIds.add(key);
      output.push({
        commentId,
        author: readText(comment.authorText) || null,
        authorUrl: normalizeUrl(comment.authorEndpoint?.commandMetadata?.webCommandMetadata?.url),
        text: readText(comment.contentText) || null,
        publishedText: readText(comment.publishedTimeText) || null,
        likeCountText: readText(comment.voteCount) || null,
        replyCount: thread?.replies?.commentRepliesRenderer?.contents?.length || 0,
      });
    });
  }

  function collectContinuationCandidates(root) {
    const candidates = [];
    walk(root, (node, path) => {
      const token = node?.continuationEndpoint?.continuationCommand?.token || node?.nextContinuationData?.continuation || null;
      if (token) {
        const joined = path.join(".");
        let score = 0;
        if (/comment/i.test(joined)) {
          score += 10;
        }
        if (/continuationItemRenderer|itemSectionRenderer|engagement/i.test(joined)) {
          score += 4;
        }
        candidates.push({ token, score });
      }
    });
    return candidates.sort((left, right) => right.score - left.score);
  }

  async function fetchCommentContinuation(token) {
    const config = getConfig();
    if (!config.INNERTUBE_API_KEY || !config.INNERTUBE_CONTEXT || !token) {
      return null;
    }
    const response = await fetch(`${location.origin}/youtubei/v1/next?key=${encodeURIComponent(config.INNERTUBE_API_KEY)}&prettyPrint=false`, {
      method: "POST",
      credentials: "same-origin",
      headers: {
        "content-type": "application/json",
      },
      body: JSON.stringify({
        context: config.INNERTUBE_CONTEXT,
        continuation: token,
      }),
    });
    if (!response.ok) {
      throw new Error(`YouTube comment continuation failed with status ${response.status}.`);
    }
    return response.json();
  }

  async function ensureCommentBootstrap() {
    const commentsNode = document.querySelector("ytd-comments, #comments");
    if (commentsNode) {
      commentsNode.scrollIntoView({ block: "center" });
    } else {
      window.scrollTo(0, Math.min(document.documentElement.scrollHeight, 2600));
    }
    await new Promise((resolve) => setTimeout(resolve, 900));
    return globalThis.ytInitialData || window.ytInitialData || null;
  }

  const initialData = globalThis.ytInitialData || window.ytInitialData || null;
  const videoObject = parseStructuredVideoObject();

  const domDescription =
    document.querySelector("#description-inline-expander")?.innerText ||
    document.querySelector("#description-inner")?.innerText ||
    document.querySelector("meta[name='description']")?.getAttribute("content") ||
    "";

  const page = {
    url: location.href,
    canonicalUrl: document.querySelector("link[rel='canonical']")?.href || location.href,
    title: videoObject?.name || document.querySelector("h1 yt-formatted-string")?.textContent || document.title.replace(/\s+-\s+YouTube$/, ""),
    description: videoObject?.description || domDescription || null,
    publishedAt: videoObject?.uploadDate || null,
    channel: videoObject?.author?.name || document.querySelector("ytd-channel-name a")?.textContent?.replace(/\s+/g, " ").trim() || null,
    channelUrl: normalizeUrl(videoObject?.author?.url || document.querySelector("ytd-channel-name a")?.href),
    thumbnailUrl: Array.isArray(videoObject?.thumbnailUrl) ? videoObject.thumbnailUrl[0] : videoObject?.thumbnailUrl || null,
    viewCount: videoObject?.interactionStatistic?.find?.((item) => item?.interactionType === "https://schema.org/WatchAction")?.userInteractionCount || null,
  };

  const seenIds = new Set();
  const comments = [];
  let continuation = collectContinuationCandidates(initialData)[0]?.token || null;
  let batchesFetched = 0;
  let source = "disabled";

  if (maxComments > 0 && !continuation) {
    const refreshedInitialData = await ensureCommentBootstrap();
    continuation = collectContinuationCandidates(refreshedInitialData)[0]?.token || null;
  }

  if (maxComments > 0 && continuation) {
    source = "youtubei";
    while (continuation && batchesFetched < maxCommentBatches && comments.length < maxComments) {
      const payload = await fetchCommentContinuation(continuation);
      if (!payload) {
        break;
      }
      batchesFetched += 1;
      parseComments(payload, seenIds, comments);
      continuation = collectContinuationCandidates(payload)[0]?.token || null;
    }
  }

  return {
    pageType: "youtube-watch",
    canonicalUrl: page.canonicalUrl,
    title: page.title,
    description: page.description ? String(page.description).replace(/\s+/g, " ").trim().slice(0, maxBodyTextLength) : null,
    bodyText: page.description ? String(page.description).replace(/\s+/g, " ").trim().slice(0, maxBodyTextLength) : null,
    channel: page.channel,
    channelUrl: page.channelUrl,
    publishedAt: page.publishedAt,
    thumbnailUrl: page.thumbnailUrl,
    viewCount: page.viewCount,
    comments: comments.slice(0, maxComments),
    commentsMeta: {
      source,
      fetchedCount: comments.length,
      batchesFetched,
      continuationRemaining: Boolean(continuation),
    },
    meta: {
      url: page.url,
    },
  };
}

export const youtubeAdapter = {
  id: "youtube",
  capabilities: {
    search: true,
    detail: true,
    comments: true,
  },

  matchesUrl(url) {
    return /(^|\.)youtube\.com$|(^|\.)youtu\.be$/i.test(url.hostname);
  },

  buildSearchUrls({ expansion }) {
    const query = encodeURIComponent(expansion.query);
    return [`https://www.youtube.com/results?search_query=${query}`];
  },

  async extractListings(context) {
    const result = await context.evaluate(extractYoutubeSearchPage, [{
      query: context.target.query,
      maxItems: context.job.limits.maxItemsPerSearch,
      maxPages: context.job.limits.maxListingPagesPerSearch,
    }]);

    return {
      ...result,
      items: result.items.map((item) => ({
        ...item,
        url: toAbsoluteUrl(item.url, context.target.url) || item.url,
        channelUrl: toAbsoluteUrl(item.channelUrl, context.target.url),
        title: normalizeWhitespace(item.title),
        snippet: normalizeWhitespace(item.snippet),
        channel: normalizeWhitespace(item.channel),
      })),
    };
  },

  async extractDetail(context) {
    const result = await context.evaluate(extractYoutubeWatchPage, [{
      maxComments: context.job.limits.maxCommentsPerPage,
      maxCommentBatches: context.job.limits.maxCommentBatches,
      maxBodyTextLength: context.job.limits.maxBodyTextLength,
    }]);

    return {
      ...result,
      canonicalUrl: toAbsoluteUrl(result.canonicalUrl, context.target.url) || context.target.url,
      title: normalizeWhitespace(result.title),
      description: truncate(normalizeWhitespace(result.description), context.job.limits.maxBodyTextLength),
      bodyText: truncate(normalizeWhitespace(result.bodyText), context.job.limits.maxBodyTextLength),
      channel: normalizeWhitespace(result.channel),
      comments: (result.comments || []).map((comment) => ({
        ...comment,
        author: normalizeWhitespace(comment.author),
        authorUrl: toAbsoluteUrl(comment.authorUrl, context.target.url),
        text: truncate(normalizeWhitespace(comment.text), 4000),
        publishedText: normalizeWhitespace(comment.publishedText),
        likeCountText: normalizeWhitespace(comment.likeCountText),
      })),
    };
  },
};
