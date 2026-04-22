import crypto from "node:crypto";

export function nowIso() {
  return new Date().toISOString();
}

export function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

export function clamp(value, { min = Number.NEGATIVE_INFINITY, max = Number.POSITIVE_INFINITY, fallback = min } = {}) {
  const numeric = Number(value);
  if (!Number.isFinite(numeric)) {
    return fallback;
  }
  return Math.min(max, Math.max(min, numeric));
}

export function normalizeWhitespace(value) {
  return String(value ?? "").replace(/\s+/g, " ").trim();
}

export function truncate(value, maxLength = 4000) {
  const text = String(value ?? "");
  const limit = clamp(maxLength, { min: 1, max: Number.MAX_SAFE_INTEGER, fallback: 4000 });
  if (text.length <= limit) {
    return text;
  }
  return `${text.slice(0, limit)}...[truncated]`;
}

export function asArray(value) {
  if (Array.isArray(value)) {
    return value.filter((item) => item !== undefined && item !== null);
  }
  if (value === undefined || value === null) {
    return [];
  }
  return [value];
}

export function uniqueBy(items, keySelector) {
  const seen = new Set();
  const output = [];
  for (const item of items) {
    const key = keySelector(item);
    if (key === undefined || key === null || seen.has(key)) {
      continue;
    }
    seen.add(key);
    output.push(item);
  }
  return output;
}

export function pickDefined(record) {
  return Object.fromEntries(Object.entries(record).filter(([, value]) => value !== undefined));
}

export function safeJsonParse(text, fallback = null) {
  try {
    return JSON.parse(text);
  } catch {
    return fallback;
  }
}

export function toAbsoluteUrl(value, baseUrl) {
  if (!value) {
    return null;
  }
  try {
    if (baseUrl) {
      return new URL(value, baseUrl).toString();
    }
    return new URL(value).toString();
  } catch {
    return null;
  }
}

export function sha1(value) {
  const serialized = typeof value === "string" ? value : JSON.stringify(value);
  return crypto.createHash("sha1").update(serialized).digest("hex");
}

export function makeId(prefix, value) {
  return `${prefix}_${sha1(value).slice(0, 12)}`;
}

const DOUBLE_BRACE_TEMPLATE_ALIASES = {
  query: "queryEncoded",
  query_encoded: "queryEncoded",
  query_plus: "queryPlus",
  query_raw: "queryRaw",
  topic: "topicEncoded",
  topic_encoded: "topicEncoded",
  topic_plus: "topicPlus",
  topic_raw: "topicRaw",
};

const SINGLE_BRACE_TEMPLATE_ALIASES = {
  query: "queryRaw",
  queryRaw: "queryRaw",
  query_raw: "queryRaw",
  queryEncoded: "queryEncoded",
  query_encoded: "queryEncoded",
  queryPlus: "queryPlus",
  query_plus: "queryPlus",
  topic: "topicRaw",
  topicRaw: "topicRaw",
  topic_raw: "topicRaw",
  topicEncoded: "topicEncoded",
  topic_encoded: "topicEncoded",
  topicPlus: "topicPlus",
  topic_plus: "topicPlus",
  locale: "locale",
};

const SUPPORTED_SEARCH_TEMPLATE_TOKENS = new Set([
  ...Object.keys(DOUBLE_BRACE_TEMPLATE_ALIASES),
  ...Object.keys(SINGLE_BRACE_TEMPLATE_ALIASES),
]);

function firstDefined(...values) {
  return values.find((value) => value !== undefined && value !== null);
}

function asTemplateString(value) {
  return value === undefined || value === null ? undefined : String(value);
}

function buildTemplateValueMap(variables = {}) {
  const base = Object.fromEntries(
    Object.entries(variables)
      .filter(([, value]) => value !== undefined && value !== null)
      .map(([key, value]) => [key, String(value)])
  );

  const queryRaw = asTemplateString(firstDefined(base.queryRaw, base.query_raw, base.query));
  const topicRaw = asTemplateString(firstDefined(base.topicRaw, base.topic_raw, base.topic));

  const queryEncoded = asTemplateString(firstDefined(
    base.queryEncoded,
    base.query_encoded,
    queryRaw === undefined ? undefined : encodeURIComponent(queryRaw)
  ));
  const queryPlus = asTemplateString(firstDefined(
    base.queryPlus,
    base.query_plus,
    queryRaw === undefined ? undefined : normalizeWhitespace(queryRaw).split(" ").filter(Boolean).join("+")
  ));
  const topicEncoded = asTemplateString(firstDefined(
    base.topicEncoded,
    base.topic_encoded,
    topicRaw === undefined ? undefined : encodeURIComponent(topicRaw)
  ));
  const topicPlus = asTemplateString(firstDefined(
    base.topicPlus,
    base.topic_plus,
    topicRaw === undefined ? undefined : normalizeWhitespace(topicRaw).split(" ").filter(Boolean).join("+")
  ));

  return pickDefined({
    ...base,
    queryRaw,
    query_raw: queryRaw,
    queryEncoded,
    query_encoded: queryEncoded,
    queryPlus,
    query_plus: queryPlus,
    topicRaw,
    topic_raw: topicRaw,
    topicEncoded,
    topic_encoded: topicEncoded,
    topicPlus,
    topic_plus: topicPlus,
  });
}

function resolveTemplateToken(token, values, aliases) {
  const normalizedToken = aliases[token] || token;
  if (Object.prototype.hasOwnProperty.call(values, normalizedToken)) {
    return values[normalizedToken];
  }
  return null;
}

export function listTemplateTokens(template) {
  const tokens = new Set();
  const text = String(template ?? "");
  const pattern = /\{\{([a-zA-Z0-9_]+)\}\}|\{([a-zA-Z0-9_]+)\}/g;
  let match = pattern.exec(text);

  while (match) {
    tokens.add(match[1] || match[2]);
    match = pattern.exec(text);
  }

  return Array.from(tokens);
}

export function hasSearchTemplatePlaceholder(template) {
  return listTemplateTokens(template).some((token) => SUPPORTED_SEARCH_TEMPLATE_TOKENS.has(token));
}

export function compileTemplate(template, variables) {
  const values = buildTemplateValueMap(variables);
  const renderedDoubleBrace = String(template ?? "").replace(/\{\{([a-zA-Z0-9_]+)\}\}/g, (match, token) => {
    const value = resolveTemplateToken(token, values, DOUBLE_BRACE_TEMPLATE_ALIASES);
    return value === null ? match : String(value);
  });

  return renderedDoubleBrace.replace(/\{([a-zA-Z0-9_]+)\}/g, (match, token) => {
    const value = resolveTemplateToken(token, values, SINGLE_BRACE_TEMPLATE_ALIASES);
    return value === null ? match : String(value);
  });
}

export async function mapPool(items, concurrency, worker) {
  const queue = Array.from(items);
  const results = new Array(queue.length);
  const workerCount = clamp(concurrency, { min: 1, max: Math.max(1, queue.length), fallback: 1 });
  let cursor = 0;

  await Promise.all(Array.from({ length: workerCount }, async () => {
    while (true) {
      const index = cursor;
      cursor += 1;
      if (index >= queue.length) {
        return;
      }
      results[index] = await worker(queue[index], index);
    }
  }));

  return results;
}
