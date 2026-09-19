import { api } from "@/shared/api/client";
import { userSessionsSchema } from "../schemas/sessions";

/** SES-Q1: GET /api/users/{userId}/sessions, session.read. */
export const listUserSessions = (userId: string, signal?: AbortSignal) =>
  api.get(`/api/users/${encodeURIComponent(userId)}/sessions`, { response: userSessionsSchema, signal });

/**
 * SES-C3: POST /api/sessions/{sessionId}/revoke, session.revoke, with a
 * required reason. 204 with no body, including for a session that had already
 * ended. Refusals are the server's, word for word.
 */
export const revokeSession = ({ sessionId, reason }: { sessionId: string; reason: string }) =>
  api.post(`/api/sessions/${encodeURIComponent(sessionId)}/revoke`, { body: { reason } });
