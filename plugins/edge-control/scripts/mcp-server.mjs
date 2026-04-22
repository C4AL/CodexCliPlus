import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
import { loadConfig } from "./lib/config.mjs";
import { BrowserCrawler as AdvancedBrowserCrawler } from "./lib/browser-crawler.mjs";
import { EdgeBridgeClient } from "./lib/edge-bridge-client.mjs";
import { compileTemplate, hasSearchTemplatePlaceholder } from "./lib/crawl-utils.mjs";
import { expandQuerySeeds } from "./lib/query-expander.mjs";

const DEFAULT_COMMAND_TIMEOUT_MS = 15000;
const DEFAULT_CRAWL_TIMEOUT_MS = 30000;
const DEFAULT_POLL_INTERVAL_MS = 250;
const DEFAULT_MAX_ITEMS = 50;
const MAX_MAX_ITEMS = 500;
const DEFAULT_MAX_TEXT_LENGTH = 4000;
const DEFAULT_MAX_HTML_LENGTH = 120000;
const DEFAULT_BATCH_CONCURRENCY = 4;
const MAX_BATCH_CONCURRENCY = 12;

const waitWorldSchema = z.enum(["ISOLATED", "MAIN"]);
const customSelectorSchema = z.object({
  name: z.string().min(1),
  selector: z.string().min(1),
  attribute: z.string().min(1).optional(),
  all: z.boolean().optional(),
  includeText: z.boolean().optional(),
  includeHtml: z.boolean().optional(),
  maxItems: z.number().int().positive().max(MAX_MAX_ITEMS).optional(),
});

const config = loadConfig();
const server = new McpServer({
  name: "edge-control",
  version: "0.1.7",
});
const bridgeClient = new EdgeBridgeClient({ config });
const advancedCrawler = new AdvancedBrowserCrawler({ client: bridgeClient });

function formatContent(value) {
  return {
    content: [
      {
        type: "text",
        text: JSON.stringify(value, null, 2),
      },
    ],
  };
}

function formatError(error) {
  return {
    isError: true,
    content: [
      {
        type: "text",
        text: error?.message || String(error),
      },
    ],
  };
}

function clampInteger(value, min, max, fallback) {
  const number = Number(value);
  if (!Number.isFinite(number)) {
    return fallback;
  }
  return Math.min(max, Math.max(min, Math.trunc(number)));
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function normalizeText(value) {
  return String(value ?? "").replace(/\s+/g, " ").trim();
}

function serializeError(error) {
  return {
    message: error?.message || String(error),
    stack: error?.stack || null,
  };
}

function dedupeStrings(values) {
  const unique = [];
  const seen = new Set();
  for (const value of values || []) {
    const normalized = normalizeText(value);
    if (!normalized || seen.has(normalized)) {
      continue;
    }
    seen.add(normalized);
    unique.push(normalized);
  }
  return unique;
}

const EN_QUERY_TEMPLATES = [
  "{topic}",
  "{topic} latest",
  "{topic} latest news",
  "{topic} latest update",
  "{topic} breaking news",
  "{topic} announcement",
  "{topic} official announcement",
  "{topic} release",
  "{topic} release notes",
  "{topic} roadmap",
  "{topic} new features",
  "{topic} benchmark",
  "{topic} review",
  "{topic} recap",
  "{topic} leak",
  "{topic} rumor",
  "{topic} community reaction",
  "{topic} developer update",
  "{topic} api update",
  "{topic} what changed",
  "{topic} launch analysis",
  "{topic} hands on",
  "{topic} discussion",
  "{topic} controversy",
];

const ZH_QUERY_TEMPLATES = [
  "{topic} \u6700\u65b0\u6d88\u606f",
  "{topic} \u6700\u65b0\u52a8\u6001",
  "{topic} \u6700\u65b0\u66f4\u65b0",
  "{topic} \u5b98\u65b9\u516c\u544a",
  "{topic} \u53d1\u5e03",
  "{topic} \u53d1\u5e03\u8bf4\u660e",
  "{topic} \u65b0\u6a21\u578b",
  "{topic} \u65b0\u529f\u80fd",
  "{topic} \u6d4b\u8bc4",
  "{topic} \u89e3\u8bfb",
  "{topic} \u8ba8\u8bba",
];

function applyQueryTemplates(topic, templates) {
  return templates.map((template) => normalizeText(template.replaceAll("{topic}", topic)));
}

function legacyExpandSearchQueries({ topic, seedQueries, maxQueries, includeChineseVariants } = {}) {
  const normalizedTopic = normalizeText(topic);
  const queries = [];
  if (normalizedTopic) {
    queries.push(...applyQueryTemplates(normalizedTopic, EN_QUERY_TEMPLATES));
    if (includeChineseVariants !== false) {
      queries.push(...applyQueryTemplates(normalizedTopic, ZH_QUERY_TEMPLATES));
    }
  }
  queries.push(...(seedQueries || []));
  const deduped = dedupeStrings(queries);
  const maxCount = clampInteger(maxQueries, 1, MAX_MAX_ITEMS, 24);
  return deduped.slice(0, maxCount);
}

function buildExpansionSeeds({ topic, seedQueries, includeChineseVariants, maxQueries }) {
  const terms = dedupeStrings([topic, ...(seedQueries || [])]);
  const limit = clampInteger(maxQueries, 1, MAX_MAX_ITEMS, 24);

  return terms.map((term, index) => ({
    id: `mcp_seed_${index + 1}`,
    term,
    intent: "auto",
    facets: ["official", "analysis", "release", "community", "demo"],
    locale: includeChineseVariants === false ? "en-US" : "zh-CN",
    includeChineseVariants,
    keywords: terms.filter((value) => value !== term).slice(0, 4),
    maxExpansions: limit,
  }));
}

function expandSearchQueries({ topic, seedQueries, maxQueries, includeChineseVariants } = {}) {
  const maxCount = clampInteger(maxQueries, 1, MAX_MAX_ITEMS, 24);
  const seeds = buildExpansionSeeds({
    topic,
    seedQueries,
    includeChineseVariants,
    maxQueries: maxCount,
  });

  const expanded = expandQuerySeeds(seeds, {
    maxExpandedQueries: maxCount,
  }).map((item) => item.query);

  return dedupeStrings([
    ...(seedQueries || []),
    ...expanded,
  ]).slice(0, maxCount);
}

function compilePatterns(patterns) {
  return (patterns || [])
    .map((pattern) => normalizeText(pattern))
    .filter(Boolean)
    .map((pattern) => {
      try {
        return new RegExp(pattern, "i");
      } catch (error) {
        throw new Error(`Invalid regular expression "${pattern}": ${error?.message || String(error)}`);
      }
    });
}

function matchesPatterns(value, expressions) {
  if (!expressions?.length) {
    return false;
  }
  const text = String(value ?? "");
  return expressions.some((expression) => expression.test(text));
}

function safeUrl(value, base) {
  try {
    return new URL(value, base);
  } catch {
    return null;
  }
}

function summarizeSnapshot(snapshot) {
  return {
    url: snapshot?.url || null,
    title: snapshot?.title || null,
    canonical: snapshot?.canonical || null,
    readyState: snapshot?.readyState || null,
    metaSummary: snapshot?.metaSummary || null,
    stats: snapshot?.stats || null,
  };
}

async function bridgeFetch(path, options = {}) {
  return bridgeClient.bridgeFetch(path, options);
}

async function sendCommand(command, args = {}, timeoutMs = DEFAULT_COMMAND_TIMEOUT_MS) {
  return bridgeClient.command(command, args, timeoutMs);
}

function registerTool(name, description, inputSchema, handler) {
  server.registerTool(name, { description, inputSchema }, async (input) => {
    try {
      return formatContent(await handler(input));
    } catch (error) {
      return formatError(error);
    }
  });
}

async function listTabs(args = {}) {
  return sendCommand("list_tabs", args);
}

async function getActiveTabSummary() {
  const tabs = await listTabs({ activeOnly: true });
  const activeTab = Array.isArray(tabs) ? tabs[0] : null;
  if (!activeTab?.id) {
    throw new Error("No active Edge tab is available.");
  }
  return activeTab;
}

async function getTabSummary(tabId) {
  const tabs = await listTabs();
  const tab = Array.isArray(tabs) ? tabs.find((item) => item?.id === tabId) : null;
  if (!tab) {
    throw new Error(`Edge tab ${tabId} was not found.`);
  }
  return tab;
}

async function poll(fn, { timeoutMs = DEFAULT_CRAWL_TIMEOUT_MS, intervalMs = DEFAULT_POLL_INTERVAL_MS, description = "condition" } = {}) {
  const startedAt = Date.now();
  let lastError = null;

  while (Date.now() - startedAt < timeoutMs) {
    try {
      const value = await fn();
      if (value) {
        return value;
      }
    } catch (error) {
      lastError = error;
    }
    await sleep(intervalMs);
  }

  if (lastError) {
    throw new Error(
      `Timed out waiting for ${description} after ${timeoutMs}ms. Last error: ${lastError?.message || String(lastError)}`
    );
  }

  throw new Error(`Timed out waiting for ${description} after ${timeoutMs}ms.`);
}

async function mapWithConcurrency(items, concurrency, worker) {
  const maxWorkers = clampInteger(concurrency, 1, MAX_BATCH_CONCURRENCY, DEFAULT_BATCH_CONCURRENCY);
  const results = new Array(items.length);
  let index = 0;

  async function runWorker() {
    while (true) {
      const currentIndex = index;
      index += 1;
      if (currentIndex >= items.length) {
        return;
      }
      results[currentIndex] = await worker(items[currentIndex], currentIndex);
    }
  }

  await Promise.all(Array.from({ length: Math.min(maxWorkers, items.length) }, () => runWorker()));
  return results;
}

function buildWaitForArgs(input) {
  const waitArgs = {};

  if (input.waitSelector) {
    waitArgs.selector = input.waitSelector;
  }
  if (input.waitXpath) {
    waitArgs.xpath = input.waitXpath;
  }
  if (input.waitText) {
    waitArgs.text = input.waitText;
  }
  if (input.waitWorld) {
    waitArgs.world = input.waitWorld;
  }

  return Object.keys(waitArgs).length ? waitArgs : null;
}

function waitResultFound(waitResult) {
  return Array.isArray(waitResult?.frames) && waitResult.frames.some((frame) => Boolean(frame?.result?.found));
}

async function evaluateInTab(tabId, expression, options = {}) {
  const payload = await sendCommand(
    "send_cdp",
    {
      tabId,
      method: "Runtime.evaluate",
      params: {
        expression,
        awaitPromise: options.awaitPromise !== false,
        returnByValue: options.returnByValue !== false,
        userGesture: Boolean(options.userGesture),
      },
      detachAfter: false,
    },
    clampInteger(options.timeoutMs, 1000, 120000, DEFAULT_COMMAND_TIMEOUT_MS)
  );

  const cdpResult = payload?.result;
  if (cdpResult?.exceptionDetails) {
    const description =
      cdpResult.exceptionDetails.exception?.description ||
      cdpResult.exceptionDetails.text ||
      "Runtime.evaluate failed.";
    throw new Error(description);
  }

  return cdpResult?.result?.value;
}

async function waitForDocumentReady(tabId, timeoutMs) {
  return poll(
    async () => {
      const state = await evaluateInTab(
        tabId,
        "({ readyState: document.readyState, hasBody: Boolean(document.body), href: location.href })",
        { timeoutMs: Math.min(timeoutMs, DEFAULT_COMMAND_TIMEOUT_MS) }
      );

      if (state?.hasBody && ["interactive", "complete"].includes(state.readyState)) {
        return state;
      }

      return null;
    },
    { timeoutMs, description: `tab ${tabId} document readiness` }
  );
}

async function waitForCrawlTarget(tabId, input, timeoutMs) {
  await waitForDocumentReady(tabId, timeoutMs);

  const waitArgs = buildWaitForArgs(input);
  if (!waitArgs) {
    return;
  }

  const waitResult = await sendCommand("wait_for", { tabId, ...waitArgs, timeoutMs }, timeoutMs);
  if (!waitResultFound(waitResult)) {
    throw new Error(`Wait condition did not resolve inside Edge tab ${tabId}.`);
  }
}

async function prepareCrawlTarget(input = {}) {
  const timeoutMs = clampInteger(input.timeoutMs, 1000, 120000, DEFAULT_CRAWL_TIMEOUT_MS);
  let tab;
  let createdTab = false;
  let navigated = false;

  if (input.url) {
    createdTab = typeof input.tabId !== "number" && input.createNewTab !== false;
    tab = await sendCommand(
      "navigate",
      {
        url: input.url,
        tabId: input.tabId,
        createNewTab: createdTab,
        active: createdTab ? input.active === true : undefined,
      },
      timeoutMs
    );
    navigated = true;
  } else if (typeof input.tabId === "number") {
    tab = await getTabSummary(input.tabId);
  } else {
    tab = await getActiveTabSummary();
  }

  if (!tab?.id) {
    throw new Error("Failed to resolve an Edge tab for crawling.");
  }

  await waitForCrawlTarget(tab.id, input, timeoutMs);

  return {
    tabId: tab.id,
    tab: await getTabSummary(tab.id).catch(() => tab),
    createdTab,
    navigated,
    timeoutMs,
  };
}

async function tryCloseTab(tabId) {
  try {
    await sendCommand(
      "send_cdp",
      {
        tabId,
        method: "Page.close",
        params: {},
        detachAfter: true,
      },
      DEFAULT_COMMAND_TIMEOUT_MS
    );
    return true;
  } catch {
    return false;
  }
}

async function runWithCrawlTarget(input, worker) {
  const prepared = await prepareCrawlTarget(input);
  const shouldClose = input.closeAfter ?? prepared.createdTab;

  try {
    const value = await worker(prepared);
    const closedTempTab = shouldClose ? await tryCloseTab(prepared.tabId) : false;
    return { prepared, value, closedTempTab };
  } catch (error) {
    if (shouldClose) {
      await tryCloseTab(prepared.tabId);
    }
    throw error;
  }
}

function buildExtractOptions(input = {}) {
  const maxItems = clampInteger(input.maxItems, 1, MAX_MAX_ITEMS, DEFAULT_MAX_ITEMS);
  return {
    includeText: input.includeText !== false,
    includeHtml: Boolean(input.includeHtml),
    includeLinks: input.includeLinks !== false,
    includeImages: Boolean(input.includeImages),
    includeHeadings: input.includeHeadings !== false,
    includeMeta: input.includeMeta !== false,
    includeJsonLd: Boolean(input.includeJsonLd),
    includeForms: Boolean(input.includeForms),
    includeTables: Boolean(input.includeTables),
    linkSelector: normalizeText(input.linkSelector) || "a[href]",
    textBlockSelector: normalizeText(input.textBlockSelector) || "article p, main p, p",
    maxItems,
    maxTextLength: clampInteger(input.maxTextLength, 200, 500000, DEFAULT_MAX_TEXT_LENGTH),
    maxHtmlLength: clampInteger(input.maxHtmlLength, 1000, 1000000, DEFAULT_MAX_HTML_LENGTH),
    customSelectors: Array.isArray(input.customSelectors)
      ? input.customSelectors
          .map((selector) => ({
            name: normalizeText(selector.name),
            selector: normalizeText(selector.selector),
            attribute: normalizeText(selector.attribute) || null,
            all: selector.all !== false,
            includeText: selector.includeText !== false,
            includeHtml: Boolean(selector.includeHtml),
            maxItems: clampInteger(selector.maxItems, 1, MAX_MAX_ITEMS, maxItems),
          }))
          .filter((selector) => selector.name && selector.selector)
      : [],
  };
}

// Keep extraction in CDP Runtime.evaluate so crawler tools can read pages without relying on screenshot flows.
function buildExtractExpression(input = {}) {
  const options = JSON.stringify(buildExtractOptions(input));
  return `(() => {
    const options = ${options};
    const maxItems = Number(options.maxItems) > 0 ? Number(options.maxItems) : ${DEFAULT_MAX_ITEMS};
    const maxTextLength = Number(options.maxTextLength) > 0 ? Number(options.maxTextLength) : ${DEFAULT_MAX_TEXT_LENGTH};
    const maxHtmlLength = Number(options.maxHtmlLength) > 0 ? Number(options.maxHtmlLength) : ${DEFAULT_MAX_HTML_LENGTH};

    function normalizeText(value) {
      return String(value ?? "").replace(/\\s+/g, " ").trim();
    }

    function truncate(value, limit) {
      const text = String(value ?? "");
      return text.length <= limit ? text : text.slice(0, limit) + "...[truncated]";
    }

    function toAbsolute(value) {
      try {
        return new URL(value, location.href).href;
      } catch {
        return null;
      }
    }

    function collectNodes(selector) {
      if (!selector) {
        return [];
      }
      try {
        return Array.from(document.querySelectorAll(selector));
      } catch {
        return [];
      }
    }

    function selectorPath(node) {
      if (!node || !node.tagName) {
        return null;
      }

      const parts = [];
      let current = node;
      while (current && current.nodeType === Node.ELEMENT_NODE && parts.length < 5) {
        let part = current.tagName.toLowerCase();
        if (current.id) {
          part += "#" + current.id;
          parts.unshift(part);
          break;
        }

        const classTokens = typeof current.className === "string"
          ? current.className.trim().split(/\\s+/).filter(Boolean).slice(0, 2)
          : [];
        if (classTokens.length) {
          part += "." + classTokens.join(".");
        }

        if (current.parentElement) {
          const siblings = Array.from(current.parentElement.children).filter((child) => child.tagName === current.tagName);
          if (siblings.length > 1) {
            part += ":nth-of-type(" + (siblings.indexOf(current) + 1) + ")";
          }
        }

        parts.unshift(part);
        current = current.parentElement;
      }

      return parts.join(" > ");
    }

    function describeRect(node) {
      if (!node || typeof node.getBoundingClientRect !== "function") {
        return null;
      }

      const rect = node.getBoundingClientRect();
      return {
        x: Math.round(rect.x),
        y: Math.round(rect.y),
        width: Math.round(rect.width),
        height: Math.round(rect.height),
      };
    }

    function uniqueBy(items, keyFn) {
      const seen = new Set();
      const output = [];
      for (const item of items) {
        const key = keyFn(item);
        if (!key || seen.has(key)) {
          continue;
        }
        seen.add(key);
        output.push(item);
        if (output.length >= maxItems) {
          break;
        }
      }
      return output;
    }

    const meta = options.includeMeta
      ? uniqueBy(
          Array.from(document.querySelectorAll("meta"))
            .map((node) => ({
              name: node.getAttribute("name"),
              property: node.getAttribute("property"),
              httpEquiv: node.getAttribute("http-equiv"),
              content: truncate(node.getAttribute("content") || "", maxTextLength),
            }))
            .filter((item) => item.name || item.property || item.httpEquiv || item.content),
          (item) => [item.name, item.property, item.httpEquiv, item.content].join("|")
        )
      : [];

    const canonical = document.querySelector('link[rel="canonical"]')?.href || null;

    const headings = options.includeHeadings
      ? collectNodes("h1, h2, h3, h4, h5, h6").slice(0, maxItems).map((node, index) => ({
          level: node.tagName.toLowerCase(),
          text: truncate(normalizeText(node.innerText || node.textContent || ""), maxTextLength),
          selector: selectorPath(node),
          domIndex: index,
        }))
      : [];

    const links = options.includeLinks
      ? (() => {
          const seen = new Map();
          const nodes = collectNodes(options.linkSelector || "a[href]");

          nodes.forEach((node, index) => {
            if (seen.size >= maxItems) {
              return;
            }

            const rawHref = node.getAttribute("href");
            const href = toAbsolute(rawHref);
            if (!href || !rawHref) {
              return;
            }

            const lowered = String(rawHref).trim().toLowerCase();
            if (
              !lowered ||
              lowered.startsWith("#") ||
              lowered.startsWith("javascript:") ||
              lowered.startsWith("mailto:") ||
              lowered.startsWith("tel:") ||
              lowered.startsWith("data:")
            ) {
              return;
            }

            let sameOrigin = false;
            try {
              sameOrigin = new URL(href).origin === location.origin;
            } catch {
              sameOrigin = false;
            }

            const entry = {
              href,
              text: truncate(normalizeText(node.innerText || node.textContent || node.getAttribute("title") || ""), maxTextLength),
              title: truncate(normalizeText(node.getAttribute("title") || ""), maxTextLength),
              rel: node.getAttribute("rel") || null,
              target: node.getAttribute("target") || null,
              download: node.hasAttribute("download"),
              sameOrigin,
              selector: selectorPath(node),
              domIndex: index,
            };

            const existing = seen.get(href);
            if (!existing || entry.text.length > existing.text.length) {
              seen.set(href, entry);
            }
          });

          return Array.from(seen.values());
        })()
      : [];

    const images = options.includeImages
      ? uniqueBy(
          collectNodes("img[src]")
            .map((node, index) => ({
              src: toAbsolute(node.getAttribute("src")),
              alt: truncate(normalizeText(node.getAttribute("alt") || ""), maxTextLength),
              title: truncate(normalizeText(node.getAttribute("title") || ""), maxTextLength),
              width: Number(node.naturalWidth || node.width || 0) || null,
              height: Number(node.naturalHeight || node.height || 0) || null,
              selector: selectorPath(node),
              domIndex: index,
            }))
            .filter((image) => image.src),
          (image) => image.src
        )
      : [];

    const text = options.includeText === false
      ? null
      : truncate(document.body?.innerText || document.documentElement?.innerText || "", maxTextLength);

    const textBlocks = options.includeText === false
      ? []
      : uniqueBy(
          collectNodes(options.textBlockSelector || "article p, main p, p")
            .map((node, index) => ({
              text: truncate(normalizeText(node.innerText || node.textContent || ""), maxTextLength),
              selector: selectorPath(node),
              domIndex: index,
            }))
            .filter((item) => item.text.length >= 40),
          (item) => item.text
        );

    const jsonLd = options.includeJsonLd
      ? collectNodes('script[type="application/ld+json"]').slice(0, maxItems).map((node, index) => {
          const raw = (node.textContent || "").trim();
          if (!raw) {
            return null;
          }

          try {
            return {
              index,
              data: JSON.parse(raw),
            };
          } catch {
            return {
              index,
              raw: truncate(raw, maxTextLength),
            };
          }
        }).filter(Boolean)
      : [];

    const forms = options.includeForms
      ? Array.from(document.forms).slice(0, maxItems).map((form, index) => ({
          index,
          action: toAbsolute(form.getAttribute("action") || form.action || ""),
          method: normalizeText(form.getAttribute("method") || form.method || "GET").toUpperCase() || "GET",
          selector: selectorPath(form),
          fields: Array.from(form.elements || []).slice(0, maxItems).map((field) => ({
            tag: field.tagName ? field.tagName.toLowerCase() : null,
            type: field.getAttribute ? field.getAttribute("type") || null : null,
            name: field.getAttribute ? field.getAttribute("name") || null : null,
            id: field.id || null,
            value: "value" in field ? truncate(field.value || "", maxTextLength) : null,
            placeholder: field.getAttribute ? field.getAttribute("placeholder") || null : null,
          })),
        }))
      : [];

    const tables = options.includeTables
      ? collectNodes("table").slice(0, maxItems).map((table, index) => {
          const rows = Array.from(table.querySelectorAll("tr")).slice(0, maxItems);
          return {
            index,
            selector: selectorPath(table),
            headers: Array.from(table.querySelectorAll("th")).slice(0, maxItems).map((cell) => truncate(normalizeText(cell.innerText || cell.textContent || ""), maxTextLength)),
            rows: rows.slice(0, 10).map((row) => Array.from(row.cells || []).slice(0, maxItems).map((cell) => truncate(normalizeText(cell.innerText || cell.textContent || ""), maxTextLength))),
          };
        })
      : [];

    const custom = (options.customSelectors || []).map((spec) => {
      const nodes = collectNodes(spec.selector);
      const limit = spec.all === false ? 1 : Math.min(spec.maxItems || maxItems, maxItems);
      return {
        name: spec.name,
        selector: spec.selector,
        count: nodes.length,
        items: nodes.slice(0, limit).map((node, index) => ({
          index,
          tag: node.tagName ? node.tagName.toLowerCase() : null,
          selector: selectorPath(node),
          attribute: spec.attribute ? node.getAttribute(spec.attribute) : null,
          text: spec.includeText === false ? null : truncate(normalizeText(node.innerText || node.textContent || ""), maxTextLength),
          html: spec.includeHtml ? truncate(node.outerHTML || "", maxHtmlLength) : null,
          href: node.getAttribute ? toAbsolute(node.getAttribute("href")) : null,
          src: node.getAttribute ? toAbsolute(node.getAttribute("src")) : null,
          rect: describeRect(node),
        })),
      };
    });

    const descriptionMeta = meta.find((item) => (
      item.name && item.name.toLowerCase() === "description"
    ) || (
      item.property && item.property.toLowerCase() === "og:description"
    )) || null;

    return {
      url: location.href,
      origin: location.origin,
      title: document.title,
      lang: document.documentElement?.lang || null,
      readyState: document.readyState,
      canonical,
      meta,
      metaSummary: {
        description: descriptionMeta?.content || null,
      },
      headings,
      links,
      images,
      text,
      textBlocks,
      jsonLd,
      forms,
      tables,
      custom,
      html: options.includeHtml ? truncate(document.documentElement.outerHTML || "", maxHtmlLength) : null,
      stats: {
        wordCount: text ? text.split(/\\s+/).filter(Boolean).length : 0,
        linkCount: links.length,
        imageCount: images.length,
        headingCount: headings.length,
        jsonLdCount: jsonLd.length,
        formCount: forms.length,
        tableCount: tables.length,
      },
    };
  })()`;
}

async function extractSnapshotFromTab(tabId, input = {}, timeoutMs = DEFAULT_CRAWL_TIMEOUT_MS) {
  return evaluateInTab(tabId, buildExtractExpression(input), { timeoutMs });
}

function buildDiscoveryConfig(input = {}) {
  return {
    sameOriginOnly: Boolean(input.sameOriginOnly),
    includeExternal: input.includeExternal !== false,
    maxLinks: clampInteger(input.maxLinks, 1, MAX_MAX_ITEMS, DEFAULT_MAX_ITEMS),
    allowPatterns: compilePatterns(input.allowPatterns),
    denyPatterns: compilePatterns(input.denyPatterns),
    textPatterns: compilePatterns(input.textPatterns),
  };
}

function scoreDiscoveredLink(link, discoveryConfig) {
  let score = 0;
  const reasons = [];

  if (link.sameOrigin) {
    score += 3;
    reasons.push("same-origin");
  }
  if (link.text) {
    score += 2;
    reasons.push("anchor-text");
  }

  const parsedUrl = safeUrl(link.href);
  if (parsedUrl && !parsedUrl.search) {
    score += 1;
    reasons.push("no-query");
  }
  if (parsedUrl && parsedUrl.pathname && parsedUrl.pathname !== "/") {
    score += 1;
    reasons.push("deep-path");
  }
  if (matchesPatterns(link.href, discoveryConfig.allowPatterns)) {
    score += 4;
    reasons.push("allow-pattern");
  }
  if (matchesPatterns(link.text, discoveryConfig.textPatterns)) {
    score += 4;
    reasons.push("text-pattern");
  }
  if (link.rel && /\bnofollow\b/i.test(link.rel)) {
    score -= 1;
    reasons.push("nofollow");
  }

  return { score, reasons };
}

function filterDiscoveredLinks(snapshot, input = {}) {
  const discoveryConfig = buildDiscoveryConfig(input);
  const sourceUrl = snapshot?.url || null;
  const source = safeUrl(sourceUrl);
  const bestByHref = new Map();

  for (const link of snapshot?.links || []) {
    const normalizedHref = safeUrl(link?.href, sourceUrl)?.href;
    if (!normalizedHref) {
      continue;
    }

    const hrefUrl = safeUrl(normalizedHref);
    const sameOrigin = source ? hrefUrl?.origin === source.origin : Boolean(link.sameOrigin);
    const text = normalizeText(link?.text);
    const title = normalizeText(link?.title);

    if (discoveryConfig.sameOriginOnly && !sameOrigin) {
      continue;
    }
    if (discoveryConfig.includeExternal === false && !sameOrigin) {
      continue;
    }
    if (matchesPatterns(normalizedHref, discoveryConfig.denyPatterns) || matchesPatterns(text, discoveryConfig.denyPatterns)) {
      continue;
    }

    const allowMatched =
      !discoveryConfig.allowPatterns.length ||
      matchesPatterns(normalizedHref, discoveryConfig.allowPatterns) ||
      matchesPatterns(text, discoveryConfig.allowPatterns);
    if (!allowMatched) {
      continue;
    }

    if (discoveryConfig.textPatterns.length && !matchesPatterns(text, discoveryConfig.textPatterns)) {
      continue;
    }

    const { score, reasons } = scoreDiscoveredLink({ ...link, href: normalizedHref, sameOrigin, text }, discoveryConfig);
    const candidate = {
      ...link,
      href: normalizedHref,
      text,
      title,
      sameOrigin,
      score,
      reasons,
    };

    const existing = bestByHref.get(normalizedHref);
    if (!existing || candidate.score > existing.score || candidate.text.length > existing.text.length) {
      bestByHref.set(normalizedHref, candidate);
    }
  }

  return Array.from(bestByHref.values())
    .sort((left, right) => {
      const scoreDelta = (right.score || 0) - (left.score || 0);
      if (scoreDelta !== 0) {
        return scoreDelta;
      }
      const domLeft = Number.isFinite(left.domIndex) ? left.domIndex : Number.MAX_SAFE_INTEGER;
      const domRight = Number.isFinite(right.domIndex) ? right.domIndex : Number.MAX_SAFE_INTEGER;
      if (domLeft !== domRight) {
        return domLeft - domRight;
      }
      return String(left.href || "").localeCompare(String(right.href || ""));
    })
    .slice(0, discoveryConfig.maxLinks);
}

function aggregateDiscoveredLinks(items, maxLinks = DEFAULT_MAX_ITEMS, sourceField = "inputUrl") {
  const aggregateLimit = clampInteger(maxLinks, 1, MAX_MAX_ITEMS, DEFAULT_MAX_ITEMS);
  const bestByHref = new Map();

  for (const item of items || []) {
    if (!item?.ok || !Array.isArray(item.discoveredLinks)) {
      continue;
    }

    const sourceValue = item.query || item[sourceField] || item.finalUrl || null;
    for (const link of item.discoveredLinks) {
      const href = link?.href;
      if (!href) {
        continue;
      }

      const existing = bestByHref.get(href);
      if (!existing) {
        bestByHref.set(href, {
          ...link,
          sources: sourceValue ? [sourceValue] : [],
        });
        continue;
      }

      if (sourceValue && !existing.sources.includes(sourceValue)) {
        existing.sources.push(sourceValue);
      }

      if ((link.score || 0) > (existing.score || 0) || String(link.text || "").length > String(existing.text || "").length) {
        Object.assign(existing, link, { sources: existing.sources });
      }
    }
  }

  return Array.from(bestByHref.values())
    .sort((left, right) => {
      const scoreDelta = (right.score || 0) - (left.score || 0);
      if (scoreDelta !== 0) {
        return scoreDelta;
      }
      return (right.sources?.length || 0) - (left.sources?.length || 0);
    })
    .slice(0, aggregateLimit);
}

function renderSearchUrl(template, query) {
  if (!hasSearchTemplatePlaceholder(template)) {
    throw new Error('searchUrlTemplate must include a supported placeholder such as "{{query}}", "{{query_encoded}}", "{{query_plus}}", "{{query_raw}}", "{{topic}}", or "{queryEncoded}".');
  }

  return compileTemplate(template, {
    query,
    queryRaw: query,
    queryEncoded: encodeURIComponent(query),
    queryPlus: query.trim().split(/\s+/).filter(Boolean).join("+"),
    topic: query,
    topicRaw: query,
    topicEncoded: encodeURIComponent(query),
    topicPlus: query.trim().split(/\s+/).filter(Boolean).join("+"),
  });
}

function summarizePreparedTarget(prepared, snapshot) {
  return {
    tabId: prepared.tabId,
    createdTab: prepared.createdTab,
    navigated: prepared.navigated,
    initialUrl: prepared.tab?.url || null,
    finalUrl: snapshot?.url || prepared.tab?.url || null,
    title: snapshot?.title || prepared.tab?.title || null,
  };
}

const crawlTargetShape = {
  tabId: z.number().int().optional(),
  url: z.string().min(1).optional(),
  createNewTab: z.boolean().optional(),
  active: z.boolean().optional(),
  closeAfter: z.boolean().optional(),
  timeoutMs: z.number().int().positive().optional(),
  waitSelector: z.string().optional(),
  waitXpath: z.string().optional(),
  waitText: z.string().optional(),
  waitWorld: waitWorldSchema.optional(),
};

const crawlExtractShape = {
  includeText: z.boolean().optional(),
  includeHtml: z.boolean().optional(),
  includeLinks: z.boolean().optional(),
  includeImages: z.boolean().optional(),
  includeHeadings: z.boolean().optional(),
  includeMeta: z.boolean().optional(),
  includeJsonLd: z.boolean().optional(),
  includeForms: z.boolean().optional(),
  includeTables: z.boolean().optional(),
  linkSelector: z.string().optional(),
  textBlockSelector: z.string().optional(),
  maxItems: z.number().int().positive().max(MAX_MAX_ITEMS).optional(),
  maxTextLength: z.number().int().positive().optional(),
  maxHtmlLength: z.number().int().positive().optional(),
  customSelectors: z.array(customSelectorSchema).optional(),
};

const crawlDiscoveryShape = {
  sameOriginOnly: z.boolean().optional(),
  includeExternal: z.boolean().optional(),
  allowPatterns: z.array(z.string().min(1)).optional(),
  denyPatterns: z.array(z.string().min(1)).optional(),
  textPatterns: z.array(z.string().min(1)).optional(),
  maxLinks: z.number().int().positive().max(MAX_MAX_ITEMS).optional(),
  aggregateMaxLinks: z.number().int().positive().max(MAX_MAX_ITEMS).optional(),
};

registerTool(
  "edge_bridge_status",
  "Check whether the local Edge bridge is configured and whether the Edge extension is connected.",
  {},
  async () => bridgeFetch("/api/status", { method: "GET" })
);

registerTool(
  "edge_command",
  "Send a raw command to the Edge Control bridge. Use this when a purpose-built tool is not enough.",
  {
    command: z.string(),
    args: z.record(z.any()).optional(),
    timeoutMs: z.number().int().positive().optional(),
  },
  async ({ command, args, timeoutMs }) => sendCommand(command, args, timeoutMs)
);

registerTool(
  "edge_expand_queries",
  "Expand a topic into a broader set of search queries for batch crawling.",
  {
    topic: z.string().optional(),
    seedQueries: z.array(z.string().min(1)).optional(),
    maxQueries: z.number().int().positive().max(MAX_MAX_ITEMS).optional(),
    includeChineseVariants: z.boolean().optional(),
  },
  async ({ topic, seedQueries, maxQueries, includeChineseVariants }) => {
    const queries = expandSearchQueries({
      topic,
      seedQueries,
      maxQueries,
      includeChineseVariants,
    });

    if (!queries.length) {
      throw new Error("edge_expand_queries requires a topic or at least one seed query.");
    }

    return {
      total: queries.length,
      queries,
    };
  }
);

registerTool(
  "edge_list_tabs",
  "List tabs in the current Edge browser session.",
  {
    currentWindow: z.boolean().optional(),
    activeOnly: z.boolean().optional(),
  },
  async ({ currentWindow, activeOnly }) => sendCommand("list_tabs", { currentWindow, activeOnly })
);

registerTool(
  "edge_focus_tab",
  "Focus a specific Edge tab, or the current active tab if tabId is omitted.",
  {
    tabId: z.number().int().optional(),
  },
  async ({ tabId }) => sendCommand("focus_tab", { tabId })
);

registerTool(
  "edge_navigate",
  "Navigate an existing tab or create a new tab at a URL.",
  {
    url: z.string().min(1),
    tabId: z.number().int().optional(),
    createNewTab: z.boolean().optional(),
    active: z.boolean().optional(),
  },
  async ({ url, tabId, createNewTab, active }) => sendCommand("navigate", { url, tabId, createNewTab, active })
);

registerTool(
  "edge_reload",
  "Reload a tab.",
  {
    tabId: z.number().int().optional(),
    bypassCache: z.boolean().optional(),
  },
  async ({ tabId, bypassCache }) => sendCommand("reload", { tabId, bypassCache })
);

registerTool(
  "edge_reload_extension",
  "Hot-reload the local Edge Control unpacked extension inside the current Edge session and wait for it to reconnect.",
  {
    delayMs: z.number().int().nonnegative().optional(),
    timeoutMs: z.number().int().positive().optional(),
    waitForHealthyMs: z.number().int().positive().optional(),
    pollIntervalMs: z.number().int().positive().optional(),
  },
  async ({ delayMs, timeoutMs, waitForHealthyMs, pollIntervalMs }) => bridgeClient.reloadExtension({
    delayMs,
    timeoutMs,
    waitForHealthyMs,
    pollIntervalMs,
  })
);

registerTool(
  "edge_wait_for",
  "Wait for a selector, xpath, text match, or active element to appear inside a tab.",
  {
    tabId: z.number().int().optional(),
    selector: z.string().optional(),
    xpath: z.string().optional(),
    text: z.string().optional(),
    activeElement: z.boolean().optional(),
    timeoutMs: z.number().int().positive().optional(),
    world: waitWorldSchema.optional(),
  },
  async (input) => sendCommand("wait_for", input, input.timeoutMs)
);

registerTool(
  "edge_query",
  "Query DOM elements by CSS selector and return a summarized match list.",
  {
    tabId: z.number().int().optional(),
    selector: z.string(),
    maxResults: z.number().int().positive().optional(),
    maxLength: z.number().int().positive().optional(),
    includeHtml: z.boolean().optional(),
    world: waitWorldSchema.optional(),
  },
  async (input) => sendCommand("query", input)
);

registerTool(
  "edge_get_dom",
  "Read a page or a specific element from the live DOM.",
  {
    tabId: z.number().int().optional(),
    selector: z.string().optional(),
    xpath: z.string().optional(),
    text: z.string().optional(),
    activeElement: z.boolean().optional(),
    includeHtml: z.boolean().optional(),
    includeText: z.boolean().optional(),
    maxLength: z.number().int().positive().optional(),
    world: waitWorldSchema.optional(),
  },
  async (input) => sendCommand("get_dom", input)
);

registerTool(
  "edge_click",
  "Click an element selected by CSS selector, xpath, text match, or the active element.",
  {
    tabId: z.number().int().optional(),
    selector: z.string().optional(),
    xpath: z.string().optional(),
    text: z.string().optional(),
    activeElement: z.boolean().optional(),
    index: z.number().int().nonnegative().optional(),
    world: waitWorldSchema.optional(),
  },
  async (input) => sendCommand("click", input)
);

registerTool(
  "edge_type",
  "Type text into an input, textarea, contenteditable element, or the active element.",
  {
    tabId: z.number().int().optional(),
    text: z.string(),
    selector: z.string().optional(),
    xpath: z.string().optional(),
    activeElement: z.boolean().optional(),
    clearFirst: z.boolean().optional(),
    world: waitWorldSchema.optional(),
  },
  async (input) => sendCommand("type", input)
);

registerTool(
  "edge_press_key",
  "Send a key press to an element or to the active element.",
  {
    tabId: z.number().int().optional(),
    key: z.string(),
    selector: z.string().optional(),
    xpath: z.string().optional(),
    activeElement: z.boolean().optional(),
    world: waitWorldSchema.optional(),
  },
  async (input) => sendCommand("press_key", input)
);

registerTool(
  "edge_eval",
  "Evaluate arbitrary JavaScript inside the page. Prefer MAIN world when you need page-owned globals.",
  {
    tabId: z.number().int().optional(),
    expression: z.string(),
    world: waitWorldSchema.optional(),
    maxLength: z.number().int().positive().optional(),
  },
  async (input) => sendCommand("eval", input)
);

registerTool(
  "edge_send_cdp",
  "Send a raw Chrome DevTools Protocol command to a tab through chrome.debugger.",
  {
    tabId: z.number().int().optional(),
    method: z.string(),
    paramsJson: z.string().optional(),
    detachAfter: z.boolean().optional(),
  },
  async ({ tabId, method, paramsJson, detachAfter }) => {
    const params = paramsJson ? JSON.parse(paramsJson) : {};
    return sendCommand("send_cdp", {
      tabId,
      method,
      params,
      detachAfter,
    });
  }
);

registerTool(
  "edge_crawl_snapshot",
  "Open or reuse a tab, wait for the page to settle, and return a normalized crawler snapshot from live page state.",
  {
    ...crawlTargetShape,
    ...crawlExtractShape,
  },
  async (input) => {
    const startedAt = Date.now();
    const { prepared, value, closedTempTab } = await runWithCrawlTarget(input, async (resolved) => {
      const snapshot = await extractSnapshotFromTab(resolved.tabId, input, resolved.timeoutMs);
      return { snapshot };
    });

    return {
      durationMs: Date.now() - startedAt,
      target: summarizePreparedTarget(prepared, value.snapshot),
      closedTempTab,
      snapshot: value.snapshot,
    };
  }
);

registerTool(
  "edge_crawl_discover_links",
  "Open or reuse a tab, extract page links through CDP, score and filter them, and optionally return the source snapshot.",
  {
    ...crawlTargetShape,
    ...crawlExtractShape,
    ...crawlDiscoveryShape,
    includeSnapshot: z.boolean().optional(),
  },
  async (input) => {
    const startedAt = Date.now();
    const extractionInput = {
      ...input,
      includeLinks: true,
      includeText: input.includeSnapshot ? input.includeText : false,
      includeHtml: input.includeSnapshot ? input.includeHtml : false,
      includeImages: input.includeSnapshot ? input.includeImages : false,
      includeHeadings: input.includeSnapshot ?? true,
      includeMeta: input.includeSnapshot ?? true,
      includeJsonLd: input.includeSnapshot ? input.includeJsonLd : false,
      includeForms: input.includeSnapshot ? input.includeForms : false,
      includeTables: input.includeSnapshot ? input.includeTables : false,
    };

    const { prepared, value, closedTempTab } = await runWithCrawlTarget(input, async (resolved) => {
      const snapshot = await extractSnapshotFromTab(resolved.tabId, extractionInput, resolved.timeoutMs);
      const links = filterDiscoveredLinks(snapshot, input);
      return { snapshot, links };
    });

    return {
      durationMs: Date.now() - startedAt,
      target: summarizePreparedTarget(prepared, value.snapshot),
      closedTempTab,
      source: summarizeSnapshot(value.snapshot),
      links: value.links,
      snapshot: input.includeSnapshot ? value.snapshot : undefined,
    };
  }
);

registerTool(
  "edge_crawl_batch",
  "Crawl many URLs in parallel using background tabs, with optional page snapshots and discovered link sets for each URL.",
  {
    urls: z.array(z.string().min(1)).min(1),
    concurrency: z.number().int().positive().max(MAX_BATCH_CONCURRENCY).optional(),
    mode: z.enum(["snapshot", "discover", "both"]).optional(),
    createNewTab: z.boolean().optional(),
    active: z.boolean().optional(),
    closeAfter: z.boolean().optional(),
    timeoutMs: z.number().int().positive().optional(),
    waitSelector: z.string().optional(),
    waitXpath: z.string().optional(),
    waitText: z.string().optional(),
    waitWorld: waitWorldSchema.optional(),
    ...crawlExtractShape,
    ...crawlDiscoveryShape,
  },
  async (input) => {
    const startedAt = Date.now();
    const urls = dedupeStrings(input.urls);
    const concurrency = clampInteger(input.concurrency, 1, MAX_BATCH_CONCURRENCY, DEFAULT_BATCH_CONCURRENCY);
    const mode = input.mode || "snapshot";
    const includeSnapshot = mode !== "discover";
    const includeDiscovery = mode !== "snapshot";

    const results = await mapWithConcurrency(urls, concurrency, async (url) => {
      const itemStartedAt = Date.now();
      try {
        const { prepared, value, closedTempTab } = await runWithCrawlTarget(
          {
            ...input,
            url,
            createNewTab: input.createNewTab ?? true,
            active: input.active ?? false,
            closeAfter: input.closeAfter ?? true,
          },
          async (resolved) => {
            const snapshot = await extractSnapshotFromTab(
              resolved.tabId,
              {
                ...input,
                includeLinks: includeDiscovery ? true : input.includeLinks ?? false,
                includeText: includeSnapshot ? input.includeText : false,
                includeHtml: includeSnapshot ? input.includeHtml : false,
                includeImages: includeSnapshot ? input.includeImages : false,
                includeHeadings: includeSnapshot ? input.includeHeadings : false,
                includeMeta: includeSnapshot ? input.includeMeta : false,
                includeJsonLd: includeSnapshot ? input.includeJsonLd : false,
                includeForms: includeSnapshot ? input.includeForms : false,
                includeTables: includeSnapshot ? input.includeTables : false,
              },
              resolved.timeoutMs
            );

            return {
              snapshot,
              discoveredLinks: includeDiscovery ? filterDiscoveredLinks(snapshot, input) : undefined,
            };
          }
        );

        return {
          ok: true,
          inputUrl: url,
          finalUrl: value.snapshot?.url || url,
          durationMs: Date.now() - itemStartedAt,
          target: summarizePreparedTarget(prepared, value.snapshot),
          closedTempTab,
          page: summarizeSnapshot(value.snapshot),
          snapshot: includeSnapshot ? value.snapshot : undefined,
          discoveredLinks: value.discoveredLinks,
        };
      } catch (error) {
        return {
          ok: false,
          inputUrl: url,
          durationMs: Date.now() - itemStartedAt,
          error: serializeError(error),
        };
      }
    });

    return {
      durationMs: Date.now() - startedAt,
      concurrency,
      mode,
      total: urls.length,
      succeeded: results.filter((item) => item.ok).length,
      failed: results.filter((item) => !item.ok).length,
      aggregateLinks: includeDiscovery ? aggregateDiscoveredLinks(results, input.aggregateMaxLinks, "inputUrl") : undefined,
      results,
    };
  }
);

registerTool(
  "edge_crawl_search",
  "Fan out many search queries through a caller-provided search URL template, crawl each search page in parallel, and merge discovered result links.",
  {
    topic: z.string().optional(),
    queries: z.array(z.string().min(1)).optional(),
    seedQueries: z.array(z.string().min(1)).optional(),
    maxQueries: z.number().int().positive().max(MAX_MAX_ITEMS).optional(),
    includeChineseVariants: z.boolean().optional(),
    searchUrlTemplate: z.string().min(1),
    resultSelector: z.string().optional(),
    maxResultsPerQuery: z.number().int().positive().max(MAX_MAX_ITEMS).optional(),
    concurrency: z.number().int().positive().max(MAX_BATCH_CONCURRENCY).optional(),
    includeSearchPageSnapshot: z.boolean().optional(),
    createNewTab: z.boolean().optional(),
    active: z.boolean().optional(),
    closeAfter: z.boolean().optional(),
    timeoutMs: z.number().int().positive().optional(),
    waitSelector: z.string().optional(),
    waitXpath: z.string().optional(),
    waitText: z.string().optional(),
    waitWorld: waitWorldSchema.optional(),
    ...crawlExtractShape,
    ...crawlDiscoveryShape,
  },
  async (input) => {
    const startedAt = Date.now();
    const queries =
      input.queries?.length
        ? dedupeStrings(input.queries)
        : expandSearchQueries({
            topic: input.topic,
            seedQueries: input.seedQueries,
            maxQueries: input.maxQueries,
            includeChineseVariants: input.includeChineseVariants,
          });

    if (!queries.length) {
      throw new Error("edge_crawl_search requires queries, or a topic/seedQueries combination to expand.");
    }
    const concurrency = clampInteger(input.concurrency, 1, MAX_BATCH_CONCURRENCY, DEFAULT_BATCH_CONCURRENCY);
    const includeSearchPageSnapshot = Boolean(input.includeSearchPageSnapshot);

    const results = await mapWithConcurrency(queries, concurrency, async (query) => {
      const itemStartedAt = Date.now();
      const searchUrl = renderSearchUrl(input.searchUrlTemplate, query);

      try {
        const { prepared, value, closedTempTab } = await runWithCrawlTarget(
          {
            ...input,
            url: searchUrl,
            createNewTab: input.createNewTab ?? true,
            active: input.active ?? false,
            closeAfter: input.closeAfter ?? true,
          },
          async (resolved) => {
            const snapshot = await extractSnapshotFromTab(
              resolved.tabId,
              {
                ...input,
                includeLinks: true,
                includeText: includeSearchPageSnapshot ? input.includeText : false,
                includeHtml: includeSearchPageSnapshot ? input.includeHtml : false,
                includeImages: includeSearchPageSnapshot ? input.includeImages : false,
                includeHeadings: includeSearchPageSnapshot ? input.includeHeadings : false,
                includeMeta: includeSearchPageSnapshot ? input.includeMeta : false,
                includeJsonLd: includeSearchPageSnapshot ? input.includeJsonLd : false,
                includeForms: includeSearchPageSnapshot ? input.includeForms : false,
                includeTables: includeSearchPageSnapshot ? input.includeTables : false,
                linkSelector: input.resultSelector || input.linkSelector,
              },
              resolved.timeoutMs
            );

            const discoveredLinks = filterDiscoveredLinks(snapshot, {
              ...input,
              maxLinks: input.maxResultsPerQuery ?? input.maxLinks,
            });

            return { snapshot, discoveredLinks };
          }
        );

        return {
          ok: true,
          query,
          inputUrl: searchUrl,
          finalUrl: value.snapshot?.url || searchUrl,
          durationMs: Date.now() - itemStartedAt,
          target: summarizePreparedTarget(prepared, value.snapshot),
          closedTempTab,
          page: summarizeSnapshot(value.snapshot),
          searchPageSnapshot: includeSearchPageSnapshot ? value.snapshot : undefined,
          discoveredLinks: value.discoveredLinks,
        };
      } catch (error) {
        return {
          ok: false,
          query,
          inputUrl: searchUrl,
          durationMs: Date.now() - itemStartedAt,
          error: serializeError(error),
        };
      }
    });

    return {
      durationMs: Date.now() - startedAt,
      concurrency,
      queries,
      totalQueries: queries.length,
      succeeded: results.filter((item) => item.ok).length,
      failed: results.filter((item) => !item.ok).length,
      aggregatedLinks: aggregateDiscoveredLinks(results, input.aggregateMaxLinks, "inputUrl"),
      results,
    };
  }
);

registerTool(
  "edge_plan_crawl_job",
  "Plan a high-level crawl job with query expansion, adapter-selected search targets, and concurrency limits before execution.",
  {
    job: z.record(z.any()),
  },
  async ({ job }) => advancedCrawler.plan(job)
);

registerTool(
  "edge_run_crawl_job",
  "Execute a high-level crawl job with multi-query expansion, adapter-driven extraction, and pooled background tabs.",
  {
    job: z.record(z.any()),
  },
  async ({ job }) => advancedCrawler.run(job)
);

const transport = new StdioServerTransport();
await server.connect(transport);
