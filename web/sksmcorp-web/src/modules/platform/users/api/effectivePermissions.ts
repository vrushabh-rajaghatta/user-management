import { api } from "@/shared/api/client";
import { effectivePermissionsSchema } from "../schemas/effectivePermissions";

/**
 * USR-Q3: GET /api/users/{userId}/effective-permissions, user.read AND
 * role.read (UA2). The current set; asOf belongs to REV-Q6, not to this read.
 */
export const getEffectivePermissions = (userId: string, signal?: AbortSignal) =>
  api.get(`/api/users/${encodeURIComponent(userId)}/effective-permissions`, {
    response: effectivePermissionsSchema,
    signal,
  });
