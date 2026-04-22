import { normalizeWhitespace, uniqueBy } from "./text.mjs";

const EN_NEWS_TEMPLATES = [
  "{topic}",
  "{topic} latest",
  "{topic} latest news",
  "{topic} latest update",
  "{topic} breaking news",
  "{topic} announcement",
  "{topic} official announcement",
  "{topic} release",
  "{topic} release notes",
  "{topic} launch",
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
  "{topic} release recap",
  "{topic} launch analysis",
  "{topic} hands on",
  "{topic} discussion",
  "{topic} controversy",
];

const ZH_NEWS_TEMPLATES = [
  "{topic} latest news",
  "{topic} official announcement",
  "{topic} update",
  "{topic} release",
  "{topic} latest",
  "{topic} release notes",
  "{topic} latest model",
  "{topic} latest features",
  "{topic} 最新消息",
  "{topic} 最新动态",
  "{topic} 最新更新",
  "{topic} 官方公告",
  "{topic} 发布会",
  "{topic} 新模型",
  "{topic} 新功能",
  "{topic} 测评",
  "{topic} 解读",
  "{topic} 讨论",
];

function applyTemplates(topic, templates) {
  return templates.map((template) =>
    normalizeWhitespace(template.replaceAll("{topic}", topic))
  );
}

export function expandQueries(options = {}) {
  const topic = normalizeWhitespace(options.topic);
  const mode = options.mode || "news";
  const includeChineseVariants = options.includeChineseVariants ?? true;
  const seedQueries = Array.isArray(options.seedQueries) ? options.seedQueries : [];
  const maxQueries = Math.max(1, Number(options.maxQueries) || 24);

  if (!topic && seedQueries.length === 0) {
    throw new Error("expandQueries requires a topic or seedQueries.");
  }

  const queries = [];
  if (topic) {
    if (mode === "news") {
      queries.push(...applyTemplates(topic, EN_NEWS_TEMPLATES));
      if (includeChineseVariants) {
        queries.push(...applyTemplates(topic, ZH_NEWS_TEMPLATES));
      }
    } else {
      queries.push(topic);
    }
  }

  queries.push(...seedQueries.map(normalizeWhitespace));

  return uniqueBy(
    queries.filter(Boolean),
    (value) => value.toLowerCase()
  ).slice(0, maxQueries);
}
