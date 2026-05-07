import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

function setMatchMedia(options: { dark?: boolean; reducedMotion?: boolean } = {}) {
  Object.defineProperty(window, 'matchMedia', {
    configurable: true,
    writable: true,
    value: vi.fn().mockImplementation((query: string) => ({
      matches: query.includes('prefers-reduced-motion')
        ? options.reducedMotion === true
        : options.dark === true,
      media: query,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      addListener: vi.fn(),
      removeListener: vi.fn(),
      dispatchEvent: vi.fn(),
    })),
  });
}

async function loadThemeStore() {
  vi.resetModules();
  const { useThemeStore } = await import('@/stores/useThemeStore');
  return useThemeStore;
}

function resetRootThemeState() {
  document.documentElement.removeAttribute('data-theme');
  document.documentElement.classList.remove('theme-transitioning');
  document.documentElement.style.removeProperty('--theme-transition-duration');
  document.documentElement.style.removeProperty('color-scheme');
}

describe('useThemeStore desktop theme sync', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    localStorage.clear();
    resetRootThemeState();
    setMatchMedia();
  });

  afterEach(() => {
    vi.clearAllTimers();
    vi.useRealTimers();
    Reflect.deleteProperty(window, '__CODEXCLIPLUS_DESKTOP_BRIDGE__');
    resetRootThemeState();
    vi.resetModules();
    vi.restoreAllMocks();
  });

  it('uses the desktop resolved theme and removes the short transition class on time', async () => {
    const useThemeStore = await loadThemeStore();

    useThemeStore.getState().applyDesktopTheme('auto', 'dark', 180);

    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
    expect(document.documentElement.style.colorScheme).toBe('dark');
    expect(document.documentElement.classList.contains('theme-transitioning')).toBe(true);
    expect(document.documentElement.style.getPropertyValue('--theme-transition-duration')).toBe(
      '180ms'
    );
    expect(useThemeStore.getState()).toMatchObject({
      theme: 'auto',
      resolvedTheme: 'dark',
    });

    await vi.advanceTimersByTimeAsync(180);

    expect(document.documentElement.classList.contains('theme-transitioning')).toBe(false);
    expect(document.documentElement.style.getPropertyValue('--theme-transition-duration')).toBe('');
  });

  it('lets the desktop resolved theme override WebView matchMedia while in auto mode', async () => {
    setMatchMedia({ dark: true });
    const useThemeStore = await loadThemeStore();

    useThemeStore.getState().applyDesktopTheme('auto', 'light', 180);

    expect(document.documentElement.getAttribute('data-theme')).toBe('white');
    expect(document.documentElement.style.colorScheme).toBe('light');
    expect(useThemeStore.getState()).toMatchObject({
      theme: 'auto',
      resolvedTheme: 'light',
    });
  });

  it('initializes from the desktop bootstrap resolved theme before React state changes', async () => {
    Object.assign(window, {
      __CODEXCLIPLUS_DESKTOP_BRIDGE__: {
        isDesktopMode: () => true,
        consumeBootstrap: vi.fn(() => ({
          desktopMode: true,
          apiBase: 'http://127.0.0.1:15345',
          desktopSessionId: 'desktop-session-1',
          theme: 'auto',
          resolvedTheme: 'dark',
        })),
      },
    });
    const useThemeStore = await loadThemeStore();

    const cleanup = useThemeStore.getState().initializeTheme();

    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
    expect(document.documentElement.style.colorScheme).toBe('dark');
    expect(document.documentElement.classList.contains('theme-transitioning')).toBe(false);
    expect(useThemeStore.getState()).toMatchObject({
      theme: 'auto',
      resolvedTheme: 'dark',
    });

    cleanup();
  });

  it('does not restart transition for duplicate desktop theme commands', async () => {
    const useThemeStore = await loadThemeStore();

    useThemeStore.getState().applyDesktopTheme('dark', 'dark', 180);
    await vi.advanceTimersByTimeAsync(180);

    useThemeStore.getState().applyDesktopTheme('dark', 'dark', 180);

    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
    expect(document.documentElement.classList.contains('theme-transitioning')).toBe(false);
    expect(document.documentElement.style.getPropertyValue('--theme-transition-duration')).toBe('');
  });

  it('disables transition when the desktop WebView prefers reduced motion', async () => {
    setMatchMedia({ reducedMotion: true });
    const useThemeStore = await loadThemeStore();

    useThemeStore.getState().applyDesktopTheme('dark', 'dark', 180);

    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
    expect(document.documentElement.classList.contains('theme-transitioning')).toBe(false);
    expect(document.documentElement.style.getPropertyValue('--theme-transition-duration')).toBe('');
  });
});
