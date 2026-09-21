import { act, screen, waitFor, within } from "@testing-library/react";
import { delay, http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { appRoutes } from "@/app/router";
import { definePermission, type PermissionCode } from "@/shared/auth/permissions";
import { expectNoAccessibilityViolations } from "@/test/axe";
import { server } from "@/test/msw/server";
import { renderWithApp } from "@/test/renderWithApp";
import { TestSessionSource } from "@/test/sessions";

/**
 * AUT-C7/C8 on the Role detail page (docs/requirements.md, "AUT-C7
 * AddPermissionToRole and AUT-C8 RemovePermissionFromRole", RG9; RG-U1 to
 * RG-U7).
 *
 * The permissions table stops being a read-only list. Add permission picks
 * from AUT-Q6's catalogue MINUS what the role already holds and minus retired
 * entries (RG-U2), and each live grant gains Revoke, which asks for a reason.
 *
 * RP6's refusal carries its remediation, and the dialog shows it word for
 * word — the catalogue requires the remediation to be IN the message.
 */

const at = (path: string) => new URL(path, window.location.origin).href;

const ROLE_READ = definePermission("role.read");
const ROLE_MANAGE = definePermission("role.manage");

const TENANT = {
  roleId: "b9000000-0000-4000-8000-000000000001",
  code: "quality-reviewer",
  name: "Quality Reviewer",
  description: null,
  isSystemRole: false,
  isActive: true,
  agentAssignable: true,
  permissionCount: 1,
  activeHolderCount: 0,
};

const SEEDED = { ...TENANT, roleId: "b9000000-0000-4000-8000-000000000002", isSystemRole: true };

const HELD = {
  rolePermissionId: "b9000000-0000-4000-8000-000000000011",
  permissionId: "b9000000-0000-4000-8000-000000000012",
  code: "user.read",
  name: "View users",
  resource: "User",
  action: "Read",
  requiresHumanActor: false,
  grantedAt: "2026-01-01T00:00:00Z",
  revokedAt: null,
};

const CATALOGUE = [
  { permissionId: HELD.permissionId, code: "user.read", name: "View users", resource: "User", action: "Read", requiresHumanActor: false, isActive: true },
  { permissionId: "b9000000-0000-4000-8000-000000000013", code: "user.create", name: "Create user", resource: "User", action: "Create", requiresHumanActor: true, isActive: true },
  { permissionId: "b9000000-0000-4000-8000-000000000014", code: "legacy.read", name: "Retired thing", resource: "Legacy", action: "Read", requiresHumanActor: false, isActive: false },
];

interface Backend {
  readonly grantReads: () => number;
  readonly posts: { path: string; body: unknown }[];
}

function backend(options: { refuse?: string; slow?: boolean; empty?: boolean } = {}): Backend {
  let grantReads = 0;
  let grants = options.empty === true ? [] : [HELD];
  const posts: { path: string; body: unknown }[] = [];

  server.use(
    http.get(at("/api/roles/administration"), () => HttpResponse.json({ roles: [TENANT, SEEDED] })),
    http.get(at("/api/permissions"), () => HttpResponse.json({ permissions: CATALOGUE })),
    http.get(at("/api/roles/:roleId/permissions"), () => {
      grantReads += 1;

      return HttpResponse.json({ permissions: grants });
    }),
    http.post(at("/api/roles/:roleId/permissions"), async ({ request }) => {
      posts.push({ path: "add", body: await request.json() });

      if (options.slow === true) {
        await delay(200);
      }

      if (options.refuse !== undefined) {
        return HttpResponse.json({ error: options.refuse }, { status: 400 });
      }

      grants = [...grants, { ...HELD, rolePermissionId: "new-grant", code: "user.create" }];

      return HttpResponse.json({ rolePermissionId: "new-grant" }, { status: 201 });
    }),
    http.post(at("/api/role-permissions/:grantId/revoke"), async ({ request }) => {
      posts.push({ path: "revoke", body: await request.json() });

      if (options.refuse !== undefined) {
        return HttpResponse.json({ error: options.refuse }, { status: 400 });
      }

      grants = [];

      return new HttpResponse(null, { status: 204 });
    }),
  );

  return { grantReads: () => grantReads, posts };
}

async function render(codes: PermissionCode[], roleId = TENANT.roleId) {
  const source = new TestSessionSource();
  const result = renderWithApp(appRoutes, { path: `/admin/roles/${roleId}`, source });

  await act(async () => {
    source.settle({ status: "authenticated", principal: { permissions: codes.map((code) => ({ code })) } });
    await Promise.resolve();
  });

  await screen.findByRole("table", { name: "Permissions" });

  return result;
}

// ---------------------------------------------------------------- RG-U1

describe("who is offered the permission actions", () => {
  it("offers Add permission and Revoke to a role.manage holder on a tenant role", async () => {
    backend();
    await render([ROLE_READ, ROLE_MANAGE]);

    expect(screen.getByRole("button", { name: "Add permission" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Revoke user\.read/ })).toBeInTheDocument();
  });

  it("offers neither to a role.read holder", async () => {
    backend();
    await render([ROLE_READ]);

    expect(screen.queryByRole("button", { name: "Add permission" })).toBeNull();
    expect(screen.queryByRole("button", { name: /Revoke/ })).toBeNull();
  });

  it("offers neither on a release-owned role", async () => {
    backend();
    await render([ROLE_READ, ROLE_MANAGE], SEEDED.roleId);

    expect(screen.queryByRole("button", { name: "Add permission" })).toBeNull();
    expect(screen.queryByRole("button", { name: /Revoke/ })).toBeNull();
  });
});

// ---------------------------------------------------------------- RG-U2

describe("what the picker offers", () => {
  it("offers the catalogue minus what the role holds and minus retired entries", async () => {
    backend();
    const { user } = await render([ROLE_READ, ROLE_MANAGE]);

    await user.click(screen.getByRole("button", { name: "Add permission" }));

    const dialog = await screen.findByRole("dialog");
    const options = within(dialog).getAllByRole("option").map((x) => x.textContent);

    // user.read is already held; legacy.read is retired.
    expect(options.join(" ")).toContain("user.create");
    expect(options.join(" ")).not.toContain("user.read");
    expect(options.join(" ")).not.toContain("legacy.read");
  });

  it("says which permissions are human-only", async () => {
    backend();
    const { user } = await render([ROLE_READ, ROLE_MANAGE]);

    await user.click(screen.getByRole("button", { name: "Add permission" }));

    const dialog = await screen.findByRole("dialog");

    expect(dialog).toHaveTextContent(/human only/i);
  });
});

// ---------------------------------------------------------------- RG-U3

describe("the revoke reason", () => {
  it("is required, and nothing is sent without it", async () => {
    const state = backend();
    const { user } = await render([ROLE_READ, ROLE_MANAGE]);

    await user.click(screen.getByRole("button", { name: /Revoke user\.read/ }));

    const dialog = await screen.findByRole("dialog");

    await user.click(within(dialog).getByRole("button", { name: "Revoke permission" }));

    expect(await within(dialog).findByRole("alert")).toHaveTextContent("A reason is required.");

    await delay(50);
    expect(state.posts).toHaveLength(0);
  });
});

// ---------------------------------------------------------------- RG-U4

describe("after each succeeds", () => {
  it("adds a permission, announces and re-reads", async () => {
    const state = backend();
    const { user } = await render([ROLE_READ, ROLE_MANAGE]);
    const before = state.grantReads();

    await user.click(screen.getByRole("button", { name: "Add permission" }));

    const dialog = await screen.findByRole("dialog");

    await user.selectOptions(within(dialog).getByLabelText("Permission"), CATALOGUE[1].permissionId);
    await user.click(within(dialog).getByRole("button", { name: "Add permission" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });

    expect(state.posts).toEqual([{ path: "add", body: { permissionId: CATALOGUE[1].permissionId } }]);
    expect(screen.getByRole("status").textContent).toBe("Permission added: user.create.");

    await waitFor(() => {
      expect(state.grantReads()).toBeGreaterThan(before);
    });
  });

  it("revokes a permission, announces and re-reads so the row leaves", async () => {
    const state = backend();
    const { user } = await render([ROLE_READ, ROLE_MANAGE]);

    await user.click(screen.getByRole("button", { name: /Revoke user\.read/ }));

    const dialog = await screen.findByRole("dialog");

    await user.type(within(dialog).getByLabelText("Reason"), "No longer needed.");
    await user.click(within(dialog).getByRole("button", { name: "Revoke permission" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });

    expect(state.posts).toEqual([{ path: "revoke", body: { reason: "No longer needed." } }]);
    expect(screen.getByRole("status").textContent).toBe("Permission revoked: user.read.");

    await waitFor(() => {
      expect(screen.queryByRole("button", { name: /Revoke user\.read/ })).toBeNull();
    });
  });

  it("is busy while sending, and sends once", async () => {
    const state = backend({ slow: true });
    const { user } = await render([ROLE_READ, ROLE_MANAGE]);

    await user.click(screen.getByRole("button", { name: "Add permission" }));

    const dialog = await screen.findByRole("dialog");

    await user.selectOptions(within(dialog).getByLabelText("Permission"), CATALOGUE[1].permissionId);

    const confirm = within(dialog).getByRole("button", { name: "Add permission" });
    await user.click(confirm);
    await user.click(confirm);

    expect(within(dialog).getByRole("button", { name: "Adding…" })).toBeInTheDocument();

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });

    expect(state.posts).toHaveLength(1);
  });
});

// ---------------------------------------------------------------- RG-U5

describe("a refusal", () => {
  /** RP6's remediation is part of the message, and the reader must see all of it. */
  it("shows RP6's remediation word for word and keeps the dialog open", async () => {
    const remediation =
      "This role is held by an agent, so it cannot be given a permission that requires a human actor. "
      + "Revoke the agent's assignment, add the permission, then grant the agent an agent-safe role.";

    backend({ refuse: remediation });
    const { user } = await render([ROLE_READ, ROLE_MANAGE]);

    await user.click(screen.getByRole("button", { name: "Add permission" }));

    const dialog = await screen.findByRole("dialog");

    await user.selectOptions(within(dialog).getByLabelText("Permission"), CATALOGUE[1].permissionId);
    await user.click(within(dialog).getByRole("button", { name: "Add permission" }));

    expect(await within(dialog).findByRole("alert")).toHaveTextContent(remediation);
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------- RG-U6

describe("accessibility", () => {
  it("has no violations with either dialog open", async () => {
    backend();
    const { user, unmount } = await render([ROLE_READ, ROLE_MANAGE]);

    await user.click(screen.getByRole("button", { name: "Add permission" }));
    await screen.findByRole("dialog");
    await expectNoAccessibilityViolations(document.body);

    unmount();

    backend();
    const second = await render([ROLE_READ, ROLE_MANAGE]);

    await second.user.click(screen.getByRole("button", { name: /Revoke user\.read/ }));
    await screen.findByRole("dialog");
    await expectNoAccessibilityViolations(document.body);
  });
});
