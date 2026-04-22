import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const thisFile = fileURLToPath(import.meta.url);
const scriptsDir = path.dirname(path.dirname(thisFile));
const defaultPluginRoot = path.resolve(scriptsDir, "..");
const stateDir = path.join(process.env.APPDATA || path.join(os.homedir(), "AppData", "Roaming"), "CodexEdgeControl");
const configPath = path.join(stateDir, "config.json");

export function getPluginRoot() {
  return process.env.EDGE_CONTROL_PLUGIN_ROOT || defaultPluginRoot;
}

export function getStateDir() {
  return stateDir;
}

export function getConfigPath() {
  return configPath;
}

export function getExtensionConfigPath() {
  return path.join(getPluginRoot(), "extension", "config.local.js");
}

export function getDefaultConfig() {
  return {
    host: "127.0.0.1",
    port: 47173,
    authToken: null,
    bridgePath: "/bridge",
  };
}

export function loadConfig() {
  const defaults = getDefaultConfig();
  if (!fs.existsSync(configPath)) {
    return defaults;
  }
  const raw = fs.readFileSync(configPath, "utf8").replace(/^\uFEFF/, "");
  const parsed = JSON.parse(raw);
  return {
    ...defaults,
    ...parsed,
  };
}

export function getBridgeHttpBaseUrl(config = loadConfig()) {
  return `http://${config.host}:${config.port}`;
}

export function getBridgeWsUrl(config = loadConfig()) {
  return `ws://${config.host}:${config.port}${config.bridgePath || "/bridge"}`;
}
