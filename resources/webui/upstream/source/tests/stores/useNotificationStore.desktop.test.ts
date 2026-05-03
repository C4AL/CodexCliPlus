import { afterEach, describe, expect, it, vi } from 'vitest';

const bridgeMocks = vi.hoisted(() => ({
  showShellNotification: vi.fn(),
}));

vi.mock('@/desktop/bridge', () => ({
  showShellNotification: bridgeMocks.showShellNotification,
}));

describe('useNotificationStore desktop notifications', () => {
  afterEach(async () => {
    bridgeMocks.showShellNotification.mockReset();
    const { useNotificationStore } = await import('@/stores/useNotificationStore');
    useNotificationStore.getState().clearAll();
  });

  it('forwards notifications to the desktop shell without queueing WebUI notifications', async () => {
    const { useNotificationStore } = await import('@/stores/useNotificationStore');

    useNotificationStore.getState().showNotification('需要处理', 'warning');

    expect(bridgeMocks.showShellNotification).toHaveBeenCalledWith('需要处理', 'warning');
    expect(useNotificationStore.getState().notifications).toEqual([]);
  });
});
