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
 * AUT-Q4 on the Role detail page (docs/requirements.md, "AUT-Q4
 * GetRoleMembers", RH-U1 to RH-U7).
 *
 * The Holders count stops being a dead number on the metadata list and becomes
 * a section that says who they are.
 *
 * TWO THINGS THIS SECTION MUST NOT DO. It must not appear for a caller who
 * cannot see the user directory (RH9 makes user.read part of the read itself),
 * and it must not offer any action — revoking a holding belongs to AUT-C2 and
 * the Users module (RH11).
 *
 * The count shown is AUT-Q4's, not the roles list's, because the list computes
 * its count at its own instant and this section answers at asOf.
 */

const at = (path: string) => new URL(path, window.location.origin).href;

const ROLE_READ = definePermission("role.read");
const USER_READ = definePermission("user.read");
const ROLE_MANAGE = definePermission("role.manage");

const ROLE = {
  roleId: "c1000000-0000-4000-8000-000000000001",
  code: "quality-reviewer",
  name: "Quality Reviewer",
  description: null,
  isSystemRole: false,
  isActive: true,
  agentAssignable: true,
  permissionCount: 0,
  // DELIBERATELY WRONG, and never displayed: the section reads its own count.
  activeHolderCount: 99,
};

const HOLDER = {
  assignmentId: "c1000000-0000-4000-8000-000000000011",
  userId: "c1000000-0000-4000-8000-000000000012",
  displayName: "Ada Lovelace",
  email: "ada@example.test",
  status: "Active",
  effectiveFrom: "2026-01-01T00:00:00Z",
  effectiveTo: null,
  assignedAt: "2026-01-01T00:00:00Z",
  assignedBy: { userId: "c1000000-0000-4000-8000-000000000013", displayName: "The System" },
  assignmentReason: "Onboarding, regulatory affairs associate.",
};

const DEPARTED = {
  ...HOLDER,
  assignmentId: "c1000000-0000-4000-8000-000000000021",
  userId: "c1000000-0000-4000-8000-000000000022",
  displayName: "Grace Hopper",
  email: null,
  status: "Inactive",
  effectiveTo: "2030-01-01T00:00:00Z",
};

interface Backend {
  readonly memberReads: () => number;
}

function backend(options: { members?: unknown[]; fail?: boolean } = {}): Backend {
  let memberReads = 0;
  const members = options.members ?? [HOLDER, DEPARTED];

  server.use(
    http.get(at("/api/roles/administration"), () => HttpResponse.json({ roles: [ROLE] })),
    http.get(at("/api/roles/:roleId/permissions"), () => HttpResponse.json({ permissions: [] })),
    http.get(at("/api/roles/:roleId/members"), () => {
      memberReads += 1;

      if (options.fail === true) {
        return HttpResponse.json({ error: "The holders could not be read." }, { status: 400 });
      }

      return HttpResponse.json({
        asOf: "2026-09-21T00:00:00Z",
        activeHolderCount: new Set(members.map((x) => (x as { userId: string }).userId)).size,
        members,
      });
    }),
  );

  return { memberReads: () => memberReads };
}

async function render(codes: PermissionCode[]) {
  const source = new TestSessionSource();
  const result = renderWithApp(appRoutes, { path: `/admin/roles/${ROLE.roleId}`, source });

  await act(async () => {
    source.settle({ status: "authenticated", principal: { permissions: codes.map((code) => ({ code })) } });
    await Promise.resolve();
  });

  await screen.findByRole("table", { name: "Permissions" });

  return result;
}

function holders() {
  return screen.getByRole("table", { name: "Holders" });
}

// ---------------------------------------------------------------- RH-U1

describe("who sees the holders", () => {
  it("shows the section to a caller holding role.read and user.read", async () => {
    backend();
    await render([ROLE_READ, USER_READ]);

    expect(await screen.findByRole("heading", { name: "Holders" })).toBeInTheDocument();
  });

  it("hides the section, and does not read it, without user.read", async () => {
    const api = backend();
    await render([ROLE_READ]);

    expect(screen.queryByRole("heading", { name: "Holders" })).toBeNull();

    // NOT MERELY HIDDEN: the request is never made. A section rendered and
    // then discarded would still have disclosed the directory.
    await waitFor(() => expect(api.memberReads()).toBe(0));
  });

  /**
   * Without role.read the whole route is denied, so the section cannot be
   * reached at all — which is why this does not wait for the page the way the
   * others do. Asserted anyway: the section's own guard must never become the
   * only thing standing between a caller and the directory.
   */
  it("hides the section without role.read, because the route itself is denied", async () => {
    const api = backend();
    const source = new TestSessionSource();

    renderWithApp(appRoutes, { path: `/admin/roles/${ROLE.roleId}`, source });

    await act(async () => {
      source.settle({ status: "authenticated", principal: { permissions: [{ code: USER_READ }] } });
      await Promise.resolve();
    });

    expect(screen.queryByRole("heading", { name: "Holders" })).toBeNull();
    expect(screen.queryByRole("table", { name: "Permissions" })).toBeNull();
    await waitFor(() => expect(api.memberReads()).toBe(0));
  });
});

// ---------------------------------------------------------------- RH-U2

describe("what the section shows", () => {
  it("shows a row per holder with its six columns", async () => {
    backend();
    await render([ROLE_READ, USER_READ]);

    const row = within(await screen.findByRole("row", { name: /Ada Lovelace/ }));

    expect(row.getByText("ada@example.test")).toBeInTheDocument();
    expect(row.getByText("Active")).toBeInTheDocument();
    expect(row.getByText("Onboarding, regulatory affairs associate.")).toBeInTheDocument();

    // No end date reads as words, as the user detail page's roles table does.
    expect(within(holders()).getByText("No end date")).toBeInTheDocument();
  });

  it("shows an inactive holder rather than hiding one", async () => {
    backend();
    await render([ROLE_READ, USER_READ]);

    const row = within(await screen.findByRole("row", { name: /Grace Hopper/ }));

    expect(row.getByText("Inactive")).toBeInTheDocument();
  });

  it("shows the count from the member read, not from the roles list", async () => {
    backend();
    await render([ROLE_READ, USER_READ]);

    const section = within(screen.getByRole("region", { name: "Holders" }));

    expect(await section.findByText(/\b2\b/)).toBeInTheDocument();
    expect(section.queryByText(/\b99\b/)).toBeNull();
  });
});

// ---------------------------------------------------------------- RH-U3

describe("the link to a holder", () => {
  it("links each holder to their user detail page", async () => {
    backend();
    await render([ROLE_READ, USER_READ]);

    const link = within(await screen.findByRole("row", { name: /Ada Lovelace/ }))
      .getByRole("link", { name: "Ada Lovelace" });

    expect(link).toHaveAttribute("href", `/admin/users/${HOLDER.userId}`);
  });
});

// ---------------------------------------------------------------- RH-U4

describe("a role nobody holds", () => {
  it("shows an empty state rather than an empty table", async () => {
    backend({ members: [] });
    await render([ROLE_READ, USER_READ]);

    expect(await screen.findByText(/no (current )?holders/i)).toBeInTheDocument();
    expect(screen.queryByRole("table", { name: "Holders" })).toBeNull();
  });
});

// ---------------------------------------------------------------- RH-U5

describe("a failed read", () => {
  it("shows the server's sentence, retries, and leaves the permissions standing", async () => {
    backend({ fail: true });
    await render([ROLE_READ, USER_READ]);

    expect(await screen.findByText("The holders could not be read.")).toBeInTheDocument();

    // The rest of the page is untouched by this section's failure.
    expect(screen.getByRole("table", { name: "Permissions" })).toBeInTheDocument();

    const api = backend();
    const before = api.memberReads();

    (await screen.findByRole("button", { name: /Try again/i })).click();

    await waitFor(() => expect(api.memberReads()).toBeGreaterThan(before));
  });
});

// ---------------------------------------------------------------- RH-U6

describe("the section offers nothing", () => {
  it("offers no action on a holder, even to a role.manage holder", async () => {
    backend();
    await render([ROLE_READ, USER_READ, ROLE_MANAGE]);

    const section = within(screen.getByRole("region", { name: "Holders" }));

    expect(section.queryByRole("button", { name: /Revoke/i })).toBeNull();
    expect(section.queryByRole("button", { name: /Remove/i })).toBeNull();
    expect(section.queryByRole("button", { name: /Grant/i })).toBeNull();
  });
});

// ---------------------------------------------------------------- RH-U7

describe("accessibility", () => {
  it("has no violations with the Holders section present", async () => {
    backend();
    const { container } = await render([ROLE_READ, USER_READ]);

    await screen.findByRole("heading", { name: "Holders" });
    await expectNoAccessibilityViolations(container);
  });
});
