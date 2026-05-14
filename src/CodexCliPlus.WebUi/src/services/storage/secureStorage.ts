import { isDesktopMode } from '@/desktop/bridge';
import { obfuscateData, deobfuscateData, isObfuscated } from '@/utils/encryption';

interface StorageOptions {
  obfuscate?: boolean;
  encrypt?: boolean;
}

class ObfuscatedStorageService {
  private shouldBlockDesktopKey(key: string): boolean {
    return isDesktopMode() && key === 'managementKey';
  }

  setItem(key: string, value: unknown, options: StorageOptions = {}): void {
    if (this.shouldBlockDesktopKey(key)) {
      localStorage.removeItem(key);
      return;
    }

    const obfuscate = options.obfuscate ?? options.encrypt ?? true;

    if (value === null || value === undefined) {
      this.removeItem(key);
      return;
    }

    const stringValue = JSON.stringify(value);
    const storedValue = obfuscate ? obfuscateData(stringValue) : stringValue;

    localStorage.setItem(key, storedValue);
  }

  getItem<T = unknown>(key: string, options: StorageOptions = {}): T | null {
    if (this.shouldBlockDesktopKey(key)) {
      return null;
    }

    const obfuscate = options.obfuscate ?? options.encrypt ?? true;
    const raw = localStorage.getItem(key);
    if (raw === null) {
      return null;
    }

    try {
      const decrypted = obfuscate ? deobfuscateData(raw) : raw;
      return JSON.parse(decrypted) as T;
    } catch {
      try {
        if (obfuscate && isObfuscated(raw)) {
          return deobfuscateData(raw) as T;
        }

        return raw as T;
      } catch {
        return null;
      }
    }
  }

  removeItem(key: string): void {
    localStorage.removeItem(key);
  }

  clear(): void {
    localStorage.clear();
  }

  migratePlaintextKeys(keys: string[]): void {
    keys.forEach((key) => {
      if (this.shouldBlockDesktopKey(key)) {
        localStorage.removeItem(key);
        return;
      }

      const raw = localStorage.getItem(key);
      if (!raw || raw.startsWith('enc::v1::')) {
        return;
      }

      let parsed: unknown;
      try {
        parsed = JSON.parse(raw);
      } catch {
        parsed = raw;
      }

      try {
        this.setItem(key, parsed);
      } catch (error) {
        console.warn(`Failed to migrate key "${key}":`, error);
      }
    });
  }

  hasItem(key: string): boolean {
    if (this.shouldBlockDesktopKey(key)) {
      return false;
    }

    return localStorage.getItem(key) !== null;
  }
}

export const obfuscatedStorage = new ObfuscatedStorageService();
export const secureStorage = obfuscatedStorage;
