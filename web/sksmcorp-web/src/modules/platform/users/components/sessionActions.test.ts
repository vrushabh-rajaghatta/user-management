import { describe, expect, it } from "vitest";
import type { UserSession } from "../schemas/sessions";
import { revokeOffered } from "./sessionActions";

/**
 * SS6 as a rule of its own. On the User detail page the Revoke column exists
 * only for a session.revoke holder, so the page alone cannot show that the
 * rule checks the permission too; a later caller (My sessions) may not have
 * that column gate.
 */
const SESSION: UserSession = {
  sessionId: "f5000000-0000-4000-8000-0000000e0001",
  createdAt: "2026-09-19T09:15:00Z",
  lastActivityAt: "2026-09-19T11:52:00Z",
  expiresAt: "2026-09-19T17:15:00Z",
  idleExpiresAt: "2026-09-19T12:22:00Z",
  ipAddress: "192.168.65.1",
  userAgent: "Mozilla/5.0",
  current: false,
};

describe("revokeOffered", () => {
  it("offers Revoke on a session that is not current, to a session.revoke holder", () => {
    expect(revokeOffered({ session: SESSION, holdsRevoke: true })).toBe(true);
  });

  it("does not offer it without session.revoke", () => {
    expect(revokeOffered({ session: SESSION, holdsRevoke: false })).toBe(false);
  });

  it("does not offer it on the current session", () => {
    expect(revokeOffered({ session: { ...SESSION, current: true }, holdsRevoke: true })).toBe(false);
  });
});
