import { z } from "zod";

/**
 * SES-Q2's response (docs/requirements.md, "SES-Q2 GetMySessions on the My
 * account page"): SES-Q1's eight fields, for the caller's own user. Declared
 * here because this module owns its read (MY7); it shares the server's
 * semantics with SES-Q1, not this client's code.
 *
 * `current` is the SERVER's fact — the session this request presented — and
 * is only ever rendered. `idleExpiresAt` is shown as a time, never counted
 * down from.
 */
export const mySessionSchema = z.object({
  sessionId: z.string().min(1),
  createdAt: z.string(),
  lastActivityAt: z.string(),
  expiresAt: z.string(),
  idleExpiresAt: z.string(),
  ipAddress: z.string().nullable(),
  userAgent: z.string().nullable(),
  current: z.boolean(),
});

export const mySessionsSchema = z.object({ sessions: z.array(mySessionSchema) });

export type MySession = z.infer<typeof mySessionSchema>;
