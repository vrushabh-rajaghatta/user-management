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
 * The User detail page's Sign-in identities and Unlock (docs/requirements.md,
 * "IDN-Q1 GetUserIdentities and Unlock on the User detail page", ID-3..ID-9),
 * through the application's own routes.
 *
 * identity.read gates the SECTION, and without it the request is never made
 * (counted). user.unlock gates UNLOCK, which is offered only when every I6
 * condition holds — including that the page is not the caller's own, judged
 * from /me. That is an affordance: the server refuses self-unlock whatever
 * this client decides (ID-7, proved over HTTP).
 */

const at = (path: string) => new URL(path, window.location.origin).href;

const READ = definePermission("user.read");
const IDENTITY_READ = definePermission("identity.read");
const UNLOCK = definePermission("user.unlock");
const ROLE_READ = definePermission("role.read");

const USER_ID = "f3000000-0000-4000-8000-00000000a001";
const CALLER_IDENTITY = "f3000000-0000-4000-8000-0000000c0001";

interface Identity {
  userIdentityId: string;
  type: "Local" | "External";
  provider: string;
  username: string | null;
  status: "Active" | "Inactive";
  deactivatedAt: string | null;
  locked: boolean;
  lockedUntil: string | null;
}

const LOCKED: Identity = {
  userIdentityId: "f3000000-0000-4000-8000-0000000d0001",
  type: "Local",
  provider: "Application",
  username: "vr.ra",
  status: "Active",
  deactivatedAt: null,
  locked: true,
  lockedUntil: "2026-09-19T15:20:04Z",
};

const EXTERNAL: Identity = {
  userIdentityId: "f3000000-0000-4000-8000-0000000d0002",
  type: "External",
  provider: "EntraId",
  username: null,
  status: "Active",
  deactivatedAt: null,
  locked: false,
  lockedUntil: null,
};

const DETAIL = {
  userId: USER_ID,
  firstName: "Vr",
  lastName: "Ra",
  displayName: "Vr Ra",
  email: "vr.ra@example.test",
  status: "Active" as "Active" | "Inactive",
  activationPending: false,
};

interface Backend {
  identities: Identity[];
  readonly identityReads: () => number;
  readonly detailReads: () => number;
  readonly roleReads: () => number;
  readonly unlocks: { path: string; body: unknown }[];
}

function backend(
  options: {
    identities?: Identity[];
    userStatus?: "Active" | "Inactive";
    read?: () => Response | Promise<Response>;
    unlock?: () => Response | Promise<Response>;
  } = {},
): Backend {
  let identityReads = 0;
  let detailReads = 0;
  let roleReads = 0;
  const state: Backend = {
    identities: options.identities ?? [LOCKED, EXTERNAL],
    identityReads: () => identityReads,
    detailReads: () => detailReads,
    roleReads: () => roleReads,
    unlocks: [],
  };

  server.use(
    http.get(at(`/api/users/${USER_ID}`), () => {
      detailReads += 1;
      return HttpResponse.json({ ...DETAIL, status: options.userStatus ?? "Active" });
    }),
    http.get(at(`/api/users/${USER_ID}/identities`), () => {
      identityReads += 1;
      return options.read !== undefined ? options.read() : HttpResponse.json({ identities: state.identities });
    }),
    http.get(at(`/api/users/${USER_ID}/role-assignments`), () => {
      roleReads += 1;
      return HttpResponse.json({ assignments: [] });
    }),
    http.post(at("/api/identities/:identityId/unlock"), async ({ request }) => {
      state.unlocks.push({ path: new URL(request.url).pathname, body: await request.json() });
      if (options.unlock !== undefined) {
        return options.unlock();
      }
      state.identities = state.identities.map((x) =>
        request.url.includes(x.userIdentityId) ? { ...x, locked: false, lockedUntil: null } : x,
      );
      return new HttpResponse(null, { status: 204 });
    }),
  );

  return state;
}

/** Renders the detail page, with /me naming the caller's session identity. */
async function render(codes: PermissionCode[], callerIdentity = CALLER_IDENTITY) {
  const source = new TestSessionSource();
  const queryClient = createQueryClient();

  queryClient.setQueryData(authKeys.me(), {
    identity: { userIdentityId: callerIdentity, username: "ada.lovelace", displayName: "Ada Lovelace" },
    permissions: codes.map((code) => ({ code, scopeType: "Global", scopeId: null })),
    session: { expiresAt: "2026-09-19T20:00:00Z", idleExpiresAt: "2026-09-19T12:15:00Z" },
  });

  const result = renderWithApp(appRoutes, { path: `/admin/users/${USER_ID}`, source, queryClient });

  await act(async () => {
    source.settle({ status: "authenticated", principal: { permissions: codes.map((code) => ({ code })) } });
    await Promise.resolve();
  });

  await screen.findByRole("heading", { level: 1, name: "Vr Ra" });

  return result;
}

const section = () => screen.findByRole("region", { name: "Sign-in identities" });

async function settle() {
  await act(async () => {
    await delay(50);
  });
}

// ---------------------------------------------------------------- ID-3

describe("the Sign-in identities section", () => {
  it("lists every identity with Type, Username, Status and Lock, to an identity.read holder", async () => {
    const state = backend();
    await render([READ, IDENTITY_READ]);

    const region = await section();
    const table = await within(region).findByRole("table");

    for (const header of ["Type", "Username", "Status", "Lock"]) {
      expect(within(table).getByRole("columnheader", { name: header })).toBeInTheDocument();
    }

    const [local, external, ...rest] = within(table).getAllByRole("row").slice(1);
    expect(rest).toHaveLength(0);

    if (local === undefined || external === undefined) {
      throw new Error("Expected two identity rows.");
    }

    expect(within(local).getByText("Local")).toBeInTheDocument();
    expect(within(local).getByText("vr.ra")).toBeInTheDocument();
    expect(within(local).getByText(/^Locked until /)).toBeInTheDocument();

    expect(within(external).getByText("External")).toBeInTheDocument();
    expect(within(external).getByText("—")).toBeInTheDocument();
    expect(within(external).getByText("Not locked")).toBeInTheDocument();

    expect(state.identityReads()).toBe(1);
  });

  it("is absent, and its request is never made, without identity.read", async () => {
    const state = backend();
    await render([READ, UNLOCK, ROLE_READ]);
    await settle();

    expect(screen.queryByRole("region", { name: "Sign-in identities" })).toBeNull();
    expect(state.identityReads()).toBe(0);
  });

  it("states its own failure with Try again, and the rest of the page stands", async () => {
    let calls = 0;
    backend({
      read: () => {
        calls += 1;
        return calls === 1
          ? HttpResponse.json({ error: "The current actor does not have permission to view identities." }, { status: 400 })
          : HttpResponse.json({ identities: [LOCKED] });
      },
    });
    const { user } = await render([READ, IDENTITY_READ]);

    const region = await section();
    expect(
      await within(region).findByText("The current actor does not have permission to view identities."),
    ).toBeInTheDocument();
    expect(screen.getByText("vr.ra@example.test")).toBeInTheDocument();

    await user.click(within(region).getByRole("button", { name: "Try again" }));
    expect(await within(region).findByText("vr.ra")).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------- ID-4

describe("when Unlock is offered", () => {
  const unlockButton = () => screen.queryByRole("button", { name: "Unlock vr.ra" });

  it("is offered on a locked, local, active identity of an active user, to a user.unlock holder, on someone else's page", async () => {
    backend();
    await render([READ, IDENTITY_READ, UNLOCK]);
    await within(await section()).findByText("vr.ra");

    expect(unlockButton()).toBeInTheDocument();
  });

  it.each([
    ["the caller lacks user.unlock", { codes: [READ, IDENTITY_READ] }],
    ["the identity is not locked", { identities: [{ ...LOCKED, locked: false, lockedUntil: null }] }],
    ["the identity is external", { identities: [{ ...LOCKED, type: "External" as const, provider: "EntraId" }] }],
    ["the identity is inactive", { identities: [{ ...LOCKED, status: "Inactive" as const, deactivatedAt: "2026-09-18T10:00:00Z" }] }],
    ["the user is inactive", { userStatus: "Inactive" as const }],
  ])("is absent when %s", async (_, setup) => {
    backend({
      identities: "identities" in setup ? setup.identities : undefined,
      userStatus: "userStatus" in setup ? setup.userStatus : undefined,
    });
    await render("codes" in setup ? setup.codes : [READ, IDENTITY_READ, UNLOCK]);
    await within(await section()).findByRole("table");

    expect(unlockButton()).toBeNull();
  });

  /**
   * THE CALLER'S OWN PAGE, judged at the USER level: the caller's session
   * identity is a DIFFERENT identity of this user than the locked one, and
   * Unlock is still absent — on every identity of the page.
   */
  it("is absent on every identity when the page is the caller's own, even on a locked one", async () => {
    backend({ identities: [LOCKED, { ...EXTERNAL, userIdentityId: CALLER_IDENTITY }] });
    await render([READ, IDENTITY_READ, UNLOCK], CALLER_IDENTITY);
    await within(await section()).findByText("vr.ra");

    expect(screen.queryByRole("button", { name: /^Unlock/ })).toBeNull();
  });
});

// ---------------------------------------------------------------- ID-5

describe("unlocking", () => {
  it("requires a reason, and sends nothing without one", async () => {
    const state = backend();
    const { user } = await render([READ, IDENTITY_READ, UNLOCK]);
    await within(await section()).findByText("vr.ra");

    await user.click(screen.getByRole("button", { name: "Unlock vr.ra" }));
    const dialog = await screen.findByRole("dialog", { name: "Unlock vr.ra" });
    await user.click(within(dialog).getByRole("button", { name: "Unlock" }));

    expect(await within(dialog).findByText("A reason is required.")).toBeInTheDocument();
    expect(state.unlocks).toHaveLength(0);
  });

  it("sends exactly the reason, re-reads the identities, announces, and offers Unlock no more", async () => {
    const state = backend();
    const { user } = await render([READ, IDENTITY_READ, UNLOCK, ROLE_READ]);
    const region = await section();
    await within(region).findByText("vr.ra");
    await screen.findByRole("region", { name: "Roles" });

    const readsBefore = state.identityReads();
    const detailBefore = state.detailReads();
    const rolesBefore = state.roleReads();

    await user.click(screen.getByRole("button", { name: "Unlock vr.ra" }));
    const dialog = await screen.findByRole("dialog", { name: "Unlock vr.ra" });
    await user.type(within(dialog).getByLabelText("Reason"), "Confirmed by phone");
    await user.click(within(dialog).getByRole("button", { name: "Unlock" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });

    expect(state.unlocks).toEqual([
      { path: `/api/identities/${LOCKED.userIdentityId}/unlock`, body: { reason: "Confirmed by phone" } },
    ]);
    await waitFor(() => {
      expect(state.identityReads()).toBeGreaterThan(readsBefore);
    });
    expect(await screen.findByText("vr.ra was unlocked.")).toBeInTheDocument();
    // The unlocked identity's own row: the external one always read "Not locked".
    await waitFor(() => {
      const row = within(region).getByText("vr.ra").closest("tr");
      expect(row).not.toBeNull();
      expect(row).toHaveTextContent("Not locked");
    });
    expect(screen.queryByRole("button", { name: "Unlock vr.ra" })).toBeNull();

    // Nothing else is re-read: unlocking changes nothing they show.
    expect(state.detailReads()).toBe(detailBefore);
    expect(state.roleReads()).toBe(rolesBefore);
  });

  it("is busy while sending, and sends once however often Unlock is pressed", async () => {
    const state = backend({
      unlock: async () => {
        await delay(200);
        return new HttpResponse(null, { status: 204 });
      },
    });
    const { user } = await render([READ, IDENTITY_READ, UNLOCK]);
    await within(await section()).findByText("vr.ra");

    await user.click(screen.getByRole("button", { name: "Unlock vr.ra" }));
    const dialog = await screen.findByRole("dialog", { name: "Unlock vr.ra" });
    await user.type(within(dialog).getByLabelText("Reason"), "Confirmed by phone");
    const confirm = within(dialog).getByRole("button", { name: "Unlock" });
    await user.click(confirm);
    await user.click(confirm);

    expect(within(dialog).getByRole("button", { name: "Unlocking…" })).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });
    expect(state.unlocks).toHaveLength(1);
  });
});

// ---------------------------------------------------------------- ID-6

describe("refusals", () => {
  async function refused(message: string) {
    const state = backend({ unlock: () => HttpResponse.json({ error: message }, { status: 400 }) });
    const { user } = await render([READ, IDENTITY_READ, UNLOCK]);
    await within(await section()).findByText("vr.ra");
    const readsBefore = state.identityReads();

    await user.click(screen.getByRole("button", { name: "Unlock vr.ra" }));
    const dialog = await screen.findByRole("dialog", { name: "Unlock vr.ra" });
    await user.type(within(dialog).getByLabelText("Reason"), "Confirmed by phone");
    await user.click(within(dialog).getByRole("button", { name: "Unlock" }));

    expect(await within(dialog).findByText(message)).toBeInTheDocument();
    expect(screen.getByRole("dialog", { name: "Unlock vr.ra" })).toBeInTheDocument();

    return { state, readsBefore };
  }

  it("shows a refusal word for word and keeps the dialog open", async () => {
    await refused("This account cannot be unlocked.");
  });

  it("re-reads the identities when the lock is no longer in force", async () => {
    const { state, readsBefore } = await refused("This account is not currently locked.");

    await waitFor(() => {
      expect(state.identityReads()).toBeGreaterThan(readsBefore);
    });
  });

  it("shows the server's self-unlock refusal as the server words it", async () => {
    await refused("An administrator cannot unlock their own account.");
  });
});

// ---------------------------------------------------------------- ID-8

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

describe("the seeded roles (I8)", () => {
  it("shows a user administrator the identities, with Unlock on an eligible locked identity", async () => {
    const state = backend();
    await render(USER_ADMINISTRATOR);
    await within(await section()).findByText("vr.ra");

    expect(state.identityReads()).toBe(1);
    expect(screen.getByRole("button", { name: "Unlock vr.ra" })).toBeInTheDocument();
  });

  it("shows a security administrator neither, and makes no identities request", async () => {
    const state = backend();
    await render(SECURITY_ADMINISTRATOR);
    await settle();

    expect(screen.queryByRole("region", { name: "Sign-in identities" })).toBeNull();
    expect(state.identityReads()).toBe(0);
  });

  it("shows an access reviewer the identities, without Unlock", async () => {
    const state = backend();
    await render(ACCESS_REVIEWER);
    await within(await section()).findByText("vr.ra");

    expect(state.identityReads()).toBe(1);
    expect(screen.queryByRole("button", { name: /^Unlock/ })).toBeNull();
  });
});

// ---------------------------------------------------------------- ID-9

describe("accessibility", () => {
  it("has no violations with the section loaded", async () => {
    backend();
    await render([READ, IDENTITY_READ, UNLOCK]);
    await within(await section()).findByText("vr.ra");

    await expectNoAccessibilityViolations(document.body);
  });

  it("has no violations with the Unlock dialog open", async () => {
    backend();
    const { user } = await render([READ, IDENTITY_READ, UNLOCK]);
    await within(await section()).findByText("vr.ra");

    await user.click(screen.getByRole("button", { name: "Unlock vr.ra" }));
    await screen.findByRole("dialog", { name: "Unlock vr.ra" });

    await expectNoAccessibilityViolations(document.body);
  });

  it("has no violations while the section loads, and when its read was refused", async () => {
    backend({
      read: async () => {
        await delay(100);
        return HttpResponse.json({ error: "The user does not exist." }, { status: 400 });
      },
    });
    await render([READ, IDENTITY_READ]);

    await screen.findByRole("group", { name: "Loading sign-in identities" });
    await expectNoAccessibilityViolations(document.body);

    await within(await section()).findByText("The user does not exist.");
    await expectNoAccessibilityViolations(document.body);
  });
});
