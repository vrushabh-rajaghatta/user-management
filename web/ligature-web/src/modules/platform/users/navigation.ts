import type { NavigationSection } from "@/shared/layout/navigation";
import { UserPermissions } from "./permissions";

/**
 * What this module contributes to the application navigation
 * (docs/frontend-architecture.md §5). Data, never markup: the shell renders it
 * and never learns what "Users" means.
 *
 * One entry today. There is no users list — the backend exposes no read or list
 * endpoint — so this area deliberately does not pretend one exists, and further
 * entries arrive when their backend contracts do.
 */
export const usersNavigation: readonly NavigationSection[] = [
  {
    label: "Users",
    items: [{ label: "Create user", to: "/users/new", permission: UserPermissions.create }],
  },
];
