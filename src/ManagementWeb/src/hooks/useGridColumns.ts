import { useState, useEffect, useCallback } from 'react';

/**
 * Hook to calculate the number of grid columns based on container width and item min-width.
 * Returns [columns, refCallback].
 */
export function useGridColumns(
  itemMinWidth: number,
  gap: number = 16
): [number, (node: HTMLDivElement | null) => void] {
  const [columns, setColumns] = useState(1);
  const [element, setElement] = useState<HTMLDivElement | null>(null);

  const refCallback = useCallback((node: HTMLDivElement | null) => {
    setElement(node);
  }, []);

  useEffect(() => {
    if (!element) return;

    let animationFrame: number | null = null;
    const updateColumns = () => {
      const containerWidth = element.clientWidth;
      const effectiveItemWidth = itemMinWidth + gap;
      const count = Math.floor((containerWidth + gap) / effectiveItemWidth);
      const nextColumns = Math.max(1, count);
      setColumns((current) => (current === nextColumns ? current : nextColumns));
    };
    const scheduleUpdate = () => {
      if (animationFrame !== null) return;
      animationFrame = window.requestAnimationFrame(() => {
        animationFrame = null;
        updateColumns();
      });
    };

    updateColumns();

    const observer =
      typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(scheduleUpdate);
    observer?.observe(element);
    window.addEventListener('resize', scheduleUpdate);

    return () => {
      if (animationFrame !== null) {
        window.cancelAnimationFrame(animationFrame);
      }
      observer?.disconnect();
      window.removeEventListener('resize', scheduleUpdate);
    };
  }, [element, itemMinWidth, gap]);

  return [columns, refCallback];
}
