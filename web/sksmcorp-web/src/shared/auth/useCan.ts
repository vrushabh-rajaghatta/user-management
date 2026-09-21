import type { PermissionCode, PermissionScope } from "./permissions";
import { useAuthSession } from "./useAuthSession";

/**
 * Whether a capability should be VISIBLE right now, re-evaluated whenever the
 * session changes. Presentation only: it never means the user may do anything
 * (docs/frontend-architecture.md §9).
 */
export function useCan(permission: PermissionCode, scope?: PermissionScope): boolean {
  return useAuthSession().can(permission, scope);
}
