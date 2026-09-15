import type { ReactNode } from "react";
import type { PermissionCode, PermissionScope } from "./permissions";
import { useCan } from "./useCan";

interface CanProps {
  readonly permission: PermissionCode;
  readonly scope?: PermissionScope;
  readonly children: ReactNode;
}

/**
 * Shows its children unless the capability is known to be denied. A rendering
 * boundary, not a security boundary: hiding something protects nothing, and the
 * server authorizes every protected operation (docs/frontend-architecture.md §9).
 */
export function Can({ permission, scope, children }: CanProps) {
  return useCan(permission, scope) ? children : null;
}
