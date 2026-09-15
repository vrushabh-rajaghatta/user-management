import type { MouseEvent } from "react";
import { Link, Outlet } from "react-router";

/**
 * Moves focus to the main content explicitly. Following "#main" alone leaves
 * focus behaviour to the browser, and some do not move it.
 */
function skipToMain(event: MouseEvent<HTMLAnchorElement>) {
  event.preventDefault();
  document.getElementById("main")?.focus();
}

/**
 * The shell for signed-in pages. Built with the foundation and first mounted
 * with the first authenticated route; until a sign-in route exists, there is
 * nothing for it to protect (docs/frontend-architecture.md §5).
 *
 * The skip link comes first, so keyboard users can bypass the header on every
 * page (WCAG 2.4.1).
 */
export function AppShell() {
  return (
    <div className="flex min-h-svh flex-col">
      <a
        href="#main"
        onClick={skipToMain}
        className="sr-only focus:not-sr-only focus:absolute focus:top-4 focus:left-4 focus:z-50 focus:rounded-md focus:bg-background focus:px-3 focus:py-2"
      >
        Skip to content
      </a>
      <header className="border-b">
        <div className="mx-auto flex h-14 w-full max-w-5xl items-center px-4 sm:px-6">
          <Link to="/" className="font-semibold">
            Ligature
          </Link>
        </div>
      </header>
      <main id="main" tabIndex={-1} className="flex-1">
        <Outlet />
      </main>
    </div>
  );
}
