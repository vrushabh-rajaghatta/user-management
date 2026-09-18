import { api } from "@/shared/api/client";

/**
 * USR-C4: POST /api/users/{userId}/deactivate. Requires user.deactivate and a
 * reason. 204 with no body.
 *
 * One controlled operation on the server: sessions, current and future roles,
 * identities and outstanding links all end with the user. The server refuses
 * the caller deactivating themselves; the client cannot tell a row is the
 * caller and does not try (USR-C4/C5 UI, U4).
 */
export const deactivateUser = ({ userId, reason }: { userId: string; reason: string }) =>
  api.post(`/api/users/${encodeURIComponent(userId)}/deactivate`, { body: { reason } });
