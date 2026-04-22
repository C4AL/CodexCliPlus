const HTML_ENTITY_MAP = new Map([
  ["amp", "&"],
  ["lt", "<"],
  ["gt", ">"],
  ["quot", "\""],
  ["apos", "'"],
  ["nbsp", " "],
]);

export function normalizeWhitespace(value) {
  return String(value || "")
    .replace(/\u00a0/g, " ")
    .replace(/\s+/g, " ")
    .trim();
}

export function truncateText(value, maxLength = 12000) {
  const text = String(value ?? "");
  const limit = Math.max(1, Number(maxLength) || 12000);
  return text.length > limit ? `${text.slice(0, limit)}...[truncated]` : text;
}

export function decodeHtmlEntities(value) {
  return String(value || "").replace(/&([a-z]+);/gi, (match, entity) => {
    return HTML_ENTITY_MAP.get(entity.toLowerCase()) || match;
  });
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

export function safeJsonParse(value, fallback = null) {
  try {
    return JSON.parse(value);
  } catch {
    return fallback;
  }
}
