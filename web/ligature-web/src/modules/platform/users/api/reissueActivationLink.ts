import { api } from "@/shared/api/client";

/**
 * CRD-C7: POST /api/users/{userId}/activation-link. Requires user.create and a
 * reason. 202 with no body: the administrator never receives the token or the
 * link, which is why no response schema is declared (§6).
 */
export const reissueActivationLink = ({ userId, reason }: { userId: string; reason: string }) =>
  api.post(`/api/users/${encodeURIComponent(userId)}/activation-link`, { body: { reason } });
