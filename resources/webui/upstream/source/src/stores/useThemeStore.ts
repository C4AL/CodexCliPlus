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
  applyDesktopTheme: (
    theme: Theme,
    resolvedTheme?: ResolvedTheme,
    transitionMs?: number
  ) => void;
  cycleTheme: () => void;
  initializeTheme: () => () => void;
}

const DEFAULT_THEME_TRANSITION_MS = 180;
let themeTransitionTimer: ReturnType<typeof window.setTimeout> | null = null;

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

const normalizeDesktopResolvedTheme = (theme: unknown): ResolvedTheme | undefined => {
  return theme === 'dark' || theme === 'light' ? theme : undefined;
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

const resolveTheme = (theme: Theme, desktopResolvedTheme?: ResolvedTheme): AppliedTheme => {
  const normalized = normalizeThemeValue(theme);
  if (normalized === 'auto') {
    if (desktopResolvedTheme) {
      return desktopResolvedTheme === 'dark' ? 'dark' : 'white';
    }
    return resolveAutoTheme();
  }
  if (normalized === 'white') {
    return 'white';
  }
  return normalized;
};

const normalizeTransitionMilliseconds = (transitionMs?: number): number => {
  if (typeof transitionMs !== 'number' || !Number.isFinite(transitionMs)) {
    return 0;
  }

  return Math.max(0, Math.min(1_000, Math.round(transitionMs)));
};

const beginThemeTransition = (transitionMs?: number) => {
  if (typeof document === 'undefined') {
    return;
  }

  const duration = normalizeTransitionMilliseconds(transitionMs);
  if (duration <= 0) {
    return;
  }

  const root = document.documentElement;
  root.style.setProperty('--theme-transition-duration', `${duration}ms`);
  root.classList.add('theme-transitioning');

  if (themeTransitionTimer) {
    window.clearTimeout(themeTransitionTimer);
  }

  themeTransitionTimer = window.setTimeout(() => {
    root.classList.remove('theme-transitioning');
    root.style.removeProperty('--theme-transition-duration');
    themeTransitionTimer = null;
  }, duration);
};

const applyTheme = (resolved: AppliedTheme, transitionMs?: number) => {
  beginThemeTransition(transitionMs);

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

const resolveThemeSelection = (
  theme: Theme,
  desktopResolvedTheme?: ResolvedTheme
): { theme: Theme; appliedTheme: AppliedTheme; resolvedTheme: ResolvedTheme } => {
  const normalizedTheme = normalizeThemeValue(theme);
  const appliedTheme = resolveTheme(normalizedTheme, desktopResolvedTheme);
  return {
    theme: normalizedTheme,
    appliedTheme,
    resolvedTheme: normalizeResolvedTheme(appliedTheme),
  };
};

export const useThemeStore = create<ThemeState>()(
  persist(
    (set, get) => ({
      theme: 'auto',
      resolvedTheme: 'light',

      setTheme: (theme) => {
        const selection = resolveThemeSelection(theme);
        applyTheme(selection.appliedTheme);
        const current = get();
        if (
          current.theme === selection.theme &&
          current.resolvedTheme === selection.resolvedTheme
        ) {
          return;
        }

        set({
          theme: selection.theme,
          resolvedTheme: selection.resolvedTheme,
        });
      },

      applyDesktopTheme: (theme, resolvedTheme, transitionMs = DEFAULT_THEME_TRANSITION_MS) => {
        const selection = resolveThemeSelection(
          theme,
          normalizeDesktopResolvedTheme(resolvedTheme)
        );
        applyTheme(selection.appliedTheme, transitionMs);
        const current = get();
        if (
          current.theme === selection.theme &&
          current.resolvedTheme === selection.resolvedTheme
        ) {
          return;
        }

        set({
          theme: selection.theme,
          resolvedTheme: selection.resolvedTheme,
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
        const { theme, setTheme, applyDesktopTheme } = get();
        const desktopBootstrap = getDesktopBootstrap();

        // 应用已保存的主题
        if (desktopBootstrap) {
          applyDesktopTheme(
            desktopBootstrap.theme || theme,
            desktopBootstrap.resolvedTheme,
            0
          );
        } else {
          setTheme(theme);
        }

        // 监听系统主题变化（仅在 auto 模式下生效）
        if (desktopBootstrap || !window.matchMedia) {
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
