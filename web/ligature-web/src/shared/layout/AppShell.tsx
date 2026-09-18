import type { MouseEvent, ReactNode } from "react";
import { Link, Outlet } from "react-router";
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarGroup,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarInset,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarProvider,
  SidebarTrigger,
  useSidebar,
} from "@/components/ui/sidebar";
import { useCallerDisplayName } from "@/shared/auth/useCallerDisplayName";
import type { NavigationGroup } from "./navigation";
import { NavigationEntry } from "./NavigationEntry";
import { useVisibleItems } from "./useVisibleItems";

/**
 * Moves focus to the main content explicitly. Following "#main" alone leaves
 * focus behaviour to the browser, and some do not move it.
 */
function skipToMain(event: MouseEvent<HTMLAnchorElement>) {
  event.preventDefault();
  document.getElementById("main")?.focus();
}

/**
 * The areas this caller may be offered, grouped. An area is offered when at
 * least one of its items is visible (§5); a group none of whose areas is offered
 * renders nothing, and with nothing to offer there is no landmark at all.
 */
function PrimaryNavigation({ groups }: { readonly groups: readonly NavigationGroup[] }) {
  const visibleItems = useVisibleItems();

  const offered = groups
    .map((group) => ({ ...group, areas: group.areas.filter((area) => visibleItems(area.items).length > 0) }))
    .filter((group) => group.areas.length > 0);

  if (offered.length === 0) {
    return null;
  }

  return (
    <nav aria-label="Main">
      {offered.map((group) => (
        <SidebarGroup key={group.label}>
          <SidebarGroupLabel>{group.label}</SidebarGroupLabel>
          <SidebarMenu>
            {group.areas.map((area) => (
              <NavigationEntry key={area.to} label={area.label} to={area.to} />
            ))}
          </SidebarMenu>
        </SidebarGroup>
      ))}
    </nav>
  );
}

/** The brand, linking home. Closes the phone sheet when followed, as entries do. */
function Brand() {
  const { isMobile, setOpenMobile } = useSidebar();

  return (
    <SidebarMenu>
      <SidebarMenuItem>
        <SidebarMenuButton
          size="lg"
          render={<Link to="/" />}
          onClick={() => {
            if (isMobile) {
              setOpenMobile(false);
            }
          }}
        >
          <span
            aria-hidden="true"
            className="flex size-8 items-center justify-center rounded-md bg-sidebar-primary text-sm font-semibold text-sidebar-primary-foreground"
          >
            L
          </span>
          <span className="font-semibold">Ligature</span>
        </SidebarMenuButton>
      </SidebarMenuItem>
    </SidebarMenu>
  );
}

interface AppShellProps {
  /**
   * Footer actions, supplied by the composition root. The shell is shared
   * infrastructure and may not import a module (docs/frontend-architecture.md
   * §2), so it offers the slot and app/ fills it with the auth module's sign-out
   * control rather than the shell reaching for it.
   */
  readonly actions?: ReactNode;

  /** The areas, composed by app/ from what each module exports. */
  readonly navigation?: readonly NavigationGroup[];
}

/**
 * The shell for signed-in pages, built from shadcn's sidebar (§5, §14).
 *
 * The skip link comes first, so keyboard users can bypass the navigation on
 * every page (WCAG 2.4.1).
 *
 * EVERYTHING IS IN A LANDMARK, audited as a whole page: the brand in the banner,
 * the areas in the "Main" navigation, the caller's name and own controls (My
 * account, Sign out) in an "Account" navigation, and the page in main. Found by
 * the My account story; a container-only audit never runs axe's region rule. The header carries a SidebarTrigger at every width:
 * below md it opens the sheet, above it restores a sidebar collapsed with
 * Ctrl/⌘+B. Only data the backend supplies is shown — no search, tenant, role
 * subtitle or counts.
 */
export function AppShell({ actions, navigation }: AppShellProps) {
  const displayName = useCallerDisplayName();

  return (
    <SidebarProvider>
      <a
        href="#main"
        onClick={skipToMain}
        className="sr-only focus:not-sr-only focus:absolute focus:top-4 focus:left-4 focus:z-50 focus:rounded-md focus:bg-background focus:px-3 focus:py-2"
      >
        Skip to content
      </a>
      <Sidebar>
        <SidebarHeader>
          <header>
            <Brand />
          </header>
        </SidebarHeader>
        <SidebarContent>
          <PrimaryNavigation groups={navigation ?? []} />
        </SidebarContent>
        {displayName === undefined && actions === undefined ? null : (
          <SidebarFooter>
            <nav aria-label="Account" className="flex flex-col gap-2">
              {displayName === undefined ? null : (
                <p className="truncate px-2 text-sm font-medium">{displayName}</p>
              )}
              {actions}
            </nav>
          </SidebarFooter>
        )}
      </Sidebar>
      <SidebarInset id="main" tabIndex={-1}>
        <div className="flex h-12 shrink-0 items-center gap-2 border-b px-4">
          <SidebarTrigger />
        </div>
        <Outlet />
      </SidebarInset>
    </SidebarProvider>
  );
}
