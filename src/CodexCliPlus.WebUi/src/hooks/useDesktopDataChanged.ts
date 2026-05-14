import { useEffect, useRef } from 'react';
import type { DesktopDataChangedEvent } from '@/desktop/bridge';

const EVENT_NAME = 'codexcliplus:dataChanged';
const SCOPE_SEPARATOR = '\u001F';

export function useDesktopDataChanged(
  scopes: string[],
  handler: (event: DesktopDataChangedEvent) => void,
  enabled = true
) {
  const handlerRef = useRef(handler);
  const scopeKey = scopes.map((scope) => scope.toLowerCase()).join(SCOPE_SEPARATOR);

  useEffect(() => {
    handlerRef.current = handler;
  }, [handler]);

  useEffect(() => {
    if (!enabled) return;
    const scopeSet = new Set(scopeKey ? scopeKey.split(SCOPE_SEPARATOR) : []);
    const listener = ((event: CustomEvent<DesktopDataChangedEvent>) => {
      const detail = event.detail;
      if (!detail?.scopes?.length) return;
      if (!detail.scopes.some((scope) => scopeSet.has(scope.toLowerCase()))) return;
      handlerRef.current(detail);
    }) as EventListener;

    window.addEventListener(EVENT_NAME, listener);
    return () => window.removeEventListener(EVENT_NAME, listener);
  }, [enabled, scopeKey]);
}
