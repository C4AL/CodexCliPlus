import { z } from "zod";
import { hasSearchTemplatePlaceholder, makeId, nowIso, uniqueBy } from "./crawl-utils.mjs";

export const crawlFacetSchema = z.enum([
  "official",
  "community",
  "analysis",
  "release",
  "interview",
  "demo",
  "benchmark",
  "rumor",
  "pricing",
  "research",
  "tutorial",
]);

export const querySeedSchema = z.object({
  id: z.string().optional(),
  term: z.string().min(1),
  aliases: z.array(z.string().min(1)).default([]),
  keywords: z.array(z.string().min(1)).default([]),
  intent: z.enum(["auto", "general", "news", "research", "product", "social", "video"]).default("auto"),
  facets: z.array(crawlFacetSchema).default([]),
  freshnessDays: z.number().int().positive().optional(),
  locale: z.string().default("en-US"),
  maxExpansions: z.number().int().positive().optional(),
}).passthrough();

export const crawlTargetSchema = z.object({
  id: z.string().optional(),
  url: z.string().url(),
  adapterId: z.string().optional(),
  kind: z.enum(["detail", "search"]).default("detail"),
  query: z.string().optional(),
  meta: z.record(z.any()).default({}),
}).passthrough();

export const crawlLimitsSchema = z.object({
  maxExpandedQueries: z.number().int().positive().default(24),
  maxSearchTargets: z.number().int().positive().default(36),
  maxSearchTargetsPerExpansion: z.number().int().positive().default(4),
  maxSearchTargetsPerProvider: z.number().int().positive().default(12),
  maxConcurrentTabs: z.number().int().positive().default(6),
  maxItemsPerSearch: z.number().int().positive().default(20),
  maxListingPagesPerSearch: z.number().int().positive().default(2),
  maxDetailItems: z.number().int().positive().default(30),
  maxDetailsPerDomain: z.number().int().positive().default(3),
  maxDetailsPerQuery: z.number().int().positive().default(4),
  maxCommentsPerPage: z.number().int().nonnegative().default(400),
  maxCommentBatches: z.number().int().nonnegative().default(12),
  maxNetworkCandidates: z.number().int().positive().default(48),
  maxNetworkPayloads: z.number().int().positive().default(16),
  maxNetworkPayloadBytes: z.number().int().positive().default(240000),
  maxBodyTextLength: z.number().int().positive().default(16000),
  maxLinksPerPage: z.number().int().positive().default(60),
}).default({});

export const crawlTimeoutSchema = z.object({
  navigationMs: z.number().int().positive().default(20000),
  evaluateMs: z.number().int().positive().default(20000),
  commandMs: z.number().int().positive().default(15000),
}).default({});

export const crawlExecutionSchema = z.object({
  cleanupTabs: z.enum(["close", "blank", "keep"]).default("close"),
  activeTabs: z.boolean().default(false),
  mainWindowOnly: z.boolean().default(true),
  mainWindowId: z.number().int().positive().optional(),
}).default({});

const urlTemplateSchema = z.string()
  .min(1)
  .transform((value) => value.trim())
  .refine((value) => /^https?:\/\//i.test(value), {
    message: "search.urlTemplates entries must be absolute http(s) URLs.",
  })
  .refine((value) => hasSearchTemplatePlaceholder(value), {
    message: "search.urlTemplates entries must include a supported placeholder such as {{query}}, {{query_encoded}}, {{query_plus}}, {{query_raw}}, {{topic}}, or {queryEncoded}.",
  });

export const crawlSearchPlanningSchema = z.object({
  mode: z.enum(["balanced", "exhaustive"]).default("balanced"),
  maxTargets: z.number().int().positive().optional(),
  maxTargetsPerProvider: z.number().int().positive().optional(),
  maxTargetsPerSeed: z.number().int().positive().optional(),
  maxProvidersPerQuery: z.number().int().positive().default(2),
  minTargetsPerProvider: z.number().int().positive().default(1),
}).default({});

export const crawlSearchSchema = z.object({
  includeAdapterSearches: z.boolean().default(true),
  urlTemplates: z.array(urlTemplateSchema).default([]),
  planning: crawlSearchPlanningSchema,
}).default({});

export const crawlOutputSchema = z.object({
  includeRawListings: z.boolean().default(false),
  includeRawDetails: z.boolean().default(false),
}).default({});

export const crawlJobSchema = z.object({
  id: z.string().optional(),
  name: z.string().optional(),
  adapters: z.array(z.string()).default([]),
  seeds: z.array(querySeedSchema).default([]),
  targets: z.array(crawlTargetSchema).default([]),
  search: crawlSearchSchema,
  limits: crawlLimitsSchema,
  timeouts: crawlTimeoutSchema,
  execution: crawlExecutionSchema,
  adapterOptions: z.record(z.any()).default({}),
  output: crawlOutputSchema,
}).passthrough();

export const crawlResultSchema = z.object({
  job: z.any(),
  plan: z.any(),
  startedAt: z.string(),
  finishedAt: z.string(),
  durationMs: z.number().int().nonnegative(),
  searchRuns: z.array(z.any()),
  items: z.array(z.any()),
  detailRuns: z.array(z.any()),
  errors: z.array(z.any()),
  summary: z.object({
    expansionCount: z.number().int().nonnegative(),
    searchTargetCount: z.number().int().nonnegative(),
    directTargetCount: z.number().int().nonnegative(),
    discoveredItemCount: z.number().int().nonnegative(),
    selectedDetailTargetCount: z.number().int().nonnegative().optional(),
    detailCount: z.number().int().nonnegative(),
    errorCount: z.number().int().nonnegative(),
  }),
});

function normalizeSeed(seed, index) {
  const parsed = querySeedSchema.parse(seed);
  return {
    ...parsed,
    term: parsed.term.trim(),
    id: parsed.id || makeId("seed", `${index}:${parsed.term}:${parsed.intent}`),
    aliases: uniqueBy(parsed.aliases.map((item) => item.trim()).filter(Boolean), (item) => item.toLowerCase()),
    keywords: uniqueBy(parsed.keywords.map((item) => item.trim()).filter(Boolean), (item) => item.toLowerCase()),
  };
}

function normalizeTarget(target, index) {
  const parsed = crawlTargetSchema.parse(target);
  return {
    ...parsed,
    id: parsed.id || makeId("target", `${index}:${parsed.url}:${parsed.kind}`),
    meta: parsed.meta || {},
  };
}

export function normalizeCrawlJob(input) {
  const parsed = crawlJobSchema.parse(input);
  const seeds = parsed.seeds.map(normalizeSeed);
  const targets = parsed.targets.map(normalizeTarget);
  const urlTemplates = uniqueBy(
    parsed.search.urlTemplates.map((template) => template.trim()).filter(Boolean),
    (template) => template
  );
  return {
    ...parsed,
    id: parsed.id || makeId("job", {
      name: parsed.name || "",
      adapters: parsed.adapters,
      seeds: seeds.map((seed) => seed.term),
      targets: targets.map((target) => target.url),
    }),
    normalizedAt: nowIso(),
    seeds,
    targets,
    search: {
      ...parsed.search,
      urlTemplates,
    },
  };
}

export function finalizeCrawlResult(input) {
  return crawlResultSchema.parse(input);
}
