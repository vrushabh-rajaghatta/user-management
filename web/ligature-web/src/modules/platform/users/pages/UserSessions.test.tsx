import { act, screen, waitFor, within } from "@testing-library/react";
import { delay, http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { appRoutes } from "@/app/router";
import { createQueryClient } from "@/app/queryClient";
import { authKeys } from "@/shared/auth/me";
import { definePermission, type PermissionCode } from "@/shared/auth/permissions";
import { expectNoAccessibilityViolations } from "@/test/axe";
import { server } from "@/test/msw/server";
import { renderWithApp } from "@/test/renderWithApp";
import { TestSessionSource } from "@/test/sessions";

/**
 * The User detail page's Active sessions and Revoke (docs/requirements.md,
 * "SES-Q1 GetActiveSessions and Revoke on the User detail page", SQ-6 to
 * SQ-12), through the application's own routes.
 *
 * session.read gates the SECTION, and without it the request is never made
 * (counted). session.revoke gates REVOKE, which is never offered on the row the
 * SERVER marks `current` — an affordance: SES-C3 has no self rule, and the
 * client never works out for itself which session is its own.
 */

const at = (path: string) => new URL(path, window.location.origin).href;

const READ = definePermission("user.read");
const SESSION_READ = definePermission("session.read");
const SESSION_REVOKE = definePermission("session.revoke");
const DEACTIVATE = definePermission("user.deactivate");

const USER_ID = "f4000000-0000-4000-8000-00000000a001";

interface Session {
  sessionId: string;
  createdAt: string;
  lastActivityAt: string;
  expiresAt: string;
  idleExpiresAt: string;
  ipAddress: string | null;
  userAgent: string | null;
  current: boolean;
}

const LAPTOP: Session = {
  sessionId: "f4000000-0000-4000-8000-0000000e0001",
  createdAt: "2026-09-19T09:15:00Z",
  lastActivityAt: "2026-09-19T11:52:00Z",
  expiresAt: "2026-09-19T17:15:00Z",
  idleExpiresAt: "2026-09-19T12:22:00Z",
  ipAddress: "192.168.65.1",
  userAgent: "Mozilla/5.0 (Macintosh) Laptop",
  current: false,
};

const PHONE: Session = {
  sessionId: "f4000000-0000-4000-8000-0000000e0002",
  createdAt: "2026-09-19T08:05:00Z",
  lastActivityAt: "2026-09-19T10:40:00Z",
  expiresAt: "2026-09-19T16:05:00Z",
  idleExpiresAt: "2026-09-19T11:10:00Z",
  ipAddress: null,
  userAgent: null,
  current: false,
};

const DETAIL = {
  userId: USER_ID,
  firstName: "V",
  lastName: "R",
  displayName: "V R",
  email: "v.r@example.test",
  status: "Active" as const,
  activationPending: false,
};

interface Backend {
  sessions: Session[];
  readonly sessionReads: () => number;
  readonly revokes: { path: string; body: unknown }[];
}

function backend(
  options: {
    sessions?: Session[];
    read?: () => Response | Promise<Response>;
    revoke?: () => Response | Promise<Response>;
  } = {},
): Backend {
  let sessionReads = 0;
  const state: Backend = {
    sessions: options.sessions ?? [LAPTOP, PHONE],
    sessionReads: () => sessionReads,
    revokes: [],
  };

  server.use(
    http.get(at(`/api/users/${USER_ID}`), () => HttpResponse.json(DETAIL)),
    http.get(at(`/api/users/${USER_ID}/sessions`), () => {
      sessionReads += 1;
      return options.read !== undefined ? options.read() : HttpResponse.json({ sessions: state.sessions });
    }),
    http.get(at(`/api/users/${USER_ID}/identities`), () => HttpResponse.json({ identities: [] })),
    http.get(at(`/api/users/${USER_ID}/role-assignments`), () => HttpResponse.json({ assignments: [] })),
    http.post(at("/api/sessions/:sessionId/revoke"), async ({ request }) => {
      state.revokes.push({ path: new URL(request.url).pathname, body: await request.json() });
      if (options.revoke !== undefined) {
        return options.revoke();
      }
      state.sessions = state.sessions.filter((x) => !request.url.includes(x.sessionId));
      return new HttpResponse(null, { status: 204 });
    }),
    http.post(at(`/api/users/${USER_ID}/sign-out-everywhere`), () => {
      state.sessions = state.sessions.filter((x) => x.current);
      return new HttpResponse(null, { status: 204 });
    }),
    http.post(at(`/api/users/${USER_ID}/deactivate`), () => {
      state.sessions = [];
      return new HttpResponse(null, { status: 204 });
    }),
  );

  return state;
}

async function render(codes: PermissionCode[]) {
  const source = new TestSessionSource();
  const queryClient = createQueryClient();

  queryClient.setQueryData(authKeys.me(), {
    identity: { userIdentityId: "f4000000-0000-4000-8000-0000000c0001", username: "ada.lovelace", displayName: "Ada Lovelace" },
    permissions: codes.map((code) => ({ code, scopeType: "Global", scopeId: null })),
    session: { expiresAt: "2026-09-19T20:00:00Z", idleExpiresAt: "2026-09-19T12:15:00Z" },
  });

  const result = renderWithApp(appRoutes, { path: `/admin/users/${USER_ID}`, source, queryClient });

  await act(async () => {
    source.settle({ status: "authenticated", principal: { permissions: codes.map((code) => ({ code })) } });
    await Promise.resolve();
  });

  await screen.findByRole("heading", { level: 1, name: "V R" });

  return result;
}

const section = () => screen.findByRole("region", { name: "Active sessions" });

/** The row showing a session, found by something only that row shows. */
async function rowOf(text: string) {
  const region = await section();
  const cell = await within(region).findByText(text);
  const row = cell.closest("tr");

  if (row === null) {
    throw new Error(`No row shows ${text}.`);
  }

  return row;
}

const revokeIn = (row: HTMLElement) => within(row).queryByRole("button", { name: /^Revoke session/ });

async function settle() {
  await act(async () => {
    await delay(50);
  });
}

// ---------------------------------------------------------------- SQ-6, SQ-7

describe("the Active sessions section", () => {
  it("lists the sessions with Signed in, Last active, IP address and Browser, to a session.read holder", async () => {
    const state = backend();
    await render([READ, SESSION_READ]);

    const table = await within(await section()).findByRole("table");

    for (const header of ["Signed in", "Last active", "IP address", "Browser"]) {
      expect(within(table).getByRole("columnheader", { name: header })).toBeInTheDocument();
    }

    const rows = within(table).getAllByRole("row").slice(1);
    expect(rows).toHaveLength(2);

    // In the server's order: most recently active first.
    expect(rows[0]).toHaveTextContent("192.168.65.1");
    expect(rows[0]).toHaveTextContent("Mozilla/5.0 (Macintosh) Laptop");

    // A missing IP address or browser is a dash, not a blank.
    expect(within(rows[1] as HTMLElement).getAllByText("—")).toHaveLength(2);

    expect(state.sessionReads()).toBe(1);
  });

  it("marks the server's current session as This session", async () => {
    backend({ sessions: [{ ...LAPTOP, current: true }, PHONE] });
    await render([READ, SESSION_READ]);

    const current = await rowOf("192.168.65.1");
    expect(within(current).getByText("This session")).toBeInTheDocument();

    expect(screen.getAllByText("This session")).toHaveLength(1);
  });

  it("says so when there are no active sessions", async () => {
    backend({ sessions: [] });
    await render([READ, SESSION_READ]);

    expect(await within(await section()).findByText("No active sessions.")).toBeInTheDocument();
  });

  it("is absent, and its request is never made, without session.read", async () => {
    const state = backend();
    await render([READ, SESSION_REVOKE]);
    await settle();

    expect(screen.queryByRole("region", { name: "Active sessions" })).toBeNull();
    expect(state.sessionReads()).toBe(0);
  });

  it("states its own failure with Try again, and the rest of the page stands", async () => {
    let calls = 0;
    backend({
      read: () => {
        calls += 1;
        return calls === 1
          ? HttpResponse.json({ error: "The current actor does not have permission to view sessions." }, { status: 400 })
          : HttpResponse.json({ sessions: [LAPTOP] });
      },
    });
    const { user } = await render([READ, SESSION_READ]);

    const region = await section();
    expect(
      await within(region).findByText("The current actor does not have permission to view sessions."),
    ).toBeInTheDocument();
    expect(screen.getByText("v.r@example.test")).toBeInTheDocument();

    await user.click(within(region).getByRole("button", { name: "Try again" }));
    expect(await within(region).findByText("192.168.65.1")).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------- SQ-8

describe("when Revoke is offered", () => {
  it("is offered on every non-current session to a session.revoke holder", async () => {
    backend();
    await render([READ, SESSION_READ, SESSION_REVOKE]);

    expect(revokeIn(await rowOf("192.168.65.1"))).toBeInTheDocument();
    expect(revokeIn((await rowOf("192.168.65.1")).nextElementSibling as HTMLElement)).toBeInTheDocument();
  });

  it("is absent without session.revoke", async () => {
    backend();
    await render([READ, SESSION_READ]);
    await rowOf("192.168.65.1");

    expect(screen.queryByRole("button", { name: /^Revoke session/ })).toBeNull();
  });

  it("is absent on the current session, and only there", async () => {
    backend({ sessions: [{ ...LAPTOP, current: true }, PHONE] });
    await render([READ, SESSION_READ, SESSION_REVOKE]);

    const current = await rowOf("192.168.65.1");

    expect(revokeIn(current)).toBeNull();
    expect(revokeIn(current.nextElementSibling as HTMLElement)).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------- SQ-9

describe("revoking", () => {
  async function openDialog(user: Awaited<ReturnType<typeof render>>["user"]) {
    const button = revokeIn(await rowOf("192.168.65.1"));

    if (button === null) {
      throw new Error("Revoke was not offered.");
    }

    await user.click(button);

    return screen.findByRole("dialog", { name: "Revoke session" });
  }

  it("requires a reason, and sends nothing without one", async () => {
    const state = backend();
    const { user } = await render([READ, SESSION_READ, SESSION_REVOKE]);

    const dialog = await openDialog(user);
    await user.click(within(dialog).getByRole("button", { name: "Revoke" }));

    expect(await within(dialog).findByText("A reason is required.")).toBeInTheDocument();
    expect(state.revokes).toHaveLength(0);
  });

  it("sends exactly the reason, re-reads the sessions, announces, and the row is gone", async () => {
    const state = backend();
    const { user } = await render([READ, SESSION_READ, SESSION_REVOKE]);
    const region = await section();
    await rowOf("192.168.65.1");
    const readsBefore = state.sessionReads();

    const dialog = await openDialog(user);
    await user.type(within(dialog).getByLabelText("Reason"), "Lost laptop");
    await user.click(within(dialog).getByRole("button", { name: "Revoke" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });

    expect(state.revokes).toEqual([
      { path: `/api/sessions/${LAPTOP.sessionId}/revoke`, body: { reason: "Lost laptop" } },
    ]);
    await waitFor(() => {
      expect(state.sessionReads()).toBeGreaterThan(readsBefore);
    });
    expect(await screen.findByText("The session was revoked.")).toBeInTheDocument();
    await waitFor(() => {
      expect(within(region).queryByText("192.168.65.1")).toBeNull();
    });
  });

  it("is busy while sending, and sends once however often Revoke is pressed", async () => {
    const state = backend({
      revoke: async () => {
        await delay(200);
        return new HttpResponse(null, { status: 204 });
      },
    });
    const { user } = await render([READ, SESSION_READ, SESSION_REVOKE]);

    const dialog = await openDialog(user);
    await user.type(within(dialog).getByLabelText("Reason"), "Lost laptop");
    const confirm = within(dialog).getByRole("button", { name: "Revoke" });
    await user.click(confirm);
    await user.click(confirm);

    expect(within(dialog).getByRole("button", { name: "Revoking…" })).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });
    expect(state.revokes).toHaveLength(1);
  });

  it("shows a refusal word for word, keeps the dialog open, and re-reads the sessions", async () => {
    const state = backend({
      revoke: () => HttpResponse.json({ error: "The session does not exist." }, { status: 400 }),
    });
    const { user } = await render([READ, SESSION_READ, SESSION_REVOKE]);
    await rowOf("192.168.65.1");
    const readsBefore = state.sessionReads();

    const dialog = await openDialog(user);
    await user.type(within(dialog).getByLabelText("Reason"), "Lost laptop");
    await user.click(within(dialog).getByRole("button", { name: "Revoke" }));

    expect(await within(dialog).findByText("The session does not exist.")).toBeInTheDocument();
    expect(screen.getByRole("dialog", { name: "Revoke session" })).toBeInTheDocument();
    await waitFor(() => {
      expect(state.sessionReads()).toBeGreaterThan(readsBefore);
    });
  });
});

// ---------------------------------------------------------------- SQ-10

describe("other actions that end sessions", () => {
  async function run(item: string, dialogName: string, confirm: string, codes: PermissionCode[]) {
    const state = backend();
    const { user } = await render(codes);
    await rowOf("192.168.65.1");
    const readsBefore = state.sessionReads();

    await user.click(screen.getByRole("button", { name: "Actions" }));
    await user.click(await screen.findByRole("menuitem", { name: item }));

    const dialog = await screen.findByRole("dialog", { name: dialogName });
    await user.type(within(dialog).getByLabelText("Reason"), "Offboarding");
    await user.click(within(dialog).getByRole("button", { name: confirm }));

    await waitFor(() => {
      expect(state.sessionReads()).toBeGreaterThan(readsBefore);
    });
  }

  it("re-reads the sessions after Sign out everywhere", async () => {
    await run("Sign out everywhere", "Sign out V R everywhere", "Sign out everywhere", [READ, SESSION_READ, SESSION_REVOKE]);
    expect(await within(await section()).findByText("No active sessions.")).toBeInTheDocument();
  });

  it("re-reads the sessions after Deactivate", async () => {
    await run("Deactivate", "Deactivate V R", "Deactivate", [READ, SESSION_READ, DEACTIVATE]);
  });
});

// ---------------------------------------------------------------- SQ-11

/** The seeded compositions (PlatformProvisioner). */
const USER_ADMINISTRATOR = [
  "user.create",
  "user.read",
  "user.update",
  "user.deactivate",
  "user.reactivate",
  "user.resetpassword",
  "user.unlock",
  "identity.read",
  "identity.manage",
  "session.read",
  "session.revoke",
].map(definePermission);

const SECURITY_ADMINISTRATOR = [
  "role.read",
  "role.manage",
  "role.grant",
  "role.revoke",
  "securitypolicy.read",
  "securitypolicy.change",
  "user.read",
].map(definePermission);

const ACCESS_REVIEWER = [
  "accessreview.read",
  "user.read",
  "role.read",
  "identity.read",
  "session.read",
  "securitypolicy.read",
].map(definePermission);

describe("the seeded roles (SS9)", () => {
  it("shows a user administrator the sessions, with Revoke", async () => {
    const state = backend();
    await render(USER_ADMINISTRATOR);

    expect(revokeIn(await rowOf("192.168.65.1"))).toBeInTheDocument();
    expect(state.sessionReads()).toBe(1);
  });

  it("shows a security administrator no section, and makes no sessions request", async () => {
    const state = backend();
    await render(SECURITY_ADMINISTRATOR);
    await settle();

    expect(screen.queryByRole("region", { name: "Active sessions" })).toBeNull();
    expect(state.sessionReads()).toBe(0);
  });

  it("shows an access reviewer the sessions, without Revoke", async () => {
    const state = backend();
    await render(ACCESS_REVIEWER);
    await rowOf("192.168.65.1");

    expect(state.sessionReads()).toBe(1);
    expect(screen.queryByRole("button", { name: /^Revoke session/ })).toBeNull();
  });
});

// ---------------------------------------------------------------- SQ-12

describe("accessibility", () => {
  it("has no violations with the section loaded", async () => {
    backend({ sessions: [{ ...LAPTOP, current: true }, PHONE] });
    await render([READ, SESSION_READ, SESSION_REVOKE]);
    await rowOf("192.168.65.1");

    await expectNoAccessibilityViolations(document.body);
  });

  it("has no violations with the Revoke dialog open", async () => {
    backend();
    const { user } = await render([READ, SESSION_READ, SESSION_REVOKE]);

    const button = revokeIn(await rowOf("192.168.65.1"));

    if (button === null) {
      throw new Error("Revoke was not offered.");
    }

    await user.click(button);
    await screen.findByRole("dialog", { name: "Revoke session" });

    await expectNoAccessibilityViolations(document.body);
  });

  it("has no violations while the section loads, and when its read was refused", async () => {
    backend({
      read: async () => {
        await delay(100);
        return HttpResponse.json({ error: "The user does not exist." }, { status: 400 });
      },
    });
    await render([READ, SESSION_READ]);

    await screen.findByRole("group", { name: "Loading active sessions" });
    await expectNoAccessibilityViolations(document.body);

    await within(await section()).findByText("The user does not exist.");
    await expectNoAccessibilityViolations(document.body);
  });
});
