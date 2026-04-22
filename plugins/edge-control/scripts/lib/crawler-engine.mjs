import { mapLimit } from "./async.mjs";
import { EdgeBridgeClient } from "./bridge-client.mjs";
import { EdgeCdpClient } from "./cdp-client.mjs";
import { expandQueries } from "./query-expansion.mjs";
import {
  buildSearchUrl,
  extractSearchResults,
} from "./site-adapters/search-results.mjs";
import { extractStructuredContent } from "./site-adapters/index.mjs";
import { normalizeWhitespace, uniqueBy } from "./text.mjs";

function summarizeError(error) {
  return {
    message: error?.message || String(error),
  };
}

export class BrowserCrawler {
  constructor(options = {}) {
    this.bridge = options.bridge || new EdgeBridgeClient(options.bridgeOptions);
    this.cdp = options.cdp || new EdgeCdpClient(this.bridge);
  }

  async ensureConnected(options = {}) {
    return this.bridge.waitForExtension(options);
  }

  async searchWeb(options = {}) {
    const topic = options.topic || "";
    const queries = options.queries?.length
      ? options.queries
      : expandQueries({
          topic,
          seedQueries: options.seedQueries,
          mode: options.mode,
          maxQueries: options.maxQueries,
          includeChineseVariants: options.includeChineseVariants,
        });
    const providers = Array.isArray(options.providers) && options.providers.length
      ? options.providers
      : ["google", "bing", "baidu", "youtube"];
    const maxResultsPerQuery = Math.max(1, Number(options.maxResultsPerQuery) || 5);
    const searchConcurrency = Math.max(1, Number(options.searchConcurrency) || 4);
    const preserveSearchTabs = options.preserveSearchTabs ?? false;

    const jobs = [];
    for (const provider of providers) {
      for (const query of queries) {
        jobs.push({ provider, query });
      }
    }

    const searchRuns = await mapLimit(jobs, searchConcurrency, async (job) => {
      const url = buildSearchUrl(job.provider, job.query);
      const searchTab = await this.cdp.navigate(url, {
        createNewTab: true,
        active: false,
        timeoutMs: 45000,
      });

      try {
        const results = await extractSearchResults(
          this.cdp,
          searchTab.id,
          job.provider,
          {
            maxResults: maxResultsPerQuery,
            timeoutMs: options.searchTimeoutMs ?? 60000,
          }
        );

        return {
          ...job,
          url,
          tabId: searchTab.id,
          results,
        };
      } finally {
        if (!preserveSearchTabs) {
          try {
            await this.cdp.closeTab(searchTab.id);
          } catch {
            // Best-effort close for search tabs.
          }
        }
      }
    });

    const discoveredResults = uniqueBy(
      searchRuns.flatMap((run) =>
        run.results.map((result) => ({
          ...result,
          searchProvider: run.provider,
          searchQuery: run.query,
        }))
      ),
      (result) => result.url
    );

    return {
      topic,
      queries,
      providers,
      searchRuns,
      discoveredResults,
    };
  }

  async crawlUrls(options = {}) {
    const urls = uniqueBy(
      (options.urls || []).map((url) => String(url || "").trim()).filter(Boolean),
      (url) => url
    );
    const crawlConcurrency = Math.max(1, Number(options.crawlConcurrency) || 4);

    const pages = await mapLimit(urls, crawlConcurrency, async (url) => {
      const pageTab = await this.cdp.navigate(url, {
        createNewTab: true,
        active: false,
        timeoutMs: 45000,
      });

      try {
        await this.cdp.waitForPageReady(pageTab.id, {
          timeoutMs: options.readyTimeoutMs ?? 45000,
        });

        const structured = await extractStructuredContent(
          this.cdp,
          pageTab.id,
          url,
          options.adapterOptions || {}
        );

        return {
          url,
          tabId: pageTab.id,
          ok: true,
          structured,
        };
      } catch (error) {
        return {
          url,
          tabId: pageTab.id,
          ok: false,
          error: summarizeError(error),
        };
      } finally {
        if (options.preservePageTabs !== true) {
          try {
            await this.cdp.closeTab(pageTab.id);
          } catch {
            // Best-effort close for crawl tabs.
          }
        }
      }
    });

    return {
      totalUrls: urls.length,
      pages,
    };
  }

  async crawlTopic(options = {}) {
    const search = await this.searchWeb(options);
    const maxPages = Math.max(1, Number(options.maxPages) || 20);
    const selectedResults = search.discoveredResults.slice(0, maxPages);
    const crawl = await this.crawlUrls({
      urls: selectedResults.map((result) => result.url),
      crawlConcurrency: options.crawlConcurrency,
      preservePageTabs: options.preservePageTabs,
      adapterOptions: options.adapterOptions,
    });

    const pagesByUrl = new Map(
      crawl.pages.map((page) => [page.url, page])
    );

    return {
      topic: normalizeWhitespace(options.topic),
      queries: search.queries,
      providers: search.providers,
      searchRuns: search.searchRuns,
      discoveredResults: selectedResults,
      pages: selectedResults.map((result) => ({
        ...result,
        crawl: pagesByUrl.get(result.url) || null,
      })),
      aggregate: {
        searchRuns: search.searchRuns.length,
        discoveredResults: search.discoveredResults.length,
        selectedPages: selectedResults.length,
        successfulPages: crawl.pages.filter((page) => page.ok).length,
      },
    };
  }
}
