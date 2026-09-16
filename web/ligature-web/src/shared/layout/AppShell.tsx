import type { MouseEvent, ReactNode } from "react";
import { Link, Outlet } from "react-router";

/**
 * Moves focus to the main content explicitly. Following "#main" alone leaves
 * focus behaviour to the browser, and some do not move it.
 */
function skipToMain(event: MouseEvent<HTMLAnchorElement>) {
  event.preventDefault();
  document.getElementById("main")?.focus();
}

interface AppShellProps {
  /**
   * Header actions, supplied by the composition root. The shell is shared
   * infrastructure and may not import a module (docs/frontend-architecture.md
   * §2), so it offers the slot and app/ fills it with the auth module's sign-out
   * control rather than the shell reaching for it.
   */
  readonly actions?: ReactNode;
}

/**
 * The shell for signed-in pages. The skip link comes first, so keyboard users
 * can bypass the header on every page (WCAG 2.4.1).
 */
export function AppShell({ actions }: AppShellProps) {
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
        <div className="mx-auto flex h-14 w-full max-w-5xl items-center justify-between gap-4 px-4 sm:px-6">
          <Link to="/" className="font-semibold">
            Ligature
          </Link>
          {actions === undefined ? null : <div className="flex shrink-0 items-center gap-2">{actions}</div>}
        </div>
      </header>
      <main id="main" tabIndex={-1} className="flex-1">
        <Outlet />
      </main>
    </div>
  );
}
