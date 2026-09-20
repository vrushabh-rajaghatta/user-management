import { act, screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { appRoutes } from "@/app/router";
import { definePermission, type PermissionCode } from "@/shared/auth/permissions";
import { expectNoAccessibilityViolations } from "@/test/axe";
import { server } from "@/test/msw/server";
import { renderWithApp } from "@/test/renderWithApp";
import { TestSessionSource } from "@/test/sessions";

/**
 * The Roles administration screen (docs/requirements.md, "Role administration
 * read", RA-U1 to RA-U7).
 *
 * READ-ONLY. Neither page offers an action, and the only requests either makes
 * are AUT-Q5 and AUT-Q3. The derived values are the server's: the screen shows
 * agentAssignable, it never computes it.
 */

const at = (path: string) => new URL(path, window.location.origin).href;

const ROLE_READ = definePermission("role.read");
const USER_READ = definePermission("user.read");

const REVIEWER = {
  roleId: "b5000000-0000-4000-8000-000000000001",
  code: "access-reviewer",
  name: "Access Reviewer",
  description: "Reads the directory for review.",
  isSystemRole: true,
  isActive: true,
  agentAssignable: true,
  permissionCount: 6,
  activeHolderCount: 2,
};

const RETIRED = {
  roleId: "b5000000-0000-4000-8000-000000000002",
  code: "tenant-retired",
  name: "Retired Tenant Role",
  description: null,
  isSystemRole: false,
  isActive: false,
  agentAssignable: false,
  permissionCount: 1,
  activeHolderCount: 0,
};

const GRANTS = [
  {
    rolePermissionId: "b5000000-0000-4000-8000-000000000011",
    permissionId: "b5000000-0000-4000-8000-000000000012",
    code: "user.create",
    name: "Create user",
    resource: "user",
    action: "create",
    requiresHumanActor: true,
    grantedAt: "2026-01-01T00:00:00Z",
    revokedAt: null,
  },
  {
    rolePermissionId: "b5000000-0000-4000-8000-000000000013",
    permissionId: "b5000000-0000-4000-8000-000000000014",
    code: "user.read",
    name: "Read users",
    resource: "user",
    action: "read",
    requiresHumanActor: false,
    grantedAt: "2026-01-01T00:00:00Z",
    revokedAt: null,
  },
];

interface Backend {
  readonly lists: string[];
  readonly grantReads: string[];
}

function backend(options: { failList?: boolean; grants?: () => Response } = {}): Backend {
  const lists: string[] = [];
  const grantReads: string[] = [];
  let listCalls = 0;

  server.use(
    http.get(at("/api/roles/administration"), ({ request }) => {
      listCalls += 1;
      const url = new URL(request.url);
      lists.push(url.search);

      if (options.failList === true && listCalls === 1) {
        return HttpResponse.json({ error: "The roles could not be read." }, { status: 400 });
      }

      const roles = url.searchParams.get("includeInactive") === "true" ? [REVIEWER, RETIRED] : [REVIEWER];

      return HttpResponse.json({ roles });
    }),
    http.get(at("/api/roles/:roleId/permissions"), ({ params }) => {
      grantReads.push(String(params.roleId));

      if (options.grants !== undefined) {
        return options.grants();
      }

      return String(params.roleId) === REVIEWER.roleId
        ? HttpResponse.json({ permissions: GRANTS })
        : HttpResponse.json({ error: "The role does not exist." }, { status: 404 });
    }),
  );

  return { lists, grantReads };
}

async function render(codes: PermissionCode[], path = "/admin/roles") {
  const source = new TestSessionSource();
  const result = renderWithApp(appRoutes, { path, source });

  await act(async () => {
    source.settle({ status: "authenticated", principal: { permissions: codes.map((code) => ({ code })) } });
    await Promise.resolve();
  });

  return result;
}

async function table() {
  return screen.findByRole("table", { name: "Roles" });
}

// ---------------------------------------------------------------- RA-U1

describe("who is offered Roles", () => {
  it("is in the Administration navigation for a role.read holder", async () => {
    backend();
    await render([USER_READ, ROLE_READ], "/admin/users");

    expect(await screen.findByRole("link", { name: "Roles" })).toBeInTheDocument();
  });

  it("is not in the navigation without role.read", async () => {
    backend();
    await render([USER_READ], "/admin/users");

    await screen.findByRole("heading", { level: 1, name: "Users" });
    expect(screen.queryByRole("link", { name: "Roles" })).toBeNull();
  });
});

// ---------------------------------------------------------------- RA-U2

describe("the roles list", () => {
  it("shows a row per role, with its six columns", async () => {
    backend();
    await render([ROLE_READ]);

    const header = within(await table()).getAllByRole("columnheader").map((cell) => cell.textContent);

    expect(header).toEqual(["Name", "Code", "Status", "Agent-assignable", "Permissions", "Holders"]);

    const reviewer = within(await table()).getByRole("row", { name: /Access Reviewer/ });

    expect(within(reviewer).getByText("access-reviewer")).toBeInTheDocument();
    expect(within(reviewer).getByText("Active")).toBeInTheDocument();
    expect(within(reviewer).getByText("Yes")).toBeInTheDocument();
    expect(within(reviewer).getByText("6")).toBeInTheDocument();
    expect(within(reviewer).getByText("2")).toBeInTheDocument();
  });

  it("reads again with includeInactive when inactive roles are shown, and marks them", async () => {
    const state = backend();
    const { user } = await render([ROLE_READ]);

    await table();
    expect(state.lists).toEqual([""]);

    await user.click(screen.getByRole("checkbox", { name: "Show inactive roles" }));

    await waitFor(() => {
      expect(state.lists).toEqual(["", "?includeInactive=true"]);
    });

    const retired = await within(await table()).findByRole("row", { name: /Retired Tenant Role/ });

    expect(within(retired).getByText("Inactive")).toBeInTheDocument();
    expect(within(retired).getByText("No")).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------- RA-U3

describe("a failed read", () => {
  it("shows the server's sentence and reads again on Try again", async () => {
    const state = backend({ failList: true });
    const { user } = await render([ROLE_READ]);

    expect(await screen.findByText("The roles could not be read.")).toBeInTheDocument();
    expect(screen.queryByRole("table", { name: "Roles" })).toBeNull();

    await user.click(screen.getByRole("button", { name: "Try again" }));

    await table();
    expect(state.lists).toHaveLength(2);
  });
});

// ---------------------------------------------------------------- RA-U4, RA-U5

describe("the role detail", () => {
  it("shows the role and its permissions, marking the human-only ones", async () => {
    const state = backend();
    await render([ROLE_READ], `/admin/roles/${REVIEWER.roleId}`);

    await screen.findByRole("heading", { level: 1, name: "Access Reviewer" });

    expect(screen.getByText("access-reviewer")).toBeInTheDocument();
    expect(screen.getByText("Reads the directory for review.")).toBeInTheDocument();

    const permissions = await screen.findByRole("table", { name: "Permissions" });
    const created = within(permissions).getByRole("row", { name: /user\.create/ });

    expect(within(created).getByText("Create user")).toBeInTheDocument();
    expect(within(created).getByText("Human only")).toBeInTheDocument();
    expect(within(permissions).getByRole("row", { name: /user\.read/ })).toBeInTheDocument();
    expect(state.grantReads).toEqual([REVIEWER.roleId]);
  });

  it("shows a not-found state for a role that does not exist", async () => {
    backend();
    await render([ROLE_READ], "/admin/roles/b5000000-0000-4000-8000-0000000000ff");

    expect(await screen.findByText("The role does not exist.")).toBeInTheDocument();
    expect(screen.queryByRole("table", { name: "Permissions" })).toBeNull();
  });
});

// ---------------------------------------------------------------- RA-U6

describe("read-only", () => {
  it("offers no action on either page, and sends only its reads", async () => {
    const state = backend();
    await render([ROLE_READ]);
    await table();

    // The only control is the inactive-roles switch; no menus, no buttons that act.
    expect(screen.queryByRole("button", { name: /Actions/ })).toBeNull();
    expect(screen.queryByRole("menuitem")).toBeNull();

    expect(state.lists).toHaveLength(1);
    expect(state.grantReads).toHaveLength(0);
  });
});

// ---------------------------------------------------------------- RA-U7

describe("accessibility", () => {
  it("has no violations on the list or the detail", async () => {
    backend();
    const { unmount } = await render([ROLE_READ]);

    await table();
    await expectNoAccessibilityViolations(document.body);

    unmount();

    await render([ROLE_READ], `/admin/roles/${REVIEWER.roleId}`);
    await screen.findByRole("table", { name: "Permissions" });
    await expectNoAccessibilityViolations(document.body);
  });
});
