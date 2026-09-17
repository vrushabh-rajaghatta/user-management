import { api } from "@/shared/api/client";

/**
 * SES-C4, administrator form: POST /api/users/{userId}/sign-out-everywhere.
 * Requires session.revoke and a reason. 204 with no body.
 *
 * If the target is the caller, the caller's own session ends too. The next
 * request is a reported 401, and authentication state handles it as for any
 * ended session.
 */
export const signOutUserEverywhere = ({ userId, reason }: { userId: string; reason: string }) =>
  api.post(`/api/users/${encodeURIComponent(userId)}/sign-out-everywhere`, { body: { reason } });
