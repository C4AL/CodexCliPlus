import { useEffect } from 'react';
import type { DesktopDataChangedEvent } from '@/desktop/bridge';

const EVENT_NAME = 'codexcliplus:dataChanged';

export function useDesktopDataChanged(
  scopes: string[],
  handler: (event: DesktopDataChangedEvent) => void,
  enabled = true
) {
  useEffect(() => {
    if (!enabled) return;
    const scopeSet = new Set(scopes.map((scope) => scope.toLowerCase()));
    const listener = ((event: CustomEvent<DesktopDataChangedEvent>) => {
      const detail = event.detail;
      if (!detail?.scopes?.length) return;
      if (!detail.scopes.some((scope) => scopeSet.has(scope.toLowerCase()))) return;
      handler(detail);
    }) as EventListener;

    window.addEventListener(EVENT_NAME, listener);
    return () => window.removeEventListener(EVENT_NAME, listener);
  }, [enabled, handler, scopes]);
}
