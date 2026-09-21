import { z } from "zod";
import { api } from "@/shared/api/client";

/**
 * GET /me — who is the caller for this request (docs/frontend-architecture.md
 * §8, §11).
 *
 * It lives in shared/auth because authentication state is infrastructure, and
 * shared/auth is the one place outside a module's api/ that may reach the HTTP
 * boundary.
 *
 * "return", not "report": a 401 here is the ANSWER, and reporting it would ask
 * AuthProvider to sign out in the middle of working out whether anyone is
 * signed in.
 */

/** The scope a permission was granted in. A UUID or null, as the server sends it. */
const meSchema = z.object({
  identity: z.object({
    userIdentityId: z.string().min(1),
    username: z.string().min(1),
    displayName: z.string().min(1),
  }),
  permissions: z.array(
    z.object({
      code: z.string().min(1),
      scopeType: z.string().min(1),
      scopeId: z.string().nullable(),
    }),
  ),
  session: z.object({
    expiresAt: z.string().min(1),
    idleExpiresAt: z.string().min(1),
  }),
});

export type Me = z.infer<typeof meSchema>;

export const me = () => api.get("/api/me", { response: meSchema, unauthorized: "return" });

/**
 * Its own key, as every query has one (§11). Resolution consumes the query
 * rather than reaching past it to the transport.
 */
export const authKeys = {
  me: () => ["auth", "me"] as const,
};
