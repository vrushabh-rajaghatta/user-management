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
 * AUT-C5/C6's lifecycle actions (docs/requirements.md, "AUT-C5 DeactivateRole
 * and AUT-C6 ReactivateRole", RD8; RD-U1 to RD-U7).
 *
 * ONE STATE-DEPENDENT ACTION. An active tenant role offers Deactivate, an
 * inactive one offers Reactivate, and never both. Edit is offered in either
 * state, because metadata is AUT-C4's concern and activity is not.
 *
 * THE COPY IS THE CONTRACT HERE. Deactivating does not remove anybody's
 * access, so the confirmation says so in as many words, and the word "strand"
 * — which implies the opposite — appears nowhere.
 */

const at = (path: string) => new URL(path, window.location.origin).href;

const ROLE_READ = definePermission("role.read");
const ROLE_MANAGE = definePermission("role.manage");

const HELD = {
  roleId: "b8000000-0000-4000-8000-000000000001",
  code: "quality-reviewer",
  name: "Quality Reviewer",
  description: "Reviews access.",
  isSystemRole: false,
  isActive: true,
  agentAssignable: true,
  permissionCount: 1,
  activeHolderCount: 3,
};

const EMPTY = {
  ...HELD,
  roleId: "b8000000-0000-4000-8000-000000000002",
  code: "empty-reviewer",
  name: "Empty Reviewer",
  activeHolderCount: 0,
};

const SEEDED = {
  ...HELD,
  roleId: "b8000000-0000-4000-8000-000000000003",
  code: "access-reviewer",
  name: "Access Reviewer",
  isSystemRole: true,
};

interface Backend {
  readonly lists: () => number;
  readonly posts: { path: string; body: unknown }[];
}

function backend(
  options: { active?: boolean; refuse?: string; slow?: boolean; renamedTo?: string } = {},
): Backend {
  let lists = 0;
  let active = options.active ?? true;
  const posts: { path: string; body: unknown }[] = [];

  const role = () => ({ ...HELD, isActive: active });

  server.use(
    http.get(at("/api/roles/administration"), () => {
      lists += 1;

      return HttpResponse.json({ roles: [role(), { ...EMPTY, isActive: active }, SEEDED] });
    }),
    http.get(at("/api/roles/:roleId/permissions"), () => HttpResponse.json({ permissions: [] })),
    http.post(at("/api/roles/:roleId/:verb"), async ({ request, params }) => {
      const verb = String(params.verb);

      if (verb !== "deactivate" && verb !== "reactivate") {
        return HttpResponse.json({ error: "unexpected route" }, { status: 404 });
      }

      posts.push({ path: verb, body: await request.json().catch(() => null) });

      if (options.slow === true) {
        await delay(200);
      }

      if (options.refuse !== undefined) {
        return HttpResponse.json({ error: options.refuse }, { status: 400 });
      }

      active = verb === "reactivate";

      // The answer is the server's, which may carry a name the page has never
      // seen — someone else renamed the role since it was read.
      return HttpResponse.json({ ...role(), name: options.renamedTo ?? HELD.name });
    }),
  );

  return { lists: () => lists, posts };
}

type User = ReturnType<typeof renderWithApp>["user"];

async function render(codes: PermissionCode[], roleId = HELD.roleId) {
  const source = new TestSessionSource();
  const result = renderWithApp(appRoutes, { path: `/admin/roles/${roleId}`, source });

  await act(async () => {
    source.settle({ status: "authenticated", principal: { permissions: codes.map((code) => ({ code })) } });
    await Promise.resolve();
  });

  await screen.findByRole("table", { name: "Permissions" });

  return result;
}

async function openDialog(user: User, name: "Deactivate" | "Reactivate") {
  await user.click(screen.getByRole("button", { name }));

  return screen.findByRole("dialog");
}

// ---------------------------------------------------------------- RD-U1

describe("which lifecycle action is offered", () => {
  it("offers Deactivate, and not Reactivate, on an active tenant role", async () => {
    backend();
    await render([ROLE_READ, ROLE_MANAGE]);

    expect(screen.getByRole("button", { name: "Deactivate" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Reactivate" })).toBeNull();
  });

  it("offers Reactivate, and not Deactivate, on an inactive tenant role", async () => {
    backend({ active: false });
    await render([ROLE_READ, ROLE_MANAGE]);

    expect(screen.getByRole("button", { name: "Reactivate" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Deactivate" })).toBeNull();
  });

  it("offers neither to a role.read holder", async () => {
    backend();
    await render([ROLE_READ]);

    expect(screen.queryByRole("button", { name: "Deactivate" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Reactivate" })).toBeNull();
  });

  it("offers neither on a release-owned role, even to a role.manage holder", async () => {
    backend();
    await render([ROLE_READ, ROLE_MANAGE], SEEDED.roleId);

    expect(screen.queryByRole("button", { name: "Deactivate" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Reactivate" })).toBeNull();
  });
});

// ---------------------------------------------------------------- RD-U2

describe("Edit alongside the lifecycle", () => {
  it("is offered in both states", async () => {
    backend();
    const { unmount } = await render([ROLE_READ, ROLE_MANAGE]);

    expect(screen.getByRole("button", { name: "Edit role" })).toBeInTheDocument();

    unmount();

    backend({ active: false });
    await render([ROLE_READ, ROLE_MANAGE]);

    expect(screen.getByRole("button", { name: "Edit role" })).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------- RD-U3

describe("what the confirmation says", () => {
  it("states that existing holders keep their access, and counts them", async () => {
    backend();
    const { user } = await render([ROLE_READ, ROLE_MANAGE]);

    const dialog = await openDialog(user, "Deactivate");

    expect(dialog).toHaveTextContent("3 active holders");
    expect(dialog).toHaveTextContent(/keep their current access/i);
    expect(dialog).toHaveTextContent(/prevent new assignments/i);
  });

  /** "Strand" implies a loss of access that does not occur (RD3). */
  it("never says the holders are stranded", async () => {
    backend();
    const { user } = await render([ROLE_READ, ROLE_MANAGE]);

    const dialog = await openDialog(user, "Deactivate");

    expect(dialog.textContent).not.toMatch(/strand/i);
  });

  it("drops the holder sentence when the role has none", async () => {
    backend();
    const { user } = await render([ROLE_READ, ROLE_MANAGE], EMPTY.roleId);

    const dialog = await openDialog(user, "Deactivate");

    expect(dialog.textContent).not.toMatch(/active holders?/i);
    expect(dialog).toHaveTextContent(/prevent new assignments/i);
  });
});

// ---------------------------------------------------------------- RD-U4

describe("the reason", () => {
  it("is required, and nothing is sent without it", async () => {
    const state = backend();
    const { user } = await render([ROLE_READ, ROLE_MANAGE]);

    const dialog = await openDialog(user, "Deactivate");

    await user.click(within(dialog).getByRole("button", { name: "Deactivate" }));

    expect(await within(dialog).findByRole("alert")).toHaveTextContent("A reason is required.");

    await delay(50);
    expect(state.posts).toHaveLength(0);
  });

  it("is sent with the deactivation", async () => {
    const state = backend();
    const { user } = await render([ROLE_READ, ROLE_MANAGE]);

    const dialog = await openDialog(user, "Deactivate");

    await user.type(within(dialog).getByLabelText("Reason"), "No longer used.");
    await user.click(within(dialog).getByRole("button", { name: "Deactivate" }));

    await waitFor(() => {
      expect(state.posts).toEqual([{ path: "deactivate", body: { reason: "No longer used." } }]);
    });
  });

  /** RD5: reactivation asks for none. */
  it("is not asked for when reactivating", async () => {
    const state = backend({ active: false });
    const { user } = await render([ROLE_READ, ROLE_MANAGE]);

    const dialog = await openDialog(user, "Reactivate");

    expect(within(dialog).queryByLabelText("Reason")).toBeNull();

    await user.click(within(dialog).getByRole("button", { name: "Reactivate" }));

    await waitFor(() => {
      expect(state.posts.map((x) => x.path)).toEqual(["reactivate"]);
    });
  });
});

// ---------------------------------------------------------------- RD-U5

describe("after it succeeds", () => {
  it("announces, re-reads, and the action flips to its inverse", async () => {
    const state = backend();
    const { user } = await render([ROLE_READ, ROLE_MANAGE]);
    const listsBefore = state.lists();

    const dialog = await openDialog(user, "Deactivate");

    await user.type(within(dialog).getByLabelText("Reason"), "Retired.");
    await user.click(within(dialog).getByRole("button", { name: "Deactivate" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });

    expect(screen.getByRole("status").textContent).toBe(`Role deactivated: ${HELD.name}.`);

    await waitFor(() => {
      expect(state.lists()).toBeGreaterThan(listsBefore);
    });

    expect(await screen.findByRole("button", { name: "Reactivate" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Deactivate" })).toBeNull();
  });

  /**
   * The announcement reports what the SERVER answered, not the name the page
   * happened to be holding. They differ whenever someone else renamed the role
   * since this page read it — a stale page, not a contrived one — and the
   * module has pinned this convention twice already (RC-U3, RM-U3).
   */
  it("announces the name the server answered, not the one on the page", async () => {
    backend({ renamedTo: "Renamed Elsewhere" });
    const { user } = await render([ROLE_READ, ROLE_MANAGE]);

    const dialog = await openDialog(user, "Deactivate");

    await user.type(within(dialog).getByLabelText("Reason"), "Retired.");
    await user.click(within(dialog).getByRole("button", { name: "Deactivate" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });

    expect(screen.getByRole("status").textContent).toBe("Role deactivated: Renamed Elsewhere.");
  });

  it("is busy while sending, and sends once", async () => {
    const state = backend({ slow: true });
    const { user } = await render([ROLE_READ, ROLE_MANAGE]);

    const dialog = await openDialog(user, "Deactivate");

    await user.type(within(dialog).getByLabelText("Reason"), "Retired.");

    const confirm = within(dialog).getByRole("button", { name: "Deactivate" });
    await user.click(confirm);
    await user.click(confirm);

    expect(within(dialog).getByRole("button", { name: "Deactivating…" })).toBeInTheDocument();

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });

    expect(state.posts).toHaveLength(1);
  });
});

// ---------------------------------------------------------------- RD-U6

describe("a refusal", () => {
  it("is shown word for word and the dialog stays open", async () => {
    backend({ refuse: "System roles cannot be deactivated." });
    const { user } = await render([ROLE_READ, ROLE_MANAGE]);

    const dialog = await openDialog(user, "Deactivate");

    await user.type(within(dialog).getByLabelText("Reason"), "Retired.");
    await user.click(within(dialog).getByRole("button", { name: "Deactivate" }));

    expect(await within(dialog).findByRole("alert")).toHaveTextContent(
      "System roles cannot be deactivated.");

    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------- RD-U7

describe("accessibility", () => {
  it("has no violations with either confirmation open", async () => {
    backend();
    const { user, unmount } = await render([ROLE_READ, ROLE_MANAGE]);

    await openDialog(user, "Deactivate");
    await expectNoAccessibilityViolations(document.body);

    unmount();

    backend({ active: false });
    const second = await render([ROLE_READ, ROLE_MANAGE]);

    await openDialog(second.user, "Reactivate");
    await expectNoAccessibilityViolations(document.body);
  });
});
