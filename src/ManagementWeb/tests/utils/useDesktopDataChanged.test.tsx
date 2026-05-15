import { renderHook } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { useDesktopDataChanged } from '@/hooks/useDesktopDataChanged';

const EVENT_NAME = 'codexcliplus:dataChanged';

const dispatchDataChanged = (scopes: string[]) => {
  window.dispatchEvent(
    new CustomEvent(EVENT_NAME, {
      detail: {
        scopes,
        sequence: 1,
      },
    })
  );
};

describe('useDesktopDataChanged', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('keeps one listener across rerenders with inline scopes and calls the latest handler', () => {
    const addSpy = vi.spyOn(window, 'addEventListener');
    const removeSpy = vi.spyOn(window, 'removeEventListener');
    const firstHandler = vi.fn();
    const secondHandler = vi.fn();

    const { rerender, unmount } = renderHook(
      ({ handler }) => useDesktopDataChanged(['usage'], handler),
      { initialProps: { handler: firstHandler } }
    );

    expect(countEventCalls(addSpy)).toBe(1);
    expect(countEventCalls(removeSpy)).toBe(0);

    rerender({ handler: secondHandler });

    expect(countEventCalls(addSpy)).toBe(1);
    expect(countEventCalls(removeSpy)).toBe(0);

    dispatchDataChanged(['usage']);

    expect(firstHandler).not.toHaveBeenCalled();
    expect(secondHandler).toHaveBeenCalledTimes(1);

    unmount();

    expect(countEventCalls(removeSpy)).toBe(1);
  });

  it('resubscribes only when the enabled flag or scope content changes', () => {
    const addSpy = vi.spyOn(window, 'addEventListener');
    const removeSpy = vi.spyOn(window, 'removeEventListener');
    const handler = vi.fn();

    const { rerender, unmount } = renderHook(
      ({ enabled, scopes }) => useDesktopDataChanged(scopes, handler, enabled),
      { initialProps: { enabled: true, scopes: ['usage'] } }
    );

    rerender({ enabled: true, scopes: ['usage'] });

    expect(countEventCalls(addSpy)).toBe(1);
    expect(countEventCalls(removeSpy)).toBe(0);

    rerender({ enabled: true, scopes: ['config'] });
    dispatchDataChanged(['usage']);
    dispatchDataChanged(['config']);

    expect(countEventCalls(addSpy)).toBe(2);
    expect(countEventCalls(removeSpy)).toBe(1);
    expect(handler).toHaveBeenCalledTimes(1);

    rerender({ enabled: false, scopes: ['config'] });
    dispatchDataChanged(['config']);

    expect(countEventCalls(removeSpy)).toBe(2);
    expect(handler).toHaveBeenCalledTimes(1);

    unmount();

    expect(countEventCalls(removeSpy)).toBe(2);
  });
});

function countEventCalls(spy: ReturnType<typeof vi.spyOn>) {
  return spy.mock.calls.filter(([eventName]) => eventName === EVENT_NAME).length;
}
