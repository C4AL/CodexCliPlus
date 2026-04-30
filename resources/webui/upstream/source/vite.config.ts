import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import path from 'path';
import { execFileSync } from 'child_process';
import fs from 'fs';

function tryReadVendoredCommit(): string | null {
  try {
    const syncPath = path.resolve(__dirname, '../sync.json');
    const raw = fs.readFileSync(syncPath, 'utf8');
    const parsed = JSON.parse(raw) as { upstreamCommit?: string };
    return parsed.upstreamCommit?.trim() || null;
  } catch {
    return null;
  }
}

function tryRunGit(args: string[]): string | null {
  try {
    const value = execFileSync('git', args, {
      cwd: __dirname,
      encoding: 'utf8',
      stdio: ['ignore', 'pipe', 'ignore'],
    }).trim();
    return value || null;
  } catch {
    return null;
  }
}

// Get version from environment, git tag, or package.json
function getVersion(): string {
  // 1. Environment variable (set by GitHub Actions)
  if (process.env.VERSION) {
    return process.env.VERSION;
  }

  // 2. Vendored upstream commit metadata
  const vendoredCommit = tryReadVendoredCommit();
  if (vendoredCommit) {
    return `upstream-${vendoredCommit.slice(0, 8)}`;
  }

  // 3. Try git tag
  const gitTag =
    tryRunGit(['describe', '--tags', '--exact-match']) ?? tryRunGit(['describe', '--tags']);
  if (gitTag) {
    return gitTag;
  }

  // 4. Fall back to package.json version
  try {
    const pkg = JSON.parse(fs.readFileSync(path.resolve(__dirname, 'package.json'), 'utf8'));
    if (pkg.version && pkg.version !== '0.0.0') {
      return pkg.version;
    }
  } catch {
    // package.json not readable
  }

  return 'dev';
}

const manualChunkGroups = [
  {
    name: 'charts',
    packages: ['chart.js', 'react-chartjs-2'],
  },
  {
    name: 'editor',
    packages: [
      '@uiw/react-codemirror',
      '@codemirror/lang-yaml',
      '@codemirror/merge',
      '@codemirror/search',
      '@codemirror/state',
      '@codemirror/view',
      'yaml',
    ],
  },
];

function getManualChunkName(id: string): string | undefined {
  const normalizedId = id.replace(/\\/g, '/');
  if (!normalizedId.includes('/node_modules/')) {
    return undefined;
  }

  for (const group of manualChunkGroups) {
    if (group.packages.some((pkg) => normalizedId.includes(`/node_modules/${pkg}/`))) {
      return group.name;
    }
  }

  return undefined;
}

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [react()],
  define: {
    __APP_VERSION__: JSON.stringify(getVersion()),
  },
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  css: {
    modules: {
      localsConvention: 'camelCase',
      generateScopedName: '[name]__[local]___[hash:base64:5]',
    },
    preprocessorOptions: {
      scss: {
        additionalData: `@use "@/styles/variables.scss" as *;`,
      },
    },
  },
  build: {
    target: 'es2020',
    outDir: path.resolve(__dirname, '../dist'),
    emptyOutDir: true,
    assetsInlineLimit: 4096,
    chunkSizeWarningLimit: 900,
    cssCodeSplit: true,
    rollupOptions: {
      output: {
        manualChunks: getManualChunkName,
      },
    },
  },
});
