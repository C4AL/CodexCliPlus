import { normalizeWhitespace, uniqueBy } from "../text.mjs";

export const SEARCH_PROVIDER_READY_SELECTORS = {
  google: "a h3, div#search a[href*='http']",
  bing: "li.b_algo h2 a, ol#b_results li.b_algo",
  baidu: "div.result a, div.c-container a",
  youtube: "ytd-video-renderer #video-title, ytd-video-renderer",
};

export function buildSearchUrl(provider, query) {
  const encoded = encodeURIComponent(query);

  switch (provider) {
    case "google":
      return `https://www.google.com/search?q=${encoded}&hl=en&gl=us`;
    case "bing":
      return `https://www.bing.com/search?q=${encoded}&setlang=en-US`;
    case "baidu":
      return `https://www.baidu.com/s?wd=${encoded}`;
    case "youtube":
      return `https://www.youtube.com/results?search_query=${encoded}`;
    default:
      throw new Error(`Unsupported search provider: ${provider}`);
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
    return decoded.startsWith("http://") || decoded.startsWith("https://")
      ? decoded
      : rawUrl;
  } catch {
    return rawUrl;
  }
}

function decodeGoogleTarget(rawUrl) {
  try {
    const parsed = new URL(rawUrl);
    if (!/google\./i.test(parsed.hostname) || parsed.pathname !== "/url") {
      return rawUrl;
    }
    return parsed.searchParams.get("q") || parsed.searchParams.get("url") || rawUrl;
  } catch {
    return rawUrl;
  }
}

function normalizeResultUrl(provider, rawUrl) {
  switch (provider) {
    case "bing":
      return decodeBingTarget(rawUrl);
    case "google":
      return decodeGoogleTarget(rawUrl);
    default:
      return rawUrl;
  }
}

function buildExtractorExpression(provider, maxResults) {
  return `(async () => {
    const provider = ${JSON.stringify(provider)};
    const maxResults = ${JSON.stringify(maxResults)};
    const text = (value) => String(value || "").replace(/\\s+/g, " ").trim();
    let items = [];

    if (provider === "google") {
      items = Array.from(document.querySelectorAll("div#search a[href]"))
        .map((link) => {
          const title =
            text(link.querySelector("h3")?.textContent) ||
            text(link.getAttribute("aria-label")) ||
            text(link.textContent);
          return {
            title,
            url: link.href || "",
            snippet: text(link.closest("div")?.innerText || ""),
          };
        });
    } else if (provider === "bing") {
      items = Array.from(document.querySelectorAll("li.b_algo"))
        .map((item) => {
          const link = item.querySelector("h2 a[href]");
          return {
            title: text(link?.textContent),
            url: link?.href || "",
            snippet: text(item.querySelector(".b_caption")?.innerText || item.innerText || ""),
          };
        });
    } else if (provider === "baidu") {
      items = Array.from(document.querySelectorAll("div.result, div.c-container"))
        .map((item) => {
          const link = item.querySelector("a[href]");
          return {
            title: text(link?.textContent),
            url: link?.href || "",
            snippet: text(item.innerText || ""),
          };
        });
    } else if (provider === "youtube") {
      items = Array.from(document.querySelectorAll("ytd-video-renderer"))
        .map((item, index) => {
          const link = item.querySelector("#video-title");
          const metadata = Array.from(item.querySelectorAll("#metadata-line span"))
            .map((node) => text(node.textContent))
            .filter(Boolean);
          return {
            rank: index + 1,
            title: text(link?.textContent),
            url: link?.href || "",
            snippet: text(
              item.querySelector("#description-text")?.textContent ||
                item.querySelector(".metadata-snippet-text")?.textContent ||
                ""
            ),
            metadata,
            channel: text(item.querySelector("#channel-name a, ytd-channel-name a")?.textContent),
          };
        });
    }

    const filtered = items
      .filter((item) => item.url && item.title)
      .map((item) => ({
        ...item,
        title: text(item.title),
        url: item.url,
        snippet: text(item.snippet),
      }));

    return JSON.stringify(filtered.slice(0, maxResults));
  })()`;
}

export async function extractSearchResults(cdp, tabId, provider, options = {}) {
  const maxResults = Math.max(1, Number(options.maxResults) || 5);
  const readySelector = SEARCH_PROVIDER_READY_SELECTORS[provider];

  if (readySelector) {
    try {
      await cdp.waitFor(tabId, readySelector, { timeoutMs: options.waitTimeoutMs ?? 30000 });
    } catch {
      // The evaluation path below still has a chance to work.
    }
  }

  const payload = await cdp.evaluateJson(
    tabId,
    buildExtractorExpression(provider, maxResults),
    {
      timeoutMs: options.timeoutMs ?? 60000,
    }
  );

  return uniqueBy(
    (payload || []).map((item) => ({
      ...item,
      title: normalizeWhitespace(item.title),
      url: normalizeResultUrl(provider, item.url),
      snippet: normalizeWhitespace(item.snippet),
    })),
    (item) => item.url
  );
}
