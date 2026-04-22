import fs from "node:fs/promises";
import path from "node:path";
import { BrowserCrawler } from "./lib/browser-crawler.mjs";

function formatError(error) {
  if (Array.isArray(error?.issues) && error.issues.length) {
    return error.issues
      .map((issue) => `${issue.path.join(".") || "<root>"}: ${issue.message}`)
      .join("\n");
  }
  return error?.stack || error?.message || String(error);
}

async function main() {
  const args = process.argv.slice(2);
  const planOnly = args.includes("--plan");
  const jsonPath = args.find((item) => !item.startsWith("--"));

  if (!jsonPath) {
    throw new Error("Usage: node run-crawl-job.mjs <job.json> [--plan]");
  }

  const absolutePath = path.resolve(process.cwd(), jsonPath);
  const raw = (await fs.readFile(absolutePath, "utf8")).replace(/^\uFEFF/, "");
  const job = JSON.parse(raw);
  const crawler = new BrowserCrawler();

  if (planOnly) {
    console.log(JSON.stringify(crawler.plan(job), null, 2));
    return;
  }

  const result = await crawler.run(job);
  console.log(JSON.stringify(result, null, 2));
}

try {
  await main();
} catch (error) {
  console.error(formatError(error));
  process.exitCode = 1;
}
