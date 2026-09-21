import { api } from "@/shared/api/client";

/**
 * USR-C5: POST /api/users/{userId}/reactivate. Requires user.reactivate and a
 * reason. 204 with no body.
 *
 * Restores nothing: no role, session or link comes back with the user.
 */
export const reactivateUser = ({ userId, reason }: { userId: string; reason: string }) =>
  api.post(`/api/users/${encodeURIComponent(userId)}/reactivate`, { body: { reason } });
