import { NavLink, useMatch } from "react-router";
import { SidebarMenuButton, SidebarMenuItem, useSidebar } from "@/components/ui/sidebar";

interface NavigationEntryProps {
  readonly label: string;
  readonly to: string;
  readonly className?: string;
}

/**
 * One navigation entry, at either level. The URL is the only state (§5): the
 * entry is current on its own path and anywhere beneath it, so an area stays
 * current across its pages and a page across its sub-pages. NavLink supplies
 * aria-current; useMatch gives the vendored button the same answer for styling.
 *
 * Following an entry closes the sheet the primary sidebar becomes on a phone
 * (§14). Done on the click rather than on a location change, so nothing sets
 * state in response to rendering.
 */
export function NavigationEntry({ label, to, className }: NavigationEntryProps) {
  const current = useMatch({ path: to, end: false }) !== null;
  const { isMobile, setOpenMobile } = useSidebar();

  return (
    <SidebarMenuItem>
      <SidebarMenuButton
        isActive={current}
        className={className}
        render={<NavLink to={to} />}
        onClick={() => {
          if (isMobile) {
            setOpenMobile(false);
          }
        }}
      >
        {label}
      </SidebarMenuButton>
    </SidebarMenuItem>
  );
}
