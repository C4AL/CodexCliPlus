function buildYouTubeVideoExpression(options = {}) {
  const commentLimit = Math.max(1, Number(options.commentLimit) || 15);
  const scrollRounds = Math.max(1, Number(options.scrollRounds) || 6);
  const scrollDelayMs = Math.max(200, Number(options.scrollDelayMs) || 1000);

  return `(async () => {
    const commentLimit = ${JSON.stringify(commentLimit)};
    const scrollRounds = ${JSON.stringify(scrollRounds)};
    const scrollDelayMs = ${JSON.stringify(scrollDelayMs)};
    const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
    const text = (value) => String(value || "").replace(/\\s+/g, " ").trim();
    const q = (selector) => document.querySelector(selector);
    const qa = (selector) => Array.from(document.querySelectorAll(selector));
    const player = globalThis.ytInitialPlayerResponse || {};
    const details = player.videoDetails || {};
    const micro = player.microformat?.playerMicroformatRenderer || {};

    const expandButton =
      q("#description-inline-expander #expand") ||
      q("tp-yt-paper-button#expand") ||
      q("ytd-text-inline-expander #expand");
    if (expandButton) {
      expandButton.scrollIntoView({ block: "center" });
      expandButton.click();
      await sleep(800);
    }

    const commentsHost = q("ytd-comments#comments");
    if (commentsHost) {
      commentsHost.scrollIntoView({ block: "start" });
      await sleep(Math.max(1000, scrollDelayMs));
    }

    for (let round = 0; round < scrollRounds; round += 1) {
      window.scrollBy(0, 1400);
      await sleep(scrollDelayMs);
    }

    const comments = qa("ytd-comment-thread-renderer")
      .slice(0, commentLimit)
      .map((item) => ({
        author: text(item.querySelector("#author-text span")?.textContent),
        published: text(item.querySelector("#published-time-text a")?.textContent),
        likes: text(item.querySelector("#vote-count-middle")?.textContent),
        text: text(item.querySelector("#content-text")?.textContent),
      }))
      .filter((item) => item.text);

    return JSON.stringify({
      type: "youtube-video",
      pageUrl: location.href,
      title: details.title || document.title,
      channel: details.author || "",
      publishDate: micro.publishDate || micro.uploadDate || null,
      viewCount: details.viewCount || null,
      description: details.shortDescription || "",
      comments,
      commentsCollected: comments.length,
    });
  })()`;
}

export function isYouTubeWatchUrl(url) {
  return /youtube\.com\/watch|youtu\.be\//i.test(String(url || ""));
}

export async function extractYouTubeVideo(cdp, tabId, options = {}) {
  const payload = await cdp.evaluateJson(
    tabId,
    buildYouTubeVideoExpression(options),
    {
      timeoutMs: options.timeoutMs ?? 180000,
      retries: options.retries ?? 5,
    }
  );

  if ((payload?.commentsCollected || 0) > 0 || options.allowFocusFallback === false) {
    return payload;
  }

  await cdp.focusTab(tabId);
  return cdp.evaluateJson(
    tabId,
    buildYouTubeVideoExpression({
      ...options,
      scrollRounds: Math.max(8, Number(options.scrollRounds) || 8),
      scrollDelayMs: Math.max(1200, Number(options.scrollDelayMs) || 1200),
    }),
    {
      timeoutMs: Math.max(180000, options.timeoutMs ?? 180000),
      retries: options.retries ?? 5,
    }
  );
}
