/**
 * 主题状态管理
 * 从原项目 src/modules/theme.js 迁移
 */

import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import { getDesktopBootstrap } from '@/desktop/bridge';
import type { Theme } from '@/types';
import { STORAGE_KEY_THEME } from '@/utils/constants';

type ResolvedTheme = 'light' | 'dark';
type AppliedTheme = ResolvedTheme | 'white';

interface ThemeState {
  theme: Theme;
  resolvedTheme: ResolvedTheme;
  setTheme: (theme: Theme) => void;
  cycleTheme: () => void;
  initializeTheme: () => () => void;
}

const getSystemTheme = (): ResolvedTheme => {
  if (window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches) {
    return 'dark';
  }
  return 'light';
};

const resolveAutoTheme = (): AppliedTheme => {
  return getSystemTheme() === 'dark' ? 'dark' : 'white';
};

const normalizeResolvedTheme = (theme: AppliedTheme): ResolvedTheme => {
  return theme === 'dark' ? 'dark' : 'light';
};

const normalizeThemeValue = (theme: unknown): Theme => {
  if (theme === 'dark' || theme === 'white' || theme === 'auto') {
    return theme;
  }
  if (theme === 'light') {
    return 'white';
  }
  return 'auto';
};

const resolveTheme = (theme: Theme): AppliedTheme => {
  const normalized = normalizeThemeValue(theme);
  if (normalized === 'auto') {
    return resolveAutoTheme();
  }
  if (normalized === 'white') {
    return 'white';
  }
  return normalized;
};

const applyTheme = (resolved: AppliedTheme) => {
  const root = document.documentElement;
  if (resolved === 'dark') {
    if (root.getAttribute('data-theme') !== 'dark') {
      root.setAttribute('data-theme', 'dark');
    }
    return;
  }

  if (resolved === 'white') {
    if (root.getAttribute('data-theme') !== 'white') {
      root.setAttribute('data-theme', 'white');
    }
    return;
  }

  if (root.hasAttribute('data-theme')) {
    root.removeAttribute('data-theme');
  }
};

export const useThemeStore = create<ThemeState>()(
  persist(
    (set, get) => ({
      theme: 'auto',
      resolvedTheme: 'light',

      setTheme: (theme) => {
        const normalizedTheme = normalizeThemeValue(theme);
        const resolved = resolveTheme(normalizedTheme);
        const normalizedResolved = normalizeResolvedTheme(resolved);
        applyTheme(resolved);
        const current = get();
        if (
          current.theme === normalizedTheme &&
          current.resolvedTheme === normalizedResolved
        ) {
          return;
        }

        set({
          theme: normalizedTheme,
          resolvedTheme: normalizedResolved,
        });
      },

      cycleTheme: () => {
        const { theme, setTheme } = get();
        const order: Theme[] = ['auto', 'white', 'dark'];
        const currentIndex = order.indexOf(theme);
        const nextTheme = order[(currentIndex + 1) % order.length];
        setTheme(nextTheme);
      },

      initializeTheme: () => {
        const { theme, setTheme } = get();
        const desktopTheme = getDesktopBootstrap()?.theme;

        // 应用已保存的主题
        setTheme(desktopTheme || theme);

        // 监听系统主题变化（仅在 auto 模式下生效）
        if (!window.matchMedia) {
          return () => {};
        }

        const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)');
        const listener = () => {
          const { theme: currentTheme } = get();
          if (currentTheme === 'auto') {
            const resolved = resolveAutoTheme();
            const normalizedResolved = normalizeResolvedTheme(resolved);
            applyTheme(resolved);
            if (get().resolvedTheme !== normalizedResolved) {
              set({ resolvedTheme: normalizedResolved });
            }
          }
        };

        mediaQuery.addEventListener('change', listener);

        return () => mediaQuery.removeEventListener('change', listener);
      },
    }),
    {
      name: STORAGE_KEY_THEME,
      merge: (persistedState, currentState) => {
        const persisted = persistedState as Partial<ThemeState> | undefined;
        return {
          ...currentState,
          ...persisted,
          theme: normalizeThemeValue(persisted?.theme),
        };
      },
    }
  )
);
