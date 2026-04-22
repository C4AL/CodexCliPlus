/// <reference types="vite/client" />

import type { CpadApi } from "../../shared/ipc";

declare global {
  interface Window {
    cpad: CpadApi;
  }
}

export {};
