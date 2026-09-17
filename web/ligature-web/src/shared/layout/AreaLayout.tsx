import { Outlet } from "react-router";
import { Sidebar, SidebarContent, SidebarGroup, SidebarHeader, SidebarMenu } from "@/components/ui/sidebar";
import type { NavigationArea } from "./navigation";
import { NavigationEntry } from "./NavigationEntry";
import { useVisibleItems } from "./useVisibleItems";

interface AreaLayoutProps {
  readonly area: NavigationArea;
}

/**
 * An area's own navigation beside its pages (docs/frontend-architecture.md §5).
 *
 * Generic: it renders the area it is given, and knows no area's name, pages or
 * permissions. It lists exactly the items useVisibleItems returns — the same
 * function the shell uses to decide whether to offer the area at all.
 *
 * One landmark at every width, laid out by breakpoint rather than rendered
 * twice (§14): a column beside the page at lg and above, a horizontal row above
 * it below. The area's title and description show only in the column.
 */
export function AreaLayout({ area }: AreaLayoutProps) {
  const visible = useVisibleItems()(area.items);

  return (
    <div className="flex min-h-0 flex-1 flex-col lg:flex-row">
      {visible.length === 0 ? null : (
        <Sidebar
          collapsible="none"
          className="h-auto w-full border-b lg:min-h-full lg:w-(--sidebar-width) lg:border-r lg:border-b-0"
        >
          <SidebarHeader className="hidden gap-1 px-4 pt-4 lg:flex">
            <p className="font-semibold">{area.title}</p>
            {area.description === undefined ? null : (
              <p className="text-sm text-muted-foreground">{area.description}</p>
            )}
          </SidebarHeader>
          <SidebarContent>
            <nav aria-label={area.title}>
              <SidebarGroup>
                <SidebarMenu className="flex-row flex-wrap lg:flex-col">
                  {visible.map((item) => (
                    <NavigationEntry key={item.to} label={item.label} to={item.to} className="w-auto lg:w-full" />
                  ))}
                </SidebarMenu>
              </SidebarGroup>
            </nav>
          </SidebarContent>
        </Sidebar>
      )}
      <div className="min-w-0 flex-1">
        <Outlet />
      </div>
    </div>
  );
}
