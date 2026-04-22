import { AdapterRegistry, createDefaultAdapterRegistry } from "./adapter-registry.mjs";
import { cdpEvaluate, getNetworkLogEntries, getNetworkLogMark, getPageResources, navigateAndWait, prepareTabForCrawl } from "./cdp-helpers.mjs";
import { normalizeCrawlJob, finalizeCrawlResult } from "./crawl-schema.mjs";
import { EdgeBridgeClient } from "./edge-bridge-client.mjs";
import { expandQuerySeeds } from "./query-expander.mjs";
import {
  compileTemplate,
  makeId,
  nowIso,
  normalizeWhitespace,
  pickDefined,
  sleep,
  toAbsoluteUrl,
  uniqueBy,
} from "./crawl-utils.mjs";

const SEARCH_PROVIDER_FAMILY_PATTERNS = [
  [/^(.+\.)?google\./i, "google"],
  [/^(.+\.)?bing\.com$/i, "bing"],
  [/^(.+\.)?baidu\.com$/i, "baidu"],
  [/^(.+\.)?duckduckgo\.com$/i, "duckduckgo"],
  [/^(.+\.)?youtube\.com$/i, "youtube"],
  [/^youtu\.be$/i, "youtube"],
];

const SEARCH_BLOCK_STATUS_SET = new Set(["blocked", "captcha", "challenge", "consent"]);

function resolveNetworkMarkSequence(mark) {
  const lastSequence = Number(mark?.lastSequence);
  if (Number.isFinite(lastSequence) && lastSequence >= 0) {
    return lastSequence;
  }

  const nextSequence = Number(mark?.nextSequence);
  if (Number.isFinite(nextSequence) && nextSequence > 0) {
    return Math.max(0, nextSequence - 1);
  }

  return undefined;
}

function normalizeItem(item, target, adapterId) {
  const canonicalUrl = toAbsoluteUrl(item.canonicalUrl || item.url, target.url) || item.url;
  const title = normalizeWhitespace(item.title);
  return {
    ...item,
    id: item.id || makeId("item", `${adapterId}:${canonicalUrl || title}:${target.id}`),
    adapterId,
    url: toAbsoluteUrl(item.url, target.url) || item.url,
    canonicalUrl,
    title,
    snippet: normalizeWhitespace(item.snippet),
    channel: normalizeWhitespace(item.channel),
    discoveredFrom: {
      targetId: target.id,
      queryId: target.queryId || null,
      query: target.query || null,
      sourceUrl: target.url,
    },
  };
}

function normalizeDetail(detail, target, adapterId) {
  return {
    ...detail,
    id: detail.id || makeId("detail", `${adapterId}:${detail.canonicalUrl || target.url}`),
    adapterId,
    url: target.url,
    canonicalUrl: toAbsoluteUrl(detail.canonicalUrl || target.url, target.url) || target.url,
    title: normalizeWhitespace(detail.title),
    description: normalizeWhitespace(detail.description),
    bodyText: normalizeWhitespace(detail.bodyText),
  };
}

function hostnameForUrl(value) {
  try {
    return new URL(value).hostname.toLowerCase();
  } catch {
    return null;
  }
}

function providerFamilyForHostname(hostname) {
  const normalized = String(hostname || "").toLowerCase().replace(/^www\./, "");
  if (!normalized) {
    return null;
  }

  for (const [pattern, provider] of SEARCH_PROVIDER_FAMILY_PATTERNS) {
    if (pattern.test(normalized)) {
      return provider;
    }
  }

  return normalized;
}

function normalizeSearchProviderKey(value) {
  if (!value) {
    return "search";
  }
  return providerFamilyForHostname(value) || "search";
}

function sourceKeyForSearchTarget(target) {
  const hostname = hostnameForUrl(target.url);
  if (hostname) {
    return normalizeSearchProviderKey(hostname);
  }
  if (target.adapterId) {
    return normalizeSearchProviderKey(target.adapterId) || `adapter:${target.adapterId}`;
  }
  return `source:${normalizeSearchProviderKey(target.source || "unknown")}`;
}

function seedKeyForSearchTarget(target) {
  return target.expansion?.seedId || target.queryId || target.query || "seedless";
}

function facetKeyForSearchTarget(target) {
  return target.expansion?.facet || target.expansion?.intent || "general";
}

function scoreSearchTarget(target) {
  const expansion = target.expansion || {};
  let score = Math.max(0, 120 - rankNumber(expansion.overallRank ?? expansion.rank, 40) * 4);
  const weight = Number(expansion.weight);

  if (Number.isFinite(weight)) {
    score += Math.round(weight * 100);
  }

  const reason = String(expansion.reason || "");
  if (reason.startsWith("seed:term")) {
    score += 24;
  } else if (reason.startsWith("seed:")) {
    score += 16;
  }

  if (reason.startsWith("temporal:")) {
    score += 8;
  }

  if (expansion.facet === "official" || expansion.facet === "research") {
    score += 6;
  }

  if (target.adapterId) {
    score += 3;
  }

  return score;
}

function orderSearchGroups(candidates, keyName) {
  const groups = new Map();

  for (const candidate of candidates) {
    const key = candidate[keyName];
    const existing = groups.get(key);
    if (!existing || candidate._score > existing.score || (
      candidate._score === existing.score && candidate._order < existing.order
    )) {
      groups.set(key, {
        score: candidate._score,
        order: candidate._order,
      });
    }
  }

  return Array.from(groups.entries())
    .sort((left, right) => {
      if (right[1].score !== left[1].score) {
        return right[1].score - left[1].score;
      }
      return left[1].order - right[1].order;
    })
    .map(([key]) => key);
}

function stripSearchTargetInternals(target) {
  const {
    _sourceKey,
    _seedKey,
    _queryKey,
    _facetKey,
    _score,
    _order,
    ...rest
  } = target;
  return rest;
}

function selectSearchTargets(targets, _expansions, job) {
  const planning = job?.search?.planning || {};
  const mode = planning.mode || "balanced";
  const candidates = uniqueBy(targets, (target) => `${target.url}::${target.query || ""}`)
    .map((target, index) => ({
      ...target,
      _sourceKey: sourceKeyForSearchTarget(target),
      _seedKey: seedKeyForSearchTarget(target),
      _queryKey: target.queryId || target.query || target.id,
      _facetKey: facetKeyForSearchTarget(target),
      _score: scoreSearchTarget(target),
      _order: index,
    }))
    .sort((left, right) => {
      if (right._score !== left._score) {
        return right._score - left._score;
      }
      return left._order - right._order;
    });

  if (!candidates.length) {
    return {
      searchTargets: [],
      reserveSearchTargets: [],
      searchPlanning: {
        mode,
        candidateCount: 0,
        selectedCount: 0,
        reserveCount: 0,
        budget: 0,
        providerCount: 0,
        seedCount: 0,
        caps: {
          minTargetsPerProvider: Math.max(1, Number(planning.minTargetsPerProvider) || 1),
          maxTargetsPerProvider: 0,
          maxTargetsPerSeed: 0,
          maxProvidersPerQuery: Math.max(1, Number(planning.maxProvidersPerQuery) || 2),
        },
        providerStats: [],
      },
    };
  }

  const candidateProviderCounts = candidates.reduce((counts, candidate) => {
    counts.set(candidate._sourceKey, (counts.get(candidate._sourceKey) || 0) + 1);
    return counts;
  }, new Map());
  const providerOrder = orderSearchGroups(candidates, "_sourceKey");

  if (mode === "exhaustive") {
    return {
      searchTargets: candidates.map(stripSearchTargetInternals),
      reserveSearchTargets: [],
      searchPlanning: {
        mode,
        candidateCount: candidates.length,
        selectedCount: candidates.length,
        reserveCount: 0,
        budget: candidates.length,
        providerCount: providerOrder.length,
        seedCount: new Set(candidates.map((candidate) => candidate._seedKey)).size,
        caps: {
          minTargetsPerProvider: Math.max(1, Number(planning.minTargetsPerProvider) || 1),
          maxTargetsPerProvider: null,
          maxTargetsPerSeed: null,
          maxProvidersPerQuery: null,
        },
        providerStats: providerOrder.map((provider) => ({
          provider,
          candidates: candidateProviderCounts.get(provider) || 0,
          selected: candidateProviderCounts.get(provider) || 0,
        })),
      },
    };
  }

  const seedOrder = orderSearchGroups(candidates, "_seedKey");
  const facetOrder = orderSearchGroups(candidates, "_facetKey");
  const providerCount = providerOrder.length;
  const seedCount = seedOrder.length;
  const requestedBudget = Number(planning.maxTargets);
  const requestedProviderCap = Number(planning.maxTargetsPerProvider);
  const requestedSeedCap = Number(planning.maxTargetsPerSeed);
  const minTargetsPerProvider = Math.max(1, Number(planning.minTargetsPerProvider) || 1);
  const maxProvidersPerQuery = Math.max(1, Number(planning.maxProvidersPerQuery) || 2);
  const itemsPerSearchYield = Math.max(1, Math.min(Number(job?.limits?.maxItemsPerSearch) || 20, 8));
  const detailSignal = Math.max(1, Math.ceil((Number(job?.limits?.maxDetailItems) || 0) / itemsPerSearchYield));
  const diversityBudget = Math.max(
    seedCount * 2,
    providerCount * 2,
    Math.ceil((seedCount + providerCount) * 1.5)
  );
  const autoBudget = Math.min(
    candidates.length,
    Math.max(providerCount * minTargetsPerProvider, detailSignal, diversityBudget)
  );
  const budget = Math.max(
    1,
    Math.min(candidates.length, Number.isFinite(requestedBudget) && requestedBudget > 0 ? requestedBudget : autoBudget)
  );
  const maxTargetsPerProvider = Math.max(
    minTargetsPerProvider,
    Number.isFinite(requestedProviderCap) && requestedProviderCap > 0
      ? requestedProviderCap
      : Math.ceil(budget / Math.max(1, providerCount)) + (providerCount > 1 ? 1 : 0)
  );
  const maxTargetsPerSeed = Math.max(
    2,
    Number.isFinite(requestedSeedCap) && requestedSeedCap > 0
      ? requestedSeedCap
      : Math.ceil(budget / Math.max(1, seedCount))
  );
  const selected = [];
  const usedIds = new Set();
  const providerCounts = new Map();
  const seedCounts = new Map();
  const facetCounts = new Map();
  const queryTargetCounts = new Map();
  const queryProviders = new Map();

  const canSelect = (candidate, { relaxProviderCap = false, relaxSeedCap = false } = {}) => {
    if (usedIds.has(candidate.id)) {
      return false;
    }
    const activeQueryProviders = queryProviders.get(candidate._queryKey) || new Set();
    if (!activeQueryProviders.has(candidate._sourceKey) && activeQueryProviders.size >= maxProvidersPerQuery) {
      return false;
    }
    if (!relaxProviderCap && (providerCounts.get(candidate._sourceKey) || 0) >= maxTargetsPerProvider) {
      return false;
    }
    if (!relaxSeedCap && (seedCounts.get(candidate._seedKey) || 0) >= maxTargetsPerSeed) {
      return false;
    }
    return true;
  };

  const commit = (candidate) => {
    selected.push(candidate);
    usedIds.add(candidate.id);
    providerCounts.set(candidate._sourceKey, (providerCounts.get(candidate._sourceKey) || 0) + 1);
    seedCounts.set(candidate._seedKey, (seedCounts.get(candidate._seedKey) || 0) + 1);
    facetCounts.set(candidate._facetKey, (facetCounts.get(candidate._facetKey) || 0) + 1);
    queryTargetCounts.set(candidate._queryKey, (queryTargetCounts.get(candidate._queryKey) || 0) + 1);
    if (!queryProviders.has(candidate._queryKey)) {
      queryProviders.set(candidate._queryKey, new Set());
    }
    queryProviders.get(candidate._queryKey).add(candidate._sourceKey);
  };

  const findBest = (predicate = () => true, options = {}) => {
    let best = null;
    let bestScore = -Infinity;

    for (const candidate of candidates) {
      if (!predicate(candidate) || !canSelect(candidate, options)) {
        continue;
      }

      let score = candidate._score;
      score -= (providerCounts.get(candidate._sourceKey) || 0) * 14;
      score -= (seedCounts.get(candidate._seedKey) || 0) * 10;
      score -= (facetCounts.get(candidate._facetKey) || 0) * 4;
      score -= (queryTargetCounts.get(candidate._queryKey) || 0) * 18;

      if ((providerCounts.get(candidate._sourceKey) || 0) < minTargetsPerProvider) {
        score += 20;
      }
      if ((seedCounts.get(candidate._seedKey) || 0) === 0) {
        score += 18;
      }
      if ((facetCounts.get(candidate._facetKey) || 0) === 0) {
        score += 8;
      }

      if (score > bestScore || (score === bestScore && candidate._order < (best?._order ?? Number.MAX_SAFE_INTEGER))) {
        best = candidate;
        bestScore = score;
      }
    }

    return best;
  };

  for (const provider of providerOrder) {
    while ((providerCounts.get(provider) || 0) < minTargetsPerProvider && selected.length < budget) {
      const candidate = findBest((item) => item._sourceKey === provider);
      if (!candidate) {
        break;
      }
      commit(candidate);
    }
  }

  for (const seed of seedOrder) {
    if (selected.length >= budget || (seedCounts.get(seed) || 0) > 0) {
      continue;
    }
    const candidate = findBest((item) => item._seedKey === seed);
    if (candidate) {
      commit(candidate);
    }
  }

  for (const facet of facetOrder) {
    if (selected.length >= budget || (facetCounts.get(facet) || 0) > 0) {
      continue;
    }
    const candidate = findBest((item) => item._facetKey === facet);
    if (candidate) {
      commit(candidate);
    }
  }

  while (selected.length < budget) {
    const candidate = findBest();
    if (!candidate) {
      break;
    }
    commit(candidate);
  }

  if (selected.length < budget && !(Number.isFinite(requestedSeedCap) && requestedSeedCap > 0)) {
    while (selected.length < budget) {
      const candidate = findBest(() => true, { relaxSeedCap: true });
      if (!candidate) {
        break;
      }
      commit(candidate);
    }
  }

  if (selected.length < budget && !(Number.isFinite(requestedProviderCap) && requestedProviderCap > 0)) {
    while (selected.length < budget) {
      const candidate = findBest(() => true, {
        relaxProviderCap: true,
        relaxSeedCap: !(Number.isFinite(requestedSeedCap) && requestedSeedCap > 0),
      });
      if (!candidate) {
        break;
      }
      commit(candidate);
    }
  }

  const remainingCandidates = candidates.filter((candidate) => !usedIds.has(candidate.id));
  const reserveBudget = Math.max(
    0,
    Math.min(
      remainingCandidates.length,
      Math.max(providerCount, Math.min(budget, providerCount * 2))
    )
  );
  const reserveSearchTargets = [];
  const reserveUsedIds = new Set();
  const reserveProviderCounts = new Map();
  const reserveSeedCounts = new Map();

  while (reserveSearchTargets.length < reserveBudget) {
    let best = null;
    let bestScore = -Infinity;

    for (const candidate of remainingCandidates) {
      if (reserveUsedIds.has(candidate.id)) {
        continue;
      }

      let score = candidate._score;
      score += Math.max(0, minTargetsPerProvider - (providerCounts.get(candidate._sourceKey) || 0)) * 18;
      score -= (reserveProviderCounts.get(candidate._sourceKey) || 0) * 12;
      score -= (reserveSeedCounts.get(candidate._seedKey) || 0) * 8;

      if (score > bestScore || (score === bestScore && candidate._order < (best?._order ?? Number.MAX_SAFE_INTEGER))) {
        best = candidate;
        bestScore = score;
      }
    }

    if (!best) {
      break;
    }

    reserveUsedIds.add(best.id);
    reserveProviderCounts.set(best._sourceKey, (reserveProviderCounts.get(best._sourceKey) || 0) + 1);
    reserveSeedCounts.set(best._seedKey, (reserveSeedCounts.get(best._seedKey) || 0) + 1);
    reserveSearchTargets.push(best);
  }

  return {
    searchTargets: selected.map(stripSearchTargetInternals),
    reserveSearchTargets: reserveSearchTargets.map(stripSearchTargetInternals),
    searchPlanning: {
      mode,
      candidateCount: candidates.length,
      selectedCount: selected.length,
      reserveCount: reserveSearchTargets.length,
      budget,
      providerCount,
      seedCount,
      caps: {
        minTargetsPerProvider,
        maxTargetsPerProvider,
        maxTargetsPerSeed,
        maxProvidersPerQuery,
      },
      providerStats: providerOrder.map((provider) => ({
        provider,
        candidates: candidateProviderCounts.get(provider) || 0,
        selected: providerCounts.get(provider) || 0,
        reserve: reserveProviderCounts.get(provider) || 0,
      })),
    },
  };
}

function rankNumber(value, fallback = 40) {
  const numeric = Number(value);
  return Number.isFinite(numeric) && numeric > 0 ? numeric : fallback;
}

function scoreDiscoveredItem(item) {
  let score = Math.max(0, 80 - rankNumber(item.rank, 30));

  if (item.adapterId && item.adapterId !== "generic-html") {
    score += 8;
  }

  if (item.discoveredFrom?.queryId) {
    score += 4;
  }

  if (item.snippet) {
    score += Math.min(6, Math.floor(String(item.snippet).length / 80));
  }

  return score;
}

function selectDetailItems(items, job) {
  const limit = Math.max(0, Number(job?.limits?.maxDetailItems) || 0);
  if (!limit) {
    return [];
  }

  const maxPerDomain = Math.max(1, Number(job?.limits?.maxDetailsPerDomain) || limit);
  const maxPerQuery = Math.max(1, Number(job?.limits?.maxDetailsPerQuery) || limit);
  const candidates = uniqueBy(items, (item) => item.canonicalUrl || item.url)
    .map((item) => ({
      ...item,
      _domain: hostnameForUrl(item.canonicalUrl || item.url) || "unknown",
      _queryKey: item.discoveredFrom?.queryId || item.discoveredFrom?.query || "no-query",
      _score: scoreDiscoveredItem(item),
    }))
    .sort((left, right) => right._score - left._score);

  const selected = [];
  const selectedKeys = new Set();
  const domainCounts = new Map();
  const queryCounts = new Map();

  const canSelect = (item, { enforceDomainCap = true, enforceQueryCap = true } = {}) => {
    if (selectedKeys.has(item.id)) {
      return false;
    }
    if (enforceDomainCap && (domainCounts.get(item._domain) || 0) >= maxPerDomain) {
      return false;
    }
    if (enforceQueryCap && (queryCounts.get(item._queryKey) || 0) >= maxPerQuery) {
      return false;
    }
    return true;
  };

  const commit = (item) => {
    selected.push(item);
    selectedKeys.add(item.id);
    domainCounts.set(item._domain, (domainCounts.get(item._domain) || 0) + 1);
    queryCounts.set(item._queryKey, (queryCounts.get(item._queryKey) || 0) + 1);
  };

  while (selected.length < limit) {
    let addedInPass = false;
    const seenQueriesInPass = new Set();

    for (const item of candidates) {
      if (selected.length >= limit) {
        break;
      }
      if (!canSelect(item) || seenQueriesInPass.has(item._queryKey)) {
        continue;
      }

      seenQueriesInPass.add(item._queryKey);
      commit(item);
      addedInPass = true;
    }

    if (!addedInPass) {
      break;
    }
  }

  for (const options of [
    { enforceDomainCap: true, enforceQueryCap: false },
    { enforceDomainCap: false, enforceQueryCap: false },
  ]) {
    if (selected.length >= limit) {
      break;
    }

    for (const item of candidates) {
      if (selected.length >= limit) {
        break;
      }
      if (canSelect(item, options)) {
        commit(item);
      }
    }
  }

  return selected.map(({ _domain, _queryKey, _score, ...item }) => item);
}

function detectSearchInterruption({ provider, url, title, text } = {}) {
  const normalizedProvider = normalizeSearchProviderKey(provider || hostnameForUrl(url) || provider || "search");
  const href = String(url || "").toLowerCase();
  const content = [title, text, href].map((value) => String(value || "").toLowerCase()).join("\n");
  const signals = [];

  if (/captcha|recaptcha|verify (?:that )?you(?:['\u2019])?re human|security check|human verification/.test(content)) {
    signals.push("captcha");
  }
  if (/before you continue|consent|privacy reminder|agree to the use of cookies|consent\.google\./.test(content)) {
    signals.push("consent");
  }
  if (/unusual traffic|automated queries|sorry\/index|detected unusual traffic|why did this happen\?|challenge/.test(content)) {
    signals.push("challenge");
  }

  if (normalizedProvider === "google" && /google\.[^/\s]+\/sorry\//.test(href)) {
    signals.push("google-sorry");
  }
  if (normalizedProvider === "google" && /consent\.google\./.test(href)) {
    signals.push("google-consent");
  }

  const uniqueSignals = Array.from(new Set(signals));
  if (!uniqueSignals.length) {
    return null;
  }

  const status = uniqueSignals.includes("captcha")
    ? "captcha"
    : uniqueSignals.includes("consent")
      ? "consent"
      : "challenge";

  return {
    status,
    signals: uniqueSignals,
    provider: normalizedProvider,
  };
}

function classifySearchExecution(target, record, error = null) {
  const provider = sourceKeyForSearchTarget(target);
  const metaHealth = record?.meta?.providerHealth || null;

  if (metaHealth?.status && SEARCH_BLOCK_STATUS_SET.has(metaHealth.status)) {
    return {
      kind: "blocked",
      status: metaHealth.status,
      provider,
      signals: Array.isArray(metaHealth.signals) ? metaHealth.signals : [],
    };
  }

  if (error) {
    const message = error?.message || String(error);
    const interruption = detectSearchInterruption({
      provider,
      url: target?.url,
      title: record?.page?.title,
      text: message,
    });
    if (interruption) {
      return {
        kind: "blocked",
        ...interruption,
      };
    }
    if (/timed out/i.test(message)) {
      return {
        kind: "timeout",
        status: "timeout",
        provider,
        signals: [],
      };
    }
    return {
      kind: "error",
      status: "error",
      provider,
      signals: [],
    };
  }

  const page = record?.page || {};
  const interruption = detectSearchInterruption({
    provider,
    url: page.url || record?.url || target?.url,
    title: page.title,
    text: [metaHealth?.reason, ...(metaHealth?.signals || [])].join(" "),
  });
  if (interruption) {
    return {
      kind: "blocked",
      ...interruption,
    };
  }

  if (Array.isArray(record?.items) && record.items.length > 0) {
    return {
      kind: "success",
      status: "ok",
      provider,
      signals: [],
    };
  }

  return {
    kind: "empty",
    status: metaHealth?.status === "empty" ? "empty" : "empty",
    provider,
    signals: Array.isArray(metaHealth?.signals) ? metaHealth.signals : [],
  };
}

function interleaveSearchTargets(targets) {
  const groups = new Map();
  const providerOrder = [];

  for (const target of targets) {
    const provider = sourceKeyForSearchTarget(target);
    if (!groups.has(provider)) {
      groups.set(provider, []);
      providerOrder.push(provider);
    }
    groups.get(provider).push(target);
  }

  const output = [];
  while (groups.size > 0) {
    for (const provider of providerOrder) {
      const group = groups.get(provider);
      if (!group?.length) {
        groups.delete(provider);
        continue;
      }
      output.push(group.shift());
      if (!group.length) {
        groups.delete(provider);
      }
    }
  }

  return output;
}

function buildSearchProviderRuntimeStats(primaryTargets, reserveTargets, providerStates) {
  const primaryCounts = primaryTargets.reduce((counts, target) => {
    const provider = sourceKeyForSearchTarget(target);
    counts.set(provider, (counts.get(provider) || 0) + 1);
    return counts;
  }, new Map());
  const reserveCounts = reserveTargets.reduce((counts, target) => {
    const provider = sourceKeyForSearchTarget(target);
    counts.set(provider, (counts.get(provider) || 0) + 1);
    return counts;
  }, new Map());
  const providers = Array.from(new Set([
    ...primaryCounts.keys(),
    ...reserveCounts.keys(),
    ...providerStates.keys(),
  ]));

  return providers.map((provider) => {
    const state = providerStates.get(provider) || {};
    return {
      provider,
      planned: primaryCounts.get(provider) || 0,
      reserve: reserveCounts.get(provider) || 0,
      started: state.started || 0,
      succeeded: state.succeeded || 0,
      empty: state.empty || 0,
      timedOut: state.timedOut || 0,
      failed: state.failed || 0,
      blocked: state.blocked || 0,
      skippedByBackoff: state.skippedByBackoff || 0,
      reserveUsed: state.reserveUsed || 0,
      lastStatus: state.lastStatus || "unknown",
      unhealthy: Boolean(state.unhealthy),
      signals: Array.from(state.signals || []).slice(0, 8),
    };
  });
}

function isRecoverableTabError(error) {
  const message = String(error?.message || error || "").toLowerCase();
  return [
    "no tab with id",
    "cannot access a chrome",
    "cannot find tab",
    "target closed",
    "session closed",
    "web contents",
    "inspected target navigated or closed",
    "is no longer available",
  ].some((fragment) => message.includes(fragment));
}

export class BrowserCrawler {
  constructor({
    client = new EdgeBridgeClient(),
    registry = createDefaultAdapterRegistry(),
    logger = () => {},
  } = {}) {
    this.client = client;
    this.registry = registry instanceof AdapterRegistry ? registry : createDefaultAdapterRegistry();
    this.logger = logger;
  }

  plan(jobInput) {
    const job = normalizeCrawlJob(jobInput);
    const expansions = expandQuerySeeds(job.seeds, {
      maxExpandedQueries: job.limits.maxExpandedQueries,
    });

    const searchableAdapters = job.search.includeAdapterSearches
      ? this.registry.searchable(job.adapters)
      : [];

    const searchTargets = [];

    for (const expansion of expansions) {
      const variables = {
        query: expansion.query,
        queryEncoded: encodeURIComponent(expansion.query),
        topic: expansion.topic,
        topicEncoded: encodeURIComponent(expansion.topic),
        locale: expansion.locale || "",
      };

      for (const template of job.search.urlTemplates) {
        searchTargets.push({
          id: makeId("search", `template:${template}:${expansion.id}`),
          kind: "search",
          url: compileTemplate(template, variables),
          adapterId: null,
          queryId: expansion.id,
          query: expansion.query,
          expansion,
          source: "template",
        });
      }

      for (const adapter of searchableAdapters) {
        const urls = adapter.buildSearchUrls ? adapter.buildSearchUrls({ expansion, job }) : [];
        for (const url of urls) {
          searchTargets.push({
            id: makeId("search", `${adapter.id}:${url}:${expansion.id}`),
            kind: "search",
            url,
            adapterId: adapter.id,
            queryId: expansion.id,
            query: expansion.query,
            expansion,
            source: adapter.id,
          });
        }
      }
    }

    const searchPlan = selectSearchTargets(searchTargets, expansions, job);

    return {
      job,
      expansions,
      searchTargets: searchPlan.searchTargets,
      reserveSearchTargets: searchPlan.reserveSearchTargets,
      searchPlanning: searchPlan.searchPlanning,
      directTargets: job.targets,
    };
  }

  async run(jobInput) {
    const startedAt = nowIso();
    const startedMs = Date.now();
    const plan = this.plan(jobInput);
    const errors = [];

    const status = await this.client.status();
    if (!status?.bridge?.connectedExtension) {
      throw new Error("Edge Control bridge is running but the Edge extension is not connected.");
    }

    if (plan.job.execution.mainWindowOnly !== false) {
      const mainWindowId = await this.resolveMainWindowId(plan.job);
      if (typeof mainWindowId !== "number") {
        throw new Error("Unable to resolve the main Edge window for crawl execution.");
      }
      plan.job.execution.mainWindowId = mainWindowId;
    }

    const sharedTabPool = await this.createTabPool(this.computePoolSize(plan), plan.job);
    let searchRuns = { records: [], errors: [] };
    let detailRuns = { records: [], errors: [] };
    let discoveredItems = [];
    let selectedDetailItems = [];
    let runtimeSearchPlanning = plan.searchPlanning;

    try {
      searchRuns = await this.executeSearchTargets(plan.job, plan.searchTargets, async (context) => {
        const adapter = context.adapter || this.registry.match(context.target.url);
        if (!adapter?.extractListings) {
          throw new Error(`Adapter ${adapter?.id || "unknown"} does not support search extraction.`);
        }
        const response = await adapter.extractListings(context);
        const items = (response.items || []).map((item) => normalizeItem(item, context.target, adapter.id));
        return {
          targetId: context.target.id,
          adapterId: adapter.id,
          queryId: context.target.queryId || null,
          query: context.target.query || null,
          url: context.target.url,
          page: response.page || null,
          items,
          meta: response.meta || {},
          raw: plan.job.output.includeRawListings ? response : undefined,
        };
      }, {
        tabPool: sharedTabPool,
        reserveTargets: plan.reserveSearchTargets,
      });
      errors.push(...searchRuns.errors);
      runtimeSearchPlanning = {
        ...(plan.searchPlanning || {}),
        reserveCount: plan.reserveSearchTargets.length,
        executedCount: searchRuns.records.length,
        reserveUsedCount: searchRuns.reserveUsedCount || 0,
        providerStats: searchRuns.providerStats || plan.searchPlanning?.providerStats || [],
      };

      discoveredItems = uniqueBy(
        searchRuns.records.flatMap((record) => record?.items || []),
        (item) => item.canonicalUrl || item.url
      );

      selectedDetailItems = selectDetailItems(discoveredItems, plan.job);
      const detailTargets = uniqueBy([
        ...selectedDetailItems.map((item) => ({
          id: makeId("detail-target", item.canonicalUrl || item.url),
          kind: "detail",
          url: item.canonicalUrl || item.url,
          adapterId: item.adapterId,
          item,
          meta: {
            discoveredFrom: item.discoveredFrom,
          },
        })),
        ...plan.directTargets,
      ], (target) => target.url);

      detailRuns = await this.executeTargets(plan.job, detailTargets, "detail", async (context) => {
        const adapter = context.adapter || this.registry.match(context.target.url);
        if (!adapter?.extractDetail) {
          throw new Error(`Adapter ${adapter?.id || "unknown"} does not support detail extraction.`);
        }
        const response = await adapter.extractDetail(context);
        return {
          targetId: context.target.id,
          adapterId: adapter.id,
          url: context.target.url,
          item: context.target.item || null,
          detail: normalizeDetail(response, context.target, adapter.id),
          raw: plan.job.output.includeRawDetails ? response : undefined,
        };
      }, { tabPool: sharedTabPool });
      errors.push(...detailRuns.errors);
    } finally {
      await this.cleanupTabs(sharedTabPool, plan.job.execution.cleanupTabs, plan.job.timeouts.commandMs);
    }

    const finishedAt = nowIso();
    return finalizeCrawlResult({
      job: plan.job,
      plan: {
        expansions: plan.expansions,
        searchTargets: plan.searchTargets,
        reserveSearchTargets: plan.reserveSearchTargets,
        searchPlanning: runtimeSearchPlanning,
        directTargets: plan.directTargets,
      },
      startedAt,
      finishedAt,
      durationMs: Date.now() - startedMs,
      searchRuns: searchRuns.records,
      items: discoveredItems,
      detailRuns: detailRuns.records,
      errors,
      summary: {
        expansionCount: plan.expansions.length,
        searchTargetCount: plan.searchTargets.length,
        directTargetCount: plan.directTargets.length,
        discoveredItemCount: discoveredItems.length,
        selectedDetailTargetCount: selectedDetailItems.length,
        detailCount: detailRuns.records.length,
        errorCount: errors.length,
      },
    });
  }

  computePoolSize(plan) {
    const hasInitialWork = plan.searchTargets.length > 0 || plan.directTargets.length > 0;
    if (!hasInitialWork) {
      return 0;
    }

    return Math.min(
      plan.job.limits.maxConcurrentTabs,
      Math.max(1, plan.searchTargets.length, plan.directTargets.length, plan.job.limits.maxDetailItems)
    );
  }

  getCrawlerOptions(job) {
    const crawlerOptions = job.adapterOptions?.__crawler || job.adapterOptions?.__browser || {};
    const parsedNetworkLogMaxEntries = Number(crawlerOptions.networkLogMaxEntries);
    const parsedNetworkLogMaxBodies = Number(crawlerOptions.networkLogMaxBodies);
    return {
      quiet: crawlerOptions.quiet !== false,
      blockHeavyResources: crawlerOptions.blockHeavyResources !== false,
      blockUrlPatterns: Array.isArray(crawlerOptions.blockUrlPatterns) ? crawlerOptions.blockUrlPatterns : [],
      bypassServiceWorker: crawlerOptions.bypassServiceWorker !== false,
      injectQuietPageScript: crawlerOptions.injectQuietPageScript !== false,
      captureNetworkLog: crawlerOptions.captureNetworkLog !== false,
      networkLogPhases: Array.isArray(crawlerOptions.networkLogPhases) && crawlerOptions.networkLogPhases.length
        ? crawlerOptions.networkLogPhases.map((value) => String(value))
        : ["detail"],
      networkLogResourceTypes: Array.isArray(crawlerOptions.networkLogResourceTypes) && crawlerOptions.networkLogResourceTypes.length
        ? crawlerOptions.networkLogResourceTypes.map((value) => String(value))
        : ["XHR", "Fetch"],
      networkLogMaxEntries: Number.isFinite(parsedNetworkLogMaxEntries) && parsedNetworkLogMaxEntries > 0
        ? Math.max(24, Math.trunc(parsedNetworkLogMaxEntries))
        : null,
      networkLogMaxBodies: Number.isFinite(parsedNetworkLogMaxBodies) && parsedNetworkLogMaxBodies >= 0
        ? Math.max(0, Math.trunc(parsedNetworkLogMaxBodies))
        : null,
      networkLogBodyUrlIncludes: Array.isArray(crawlerOptions.networkLogBodyUrlIncludes)
        ? crawlerOptions.networkLogBodyUrlIncludes.map((value) => String(value)).filter(Boolean)
        : [],
    };
  }

  async ensureTabSlotPrepared(slot, job) {
    if (slot.prepared) {
      return slot;
    }

    await prepareTabForCrawl(this.client, {
      tabId: slot.tabId,
      timeoutMs: job.timeouts.commandMs,
      ...this.getCrawlerOptions(job),
    });

    slot.prepared = true;
    return slot;
  }

  async replaceTabSlot(slot, job) {
    if (typeof slot.tabId === "number") {
      try {
        await this.client.closeTab(slot.tabId, job.timeouts.commandMs);
      } catch {
        // Best effort cleanup before replacing the worker tab.
      }
    }

    const tab = await this.client.navigate({
      url: "about:blank",
      windowId: job.execution.mainWindowOnly !== false ? job.execution.mainWindowId : undefined,
      createNewTab: true,
      active: Boolean(job.execution.activeTabs),
      timeoutMs: job.timeouts.commandMs,
    });

    slot.tabId = tab.id;
    slot.prepared = false;
    slot.networkLogReady = false;
    slot.lastUrl = "about:blank";
    return this.ensureTabSlotPrepared(slot, job);
  }

  async prepareNetworkCapture(slot, target, job, phase) {
    const crawlerOptions = this.getCrawlerOptions(job);
    if (!crawlerOptions.captureNetworkLog || !crawlerOptions.networkLogPhases.includes(phase)) {
      return null;
    }

    const result = await this.client.startNetworkLog({
      tabId: slot.tabId,
      maxEntries: crawlerOptions.networkLogMaxEntries || Math.max(
        64,
        (Number(job?.limits?.maxNetworkCandidates) || 0) * 6,
        (Number(job?.limits?.maxNetworkPayloads) || 0) * 10
      ),
      maxBodies: crawlerOptions.networkLogMaxBodies ?? Math.max(
        12,
        (Number(job?.limits?.maxNetworkPayloads) || 0) * 3
      ),
      maxBodyBytes: job?.limits?.maxNetworkPayloadBytes,
      resourceTypes: crawlerOptions.networkLogResourceTypes,
      bodyUrlIncludes: crawlerOptions.networkLogBodyUrlIncludes,
      captureBodies: true,
      clear: true,
      timeoutMs: job.timeouts.commandMs,
    });
    slot.networkLogReady = true;
    slot.lastNetworkLogMeta = result?.meta || result || null;
    return slot.lastNetworkLogMeta;
  }

  async navigateWithRecovery(slot, target, job, phase = "detail") {
    await this.ensureTabSlotPrepared(slot, job);
    await this.prepareNetworkCapture(slot, target, job, phase).catch(() => null);

    try {
      const navigation = await navigateAndWait(this.client, {
        tabId: slot.tabId,
        url: target.url,
        timeoutMs: job.timeouts.navigationMs,
      });
      slot.lastUrl = target.url;
      return navigation;
    } catch (error) {
      if (!isRecoverableTabError(error)) {
        throw error;
      }

      await this.replaceTabSlot(slot, job);
      await this.prepareNetworkCapture(slot, target, job, phase).catch(() => null);
      const navigation = await navigateAndWait(this.client, {
        tabId: slot.tabId,
        url: target.url,
        timeoutMs: job.timeouts.navigationMs,
      });
      slot.lastUrl = target.url;
      return navigation;
    }
  }

  buildNetworkContext(tabId, job) {
    return {
      getMark: (options = {}) => getNetworkLogMark(this.client, {
        tabId,
        timeoutMs: options.timeoutMs || job.timeouts.commandMs,
        ...options,
      }),
      getResponses: (options = {}) => getNetworkLogEntries(this.client, {
        tabId,
        timeoutMs: options.timeoutMs || job.timeouts.commandMs,
        ...options,
      }),
      getResponsesSinceMark: (mark, options = {}) => getNetworkLogEntries(this.client, {
        tabId,
        timeoutMs: options.timeoutMs || job.timeouts.commandMs,
        sinceSequence: resolveNetworkMarkSequence(mark),
        ...options,
      }),
      getPageResources: (options = {}) => getPageResources(this.client, {
        tabId,
        timeoutMs: options.timeoutMs || job.timeouts.commandMs,
        ...options,
      }),
    };
  }

  async resolveMainWindowId(job) {
    const configuredWindowId = Number(job?.execution?.mainWindowId);
    if (Number.isFinite(configuredWindowId) && configuredWindowId > 0) {
      return Math.trunc(configuredWindowId);
    }

    const activeTabs = await this.client.listTabs({
      currentWindow: true,
      activeOnly: true,
    }).catch(() => []);
    const activeTab = Array.isArray(activeTabs) ? activeTabs[0] : null;
    if (typeof activeTab?.windowId === "number") {
      return activeTab.windowId;
    }

    const allTabs = await this.client.listTabs({ activeOnly: true }).catch(() => []);
    const fallbackActiveTab = Array.isArray(allTabs) ? allTabs[0] : null;
    return typeof fallbackActiveTab?.windowId === "number" ? fallbackActiveTab.windowId : null;
  }

  async executeSearchTargets(job, targets, handler, { tabPool = null, reserveTargets = [] } = {}) {
    if (!targets.length) {
      return { records: [], errors: [], providerStats: [], reserveUsedCount: 0 };
    }

    const errors = [];
    const records = [];
    const ownsTabPool = !Array.isArray(tabPool) || tabPool.length === 0;
    const workerSlots = ownsTabPool
      ? await this.createTabPool(Math.min(job.limits.maxConcurrentTabs, targets.length), job)
      : tabPool;
    if (!workerSlots.length) {
      throw new Error("No worker tabs are available for crawl execution.");
    }

    const primaryQueue = interleaveSearchTargets(targets);
    const reserveQueue = interleaveSearchTargets(reserveTargets);
    const providerStates = new Map();
    const desiredRecordCount = targets.length;
    const steadyCap = Math.max(
      1,
      Math.min(
        workerSlots.length,
        Math.max(
          2,
          Math.ceil(workerSlots.length / Math.max(1, new Set(primaryQueue.map((target) => sourceKeyForSearchTarget(target))).size))
        )
      )
    );

    const getProviderState = (provider) => {
      if (!providerStates.has(provider)) {
        providerStates.set(provider, {
          provider,
          started: 0,
          succeeded: 0,
          empty: 0,
          timedOut: 0,
          failed: 0,
          blocked: 0,
          skippedByBackoff: 0,
          reserveUsed: 0,
          inFlight: 0,
          unhealthy: false,
          lastStatus: "unknown",
          signals: new Set(),
        });
      }
      return providerStates.get(provider);
    };

    const totalInFlight = () => Array.from(providerStates.values()).reduce((sum, state) => sum + (state.inFlight || 0), 0);
    const maxInFlightForProvider = (state) => {
      if (state.unhealthy) {
        return 0;
      }
      return state.succeeded > 0 ? steadyCap : 1;
    };

    const pruneUnhealthyTargets = (queue) => {
      for (let index = 0; index < queue.length; index += 1) {
        const provider = sourceKeyForSearchTarget(queue[index]);
        const state = getProviderState(provider);
        if (!state.unhealthy) {
          continue;
        }
        state.skippedByBackoff += 1;
        queue.splice(index, 1);
        index -= 1;
      }
    };

    const countHealthyQueuedTargets = (queue) => {
      pruneUnhealthyTargets(queue);
      return queue.length;
    };

    const takeEligibleTarget = (queue, { fromReserve = false } = {}) => {
      pruneUnhealthyTargets(queue);

      for (let index = 0; index < queue.length; index += 1) {
        const target = queue[index];
        const provider = sourceKeyForSearchTarget(target);
        const state = getProviderState(provider);
        if (state.inFlight >= maxInFlightForProvider(state)) {
          continue;
        }

        queue.splice(index, 1);
        state.inFlight += 1;
        state.started += 1;
        if (fromReserve) {
          state.reserveUsed += 1;
        }

        return {
          target,
          provider,
          state,
        };
      }

      return null;
    };

    const acquireNextTarget = () => {
      const remainingNeeded = Math.max(0, desiredRecordCount - records.length - totalInFlight());
      const primaryTarget = takeEligibleTarget(primaryQueue);
      if (primaryTarget) {
        return primaryTarget;
      }

      if (countHealthyQueuedTargets(primaryQueue) < remainingNeeded) {
        return takeEligibleTarget(reserveQueue, { fromReserve: true });
      }

      return null;
    };

    const applyOutcome = (state, outcome) => {
      state.lastStatus = outcome.status;

      for (const signal of outcome.signals || []) {
        state.signals.add(signal);
      }

      if (outcome.kind === "success") {
        state.succeeded += 1;
        return;
      }

      if (outcome.kind === "empty") {
        state.empty += 1;
        return;
      }

      state.failed += 1;

      if (outcome.kind === "timeout") {
        state.timedOut += 1;
        if (state.succeeded === 0) {
          state.unhealthy = true;
        }
        return;
      }

      if (outcome.kind === "blocked") {
        state.blocked += 1;
        state.unhealthy = true;
        return;
      }

      if (state.succeeded === 0 && state.failed >= 2) {
        state.unhealthy = true;
      }
    };

    try {
      await Promise.all(workerSlots.map((slot, workerIndex) => (async () => {
        while (records.length < desiredRecordCount) {
          const next = acquireNextTarget();
          if (!next) {
            const remainingNeeded = Math.max(0, desiredRecordCount - records.length - totalInFlight());
            if (remainingNeeded <= 0) {
              return;
            }
            const queuedCount = countHealthyQueuedTargets(primaryQueue) + countHealthyQueuedTargets(reserveQueue);
            if (queuedCount <= 0) {
              return;
            }
            await sleep(50);
            continue;
          }

          const { target, state } = next;
          const adapter = target.adapterId ? this.registry.get(target.adapterId) : this.registry.match(target.url);

          try {
            const navigation = await this.navigateWithRecovery(slot, target, job, "search");
            const network = this.buildNetworkContext(navigation.tabId, job);
            const context = {
              client: this.client,
              registry: this.registry,
              adapter,
              job,
              phase: "search",
              target,
              tabId: navigation.tabId,
              workerIndex,
              navigation,
              adapterOptions: job.adapterOptions[adapter?.id || ""] || {},
              evaluate: (expression, args = [], options = {}) => cdpEvaluate(this.client, {
                tabId: navigation.tabId,
                expression,
                args,
                timeoutMs: options.timeoutMs || job.timeouts.evaluateMs,
              }),
              getNetworkLogEntries: network.getResponses,
              getPageResources: network.getPageResources,
              network,
            };

            const record = await handler(context);
            records.push(record);
            applyOutcome(state, classifySearchExecution(target, record));
          } catch (error) {
            const structured = {
              phase: "search",
              targetId: target.id,
              adapterId: adapter?.id || null,
              url: target.url,
              error: error?.message || String(error),
            };
            errors.push(structured);
            records.push(pickDefined({
              targetId: target.id,
              adapterId: adapter?.id || null,
              url: target.url,
              error: structured.error,
            }));
            applyOutcome(state, classifySearchExecution(target, null, error));
            this.logger(structured);
          } finally {
            state.inFlight = Math.max(0, state.inFlight - 1);
          }
        }
      })()));
    } finally {
      if (ownsTabPool) {
        await this.cleanupTabs(workerSlots, job.execution.cleanupTabs, job.timeouts.commandMs);
      }
    }

    return {
      records,
      errors,
      providerStats: buildSearchProviderRuntimeStats(targets, reserveTargets, providerStates),
      reserveUsedCount: Array.from(providerStates.values()).reduce((sum, state) => sum + (state.reserveUsed || 0), 0),
    };
  }

  async executeTargets(job, targets, phase, handler, { tabPool = null } = {}) {
    if (!targets.length) {
      return { records: [], errors: [] };
    }

    const errors = [];
    const records = new Array(targets.length);
    const ownsTabPool = !Array.isArray(tabPool) || tabPool.length === 0;
    const workerSlots = ownsTabPool
      ? await this.createTabPool(Math.min(job.limits.maxConcurrentTabs, targets.length), job)
      : tabPool;
    if (!workerSlots.length) {
      throw new Error("No worker tabs are available for crawl execution.");
    }
    let cursor = 0;

    try {
      await Promise.all(workerSlots.map((slot, workerIndex) => (async () => {
        while (true) {
          const index = cursor;
          cursor += 1;
          if (index >= targets.length) {
            return;
          }

          const target = targets[index];
          const adapter = target.adapterId ? this.registry.get(target.adapterId) : this.registry.match(target.url);

          try {
            const navigation = await this.navigateWithRecovery(slot, target, job, phase);
            const network = this.buildNetworkContext(navigation.tabId, job);

            const context = {
              client: this.client,
              registry: this.registry,
              adapter,
              job,
              phase,
              target,
              tabId: navigation.tabId,
              workerIndex,
              navigation,
              adapterOptions: job.adapterOptions[adapter?.id || ""] || {},
              evaluate: (expression, args = [], options = {}) => cdpEvaluate(this.client, {
                tabId: navigation.tabId,
                expression,
                args,
                timeoutMs: options.timeoutMs || job.timeouts.evaluateMs,
              }),
              getNetworkLogEntries: network.getResponses,
              getPageResources: network.getPageResources,
              network,
            };

            records[index] = await handler(context);
          } catch (error) {
            const structured = {
              phase,
              targetId: target.id,
              adapterId: adapter?.id || null,
              url: target.url,
              error: error?.message || String(error),
            };
            errors.push(structured);
            records[index] = pickDefined({
              targetId: target.id,
              adapterId: adapter?.id || null,
              url: target.url,
              error: structured.error,
            });
            this.logger(structured);
          }
        }
      })()));
    } finally {
      if (ownsTabPool) {
        await this.cleanupTabs(workerSlots, job.execution.cleanupTabs, job.timeouts.commandMs);
      }
    }

    return {
      records: records.filter(Boolean),
      errors,
    };
  }

  async createTabPool(size, job) {
    if (size <= 0) {
      return [];
    }

    const tabs = await Promise.all(Array.from({ length: size }, async (_unused, index) => {
      const tab = await this.client.navigate({
        url: "about:blank",
        windowId: job?.execution?.mainWindowOnly !== false ? job?.execution?.mainWindowId : undefined,
        createNewTab: true,
        active: Boolean(job?.execution?.activeTabs) && index === 0,
        timeoutMs: job?.timeouts?.commandMs || 15000,
      });
      return typeof tab?.id === "number"
        ? {
            tabId: tab.id,
            prepared: false,
            networkLogReady: false,
            lastUrl: "about:blank",
          }
        : null;
    }));
    return tabs.filter(Boolean);
  }

  async cleanupTabs(tabSlots, strategy, timeoutMs) {
    if (strategy === "keep") {
      return;
    }

    const tabIds = tabSlots
      .map((slot) => (typeof slot === "number" ? slot : slot?.tabId))
      .filter((tabId) => typeof tabId === "number");

    await Promise.all(tabIds.map(async (tabId) => {
      try {
        if (strategy === "blank") {
          await this.client.blankTab(tabId, timeoutMs);
          return;
        }
        await this.client.closeTab(tabId, timeoutMs);
      } catch {
        // Best effort cleanup.
      }
    }));
  }
}
