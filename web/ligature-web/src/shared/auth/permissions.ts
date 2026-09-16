declare const permissionCode: unique symbol;

/**
 * A permission code from the backend catalogue, e.g. "user.create". Branded so
 * a code is only ever produced by the module that owns it, through
 * definePermission; shared/auth knows the type and never the values
 * (docs/frontend-architecture.md §9).
 */
export type PermissionCode = string & { readonly [permissionCode]: true };

export function definePermission(code: string): PermissionCode {
  return code as PermissionCode;
}

/**
 * The scope a permission applies in: a scope type and a scope ID. No scope means
 * global. A permission in one scope says nothing about another.
 */
export interface PermissionScope {
  readonly type: string;
  readonly id: string;
}
