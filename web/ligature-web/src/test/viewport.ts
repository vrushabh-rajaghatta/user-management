/**
 * The viewport, for components whose behaviour depends on it.
 *
 * jsdom lays nothing out and has no matchMedia, so the vendored use-mobile hook
 * (docs/frontend-architecture.md §14) would throw. This stub answers media
 * queries from window.innerWidth, which is what that hook itself reads, so a
 * test changes the width once and both agree.
 *
 * Only max-width and min-width queries are understood. Anything else answers
 * false, rather than pretending to evaluate it.
 */
export function installViewport(): void {
  Object.defineProperty(window, "matchMedia", {
    configurable: true,
    writable: true,
    value: (query: string): MediaQueryList => ({
      media: query,
      get matches() {
        return evaluate(query, window.innerWidth);
      },
      onchange: null,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
      addListener: () => undefined,
      removeListener: () => undefined,
      dispatchEvent: () => false,
    }),
  });
}

/** jsdom's default, which is a desktop width. */
export const DESKTOP_WIDTH = 1024;

export function setViewportWidth(width: number): void {
  Object.defineProperty(window, "innerWidth", { configurable: true, writable: true, value: width });
}

function evaluate(query: string, width: number): boolean {
  const max = /max-width:\s*(\d+)px/.exec(query);
  if (max !== null) {
    return width <= Number(max[1]);
  }

  const min = /min-width:\s*(\d+)px/.exec(query);
  if (min !== null) {
    return width >= Number(min[1]);
  }

  return false;
}
