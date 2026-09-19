import { api } from "@/shared/api/client";
import { userIdentitiesSchema } from "../schemas/identities";

/** IDN-Q1: GET /api/users/{userId}/identities, identity.read. */
export const listUserIdentities = (userId: string, signal?: AbortSignal) =>
  api.get(`/api/users/${encodeURIComponent(userId)}/identities`, { response: userIdentitiesSchema, signal });

/**
 * CRD-C6: POST /api/identities/{identityId}/unlock, user.unlock, with a
 * required reason. 204 with no body. Refusals are the server's, word for word:
 * it — not this client — decides eligibility, including that an administrator
 * cannot unlock any identity of their own user.
 */
export const unlockIdentity = ({ identityId, reason }: { identityId: string; reason: string }) =>
  api.post(`/api/identities/${encodeURIComponent(identityId)}/unlock`, { body: { reason } });
