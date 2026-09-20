import { api } from "@/shared/api/client";
import { createdRoleSchema, type CreateRoleForm } from "../schemas/createRole";
import { rolePermissionsSchema, rolesSchema } from "../schemas/roles";

/** AUT-Q5: GET /api/roles/administration, role.read. Not the grantable-role list. */
export const listRoles = (includeInactive: boolean, signal?: AbortSignal) =>
  api.get(`/api/roles/administration${includeInactive ? "?includeInactive=true" : ""}`, {
    response: rolesSchema,
    signal,
  });

/**
 * AUT-Q3: GET /api/roles/{roleId}/permissions, role.read. The current state
 * only — asOf is the contract's, not this screen's (RA5). A role that does not
 * exist answers 404, which reaches the caller as an ApiError.
 */
export const getRolePermissions = (roleId: string, signal?: AbortSignal) =>
  api.get(`/api/roles/${encodeURIComponent(roleId)}/permissions`, {
    response: rolePermissionsSchema,
    signal,
  });

/**
 * AUT-C3: POST /api/roles, role.manage. Sends exactly what was typed; the
 * server trims the name and description, and stores the code as supplied.
 */
export const createRole = (form: CreateRoleForm) =>
  api.post("/api/roles", { body: form, response: createdRoleSchema });
