import { definePermission } from "@/shared/auth/permissions";

/**
 * This module's permission codes, verbatim from the backend catalogue
 * (docs/frontend-architecture.md §9). shared/auth knows the TYPE and never the
 * values, which is why they are defined here, by the module whose vocabulary
 * they are.
 */
export const UserPermissions = {
  create: definePermission("user.create"),
  read: definePermission("user.read"),
  resetPassword: definePermission("user.resetpassword"),

  // USR-C2.
  update: definePermission("user.update"),

  // USR-C4 / USR-C5.
  deactivate: definePermission("user.deactivate"),
  reactivate: definePermission("user.reactivate"),

  // A session permission, used here because signing a user out everywhere is
  // started from the Users table. No sessions module exists to own it yet.
  revokeSessions: definePermission("session.revoke"),

  // Role assignment (AUT-C1, AUT-C2, AUT-Q2), started from the Users table. No
  // roles module exists to own them yet.
  readRoles: definePermission("role.read"),
  grantRoles: definePermission("role.grant"),
  revokeRoles: definePermission("role.revoke"),
} as const;
