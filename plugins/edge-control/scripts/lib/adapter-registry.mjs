import { genericHtmlAdapter } from "./adapters/generic-html.mjs";
import { youtubeAdapter } from "./adapters/youtube.mjs";

export class AdapterRegistry {
  constructor(adapters = []) {
    this.adapters = new Map();
    for (const adapter of adapters) {
      this.register(adapter);
    }
  }

  register(adapter) {
    if (!adapter?.id) {
      throw new Error("Cannot register an adapter without an id.");
    }
    this.adapters.set(adapter.id, adapter);
    return this;
  }

  get(id) {
    return this.adapters.get(id) || null;
  }

  getAll() {
    return Array.from(this.adapters.values());
  }

  resolveMany(ids = []) {
    if (!ids.length) {
      return this.getAll();
    }
    return ids.map((id) => {
      const adapter = this.get(id);
      if (!adapter) {
        throw new Error(`Unknown crawler adapter: ${id}`);
      }
      return adapter;
    });
  }

  searchable(ids = []) {
    return this.resolveMany(ids).filter((adapter) => adapter.capabilities?.search);
  }

  match(url) {
    const parsed = typeof url === "string" ? new URL(url) : url;
    for (const adapter of this.getAll()) {
      if (typeof adapter.matchesUrl === "function" && adapter.matchesUrl(parsed)) {
        return adapter;
      }
    }
    return this.get("generic-html");
  }
}

export function createDefaultAdapterRegistry(extraAdapters = []) {
  return new AdapterRegistry([
    youtubeAdapter,
    genericHtmlAdapter,
    ...extraAdapters,
  ]);
}
