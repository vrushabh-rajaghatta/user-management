import { act, screen, waitFor, within } from "@testing-library/react";
import { delay, http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { appRoutes } from "@/app/router";
import { definePermission, type PermissionCode } from "@/shared/auth/permissions";
import { expectNoAccessibilityViolations } from "@/test/axe";
import { server } from "@/test/msw/server";
import { renderWithApp } from "@/test/renderWithApp";
import { TestSessionSource } from "@/test/sessions";
import { userKeys } from "../hooks/userKeys";

/**
 * The User detail page (docs/requirements.md, "USR-Q1 GetUser v2 and the User
 * detail page (story 1)", DV-3..DV-9), through the application's own routes.
 *
 * THE PAGE IS COMPOSED FROM INDEPENDENTLY AUTHORISED READS (G2). The page
 * needs user.read; Roles needs role.read, and without it its request is NOT
 * MADE — these tests count requests, not only what renders.
 *
 * THE SERVER IS THE SOURCE OF TRUTH (G5). After an action the page re-reads
 * GetUser; nothing is patched locally.
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
const UPDATE = definePermission("user.update");

type Status = "Active" | "Inactive";

interface Detail {
  userId: string;
  firstName: string;
  lastName: string;
  displayName: string;
  email: string | null;
  status: Status;
  activationPending: boolean;
}

const ADA: Detail = {
  userId: "f2000000-0000-4000-8000-00000000ada1",
  firstName: "Augusta Ada",
  lastName: "King",
  displayName: "Ada L.",
  email: "ada@example.test",
  status: "Active",
  activationPending: false,
};

const USER_ADMIN_ROLE = "f2000000-0000-4000-8000-0000000000b2";

const ASSIGNMENT = {
  assignmentId: "f2000000-0000-4000-8000-0000000000a1",
  roleId: "f2000000-0000-4000-8000-0000000000b1",
  roleName: "Access Reviewer",
  effectiveFrom: "2026-09-01T00:00:00Z",
  effectiveTo: null,
  state: "Active",
  assignedAt: "2026-09-01T00:00:00Z",
  assignedBy: { userId: "f2000000-0000-4000-8000-0000000000c1", displayName: "Grace H." },
  assignmentReason: "Quarterly review",
  revokedAt: null,
  revokedBy: null,
  revocationReason: null,
};

interface Backend {
  detail: Detail;
  readonly detailReads: () => number;
  readonly listReads: () => number;
  readonly roleReads: () => number;
  readonly posts: string[];
}

/**
 * GetUser over a mutable detail (actions change it as the server would), the
 * list, AUT-Q2, and the lifecycle commands — every request counted.
 */
function backend(
  options: {
    detail?: Partial<Detail>;
    getUser?: () => Response | Promise<Response>;
    roles?: () => Response | Promise<Response>;
    assignments?: unknown[];
  } = {},
): Backend {
  let detailReads = 0;
  let listReads = 0;
  let roleReads = 0;
  const state: Backend = {
    detail: { ...ADA, ...options.detail },
    detailReads: () => detailReads,
    listReads: () => listReads,
    roleReads: () => roleReads,
    posts: [],
  };

  server.use(
    http.get(at("/api/users"), () => {
      listReads += 1;
      const { userId, displayName, email, activationPending, status } = state.detail;
      return HttpResponse.json({
        users: [{ userId, displayName, email, activationPending, status }],
        page: 1,
        pageSize: 25,
        hasMore: false,
      });
    }),
    http.get(at("/api/users/:userId"), ({ params }) => {
      detailReads += 1;
      if (options.getUser !== undefined) {
        return options.getUser();
      }
      return params.userId === state.detail.userId
        ? HttpResponse.json(state.detail)
        : HttpResponse.json({ error: "The user does not exist." }, { status: 400 });
    }),
    http.get(at("/api/users/:userId/role-assignments"), () => {
      roleReads += 1;
      return options.roles !== undefined
        ? options.roles()
        : HttpResponse.json({ assignments: options.assignments ?? [ASSIGNMENT] });
    }),
    http.get(at("/api/roles"), () =>
      HttpResponse.json({
        roles: [
          { roleId: ASSIGNMENT.roleId, name: "Access Reviewer", description: null },
          { roleId: USER_ADMIN_ROLE, name: "User Administrator", description: null },
        ],
      }),
    ),
    http.post(at("/api/users/:userId/:action"), ({ request, params }) => {
      const action = String(params.action);
      state.posts.push(new URL(request.url).pathname);
      if (action === "deactivate") state.detail = { ...state.detail, status: "Inactive" };
      if (action === "reactivate") state.detail = { ...state.detail, status: "Active" };
      return new HttpResponse(null, { status: 204 });
    }),
  );

  return state;
}

const detailPath = (userId = ADA.userId) => `/admin/users/${userId}`;

async function render(codes: PermissionCode[], path = detailPath()) {
  const source = new TestSessionSource();
  const result = renderWithApp(appRoutes, { path, source });

  await act(async () => {
    source.settle({ status: "authenticated", principal: { permissions: codes.map((code) => ({ code })) } });
    await Promise.resolve();
  });

  return result;
}

async function header(name = ADA.displayName) {
  return screen.findByRole("heading", { level: 1, name });
}

type User = ReturnType<typeof renderWithApp>["user"];

/** The page's actions, sorted; empty when it offers none. */
async function pageMenu(user: User): Promise<string[]> {
  const button = screen.queryByRole("button", { name: "Actions" });

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

async function choose(user: User, item: string) {
  await user.click(screen.getByRole("button", { name: "Actions" }));
  await user.click(await screen.findByRole("menuitem", { name: item }));
}

// ---------------------------------------------------------------- DV-3

describe("reaching the page", () => {
  it("is linked from the display name in the Users table", async () => {
    backend();
    const { user, router } = await render([READ], "/admin/users");

    const link = await screen.findByRole("link", { name: ADA.displayName });
    expect(link).toHaveAttribute("href", detailPath());

    await user.click(link);

    expect(await header()).toBeInTheDocument();
    expect(router.state.location.pathname).toBe(detailPath());
  });

  it("renders the header from GetUser alone on a direct load, without reading the list", async () => {
    const state = backend();
    await render([READ]);

    expect(await header()).toBeInTheDocument();
    expect(screen.getByText("Augusta Ada King")).toBeInTheDocument();
    expect(screen.getByText("ada@example.test")).toBeInTheDocument();
    expect(state.listReads()).toBe(0);
    expect(state.detailReads()).toBe(1);
  });

  it("says No email address when the user has none", async () => {
    backend({ detail: { email: null } });
    await render([READ]);

    await header();
    expect(screen.getByText("No email address")).toBeInTheDocument();
  });

  it("marks an inactive user and a user pending activation, in words", async () => {
    backend({ detail: { status: "Inactive", activationPending: true } });
    await render([READ]);

    await header();
    expect(screen.getByText("Inactive")).toBeInTheDocument();
    expect(screen.getByText("Pending activation")).toBeInTheDocument();
  });

  it("shows no marker for an active, activated user", async () => {
    backend();
    await render([READ]);

    await header();
    expect(screen.queryByText("Inactive")).toBeNull();
    expect(screen.queryByText("Pending activation")).toBeNull();
  });

  it("keeps Users current in the Administration navigation", async () => {
    backend();
    await render([READ]);

    await header();
    const nav = screen.getByRole("navigation", { name: "Administration" });
    expect(within(nav).getByRole("link", { name: "Users" })).toHaveAttribute("aria-current", "page");
  });

  it("shows the denied state, and reads nothing, without user.read", async () => {
    const state = backend();
    await render([ROLE_READ]);

    expect(await screen.findByRole("heading", { level: 1, name: "Not available" })).toBeInTheDocument();
    expect(state.detailReads()).toBe(0);
    expect(state.roleReads()).toBe(0);
  });
});

// ---------------------------------------------------------------- DV-4

describe("loading and errors", () => {
  it("shows a skeleton while GetUser loads", async () => {
    backend({
      getUser: async () => {
        await delay(200);
        return HttpResponse.json(ADA);
      },
    });
    await render([READ]);

    expect(await screen.findByRole("group", { name: "Loading user" })).toHaveAttribute("aria-busy", "true");
    expect(await header()).toBeInTheDocument();
  });

  it("states a failure word for word, and Try again reads again", async () => {
    let calls = 0;
    const state = backend({
      getUser: () => {
        calls += 1;
        return calls === 1
          ? HttpResponse.json({ error: "The request could not be completed." }, { status: 500 })
          : HttpResponse.json(ADA);
      },
    });
    const { user } = await render([READ]);

    expect(await screen.findByText("The request could not be completed.")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Try again" }));

    expect(await header()).toBeInTheDocument();
    expect(state.detailReads()).toBe(2);
  });

  it("states an unknown user as the server words it, without redirecting", async () => {
    backend();
    const { router } = await render([READ], detailPath("f2000000-0000-4000-8000-00000000dead"));

    expect(await screen.findByText("The user does not exist.")).toBeInTheDocument();
    expect(router.state.location.pathname).toBe(detailPath("f2000000-0000-4000-8000-00000000dead"));
  });
});

// ---------------------------------------------------------------- DV-5

/** The Users table's matrix, as data: the page must offer exactly the same. */
function expectedMenu(detail: Detail, holds: ReadonlySet<PermissionCode>): string[] {
  const menu: string[] = [];

  if (detail.status === "Active") {
    if (holds.has(DEACTIVATE)) menu.push("Deactivate");
    if (detail.activationPending && holds.has(CREATE)) menu.push("Resend activation link");
    if (!detail.activationPending && holds.has(RESET)) menu.push("Reset password");
    if (holds.has(REVOKE)) menu.push("Sign out everywhere");
  } else if (holds.has(REACTIVATE)) {
    menu.push("Reactivate");
  }

  if (holds.has(ROLE_READ)) menu.push("Manage roles");
  if (holds.has(UPDATE)) menu.push("Edit profile");

  return menu.sort();
}

const COMBINATIONS: Partial<Detail>[] = [
  { status: "Active", activationPending: false },
  { status: "Active", activationPending: true },
  { status: "Inactive", activationPending: false },
  { status: "Inactive", activationPending: true },
];

const PERMISSION_SETS: { name: string; codes: PermissionCode[] }[] = [
  { name: "user.read only", codes: [READ] },
  { name: "user.create", codes: [READ, CREATE] },
  { name: "user.resetpassword", codes: [READ, RESET] },
  { name: "session.revoke", codes: [READ, REVOKE] },
  { name: "user.deactivate", codes: [READ, DEACTIVATE] },
  { name: "user.reactivate", codes: [READ, REACTIVATE] },
  { name: "user.update", codes: [READ, UPDATE] },
  { name: "role.read", codes: [READ, ROLE_READ] },
  { name: "everything", codes: [READ, CREATE, RESET, REVOKE, DEACTIVATE, REACTIVATE, ROLE_READ, ROLE_GRANT, UPDATE] },
];

describe("the action matrix", () => {
  for (const combination of COMBINATIONS) {
    it.each(PERMISSION_SETS)(
      `offers ${combination.status ?? ""}${combination.activationPending === true ? " pending" : ""} exactly the row's actions, to $name`,
      async ({ codes }) => {
        backend({ detail: combination });
        const { user } = await render(codes);
        await header();

        expect(await pageMenu(user)).toEqual(expectedMenu({ ...ADA, ...combination }, new Set(codes)));
      },
    );
  }
});

// ---------------------------------------------------------------- DV-6

describe("after an action, the server is read again", () => {
  it("re-reads GetUser and invalidates the list after Deactivate, and shows what the server now says", async () => {
    const state = backend();
    const { user, queryClient } = await render([READ, DEACTIVATE]);
    await header();
    queryClient.setQueryData(userKeys.list({ page: 1 }), { users: [], page: 1, pageSize: 25, hasMore: false });

    await choose(user, "Deactivate");
    const dialog = await screen.findByRole("dialog", { name: `Deactivate ${ADA.displayName}` });
    await user.type(within(dialog).getByLabelText("Reason"), "Left the company");
    await user.click(within(dialog).getByRole("button", { name: "Deactivate" }));

    await waitFor(() => {
      expect(state.detailReads()).toBe(2);
    });
    expect(state.posts).toEqual([`/api/users/${ADA.userId}/deactivate`]);
    expect(await screen.findByText("Inactive")).toBeInTheDocument();
    expect(queryClient.getQueryState(userKeys.list({ page: 1 }))?.isInvalidated).toBe(true);
  });

  it("re-reads GetUser and AUT-Q2, and invalidates the list, after Manage roles changes something", async () => {
    const state = backend();
    server.use(
      http.post(at("/api/users/:userId/role-assignments"), () =>
        HttpResponse.json({ userRoleAssignmentId: "f2000000-0000-4000-8000-0000000000a9" }, { status: 201 }),
      ),
    );
    const { user, queryClient } = await render([READ, ROLE_READ, ROLE_GRANT]);
    await header();
    await screen.findByRole("region", { name: "Roles" });
    queryClient.setQueryData(userKeys.list({ page: 1 }), { users: [], page: 1, pageSize: 25, hasMore: false });

    const rolesBefore = state.roleReads();
    const detailBefore = state.detailReads();

    await user.click(within(screen.getByRole("region", { name: "Roles" })).getByRole("button", { name: "Manage roles" }));
    const dialog = await screen.findByRole("dialog", { name: `Roles for ${ADA.displayName}` });
    await user.selectOptions(await within(dialog).findByLabelText("Role"), USER_ADMIN_ROLE);
    await user.type(within(dialog).getByLabelText("Reason"), "Needs administration access");
    await user.click(within(dialog).getByRole("button", { name: "Grant role" }));

    await waitFor(() => {
      expect(state.roleReads()).toBeGreaterThan(rolesBefore);
      expect(state.detailReads()).toBeGreaterThan(detailBefore);
    });
    expect(queryClient.getQueryState(userKeys.list({ page: 1 }))?.isInvalidated).toBe(true);
  });
});

// ---------------------------------------------------------------- DV-7

describe("the Roles section", () => {
  it("lists the current assignments, with the server's state as sent, to a role.read holder", async () => {
    const state = backend();
    await render([READ, ROLE_READ]);
    await header();

    const roles = await screen.findByRole("region", { name: "Roles" });
    const table = await within(roles).findByRole("table");

    expect(within(table).getByRole("columnheader", { name: "Role" })).toBeInTheDocument();
    expect(within(table).getByRole("columnheader", { name: "From" })).toBeInTheDocument();
    expect(within(table).getByRole("columnheader", { name: "To" })).toBeInTheDocument();
    expect(within(table).getByRole("columnheader", { name: "State" })).toBeInTheDocument();
    expect(within(table).getByText("Access Reviewer")).toBeInTheDocument();
    expect(within(table).getByText("No end date")).toBeInTheDocument();
    expect(within(table).getByText("Active")).toBeInTheDocument();

    // Provenance is Manage roles' to show, not this page's.
    expect(within(roles).queryByText("Quarterly review")).toBeNull();
    expect(within(roles).queryByText("Grace H.")).toBeNull();

    // Current only: AUT-Q2's default, never includeInactive.
    expect(state.roleReads()).toBe(1);
  });

  it("asks AUT-Q2 for current assignments only", async () => {
    let url: URL | undefined;
    backend();
    server.use(
      http.get(at("/api/users/:userId/role-assignments"), ({ request }) => {
        url = new URL(request.url);
        return HttpResponse.json({ assignments: [] });
      }),
    );
    await render([READ, ROLE_READ]);
    await header();
    await screen.findByRole("region", { name: "Roles" });

    await waitFor(() => {
      expect(url).toBeDefined();
    });
    expect(url?.searchParams.get("includeInactive")).not.toBe("true");
  });

  it("shows the server's state word for word, whatever it is", async () => {
    backend({ assignments: [{ ...ASSIGNMENT, state: "Future", effectiveTo: "2027-01-01T00:00:00Z" }] });
    await render([READ, ROLE_READ]);
    await header();

    const roles = await screen.findByRole("region", { name: "Roles" });
    expect(await within(roles).findByText("Future")).toBeInTheDocument();
    expect(within(roles).queryByText("No end date")).toBeNull();
  });

  it("says No current roles. when there are none", async () => {
    backend({ assignments: [] });
    await render([READ, ROLE_READ]);
    await header();

    const roles = await screen.findByRole("region", { name: "Roles" });
    expect(await within(roles).findByText("No current roles.")).toBeInTheDocument();
  });

  it("is absent, and its request is never made, without role.read", async () => {
    const state = backend();
    await render([READ, UPDATE, DEACTIVATE]);
    await header();

    // Give any stray request the chance to happen before counting.
    await act(async () => {
      await delay(50);
    });

    expect(screen.queryByRole("region", { name: "Roles" })).toBeNull();
    expect(state.roleReads()).toBe(0);
  });

  it("states its own failure with Try again, and the rest of the page stands", async () => {
    let calls = 0;
    backend({
      roles: () => {
        calls += 1;
        return calls === 1
          ? HttpResponse.json({ error: "The request could not be completed." }, { status: 500 })
          : HttpResponse.json({ assignments: [ASSIGNMENT] });
      },
    });
    const { user } = await render([READ, ROLE_READ]);
    await header();

    const roles = await screen.findByRole("region", { name: "Roles" });
    expect(await within(roles).findByText("The request could not be completed.")).toBeInTheDocument();
    expect(screen.getByText("ada@example.test")).toBeInTheDocument();

    await user.click(within(roles).getByRole("button", { name: "Try again" }));
    expect(await within(roles).findByText("Access Reviewer")).toBeInTheDocument();
  });

  it("offers Manage roles in the section, which opens the existing dialog", async () => {
    backend();
    const { user } = await render([READ, ROLE_READ]);
    await header();

    const roles = await screen.findByRole("region", { name: "Roles" });
    await user.click(within(roles).getByRole("button", { name: "Manage roles" }));

    expect(await screen.findByRole("dialog", { name: `Roles for ${ADA.displayName}` })).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------- DV-8

/** The seeded compositions (PlatformProvisioner), as a client would receive them from /me. */
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

describe("the permission split between the seeded roles (G2)", () => {
  it("shows a user administrator the detail, with no Roles request and no Roles section", async () => {
    const state = backend();
    await render(USER_ADMINISTRATOR);
    await header();

    await act(async () => {
      await delay(50);
    });

    expect(screen.getByText("ada@example.test")).toBeInTheDocument();
    expect(state.roleReads()).toBe(0);
    expect(screen.queryByRole("region", { name: "Roles" })).toBeNull();
  });

  it("shows a security administrator the detail, with the Roles request made and the section shown", async () => {
    const state = backend();
    await render(SECURITY_ADMINISTRATOR);
    await header();

    expect(await screen.findByRole("region", { name: "Roles" })).toBeInTheDocument();
    expect(screen.getByText("ada@example.test")).toBeInTheDocument();
    expect(state.roleReads()).toBe(1);
  });
});

// ---------------------------------------------------------------- DV-9

describe("accessibility", () => {
  it("has no violations on the loaded page, with Roles", async () => {
    backend();
    await render([READ, ROLE_READ, UPDATE]);
    await header();
    await screen.findByRole("region", { name: "Roles" });

    await expectNoAccessibilityViolations(document.body);
  });

  it("has no violations with a dialog open", async () => {
    backend();
    const { user } = await render([READ, UPDATE]);
    await header();

    await choose(user, "Edit profile");
    await screen.findByRole("dialog", { name: `Edit profile for ${ADA.displayName}` });

    await expectNoAccessibilityViolations(document.body);
  });

  it("has no violations while loading, and when a read failed", async () => {
    backend({
      getUser: async () => {
        await delay(100);
        return HttpResponse.json({ error: "The request could not be completed." }, { status: 500 });
      },
    });
    await render([READ]);

    await screen.findByRole("group", { name: "Loading user" });
    await expectNoAccessibilityViolations(document.body);

    await screen.findByText("The request could not be completed.");
    await expectNoAccessibilityViolations(document.body);
  });
});
