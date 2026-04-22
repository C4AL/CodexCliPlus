import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const version = process.argv[2];

if (!/^\d+\.\d+\.\d+$/.test(version || "")) {
  throw new Error("Usage: node sync-version.mjs <semver>");
}

const scriptsDir = path.dirname(fileURLToPath(import.meta.url));
const pluginRoot = path.resolve(scriptsDir, "..");

function readJson(relativePath) {
  const filePath = path.join(pluginRoot, relativePath);
  return JSON.parse(fs.readFileSync(filePath, "utf8").replace(/^\uFEFF/, ""));
}

function writeJson(relativePath, value) {
  const filePath = path.join(pluginRoot, relativePath);
  fs.writeFileSync(filePath, `${JSON.stringify(value, null, 2)}\n`, "utf8");
}

const manifest = readJson("extension/manifest.json");
manifest.version = version;
writeJson("extension/manifest.json", manifest);

const pluginJson = readJson(".codex-plugin/plugin.json");
pluginJson.version = version;
writeJson(".codex-plugin/plugin.json", pluginJson);

const packageJson = readJson("scripts/package.json");
packageJson.version = version;
writeJson("scripts/package.json", packageJson);

const packageLock = readJson("scripts/package-lock.json");
packageLock.version = version;
if (packageLock.packages?.[""]) {
  packageLock.packages[""].version = version;
}
writeJson("scripts/package-lock.json", packageLock);

const mcpServerPath = path.join(pluginRoot, "scripts", "mcp-server.mjs");
const mcpServerText = fs.readFileSync(mcpServerPath, "utf8");
const nextMcpServerText = mcpServerText.replace(/version:\s*"[^"]+"/, `version: "${version}"`);
if (nextMcpServerText === mcpServerText) {
  throw new Error("Could not update MCP server version.");
}
fs.writeFileSync(mcpServerPath, nextMcpServerText, "utf8");

process.stdout.write(`${version}\n`);
