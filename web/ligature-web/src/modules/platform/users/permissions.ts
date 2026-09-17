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
} as const;
