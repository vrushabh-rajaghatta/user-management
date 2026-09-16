import type { MouseEvent, ReactNode } from "react";
import { Link, NavLink, Outlet } from "react-router";
import { useAuthSession } from "@/shared/auth/useAuthSession";
import type { NavigationSection } from "./navigation";

/**
 * Moves focus to the main content explicitly. Following "#main" alone leaves
 * focus behaviour to the browser, and some do not move it.
 */
function skipToMain(event: MouseEvent<HTMLAnchorElement>) {
  event.preventDefault();
  document.getElementById("main")?.focus();
}

/**
 * One navigation section, with the entries this caller may see.
 *
 * Its own component so the filtering can use a hook, and so a section whose
 * entries are all hidden renders nothing rather than a heading over an empty
 * list. `can` comes from useAuthSession, never from global state (§9), and
 * answers "should this be visible", never "is this permitted".
 */
function Section({ section }: { readonly section: NavigationSection }) {
  const { can } = useAuthSession();

  const visible = section.items.filter((item) => item.permission === undefined || can(item.permission));

  if (visible.length === 0) {
    return null;
  }

  return (
    <div className="flex flex-col gap-1">
      <p className="text-xs font-medium tracking-wide text-muted-foreground uppercase">{section.label}</p>
      <ul className="flex flex-col gap-1">
        {visible.map((item) => (
          <li key={item.to}>
            <NavLink
              to={item.to}
              className={({ isActive }) =>
                `block rounded-md px-2 py-1 text-sm hover:bg-muted ${isActive ? "bg-muted font-medium" : ""}`
              }
            >
              {item.label}
            </NavLink>
          </li>
        ))}
      </ul>
    </div>
  );
}

interface AppShellProps {
  /**
   * Header actions, supplied by the composition root. The shell is shared
   * infrastructure and may not import a module (docs/frontend-architecture.md
   * §2), so it offers the slot and app/ fills it with the auth module's sign-out
   * control rather than the shell reaching for it.
   */
  readonly actions?: ReactNode;

  /** The navigation area, composed by app/ from what each module exports. */
  readonly navigation?: readonly NavigationSection[];
}

/**
 * The shell for signed-in pages. The skip link comes first, so keyboard users
 * can bypass the header and the navigation on every page (WCAG 2.4.1).
 */
export function AppShell({ actions, navigation }: AppShellProps) {
  const sections = navigation ?? [];

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
      <div className="mx-auto flex w-full max-w-5xl flex-1 flex-col gap-6 sm:flex-row">
        {sections.length === 0 ? null : (
          <nav aria-label="Main" className="flex flex-col gap-4 px-4 pt-8 sm:w-48 sm:shrink-0 sm:px-6">
            {sections.map((section) => (
              <Section key={section.label} section={section} />
            ))}
          </nav>
        )}
        <main id="main" tabIndex={-1} className="flex-1">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
