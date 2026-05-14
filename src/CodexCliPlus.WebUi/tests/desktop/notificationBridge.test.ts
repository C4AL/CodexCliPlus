import { afterEach, describe, expect, it, vi } from 'vitest';
import type { NotificationType } from '@/types';

async function loadNotificationBridge(
  showShellNotification?: (message: string, type?: NotificationType) => boolean | void
) {
  vi.resetModules();
  Reflect.deleteProperty(window, '__CODEXCLIPLUS_DESKTOP_BRIDGE__');
  if (showShellNotification) {
    Object.assign(window, {
      __CODEXCLIPLUS_DESKTOP_BRIDGE__: {
        showShellNotification,
      },
    });
  }

  const bridge = await import('@/desktop/bridge');
  return bridge.showShellNotification;
}

describe('notification desktop bridge', () => {
  afterEach(() => {
    Reflect.deleteProperty(window, '__CODEXCLIPLUS_DESKTOP_BRIDGE__');
    vi.resetModules();
  });

  it('forwards normalized shell notification payloads', async () => {
    const calls: Array<{ message: string; type?: NotificationType }> = [];
    const showShellNotification = await loadNotificationBridge((message, type) => {
      calls.push({ message, type });
      return true;
    });

    expect(showShellNotification('  需要处理  ', 'warning')).toBe(true);

    expect(calls).toEqual([{ message: '需要处理', type: 'warning' }]);
  });

  it('normalizes invalid notification levels to info', async () => {
    const calls: Array<{ message: string; type?: NotificationType }> = [];
    const showShellNotification = await loadNotificationBridge((message, type) => {
      calls.push({ message, type });
      return true;
    });

    expect(showShellNotification('状态已更新', 'invalid' as NotificationType)).toBe(true);

    expect(calls).toEqual([{ message: '状态已更新', type: 'info' }]);
  });

  it('returns false when desktop notification bridge is unavailable', async () => {
    const showShellNotification = await loadNotificationBridge();

    expect(showShellNotification('状态已更新', 'success')).toBe(false);
  });
});
