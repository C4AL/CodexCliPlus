type FloatingSidebarPositioningOptions = {
  floatingElement: HTMLDivElement | null;
  anchorElement: HTMLElement | null;
  workspaceElement: HTMLElement | null;
  enabled: boolean;
};

const clearFloatingStyles = (floatingElement: HTMLDivElement) => {
  floatingElement.style.removeProperty('transform');
  floatingElement.style.removeProperty('width');
  floatingElement.style.removeProperty('max-height');
  floatingElement.style.removeProperty('opacity');
  floatingElement.style.removeProperty('pointer-events');
};

const computeHeaderHeight = () => {
  const header = document.querySelector('.main-header') as HTMLElement | null;
  if (header) return header.getBoundingClientRect().height;

  const raw = getComputedStyle(document.documentElement).getPropertyValue('--header-height');
  const parsed = Number.parseFloat(raw);
  return Number.isFinite(parsed) ? parsed : 64;
};

export function attachFloatingSidebarPositioning({
  floatingElement,
  anchorElement,
  workspaceElement,
  enabled,
}: FloatingSidebarPositioningOptions): (() => void) | undefined {
  if (!floatingElement) return undefined;

  if (!enabled || !anchorElement || !workspaceElement) {
    clearFloatingStyles(floatingElement);
    return undefined;
  }

  let headerHeight = computeHeaderHeight();
  const contentScroller = document.querySelector('.content') as HTMLElement | null;
  let cachedFloatingHeight = floatingElement.getBoundingClientRect().height || 200;
  let frameId = 0;

  const updateFloatingPosition = () => {
    frameId = 0;

    const anchorRect = anchorElement.getBoundingClientRect();
    const workspaceRect = workspaceElement.getBoundingClientRect();
    const stickyTop = headerHeight + 20;
    const viewportPadding = 16;
    const maxTop = workspaceRect.bottom - cachedFloatingHeight;
    const unclampedTop = Math.min(Math.max(anchorRect.top, stickyTop), maxTop);
    const top = Math.max(unclampedTop, viewportPadding);
    const left = Math.max(anchorRect.left, viewportPadding);
    const width = Math.max(
      Math.min(anchorRect.width, window.innerWidth - left - viewportPadding),
      220
    );
    const maxHeight = Math.max(window.innerHeight - top - viewportPadding, 160);
    const isVisible = workspaceRect.bottom > stickyTop + 24 && anchorRect.top < window.innerHeight;

    floatingElement.style.transform = `translate3d(${left}px, ${top}px, 0)`;
    floatingElement.style.width = `${width}px`;
    floatingElement.style.maxHeight = `${maxHeight}px`;
    floatingElement.style.opacity = isVisible ? '1' : '0';
    floatingElement.style.pointerEvents = isVisible ? 'auto' : 'none';
  };

  const requestPositionUpdate = () => {
    if (frameId) cancelAnimationFrame(frameId);
    frameId = requestAnimationFrame(updateFloatingPosition);
  };

  const handleResize = () => {
    headerHeight = computeHeaderHeight();
    cachedFloatingHeight = floatingElement.getBoundingClientRect().height || cachedFloatingHeight;
    requestPositionUpdate();
  };

  requestPositionUpdate();

  window.addEventListener('resize', handleResize);
  window.addEventListener('scroll', requestPositionUpdate, { passive: true });
  contentScroller?.addEventListener('scroll', requestPositionUpdate, { passive: true });

  const resizeObserver =
    typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(requestPositionUpdate);
  resizeObserver?.observe(anchorElement);
  resizeObserver?.observe(workspaceElement);

  return () => {
    if (frameId) cancelAnimationFrame(frameId);
    resizeObserver?.disconnect();
    window.removeEventListener('resize', handleResize);
    window.removeEventListener('scroll', requestPositionUpdate);
    contentScroller?.removeEventListener('scroll', requestPositionUpdate);
    clearFloatingStyles(floatingElement);
  };
}
