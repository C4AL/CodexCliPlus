import { afterEach, describe, expect, it, vi } from 'vitest';

const bridgeMocks = vi.hoisted(() => ({
  showShellNotification: vi.fn(),
}));

vi.mock('@/api/desktopBridge', () => ({
  showShellNotification: bridgeMocks.showShellNotification,
}));

describe('useNotificationStore desktop notifications', () => {
  afterEach(async () => {
    bridgeMocks.showShellNotification.mockReset();
    const { useNotificationStore } = await import('@/state/useNotificationStore');
    useNotificationStore.getState().clearAll();
  });

  it('forwards notifications to the desktop shell without queueing WebUI notifications', async () => {
    const { useNotificationStore } = await import('@/state/useNotificationStore');

    useNotificationStore.getState().showNotification('需要处理', 'warning');

    expect(bridgeMocks.showShellNotification).toHaveBeenCalledWith('需要处理', 'warning');
    expect(useNotificationStore.getState().notifications).toEqual([]);
  });
});
