import { z } from "zod";

/**
 * SES-Q1's response (docs/requirements.md, "SES-Q1 GetActiveSessions and
 * Revoke on the User detail page"): exactly these eight fields per session,
 * most recently active first.
 *
 * `current` is the SERVER's fact — the session this request presented — and
 * the client never infers it. `idleExpiresAt` is a conservative instant, as in
 * /me: shown as a time, never counted down from.
 */
export const userSessionSchema = z.object({
  sessionId: z.string().min(1),
  createdAt: z.string(),
  lastActivityAt: z.string(),
  expiresAt: z.string(),
  idleExpiresAt: z.string(),
  ipAddress: z.string().nullable(),
  userAgent: z.string().nullable(),
  current: z.boolean(),
});

export const userSessionsSchema = z.object({ sessions: z.array(userSessionSchema) });

export type UserSession = z.infer<typeof userSessionSchema>;
