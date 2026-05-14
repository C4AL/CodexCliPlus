/**
 * 主题状态管理
 * 从原项目 src/modules/theme.js 迁移
 */

import { create } from 'zustand';
import { getDesktopBootstrap } from '@/desktop/bridge';
import type { Theme } from '@/types';

type ResolvedTheme = 'light' | 'dark';
type AppliedTheme = 'dark' | 'white';

interface ThemeState {
  theme: Theme;
  resolvedTheme: ResolvedTheme;
  applyDesktopTheme: (theme: Theme, resolvedTheme?: ResolvedTheme, transitionMs?: number) => void;
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

const prefersReducedMotion = (): boolean => {
  return (
    typeof window !== 'undefined' &&
    typeof window.matchMedia === 'function' &&
    window.matchMedia('(prefers-reduced-motion: reduce)').matches
  );
};

const clearThemeTransition = (root: HTMLElement) => {
  if (themeTransitionTimer) {
    window.clearTimeout(themeTransitionTimer);
    themeTransitionTimer = null;
  }

  root.classList.remove('theme-transitioning');
  root.style.removeProperty('--theme-transition-duration');
};

const beginThemeTransition = (transitionMs?: number) => {
  if (typeof document === 'undefined') {
    return;
  }

  const root = document.documentElement;
  const duration = normalizeTransitionMilliseconds(transitionMs);
  if (duration <= 0 || prefersReducedMotion()) {
    clearThemeTransition(root);
    return;
  }

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

const applyTheme = (resolved: AppliedTheme, transitionMs?: number, animate = true) => {
  const root = document.documentElement;
  if (animate) {
    beginThemeTransition(transitionMs);
  }

  if (resolved === 'dark') {
    if (root.getAttribute('data-theme') !== 'dark') {
      root.setAttribute('data-theme', 'dark');
    }
    root.style.colorScheme = 'dark';
    return;
  }

  if (root.getAttribute('data-theme') !== 'white') {
    root.setAttribute('data-theme', 'white');
  }
  root.style.colorScheme = 'light';
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

export const useThemeStore = create<ThemeState>()((set, get) => ({
  theme: 'auto',
  resolvedTheme: 'light',

  applyDesktopTheme: (theme, resolvedTheme, transitionMs = DEFAULT_THEME_TRANSITION_MS) => {
    const selection = resolveThemeSelection(theme, normalizeDesktopResolvedTheme(resolvedTheme));
    const current = get();
    const unchanged =
      current.theme === selection.theme && current.resolvedTheme === selection.resolvedTheme;

    applyTheme(selection.appliedTheme, transitionMs, !unchanged);
    if (unchanged) {
      return;
    }

    set({
      theme: selection.theme,
      resolvedTheme: selection.resolvedTheme,
    });
  },

  initializeTheme: () => {
    const { theme, applyDesktopTheme } = get();
    const desktopBootstrap = getDesktopBootstrap();

    if (desktopBootstrap) {
      applyDesktopTheme(desktopBootstrap.theme || theme, desktopBootstrap.resolvedTheme, 0);
    } else {
      applyDesktopTheme(theme, getSystemTheme(), 0);
    }

    return () => {};
  },
}));
