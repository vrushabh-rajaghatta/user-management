import { act, screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import type { RouteObject } from "react-router";
import { describe, expect, it } from "vitest";
import { definePermission, type PermissionCode } from "@/shared/auth/permissions";
import { expectNoAccessibilityViolations } from "@/test/axe";
import { server } from "@/test/msw/server";
import { renderWithApp } from "@/test/renderWithApp";
import { TestSessionSource } from "@/test/sessions";
import { UsersPage } from "./UsersPage";

/**
 * Manage roles: AUT-Q2's read and AUT-C1 / AUT-C2 from the Users table
 * (docs/requirements.md, "AUT-Q2").
 *
 * What is offered comes from the caller's effective permissions, never from
 * role names. The assignment state is the server's: the UI shows it as sent
 * and derives nothing from the dates. The server is authoritative for every
 * refusal, and its message is shown word for word.
 */

const at = (path: string) => new URL(path, window.location.origin).href;

const READ = definePermission("user.read");
const ROLE_READ = definePermission("role.read");
const ROLE_GRANT = definePermission("role.grant");
const ROLE_REVOKE = definePermission("role.revoke");
const RESET = definePermission("user.resetpassword");

const VRU = {
  userId: "d1000000-0000-4000-8000-00000000f001",
  displayName: "Vru Raj",
  email: "vru@example.test",
  activationPending: false,
};

const ADA = { userId: "a1000000-0000-4000-8000-0000000000ad", displayName: "Ada Lovelace" };

const REVIEWER = { roleId: "b1000000-0000-4000-8000-0000000000a1", name: "Access Reviewer", description: "Read-only review." };
const USER_ADMIN = { roleId: "b1000000-0000-4000-8000-0000000000a2", name: "User Administrator", description: null };

type State = "Active" | "Future" | "Ended" | "Revoked";

function assignment(id: string, roleName: string, state: State, overrides: Record<string, unknown> = {}) {
  return {
    assignmentId: id,
    roleId: roleName === REVIEWER.name ? REVIEWER.roleId : USER_ADMIN.roleId,
    roleName,
    effectiveFrom: "2026-09-01T09:00:00Z",
    effectiveTo: null,
    state,
    assignedAt: "2026-08-31T10:00:00Z",
    assignedBy: ADA,
    assignmentReason: "Quarterly review; REQ-7.",
    revokedAt: null,
    revokedBy: null,
    revocationReason: null,
    ...overrides,
  };
}

const ACTIVE = assignment("c1000000-0000-4000-8000-000000000001", REVIEWER.name, "Active");
const FUTURE = assignment("c1000000-0000-4000-8000-000000000002", USER_ADMIN.name, "Future", {
  effectiveFrom: "2026-10-01T09:00:00Z",
});
const ENDED = assignment("c1000000-0000-4000-8000-000000000003", USER_ADMIN.name, "Ended", {
  effectiveFrom: "2026-06-01T09:00:00Z",
  effectiveTo: "2026-07-01T09:00:00Z",
});
const REVOKED = assignment("c1000000-0000-4000-8000-000000000004", REVIEWER.name, "Revoked", {
  effectiveFrom: "2026-10-05T09:00:00Z",
  effectiveTo: "2026-10-05T09:00:00Z",
  revokedAt: "2026-09-18T10:00:00Z",
  revokedBy: ADA,
  revocationReason: "Offer withdrawn.",
});

interface Backend {
  readonly reads: string[];
  readonly grants: unknown[];
  readonly revokes: { path: string; body: unknown }[];
  current: ReturnType<typeof assignment>[];
  history: ReturnType<typeof assignment>[];
}

/**
 * The Users list, the assignments of VRU (current by default, history on
 * request), the grantable roles, and both commands — each recording what the
 * page sent, so a test proves what reached the server.
 */
function backend(): Backend {
  const state: Backend = { reads: [], grants: [], revokes: [], current: [ACTIVE, FUTURE], history: [ENDED, REVOKED] };

  server.use(
    http.get(at("/api/users"), () => HttpResponse.json({ users: [VRU], page: 1, pageSize: 25, hasMore: false })),
    http.get(at(`/api/users/${VRU.userId}/role-assignments`), ({ request }) => {
      const search = new URL(request.url).search;
      state.reads.push(search);
      const includeInactive = new URL(request.url).searchParams.get("includeInactive") === "true";
      return HttpResponse.json({ assignments: includeInactive ? [...state.current, ...state.history] : state.current });
    }),
    http.get(at("/api/roles"), () => HttpResponse.json({ roles: [REVIEWER, USER_ADMIN] })),
    http.post(at(`/api/users/${VRU.userId}/role-assignments`), async ({ request }) => {
      state.grants.push(await request.json());
      state.current = [...state.current, assignment("c1000000-0000-4000-8000-0000000000ff", USER_ADMIN.name, "Active")];
      return HttpResponse.json({ userRoleAssignmentId: "c1000000-0000-4000-8000-0000000000ff" }, { status: 201 });
    }),
    http.post(at("/api/role-assignments/:id/revoke"), async ({ request }) => {
      state.revokes.push({ path: new URL(request.url).pathname, body: await request.json() });
      state.current = state.current.filter((x) => !request.url.includes(x.assignmentId));
      return new HttpResponse(null, { status: 204 });
    }),
  );

  return state;
}

function routes(): RouteObject[] {
  return [{ path: "/admin/users", element: <UsersPage /> }];
}

/** Settles the session before any assertion, for UsersPage.test.tsx's reason. */
async function render(...codes: PermissionCode[]) {
  const source = new TestSessionSource();
  const result = renderWithApp(routes(), { path: "/admin/users", source });

  await act(async () => {
    source.settle({ status: "authenticated", principal: { permissions: codes.map((code) => ({ code })) } });
    await Promise.resolve();
  });

  return result;
}

const actions = () => screen.findByRole("button", { name: /^Actions for Vru Raj/ });

async function openManageRoles(...codes: PermissionCode[]) {
  const rendered = await render(READ, ROLE_READ, ...codes);

  await rendered.user.click(await actions());
  await rendered.user.click(await screen.findByRole("menuitem", { name: "Manage roles" }));

  const dialog = await screen.findByRole("dialog", { name: "Roles for Vru Raj" });
  await within(dialog).findByRole("table", { name: "Role assignments" });

  return { ...rendered, dialog };
}

const rowFor = (dialog: HTMLElement, roleName: string, state: State) =>
  within(within(dialog).getByRole("table", { name: "Role assignments" }))
    .getAllByRole("row")
    .find((row) => row.textContent.includes(roleName) && row.textContent.includes(state));

/** The row, or a failure naming which one was missing. */
function requireRow(dialog: HTMLElement, roleName: string, state: State): HTMLElement {
  const row = rowFor(dialog, roleName, state);

  if (row === undefined) {
    throw new Error(`No ${state} ${roleName} row in the assignments.`);
  }

  return row;
}

// ---------------------------------------------------------------- who is offered what

describe("Manage roles is offered by permission", () => {
  it("is offered to a caller who holds role.read", async () => {
    backend();
    const { user } = await render(READ, ROLE_READ);

    await user.click(await actions());

    expect(await screen.findByRole("menuitem", { name: "Manage roles" })).toBeInTheDocument();
  });

  /** role.read alone makes the Actions button appear on a row with nothing else to offer. */
  it("gives a row an Actions button when Manage roles is its only action", async () => {
    backend();
    await render(READ, ROLE_READ);

    expect(await actions()).toBeInTheDocument();
  });

  it("is not offered without role.read, even to a caller who can grant and revoke", async () => {
    backend();
    const { user } = await render(READ, ROLE_GRANT, ROLE_REVOKE, RESET);

    await user.click(await actions());
    const menu = await screen.findByRole("menu");

    expect(within(menu).queryByRole("menuitem", { name: "Manage roles" })).toBeNull();
  });

  /** The access reviewer's path: the assignments, and nothing to do with them. */
  it("shows a role.read-only caller the assignments with no Grant and no Revoke", async () => {
    backend();
    const { dialog } = await openManageRoles();

    expect(rowFor(dialog, REVIEWER.name, "Active")).toBeDefined();
    expect(within(dialog).queryByRole("button", { name: "Grant role" })).toBeNull();
    expect(within(dialog).queryByRole("button", { name: /^Revoke / })).toBeNull();
  });

  it("offers Grant only with role.grant", async () => {
    backend();
    const { dialog } = await openManageRoles(ROLE_GRANT);

    expect(within(dialog).getByRole("button", { name: "Grant role" })).toBeInTheDocument();
    expect(within(dialog).queryByRole("button", { name: /^Revoke / })).toBeNull();
  });

  /** Revoke on Active and Future only: an ended or revoked assignment has nothing left to close. */
  it("offers Revoke with role.revoke on Active and Future assignments only", async () => {
    backend();
    const { dialog, user } = await openManageRoles(ROLE_REVOKE);

    await user.click(within(dialog).getByRole("switch", { name: "Show history" }));
    await waitFor(() => {
      expect(rowFor(dialog, USER_ADMIN.name, "Ended")).toBeDefined();
    });

    for (const [roleName, state, offered] of [
      [REVIEWER.name, "Active", true],
      [USER_ADMIN.name, "Future", true],
      [USER_ADMIN.name, "Ended", false],
      [REVIEWER.name, "Revoked", false],
    ] as const) {
      const row = requireRow(dialog, roleName, state);
      expect(within(row).queryByRole("button", { name: `Revoke ${roleName}` }) !== null).toBe(offered);
    }

    expect(within(dialog).queryByRole("button", { name: "Grant role" })).toBeNull();
  });
});

// ---------------------------------------------------------------- the read

describe("the assignments", () => {
  it("asks for current assignments first, and history only when asked", async () => {
    const state = backend();
    const { dialog, user } = await openManageRoles();

    expect(state.reads[0]).not.toContain("includeInactive=true");
    expect(rowFor(dialog, USER_ADMIN.name, "Ended")).toBeUndefined();

    await user.click(within(dialog).getByRole("switch", { name: "Show history" }));

    await waitFor(() => {
      expect(state.reads.at(-1)).toContain("includeInactive=true");
    });
    expect(await within(dialog).findByText("Offer withdrawn.")).toBeInTheDocument();
    expect(rowFor(dialog, REVIEWER.name, "Revoked")).toBeDefined();
  });

  it("shows each assignment's role, state, who granted it and why", async () => {
    backend();
    const { dialog } = await openManageRoles();

    const row = requireRow(dialog, REVIEWER.name, "Active");

    expect(within(row).getByText("Active")).toBeInTheDocument();
    expect(within(row).getByText(/Ada Lovelace/)).toBeInTheDocument();
    expect(within(row).getByText("Quarterly review; REQ-7.")).toBeInTheDocument();
  });

  /**
   * The state is the server's. An assignment whose dates have long passed but
   * which the server calls Active is shown as Active: the client derives
   * nothing.
   */
  it("shows the state exactly as the server sent it, whatever the dates say", async () => {
    const state = backend();
    state.current = [
      assignment("c1000000-0000-4000-8000-0000000000aa", REVIEWER.name, "Active", {
        effectiveFrom: "2020-01-01T00:00:00Z",
        effectiveTo: "2020-02-01T00:00:00Z",
      }),
    ];

    const { dialog } = await openManageRoles();

    expect(rowFor(dialog, REVIEWER.name, "Active")).toBeDefined();
    expect(within(dialog).queryByText("Ended")).toBeNull();
  });
});

// ---------------------------------------------------------------- granting

describe("granting a role", () => {
  it("sends the chosen role, local times as UTC, and the reason; then reads again", async () => {
    const state = backend();
    const { dialog, user } = await openManageRoles(ROLE_GRANT);
    const readsBefore = state.reads.length;

    await user.selectOptions(within(dialog).getByLabelText("Role"), USER_ADMIN.roleId);
    await user.type(within(dialog).getByLabelText("Starts"), "2026-10-01T09:00");
    await user.type(within(dialog).getByLabelText("Reason"), "Covering for the team lead; REQ-9.");
    await user.click(within(dialog).getByRole("button", { name: "Grant role" }));

    await waitFor(() => {
      expect(state.grants).toHaveLength(1);
    });

    expect(state.grants[0]).toEqual({
      roleId: USER_ADMIN.roleId,
      effectiveFrom: new Date("2026-10-01T09:00").toISOString(),
      reason: "Covering for the team lead; REQ-9.",
    });

    await waitFor(() => {
      expect(state.reads.length).toBeGreaterThan(readsBefore);
    });
  });

  it("offers only the active roles the server lists", async () => {
    backend();
    const { dialog } = await openManageRoles(ROLE_GRANT);

    const options = within(within(dialog).getByLabelText("Role")).getAllByRole("option");

    expect(options.map((x) => x.textContent)).toEqual(
      expect.arrayContaining([REVIEWER.name, USER_ADMIN.name]),
    );
  });

  it("requires a role and a reason before sending anything", async () => {
    const state = backend();
    const { dialog, user } = await openManageRoles(ROLE_GRANT);

    await user.click(within(dialog).getByRole("button", { name: "Grant role" }));

    expect(await within(dialog).findAllByRole("alert")).not.toHaveLength(0);
    expect(state.grants).toHaveLength(0);
  });

  /** The server decides overlap; its refusal is shown word for word and the dialog stays open. */
  it("shows the server's refusal word for word and stays open", async () => {
    backend();
    server.use(
      http.post(at(`/api/users/${VRU.userId}/role-assignments`), () =>
        HttpResponse.json(
          { error: "The user already holds this role for this scope in an overlapping period." },
          { status: 400 },
        ),
      ),
    );

    const { dialog, user } = await openManageRoles(ROLE_GRANT);

    await user.selectOptions(within(dialog).getByLabelText("Role"), REVIEWER.roleId);
    await user.type(within(dialog).getByLabelText("Reason"), "Again.");
    await user.click(within(dialog).getByRole("button", { name: "Grant role" }));

    expect(
      await within(dialog).findByText("The user already holds this role for this scope in an overlapping period."),
    ).toBeInTheDocument();
    expect(screen.getByRole("dialog", { name: "Roles for Vru Raj" })).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------- revoking

describe("revoking a role", () => {
  it("sends exactly the reason to the assignment's revoke endpoint, then reads again", async () => {
    const state = backend();
    const { dialog, user } = await openManageRoles(ROLE_REVOKE);
    const readsBefore = state.reads.length;

    await user.click(within(requireRow(dialog, USER_ADMIN.name, "Future")).getByRole("button", { name: `Revoke ${USER_ADMIN.name}` }));

    const confirm = await screen.findByRole("dialog", { name: `Revoke ${USER_ADMIN.name} from Vru Raj` });

    await user.type(within(confirm).getByLabelText("Reason"), "Offer withdrawn.");
    await user.click(within(confirm).getByRole("button", { name: "Revoke role" }));

    await waitFor(() => {
      expect(state.revokes).toHaveLength(1);
    });

    expect(state.revokes[0]).toEqual({
      path: `/api/role-assignments/${FUTURE.assignmentId}/revoke`,
      body: { reason: "Offer withdrawn." },
    });

    await waitFor(() => {
      expect(state.reads.length).toBeGreaterThan(readsBefore);
    });
  });

  it("requires a reason before sending anything", async () => {
    const state = backend();
    const { dialog, user } = await openManageRoles(ROLE_REVOKE);

    await user.click(within(requireRow(dialog, REVIEWER.name, "Active")).getByRole("button", { name: `Revoke ${REVIEWER.name}` }));

    const confirm = await screen.findByRole("dialog", { name: `Revoke ${REVIEWER.name} from Vru Raj` });

    await user.click(within(confirm).getByRole("button", { name: "Revoke role" }));

    expect(within(confirm).getByRole("alert")).toHaveTextContent("A reason is required.");
    expect(state.revokes).toHaveLength(0);
  });
});

// ---------------------------------------------------------------- accessibility

describe("Manage roles accessibility", () => {
  it("has no violations with the dialog open for a caller who can grant and revoke", async () => {
    backend();
    await openManageRoles(ROLE_GRANT, ROLE_REVOKE);

    await expectNoAccessibilityViolations(document.body);
  });
});
