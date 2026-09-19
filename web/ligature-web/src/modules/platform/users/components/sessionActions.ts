import type { UserSession } from "../schemas/sessions";

/**
 * Whether Revoke is OFFERED on a session (docs/requirements.md, "SES-Q1
 * GetActiveSessions and Revoke on the User detail page", SS6): the caller holds
 * session.revoke, and the row is not the one the SERVER marked `current`.
 *
 * AN AFFORDANCE, NOT AUTHORIZATION. SES-C3 has no self rule — revoking one's
 * own session is signing out, which the account footer already offers — so
 * this only keeps the page from offering it here. The client never works out
 * for itself which session is its own.
 */
export function revokeOffered({
  session,
  holdsRevoke,
}: {
  readonly session: UserSession;
  readonly holdsRevoke: boolean;
}): boolean {
  return holdsRevoke && !session.current;
}
