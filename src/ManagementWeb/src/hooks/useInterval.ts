/**
 * 定时器 Hook
 */

import { useEffect, useRef } from 'react';

export function useInterval(
  callback: () => void,
  delay: number | null,
  options: { pauseWhenHidden?: boolean } = {}
) {
  const savedCallback = useRef<(() => void) | null>(null);
  const pauseWhenHidden = options.pauseWhenHidden ?? true;

  useEffect(() => {
    savedCallback.current = callback;
  }, [callback]);

  useEffect(() => {
    if (delay === null) return;

    const tick = () => {
      if (pauseWhenHidden && typeof document !== 'undefined' && document.hidden) {
        return;
      }
      savedCallback.current?.();
    };

    const id = setInterval(tick, delay);
    return () => clearInterval(id);
  }, [delay, pauseWhenHidden]);
}
