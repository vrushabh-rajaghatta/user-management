import { act, screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { appRoutes } from "@/app/router";
import { RolePermissions } from "@/modules/platform/roles";
import { definePermission, type PermissionCode } from "@/shared/auth/permissions";
import { expectNoAccessibilityViolations } from "@/test/axe";
import { server } from "@/test/msw/server";
import { renderWithApp } from "@/test/renderWithApp";
import { TestSessionSource } from "@/test/sessions";

/**
 * USR-Q3 on the User detail page (docs/requirements.md, "USR-Q3
 * GetUserAccessSummary", UA-U1 to UA-U6).
 *
 * The page gains an Effective permissions section, so the composition it
 * shows becomes: Profile (USR-Q1), Roles (AUT-Q2), Effective permissions
 * (USR-Q3) — what was granted, and what is in effect.
 *
 * THE GUARD IS THE POINT (UA2). The section needs user.read AND role.read,
 * and a user.read-only caller must not merely fail to see it: the request must
 * never be made. That caller can read this very user's profile, which is
 * exactly why the boundary needs a test on this page and not only on the
 * server.
 */

const at = (path: string) => new URL(path, window.location.origin).href;

const USER_READ = definePermission("user.read");
const ROLE_READ = RolePermissions.read;
const DEACTIVATE = definePermission("user.deactivate");

const ADA = {
  userId: "d1000000-0000-4000-8000-000000000001",
  firstName: "Ada",
  lastName: "Lovelace",
  displayName: "Ada Lovelace",
  email: "ada@example.test",
  status: "Active",
  activationPending: false,
};

const PERMISSIONS = [
  { code: "role.read", scopeType: "Global", scopeId: null },
  { code: "user.read", scopeType: "Global", scopeId: null },
];

interface Backend {
  readonly setReads: () => number;
}

function backend(
  options: { permissions?: unknown[]; status?: string; fail?: boolean } = {},
): Backend {
  let setReads = 0;

  // The SERVER's state, so a command can change it and the next read sees
  // something different — which is what a transition test needs.
  let status = options.status ?? ADA.status;
  let permissions = options.permissions ?? PERMISSIONS;

  server.use(
    http.get(at("/api/users/:userId"), () => HttpResponse.json({ ...ADA, status })),
    http.get(at("/api/users/:userId/role-assignments"), () => HttpResponse.json({ assignments: [] })),
    http.get(at("/api/users/:userId/effective-permissions"), () => {
      setReads += 1;

      if (options.fail === true) {
        return HttpResponse.json({ error: "The effective permissions could not be read." }, { status: 400 });
      }

      return HttpResponse.json({ userId: ADA.userId, status, permissions });
    }),

    // USR-C4: deactivation empties the set, because the actor gate refuses an
    // inactive user.
    http.post(at("/api/users/:userId/deactivate"), () => {
      status = "Inactive";
      permissions = [];

      return new HttpResponse(null, { status: 204 });
    }),
    http.get(at("/api/users/:userId/identities"), () => HttpResponse.json({ identities: [] })),
    http.get(at("/api/users/:userId/sessions"), () => HttpResponse.json({ sessions: [] })),
    http.get(at("/api/users"), () =>
      HttpResponse.json({ users: [{ ...ADA, status }], page: 1, pageSize: 25, hasMore: false }),
    ),
  );

  return { setReads: () => setReads };
}

async function render(codes: PermissionCode[]) {
  const source = new TestSessionSource();
  const result = renderWithApp(appRoutes, { path: `/admin/users/${ADA.userId}`, source });

  await act(async () => {
    source.settle({ status: "authenticated", principal: { permissions: codes.map((code) => ({ code })) } });
    await Promise.resolve();
  });

  await screen.findByRole("heading", { level: 1, name: ADA.displayName });

  return result;
}

// ---------------------------------------------------------------- UA-U1

describe("who sees the effective permissions", () => {
  it("shows the section to a caller holding user.read and role.read", async () => {
    backend();
    await render([USER_READ, ROLE_READ]);

    expect(await screen.findByRole("heading", { name: "Effective permissions" })).toBeInTheDocument();
  });

  /**
   * THE BYPASS TEST, on the client side. This caller can read the profile —
   * the page rendered — and must not be able to read what the user can do.
   */
  it("hides the section, and never reads it, without role.read", async () => {
    const api = backend();
    await render([USER_READ]);

    // The page itself is there: this caller is entitled to the profile.
    expect(screen.getByRole("heading", { level: 1, name: ADA.displayName })).toBeInTheDocument();

    expect(screen.queryByRole("heading", { name: "Effective permissions" })).toBeNull();

    // NOT MERELY HIDDEN. A section rendered and discarded would still have
    // asked the server for the answer.
    await waitFor(() => {
      expect(api.setReads()).toBe(0);
    });
  });
});

// ---------------------------------------------------------------- UA-U2

describe("what the section shows", () => {
  it("lists the permission codes", async () => {
    backend();
    await render([USER_READ, ROLE_READ]);

    const section = within(await screen.findByRole("region", { name: "Effective permissions" }));

    expect(await section.findByText("role.read")).toBeInTheDocument();
    expect(section.getByText("user.read")).toBeInTheDocument();
  });

  it("shows a non-global scope where there is one", async () => {
    backend({
      permissions: [{ code: "document.approve", scopeType: "Project", scopeId: "p-1" }],
    });
    await render([USER_READ, ROLE_READ]);

    const row = within(await screen.findByRole("row", { name: /document\.approve/ }));

    // THE ID, NOT JUST THE TYPE. Asserting only "Project" passes against a
    // cell that prints the type and drops the id, which is a different scope
    // from the one the server sent.
    expect(row.getByText(/Project/)).toBeInTheDocument();
    expect(row.getByText(/p-1/)).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------- UA-U3

describe("an empty set explains itself", () => {
  it("says the user is inactive when they are", async () => {
    backend({ permissions: [], status: "Inactive" });
    await render([USER_READ, ROLE_READ]);

    const section = within(await screen.findByRole("region", { name: "Effective permissions" }));

    expect(await section.findByText(/inactive/i)).toBeInTheDocument();
  });

  it("says there are none when the user is active and holds none", async () => {
    backend({ permissions: [] });
    await render([USER_READ, ROLE_READ]);

    const section = within(await screen.findByRole("region", { name: "Effective permissions" }));
    const empty = await section.findByText(/no effective permissions/i);

    expect(empty).toBeInTheDocument();

    // And it must NOT claim the user is inactive, because they are not.
    expect(section.queryByText(/inactive/i)).toBeNull();
  });
});

// -------------------------------------------------- the transition (UA-U3)

describe("after a command changes what the user can do", () => {
  /**
   * THE DEFECT THIS TEST EXISTS FOR, found in the browser and not by any test
   * here: after Deactivate, the page showed the user as Inactive while this
   * section still read "No effective permissions." — the stale answer's
   * explanation, not merely its data.
   *
   * No test in this file could have caught it, because none of them performed
   * an action: the defect lives entirely in the transition. Nor could the
   * mutation campaign, because a missing invalidation has no code to delete.
   */
  it("re-reads the set after the user is deactivated, and explains the new emptiness", async () => {
    backend();
    const { user } = await render([USER_READ, ROLE_READ, DEACTIVATE]);

    const section = within(await screen.findByRole("region", { name: "Effective permissions" }));

    await section.findByText("role.read");

    await user.click(screen.getByRole("button", { name: "Actions" }));
    await user.click(await screen.findByRole("menuitem", { name: "Deactivate" }));

    const dialog = await screen.findByRole("dialog");

    await user.type(within(dialog).getByRole("textbox", { name: /reason/i }), "No longer with us.");
    await user.click(within(dialog).getByRole("button", { name: "Deactivate" }));

    // The section says WHY it is now empty, rather than keeping the answer it
    // was given while the user was still active.
    expect(
      await within(screen.getByRole("region", { name: "Effective permissions" })).findByText(/inactive/i),
    ).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------- UA-U4

describe("a failed read", () => {
  it("shows the server's sentence and leaves the page standing", async () => {
    backend({ fail: true });
    await render([USER_READ, ROLE_READ]);

    expect(await screen.findByText("The effective permissions could not be read.")).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 1, name: ADA.displayName })).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------- UA-U5

describe("the section offers nothing", () => {
  it("offers no action on a permission", async () => {
    backend();
    await render([USER_READ, ROLE_READ]);

    const section = within(await screen.findByRole("region", { name: "Effective permissions" }));

    await section.findByText("role.read");

    expect(section.queryByRole("button")).toBeNull();
    expect(section.queryByRole("link")).toBeNull();
  });
});

// ---------------------------------------------------------------- UA-U6

describe("accessibility", () => {
  it("has no violations with the section present", async () => {
    backend();
    const { container } = await render([USER_READ, ROLE_READ]);

    await screen.findByRole("heading", { name: "Effective permissions" });
    await expectNoAccessibilityViolations(container);
  });
});
