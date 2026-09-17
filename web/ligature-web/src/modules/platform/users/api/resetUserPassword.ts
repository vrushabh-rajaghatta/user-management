import { api } from "@/shared/api/client";

/**
 * CRD-C5: POST /api/users/{userId}/password-reset. Requires user.resetpassword
 * and a reason. 202 with no body: the administrator never receives the token or
 * the password, which is why no response schema is declared (§6).
 */
export const resetUserPassword = ({ userId, reason }: { userId: string; reason: string }) =>
  api.post(`/api/users/${encodeURIComponent(userId)}/password-reset`, { body: { reason } });
