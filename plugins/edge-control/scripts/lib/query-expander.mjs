import { makeId, normalizeWhitespace, uniqueBy } from "./crawl-utils.mjs";

const ROOT_NOISE_PATTERNS = [
  /\b(latest|recent|new|today(?:'s)?|this\s+(?:week|month)|fresh)\b/gi,
  /\b(news|updates|update|announcements|announcement|release(?:s| notes)?|launch(?:es)?|rumou?rs?|leaks?)\b/gi,
  /\u6700\u65b0(?:\u6d88\u606f|\u52a8\u6001|\u66f4\u65b0)?/g,
  /\u5b98\u65b9(?:\u516c\u544a|\u6d88\u606f)?/g,
  /\u53d1\u5e03(?:\u8bf4\u660e)?/g,
];

const DEFAULT_FACET_BY_INTENT = {
  general: "analysis",
  news: "official",
  research: "research",
  product: "release",
  social: "community",
  video: "demo",
};

const TEMPLATE_SETS = {
  en: {
    general: [
      { facet: "analysis", weight: 0.88, build: (topic) => `${topic} overview` },
      { facet: "analysis", weight: 0.84, build: (topic) => `${topic} explained` },
      { facet: "community", weight: 0.82, build: (topic) => `${topic} discussion` },
      { facet: "tutorial", weight: 0.78, build: (topic) => `${topic} guide` },
    ],
    news: [
      { facet: "official", weight: 1.0, build: (topic) => `${topic} latest news` },
      { facet: "official", weight: 0.99, build: (topic) => `${topic} official updates` },
      { facet: "official", weight: 0.97, build: (topic) => `${topic} official blog` },
      { facet: "release", weight: 0.96, build: (topic) => `${topic} announcements` },
      { facet: "release", weight: 0.95, build: (topic) => `${topic} release notes` },
      { facet: "release", weight: 0.94, build: (topic) => `${topic} product updates` },
      { facet: "analysis", weight: 0.92, build: (topic) => `${topic} latest developments` },
      { facet: "analysis", weight: 0.9, build: (topic) => `${topic} breaking news` },
      { facet: "interview", weight: 0.84, build: (topic) => `${topic} interview` },
      { facet: "demo", weight: 0.82, build: (topic) => `${topic} demo` },
      { facet: "community", weight: 0.8, build: (topic) => `${topic} reactions` },
      { facet: "rumor", weight: 0.68, build: (topic) => `${topic} leaks rumors` },
    ],
    research: [
      { facet: "research", weight: 0.98, build: (topic) => `${topic} research update` },
      { facet: "research", weight: 0.95, build: (topic) => `${topic} paper` },
      { facet: "benchmark", weight: 0.92, build: (topic) => `${topic} benchmark` },
      { facet: "analysis", weight: 0.88, build: (topic) => `${topic} evaluation` },
      { facet: "analysis", weight: 0.84, build: (topic) => `${topic} recap` },
    ],
    product: [
      { facet: "release", weight: 0.98, build: (topic) => `${topic} release` },
      { facet: "release", weight: 0.94, build: (topic) => `${topic} new features` },
      { facet: "pricing", weight: 0.9, build: (topic) => `${topic} pricing update` },
      { facet: "release", weight: 0.88, build: (topic) => `${topic} roadmap` },
      { facet: "demo", weight: 0.86, build: (topic) => `${topic} hands on` },
      { facet: "tutorial", weight: 0.82, build: (topic) => `${topic} walkthrough` },
    ],
    social: [
      { facet: "community", weight: 0.9, build: (topic) => `${topic} reactions` },
      { facet: "community", weight: 0.86, build: (topic) => `${topic} commentary` },
      { facet: "analysis", weight: 0.84, build: (topic) => `${topic} debate` },
    ],
    video: [
      { facet: "demo", weight: 0.96, build: (topic) => `${topic} video` },
      { facet: "interview", weight: 0.92, build: (topic) => `${topic} live` },
      { facet: "analysis", weight: 0.9, build: (topic) => `${topic} recap` },
      { facet: "demo", weight: 0.86, build: (topic) => `${topic} keynote` },
    ],
  },
  zh: {
    general: [
      { facet: "analysis", weight: 0.86, build: (topic) => `${topic} \u89e3\u8bfb` },
      { facet: "community", weight: 0.82, build: (topic) => `${topic} \u8ba8\u8bba` },
      { facet: "tutorial", weight: 0.8, build: (topic) => `${topic} \u6307\u5357` },
    ],
    news: [
      { facet: "official", weight: 0.99, build: (topic) => `${topic} \u6700\u65b0\u6d88\u606f` },
      { facet: "official", weight: 0.97, build: (topic) => `${topic} \u5b98\u65b9\u516c\u544a` },
      { facet: "release", weight: 0.95, build: (topic) => `${topic} \u6700\u65b0\u66f4\u65b0` },
      { facet: "release", weight: 0.93, build: (topic) => `${topic} \u53d1\u5e03\u8bf4\u660e` },
      { facet: "analysis", weight: 0.9, build: (topic) => `${topic} \u6700\u65b0\u52a8\u6001` },
      { facet: "analysis", weight: 0.88, build: (topic) => `${topic} \u6df1\u5ea6\u89e3\u8bfb` },
      { facet: "interview", weight: 0.82, build: (topic) => `${topic} \u91c7\u8bbf` },
      { facet: "demo", weight: 0.8, build: (topic) => `${topic} \u6f14\u793a` },
    ],
    research: [
      { facet: "research", weight: 0.97, build: (topic) => `${topic} \u8bba\u6587` },
      { facet: "research", weight: 0.94, build: (topic) => `${topic} \u7814\u7a76\u8fdb\u5c55` },
      { facet: "benchmark", weight: 0.9, build: (topic) => `${topic} \u8bc4\u6d4b` },
      { facet: "analysis", weight: 0.86, build: (topic) => `${topic} \u8bc4\u4f30` },
    ],
    product: [
      { facet: "release", weight: 0.97, build: (topic) => `${topic} \u53d1\u5e03` },
      { facet: "release", weight: 0.94, build: (topic) => `${topic} \u65b0\u529f\u80fd` },
      { facet: "pricing", weight: 0.88, build: (topic) => `${topic} \u4ef7\u683c\u66f4\u65b0` },
      { facet: "tutorial", weight: 0.84, build: (topic) => `${topic} \u4e0a\u624b` },
    ],
    social: [
      { facet: "community", weight: 0.88, build: (topic) => `${topic} \u8ba8\u8bba` },
      { facet: "community", weight: 0.86, build: (topic) => `${topic} \u8bc4\u4ef7` },
      { facet: "analysis", weight: 0.84, build: (topic) => `${topic} \u4e89\u8bae` },
    ],
    video: [
      { facet: "demo", weight: 0.94, build: (topic) => `${topic} \u89c6\u9891` },
      { facet: "interview", weight: 0.9, build: (topic) => `${topic} \u76f4\u64ad` },
      { facet: "analysis", weight: 0.86, build: (topic) => `${topic} \u56de\u987e` },
    ],
  },
};

function inferIntent(seed) {
  if (seed.intent && seed.intent !== "auto") {
    return seed.intent;
  }

  const text = `${seed.term} ${seed.keywords.join(" ")}`.toLowerCase();
  if (/\b(news|latest|update|announce|release|launch|leak|rumor)\b/.test(text)) {
    return "news";
  }
  if (/\b(paper|research|benchmark|eval|evaluation)\b/.test(text)) {
    return "research";
  }
  if (/\b(price|pricing|plan|subscription|feature|product)\b/.test(text)) {
    return "product";
  }
  if (/\b(video|livestream|interview|watch)\b/.test(text)) {
    return "video";
  }
  if (/\b(reaction|community|discussion|debate|opinion)\b/.test(text)) {
    return "social";
  }
  return "general";
}

function deriveTopic(term) {
  let topic = normalizeWhitespace(term);
  for (const pattern of ROOT_NOISE_PATTERNS) {
    topic = normalizeWhitespace(topic.replace(pattern, " "));
  }
  return topic || normalizeWhitespace(term);
}

function scoreAlias(term, topic) {
  return normalizeWhitespace(term).toLowerCase() === normalizeWhitespace(topic).toLowerCase() ? 0 : -0.04;
}

function shouldIncludeChineseVariants(seed, topic) {
  if (seed.includeChineseVariants === false) {
    return false;
  }
  if (seed.includeChineseVariants === true) {
    return true;
  }

  const locale = String(seed.locale || "").toLowerCase();
  return locale.startsWith("zh") || /[\u3400-\u9fff]/.test(`${seed.term} ${topic} ${seed.aliases.join(" ")} ${seed.keywords.join(" ")}`);
}

function buildTemplateList(intent, seed, topic) {
  const templates = [
    ...(TEMPLATE_SETS.en[intent] || []),
    ...(intent !== "general" ? TEMPLATE_SETS.en.general : []),
  ];

  if (shouldIncludeChineseVariants(seed, topic)) {
    templates.push(
      ...(TEMPLATE_SETS.zh[intent] || []),
      ...(intent !== "general" ? TEMPLATE_SETS.zh.general : [])
    );
  }

  return templates;
}

function buildContextTerms(seed, topic) {
  const aliases = uniqueBy(
    seed.aliases.map((value) => normalizeWhitespace(value)).filter(Boolean),
    (value) => value.toLowerCase()
  ).filter((value) => value.toLowerCase() !== topic.toLowerCase());

  const keywords = uniqueBy(
    seed.keywords.map((value) => normalizeWhitespace(value)).filter(Boolean),
    (value) => value.toLowerCase()
  );

  const keywordContexts = [];
  for (const keyword of keywords.slice(0, 4)) {
    keywordContexts.push(`${topic} ${keyword}`);
    for (const alias of aliases.slice(0, 2)) {
      keywordContexts.push(`${alias} ${keyword}`);
    }
  }

  return {
    aliases,
    directTerms: uniqueBy(
      [seed.term, topic, ...aliases].map((value) => normalizeWhitespace(value)).filter(Boolean),
      (value) => value.toLowerCase()
    ),
    templateTerms: uniqueBy(
      [topic, ...aliases.slice(0, 2), ...keywordContexts.slice(0, 4)].map((value) => normalizeWhitespace(value)).filter(Boolean),
      (value) => value.toLowerCase()
    ),
    keywordContexts: uniqueBy(
      keywordContexts.map((value) => normalizeWhitespace(value)).filter(Boolean),
      (value) => value.toLowerCase()
    ),
  };
}

function buildTemporalCandidates(topic, intent, seed) {
  if (!["news", "research", "product"].includes(intent)) {
    return [];
  }

  const year = new Date().getUTCFullYear();
  const includeChinese = shouldIncludeChineseVariants(seed, topic);
  const facet = DEFAULT_FACET_BY_INTENT[intent] || "analysis";

  return [
    {
      query: normalizeWhitespace(`${topic} ${year}`),
      facet,
      reason: "temporal:year",
      weight: 0.89,
    },
    {
      query: normalizeWhitespace(includeChinese ? `${topic} ${year} \u6700\u65b0` : `${topic} ${year} update`),
      facet,
      reason: "temporal:latest",
      weight: 0.87,
    },
  ];
}

function selectCandidates(candidates, maxItems) {
  const deduped = uniqueBy(
    candidates
      .map((candidate) => ({
        ...candidate,
        query: normalizeWhitespace(candidate.query),
      }))
      .filter((candidate) => candidate.query),
    (candidate) => candidate.query.toLowerCase()
  ).sort((left, right) => right.weight - left.weight);

  const selected = [];
  const usedQueries = new Set();
  const usedFacets = new Set();

  const pushCandidate = (candidate, { trackFacet = true } = {}) => {
    const key = candidate.query.toLowerCase();
    if (usedQueries.has(key) || selected.length >= maxItems) {
      return false;
    }

    usedQueries.add(key);
    if (trackFacet) {
      usedFacets.add(candidate.facet);
    }
    selected.push(candidate);
    return true;
  };

  for (const candidate of deduped) {
    if (candidate.reason.startsWith("seed:")) {
      pushCandidate(candidate, { trackFacet: false });
    }
  }

  for (const candidate of deduped) {
    if (selected.length >= maxItems) {
      break;
    }
    if (!usedFacets.has(candidate.facet)) {
      pushCandidate(candidate);
    }
  }

  for (const candidate of deduped) {
    if (selected.length >= maxItems) {
      break;
    }
    pushCandidate(candidate);
  }

  return selected;
}

function expandOneSeed(seed, options) {
  const topic = deriveTopic(seed.term);
  const intent = inferIntent(seed);
  const maxItems = seed.maxExpansions || options.maxPerSeed || options.maxExpandedQueries || 24;
  const defaultFacet = seed.facets[0] || DEFAULT_FACET_BY_INTENT[intent] || "analysis";
  const contextTerms = buildContextTerms(seed, topic);
  const templates = buildTemplateList(intent, seed, topic);
  const candidates = [];

  for (const term of contextTerms.directTerms) {
    candidates.push({
      query: term,
      facet: defaultFacet,
      reason: term.toLowerCase() === normalizeWhitespace(seed.term).toLowerCase() ? "seed:term" : "seed:topic",
      weight: 1 + scoreAlias(term, topic),
    });
  }

  for (const term of contextTerms.keywordContexts) {
    candidates.push({
      query: term,
      facet: defaultFacet,
      reason: "seed:keyword",
      weight: 0.94 + scoreAlias(term, topic),
    });
  }

  for (const term of contextTerms.templateTerms) {
    for (const template of templates) {
      candidates.push({
        query: normalizeWhitespace(template.build(term)),
        facet: template.facet,
        reason: `${intent}:${template.facet}`,
        weight: template.weight + scoreAlias(term, topic),
      });
    }
  }

  candidates.push(...buildTemporalCandidates(topic, intent, seed));

  for (const facet of seed.facets) {
    if (facet === "official") {
      candidates.push({
        query: normalizeWhitespace(`${topic} official channel`),
        facet,
        reason: "facet:official",
        weight: 0.91,
      });
      candidates.push({
        query: normalizeWhitespace(`${topic} official website`),
        facet,
        reason: "facet:official",
        weight: 0.9,
      });
      candidates.push({
        query: normalizeWhitespace(`${topic} official blog`),
        facet,
        reason: "facet:official",
        weight: 0.89,
      });
      if (shouldIncludeChineseVariants(seed, topic)) {
        candidates.push({
          query: normalizeWhitespace(`${topic} \u5b98\u65b9\u7f51\u7ad9`),
          facet,
          reason: "facet:official",
          weight: 0.88,
        });
        candidates.push({
          query: normalizeWhitespace(`${topic} \u5b98\u65b9\u535a\u5ba2`),
          facet,
          reason: "facet:official",
          weight: 0.87,
        });
      }
    }

    if (facet === "rumor") {
      candidates.push({
        query: normalizeWhitespace(`${topic} leak rumor`),
        facet,
        reason: "facet:rumor",
        weight: 0.7,
      });
    }
  }

  return selectCandidates(candidates, maxItems).map((candidate, index) => ({
    id: makeId("query", `${seed.id}:${candidate.query}`),
    seedId: seed.id,
    topic,
    intent,
    rank: index + 1,
    locale: seed.locale,
    freshnessDays: seed.freshnessDays || null,
    ...candidate,
  }));
}

export function expandQuerySeeds(seeds, options = {}) {
  const expanded = seeds.flatMap((seed) => expandOneSeed(seed, options));
  const unique = uniqueBy(
    expanded.sort((left, right) => right.weight - left.weight),
    (candidate) => `${candidate.query.toLowerCase()}::${candidate.locale}`
  );
  const limit = options.maxExpandedQueries || unique.length;
  return unique.slice(0, limit).map((candidate, index) => ({
    ...candidate,
    overallRank: index + 1,
  }));
}
