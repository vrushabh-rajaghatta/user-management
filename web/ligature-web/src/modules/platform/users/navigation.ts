import type { NavigationItem } from "@/shared/layout/navigation";
import { UserPermissions } from "./permissions";

/**
 * What this module contributes to the navigation of the area it sits in
 * (docs/frontend-architecture.md §5). Data, never markup.
 *
 * One entry: Users, the page USR-Q2 backs, shown to holders of user.read. New
 * user is not an entry — it is an action on that page, and user.create alone
 * does not make the Users list visible.
 */
export const usersNavigation: readonly NavigationItem[] = [
  { label: "Users", to: "/admin/users", permission: UserPermissions.read },
];
