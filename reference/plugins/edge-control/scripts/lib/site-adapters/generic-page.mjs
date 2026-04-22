import { normalizeWhitespace, truncateText } from "../text.mjs";

function buildGenericPageExpression(options = {}) {
  const payload = {
    includeHtml: options.includeHtml ?? false,
    includeLinks: options.includeLinks ?? true,
    includeImages: options.includeImages ?? true,
    includeMeta: options.includeMeta ?? true,
    includeJsonLd: options.includeJsonLd ?? true,
    includeForms: options.includeForms ?? true,
    includeTables: options.includeTables ?? true,
    maxParagraphs: options.maxParagraphs ?? 20,
    maxHeadings: options.maxHeadings ?? 20,
    maxLinks: options.maxLinks ?? 40,
    maxImages: options.maxImages ?? 20,
    maxJsonLd: options.maxJsonLd ?? 8,
    maxForms: options.maxForms ?? 10,
    maxTables: options.maxTables ?? 10,
    maxTableRows: options.maxTableRows ?? 6,
    maxCustomMatches: options.maxCustomMatches ?? 8,
    customSelectors: options.customSelectors || {},
  };

  return `(async () => {
    const options = ${JSON.stringify(payload)};
    const text = (value) => String(value || "").replace(/\\s+/g, " ").trim();
    const take = (items, limit) => items.filter(Boolean).slice(0, limit);
    const root = document.documentElement;
    const body = document.body;

    const headings = take(
      Array.from(document.querySelectorAll("h1, h2, h3, h4, h5, h6")).map((node) => ({
        level: node.tagName.toLowerCase(),
        text: text(node.textContent),
      })),
      options.maxHeadings
    );

    const paragraphs = take(
      Array.from(document.querySelectorAll("article p, main p, p")).map((node) => text(node.textContent)),
      options.maxParagraphs
    );

    const links = options.includeLinks
      ? take(
          Array.from(document.querySelectorAll("a[href]")).map((node) => ({
            text: text(node.textContent),
            url: node.href,
            rel: node.rel || null,
          })),
          options.maxLinks
        )
      : [];

    const images = options.includeImages
      ? take(
          Array.from(document.querySelectorAll("img[src]")).map((node) => ({
            alt: text(node.alt),
            url: node.src,
            width: node.naturalWidth || null,
            height: node.naturalHeight || null,
          })),
          options.maxImages
        )
      : [];

    const meta = options.includeMeta
      ? {
          language: document.documentElement.lang || null,
          description:
            document.querySelector("meta[name='description']")?.content ||
            document.querySelector("meta[property='og:description']")?.content ||
            "",
          keywords: document.querySelector("meta[name='keywords']")?.content || "",
          ogTitle: document.querySelector("meta[property='og:title']")?.content || "",
          ogType: document.querySelector("meta[property='og:type']")?.content || "",
          canonicalUrl: document.querySelector("link[rel='canonical']")?.href || null,
        }
      : null;

    const jsonLd = options.includeJsonLd
      ? take(
          Array.from(document.querySelectorAll("script[type='application/ld+json']")).map((node) => {
            const raw = text(node.textContent);
            try {
              return JSON.parse(raw);
            } catch {
              return raw;
            }
          }),
          options.maxJsonLd
        )
      : [];

    const forms = options.includeForms
      ? take(
          Array.from(document.querySelectorAll("form")).map((form) => ({
            action: form.action || null,
            method: (form.method || "get").toLowerCase(),
            inputs: Array.from(form.querySelectorAll("input, textarea, select")).map((field) => ({
              tag: field.tagName.toLowerCase(),
              name: field.getAttribute("name"),
              type: field.getAttribute("type"),
              placeholder: field.getAttribute("placeholder"),
            })),
          })),
          options.maxForms
        )
      : [];

    const tables = options.includeTables
      ? take(
          Array.from(document.querySelectorAll("table")).map((table) => ({
            caption: text(table.querySelector("caption")?.textContent),
            rows: Array.from(table.querySelectorAll("tr"))
              .slice(0, options.maxTableRows)
              .map((row) =>
                Array.from(row.querySelectorAll("th, td")).map((cell) => text(cell.textContent))
              ),
          })),
          options.maxTables
        )
      : [];

    const custom = Object.fromEntries(
      Object.entries(options.customSelectors || {}).map(([name, selector]) => {
        const values = Array.from(document.querySelectorAll(selector))
          .slice(0, options.maxCustomMatches)
          .map((node) => ({
            text: text(node.textContent),
            html: text(node.outerHTML),
          }));
        return [name, values];
      })
    );

    return JSON.stringify({
      type: "generic-page",
      title: text(document.title),
      url: location.href,
      html: options.includeHtml ? root.outerHTML : null,
      canonicalUrl: meta?.canonicalUrl || null,
      description: meta?.description || "",
      headings,
      paragraphs,
      bodyText: text(body?.innerText || ""),
      links,
      images,
      meta,
      jsonLd,
      forms,
      tables,
      custom,
    });
  })()`;
}

export async function extractGenericPage(cdp, tabId, options = {}) {
  const payload = await cdp.evaluateJson(
    tabId,
    buildGenericPageExpression(options),
    {
      timeoutMs: options.timeoutMs ?? 60000,
    }
  );

  if (!payload) {
    return null;
  }

  return {
    ...payload,
    title: normalizeWhitespace(payload.title),
    description: truncateText(
      normalizeWhitespace(payload.description),
      options.maxDescriptionLength ?? 4000
    ),
    bodyText: truncateText(
      normalizeWhitespace(payload.bodyText),
      options.maxBodyTextLength ?? 16000
    ),
  };
}
