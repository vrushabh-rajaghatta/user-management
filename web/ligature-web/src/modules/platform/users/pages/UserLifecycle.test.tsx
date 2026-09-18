import { act, screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import type { RouteObject } from "react-router";
import { describe, expect, it } from "vitest";
import { definePermission, type PermissionCode } from "@/shared/auth/permissions";
import { expectNoAccessibilityViolations } from "@/test/axe";
import { server } from "@/test/msw/server";
import { renderWithApp } from "@/test/renderWithApp";
import { TestSessionSource } from "@/test/sessions";
import { userKeys } from "../hooks/userKeys";
import { UsersPage } from "./UsersPage";

/**
 * USR-C4 / USR-C5 in the Users table (docs/requirements.md, "USR-C4 / USR-C5 —
 * the Users-table UI", and USR-Q1 Amendment 2).
 *
 * THESE TESTS ARE ABOUT AFFORDANCES, NOT AUTHORIZATION. What a row offers is
 * derived from its status, its activationPending and the caller's permissions;
 * the API decides what is accepted. So nothing here re-states a server rule:
 * self-deactivation, for instance, is the server's to refuse and its own tests
 * prove it. Here the refusal is only something the dialog shows word for word.
 */

const at = (path: string) => new URL(path, window.location.origin).href;

const READ = definePermission("user.read");
const CREATE = definePermission("user.create");
const RESET = definePermission("user.resetpassword");
const REVOKE = definePermission("session.revoke");
const DEACTIVATE = definePermission("user.deactivate");
const REACTIVATE = definePermission("user.reactivate");
const ROLE_READ = definePermission("role.read");
const ROLE_GRANT = definePermission("role.grant");

type Status = "Active" | "Inactive";

interface Row {
  userId: string;
  displayName: string;
  email: string;
  activationPending: boolean;
  status: Status;
}

/** The four lifecycle combinations; all of them occur after USR-C4 and USR-C5. */
const ACTIVE_PENDING: Row = {
  userId: "e1000000-0000-4000-8000-000000000001",
  displayName: "Active Pending",
  email: "active-pending@example.test",
  activationPending: true,
  status: "Active",
};
const ACTIVE_ACTIVATED: Row = {
  userId: "e1000000-0000-4000-8000-000000000002",
  displayName: "Active Activated",
  email: "active-activated@example.test",
  activationPending: false,
  status: "Active",
};
const INACTIVE_PENDING: Row = {
  userId: "e1000000-0000-4000-8000-000000000003",
  displayName: "Inactive Pending",
  email: "inactive-pending@example.test",
  activationPending: true,
  status: "Inactive",
};
const INACTIVE_ACTIVATED: Row = {
  userId: "e1000000-0000-4000-8000-000000000004",
  displayName: "Inactive Activated",
  email: "inactive-activated@example.test",
  activationPending: false,
  status: "Inactive",
};

const ROWS = [ACTIVE_ACTIVATED, ACTIVE_PENDING, INACTIVE_ACTIVATED, INACTIVE_PENDING];

interface Backend {
  readonly listReads: () => number;
  readonly posts: { path: string; body: unknown }[];
  rows: Row[];
}

/**
 * GET /api/users over mutable rows, and both lifecycle commands, which flip
 * the row's status as the server would — so a test proves the list was READ
 * AGAIN, not patched on the client.
 */
function backend(rows: Row[] = ROWS.map((x) => ({ ...x }))): Backend {
  let reads = 0;
  const state: Backend = { listReads: () => reads, posts: [], rows };

  server.use(
    http.get(at("/api/users"), () => {
      reads += 1;
      return HttpResponse.json({ users: state.rows, page: 1, pageSize: 25, hasMore: false });
    }),
    http.post(at("/api/users/:userId/:action"), async ({ request, params }) => {
      const action = String(params.action);

      if (action !== "deactivate" && action !== "reactivate") {
        return new HttpResponse(null, { status: 404 });
      }

      state.posts.push({ path: new URL(request.url).pathname, body: await request.json() });
      state.rows = state.rows.map((x) =>
        x.userId === params.userId ? { ...x, status: action === "deactivate" ? "Inactive" : "Active" } : x,
      );

      return new HttpResponse(null, { status: 204 });
    }),
    http.get(at("/api/users/:userId/role-assignments"), () => HttpResponse.json({ assignments: [] })),
    http.get(at("/api/roles"), () =>
      HttpResponse.json({ roles: [{ roleId: "b1000000-0000-4000-8000-0000000000a1", name: "Access Reviewer", description: null }] }),
    ),
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

  await screen.findByRole("table", { name: "Users" });

  return result;
}

const rowOf = (name: string) =>
  screen.getAllByRole("row").find((x) => within(x).queryByText(name, { exact: true }) !== null);

function requireRow(name: string): HTMLElement {
  const row = rowOf(name);

  if (row === undefined) {
    throw new Error(`No row for ${name}.`);
  }

  return row;
}

const actionsButton = (name: string) => screen.queryByRole("button", { name: new RegExp(`^Actions for ${name}`) });

/** The menu a row offers, sorted; empty when the row has no Actions button. */
async function menuOf(user: ReturnType<typeof renderWithApp>["user"], name: string): Promise<string[]> {
  const button = actionsButton(name);

  if (button === null) {
    return [];
  }

  await user.click(button);
  const menu = await screen.findByRole("menu");
  const items = within(menu)
    .getAllByRole("menuitem")
    .map((x) => x.textContent)
    .sort();

  await user.keyboard("{Escape}");
  await waitFor(() => {
    expect(screen.queryByRole("menu")).toBeNull();
  });

  return items;
}

/**
 * The contract's matrix, as data (USR-C4 / USR-C5 UI, "The action matrix").
 * The permission named in each cell shows the action; nothing else does.
 */
function expectedMenu(row: Row, holds: ReadonlySet<PermissionCode>): string[] {
  const menu: string[] = [];

  if (row.status === "Active") {
    if (holds.has(DEACTIVATE)) menu.push("Deactivate");
    if (row.activationPending && holds.has(CREATE)) menu.push("Resend activation link");
    if (!row.activationPending && holds.has(RESET)) menu.push("Reset password");
    if (holds.has(REVOKE)) menu.push("Sign out everywhere");
  } else if (holds.has(REACTIVATE)) {
    menu.push("Reactivate");
  }

  if (holds.has(ROLE_READ)) menu.push("Manage roles");

  return menu.sort();
}

// ---------------------------------------------------------------- U-M1, U-M2

const PERMISSION_SETS: { name: string; codes: PermissionCode[] }[] = [
  { name: "user.read only", codes: [READ] },
  { name: "user.create", codes: [READ, CREATE] },
  { name: "user.resetpassword", codes: [READ, RESET] },
  { name: "session.revoke", codes: [READ, REVOKE] },
  { name: "user.deactivate", codes: [READ, DEACTIVATE] },
  { name: "user.reactivate", codes: [READ, REACTIVATE] },
  { name: "the read-only reviewer (user.read, role.read)", codes: [READ, ROLE_READ] },
  { name: "everything", codes: [READ, CREATE, RESET, REVOKE, DEACTIVATE, REACTIVATE, ROLE_READ, ROLE_GRANT] },
];

describe("the action matrix", () => {
  it.each(PERMISSION_SETS)("offers each lifecycle state exactly its actions, to a caller holding $name", async ({ codes }) => {
    backend();
    const { user } = await render(...codes);
    const holds = new Set<PermissionCode>(codes);

    for (const row of ROWS) {
      expect({ row: row.displayName, menu: await menuOf(user, row.displayName) }).toEqual({
        row: row.displayName,
        menu: expectedMenu(row, holds),
      });
    }
  });

  it("offers no Deactivate on an inactive row, even to a caller who holds user.deactivate", async () => {
    backend();
    const { user } = await render(READ, DEACTIVATE, REACTIVATE);

    expect(await menuOf(user, "Inactive Activated")).not.toContain("Deactivate");
    expect(await menuOf(user, "Active Activated")).toContain("Deactivate");
  });

  it("offers no Reactivate on an active row, even to a caller who holds user.reactivate", async () => {
    backend();
    const { user } = await render(READ, DEACTIVATE, REACTIVATE);

    expect(await menuOf(user, "Active Pending")).not.toContain("Reactivate");
    expect(await menuOf(user, "Inactive Pending")).toContain("Reactivate");
  });

  it("gives an inactive row no Actions button when its only possible actions are withheld", async () => {
    backend();
    await render(READ, CREATE, RESET, REVOKE);

    expect(actionsButton("Inactive Pending")).toBeNull();
    expect(actionsButton("Inactive Activated")).toBeNull();
    expect(actionsButton("Active Activated")).not.toBeNull();
  });
});

// ---------------------------------------------------------------- U-M3

describe("the Inactive marker", () => {
  it("marks inactive rows, in text, and no active row", async () => {
    backend();
    await render(READ);

    expect(within(requireRow("Inactive Pending")).getByText("Inactive")).toBeInTheDocument();
    expect(within(requireRow("Inactive Activated")).getByText("Inactive")).toBeInTheDocument();
    expect(within(requireRow("Active Pending")).queryByText("Inactive")).toBeNull();
    expect(within(requireRow("Active Activated")).queryByText("Inactive")).toBeNull();
  });

  it("states an error rather than guessing when a row's status is neither Active nor Inactive", async () => {
    backend([{ ...ACTIVE_ACTIVATED, status: "Suspended" as Status }]);

    const source = new TestSessionSource();
    renderWithApp(routes(), { path: "/admin/users", source });

    await act(async () => {
      source.settle({ status: "authenticated", principal: { permissions: [{ code: READ }] } });
      await Promise.resolve();
    });

    expect(await screen.findByRole("alert")).toBeInTheDocument();
    expect(screen.queryByText("Active Activated")).toBeNull();
  });
});

// ---------------------------------------------------------------- U-D1, U-D2, U-D3

async function openAction(user: ReturnType<typeof renderWithApp>["user"], name: string, action: string) {
  const button = actionsButton(name);

  if (button === null) {
    throw new Error(`No Actions button for ${name}.`);
  }

  await user.click(button);
  await user.click(await screen.findByRole("menuitem", { name: action }));

  return screen.findByRole("dialog", { name: `${action} ${name}` });
}

describe("deactivating a user", () => {
  it("states the cascade, and that reactivation restores none of it", async () => {
    backend();
    const { user } = await render(READ, DEACTIVATE);

    const dialog = await openAction(user, "Active Activated", "Deactivate");

    expect(dialog).toHaveTextContent(
      "They'll be signed out everywhere and won't be able to sign in. All of their current and future roles are revoked, and reactivating them later will not restore any of them. Any activation or password-reset link they have stops working.",
    );
  });

  it("requires a reason before sending anything", async () => {
    const state = backend();
    const { user } = await render(READ, DEACTIVATE);

    const dialog = await openAction(user, "Active Activated", "Deactivate");

    await user.type(within(dialog).getByLabelText("Reason"), "   ");
    await user.click(within(dialog).getByRole("button", { name: "Deactivate" }));

    expect(within(dialog).getByRole("alert")).toHaveTextContent("A reason is required.");
    expect(state.posts).toEqual([]);
  });

  it("sends exactly the reason to the row's own route, announces it, and reads the list again", async () => {
    const state = backend();
    const { user } = await render(READ, DEACTIVATE, REACTIVATE, ROLE_READ);
    const readsBefore = state.listReads();

    const dialog = await openAction(user, "Active Activated", "Deactivate");

    await user.type(within(dialog).getByLabelText("Reason"), "Left the company.");
    await user.click(within(dialog).getByRole("button", { name: "Deactivate" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });

    expect(state.posts).toEqual([
      { path: `/api/users/${ACTIVE_ACTIVATED.userId}/deactivate`, body: { reason: "Left the company." } },
    ]);
    expect(screen.getByRole("status")).toHaveTextContent("Active Activated has been deactivated.");

    // U7 — read again, not patched: the marker and the actions are the server's.
    await waitFor(() => {
      expect(state.listReads()).toBeGreaterThan(readsBefore);
    });
    expect(await within(requireRow("Active Activated")).findByText("Inactive")).toBeInTheDocument();
    expect(await menuOf(user, "Active Activated")).toEqual(["Manage roles", "Reactivate"]);
  });

  /**
   * U-D2 and U4. The dialog shows the server's refusal word for word — here
   * the self rule, which the client does not and cannot anticipate.
   */
  it("keeps the dialog open and shows the server's refusal word for word", async () => {
    backend();
    server.use(
      http.post(at(`/api/users/${ACTIVE_ACTIVATED.userId}/deactivate`), () =>
        HttpResponse.json({ error: "A user cannot deactivate themselves." }, { status: 400 }),
      ),
    );

    const { user } = await render(READ, DEACTIVATE);
    const dialog = await openAction(user, "Active Activated", "Deactivate");

    await user.type(within(dialog).getByLabelText("Reason"), "Testing myself.");
    await user.click(within(dialog).getByRole("button", { name: "Deactivate" }));

    expect(await within(dialog).findByRole("alert")).toHaveTextContent("A user cannot deactivate themselves.");
    expect(screen.getByRole("dialog", { name: "Deactivate Active Activated" })).toBeInTheDocument();
  });

  /**
   * U7 — that user's role assignments are invalidated too: deactivation
   * revoked them. Seeded into the cache directly, because a remounted query
   * refetches on its own and would prove nothing.
   */
  it("invalidates that user's cached role assignments", async () => {
    backend();
    const { user, queryClient } = await render(READ, DEACTIVATE);

    const key = userKeys.roleAssignments(ACTIVE_ACTIVATED.userId, { includeInactive: false });
    queryClient.setQueryData(key, { assignments: [] });

    const dialog = await openAction(user, "Active Activated", "Deactivate");
    await user.type(within(dialog).getByLabelText("Reason"), "Left the company.");
    await user.click(within(dialog).getByRole("button", { name: "Deactivate" }));

    await waitFor(() => {
      expect(queryClient.getQueryState(key)?.isInvalidated).toBe(true);
    });
  });
});

describe("reactivating a user", () => {
  it("states that sign-in returns, no role does, and the two recovery paths", async () => {
    backend();
    const { user } = await render(READ, REACTIVATE);

    const dialog = await openAction(user, "Inactive Activated", "Reactivate");

    expect(dialog).toHaveTextContent(
      "They'll be able to sign in again, but they will have no roles: grant any access they need afresh. If they had activated their account, they sign in with their existing password. If they had not, send them a new activation link.",
    );
  });

  it("requires a reason before sending anything", async () => {
    const state = backend();
    const { user } = await render(READ, REACTIVATE);

    const dialog = await openAction(user, "Inactive Activated", "Reactivate");

    await user.click(within(dialog).getByRole("button", { name: "Reactivate" }));

    expect(within(dialog).getByRole("alert")).toHaveTextContent("A reason is required.");
    expect(state.posts).toEqual([]);
  });

  /** U5 — a pending user comes back to Resend, through the existing rule. */
  it("sends exactly the reason, announces it, and a pending user is offered Resend again", async () => {
    const state = backend();
    const { user } = await render(READ, CREATE, REACTIVATE, DEACTIVATE);

    const dialog = await openAction(user, "Inactive Pending", "Reactivate");

    await user.type(within(dialog).getByLabelText("Reason"), "Rehired.");
    await user.click(within(dialog).getByRole("button", { name: "Reactivate" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });

    expect(state.posts).toEqual([
      { path: `/api/users/${INACTIVE_PENDING.userId}/reactivate`, body: { reason: "Rehired." } },
    ]);
    expect(screen.getByRole("status")).toHaveTextContent("Inactive Pending has been reactivated.");

    await waitFor(() => {
      expect(within(requireRow("Inactive Pending")).queryByText("Inactive", { exact: true })).toBeNull();
    });
    expect(await menuOf(user, "Inactive Pending")).toEqual(["Deactivate", "Resend activation link"]);
  });

  /** U5 — an activated user comes back to Reset password. */
  it("offers an activated user Reset password once reactivated", async () => {
    backend();
    const { user } = await render(READ, RESET, REACTIVATE);

    const dialog = await openAction(user, "Inactive Activated", "Reactivate");
    await user.type(within(dialog).getByLabelText("Reason"), "Rehired.");
    await user.click(within(dialog).getByRole("button", { name: "Reactivate" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });

    await waitFor(async () => {
      expect(await menuOf(user, "Inactive Activated")).toEqual(["Reset password"]);
    });
  });
});

// ---------------------------------------------------------------- U-R1

describe("Manage roles by lifecycle status", () => {
  it("shows an inactive user's assignments and no Grant form", async () => {
    backend();
    const { user } = await render(READ, ROLE_READ, ROLE_GRANT);

    await user.click(actionsButton("Inactive Activated") ?? document.body);
    await user.click(await screen.findByRole("menuitem", { name: "Manage roles" }));

    const dialog = await screen.findByRole("dialog", { name: "Roles for Inactive Activated" });

    expect(await within(dialog).findByText("No current role assignments.")).toBeInTheDocument();
    expect(within(dialog).queryByRole("button", { name: "Grant role" })).toBeNull();
  });

  it("still shows the Grant form for an active user", async () => {
    backend();
    const { user } = await render(READ, ROLE_READ, ROLE_GRANT);

    await user.click(actionsButton("Active Activated") ?? document.body);
    await user.click(await screen.findByRole("menuitem", { name: "Manage roles" }));

    const dialog = await screen.findByRole("dialog", { name: "Roles for Active Activated" });

    expect(await within(dialog).findByRole("button", { name: "Grant role" })).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------- U-A1

describe("accessibility", () => {
  it("has no violations with inactive rows listed", async () => {
    backend();
    const { container } = await render(READ, DEACTIVATE, REACTIVATE, ROLE_READ);

    await expectNoAccessibilityViolations(container);
  });

  it("has no violations with the Deactivate confirmation open", async () => {
    backend();
    const { user } = await render(READ, DEACTIVATE);

    await openAction(user, "Active Activated", "Deactivate");

    await expectNoAccessibilityViolations(document.body);
  });

  it("has no violations with the Reactivate confirmation open", async () => {
    backend();
    const { user } = await render(READ, REACTIVATE);

    await openAction(user, "Inactive Activated", "Reactivate");

    await expectNoAccessibilityViolations(document.body);
  });
});
