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
 * AUT-C4's Edit role dialog (docs/requirements.md, "AUT-C4
 * UpdateRoleMetadata", RM7; RM-U1 to RM-U7).
 *
 * THE FORM OPENS ON THE SERVER'S VALUES, so "dirty" means "differs from what
 * the server returned" — Edit profile's rule, not New role's. What is sent is
 * still exactly what was typed, untrimmed: the server owns every rule, and
 * the answer it returns is what the page then announces (RM6).
 *
 * EDIT IS OFFERED FOR A TENANT ROLE ONLY. A release-owned role's page offers
 * nothing to anyone, because no caller can legitimately edit one (RM4).
 */

const at = (path: string) => new URL(path, window.location.origin).href;

const ROLE_READ = definePermission("role.read");
const ROLE_MANAGE = definePermission("role.manage");

const TENANT = {
  roleId: "b7000000-0000-4000-8000-000000000001",
  code: "quality-reviewer",
  name: "Quality Reviewer",
  description: "Reviews access.",
  isSystemRole: false,
  isActive: true,
  agentAssignable: true,
  permissionCount: 0,
  activeHolderCount: 0,
};

const SEEDED = {
  roleId: "b7000000-0000-4000-8000-000000000002",
  code: "access-reviewer",
  name: "Access Reviewer",
  description: "Reads the directory for review.",
  isSystemRole: true,
  isActive: true,
  agentAssignable: true,
  permissionCount: 6,
  activeHolderCount: 2,
};

interface Backend {
  readonly lists: () => number;
  readonly posts: unknown[];
}

function backend(options: { refuse?: string; slow?: boolean; stored?: string } = {}): Backend {
  let lists = 0;
  let name = TENANT.name;
  const posts: unknown[] = [];

  server.use(
    http.get(at("/api/roles/administration"), () => {
      lists += 1;

      return HttpResponse.json({ roles: [{ ...TENANT, name }, SEEDED] });
    }),
    http.get(at("/api/roles/:roleId/permissions"), () => HttpResponse.json({ permissions: [] })),
    http.post(at("/api/roles/:roleId/metadata"), async ({ request }) => {
      posts.push(await request.json());

      if (options.slow === true) {
        await delay(200);
      }

      if (options.refuse !== undefined) {
        return HttpResponse.json({ error: options.refuse }, { status: 400 });
      }

      // The server stores the NORMALISED name; the page must use this one.
      name = options.stored ?? "Access Reviewer";

      return HttpResponse.json({ ...TENANT, name });
    }),
  );

  return { lists: () => lists, posts };
}

type User = ReturnType<typeof renderWithApp>["user"];

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

async function openDialog(user: User) {
  await user.click(screen.getByRole("button", { name: "Edit role" }));

  return screen.findByRole("dialog", { name: "Edit role" });
}

async function fill(user: User, dialog: HTMLElement, values: { name?: string; description?: string }) {
  for (const [label, value] of [
    ["Name", values.name],
    ["Description", values.description],
  ] as const) {
    if (value === undefined) {
      continue;
    }

    const field = within(dialog).getByLabelText(label);
    await user.clear(field);
    field.focus();
    await user.paste(value);
  }
}

// ---------------------------------------------------------------- RM-U1

describe("who is offered Edit", () => {
  it("is offered to a role.manage holder on a tenant role", async () => {
    backend();
    await render([ROLE_READ, ROLE_MANAGE]);

    expect(screen.getByRole("button", { name: "Edit role" })).toBeInTheDocument();
  });

  it("is not offered to a role.read holder", async () => {
    backend();
    await render([ROLE_READ]);

    expect(screen.queryByRole("button", { name: "Edit role" })).toBeNull();
  });

  it("is not offered on a release-owned role, even to a role.manage holder", async () => {
    backend();
    await render([ROLE_READ, ROLE_MANAGE], SEEDED.roleId);

    expect(screen.queryByRole("button", { name: "Edit role" })).toBeNull();
  });
});

// ---------------------------------------------------------------- RM-U2

describe("what it sends", () => {
  it("opens on the stored values", async () => {
    backend();
    const { user } = await render([ROLE_READ, ROLE_MANAGE]);

    const dialog = await openDialog(user);

    expect(within(dialog).getByLabelText("Name")).toHaveValue(TENANT.name);
    expect(within(dialog).getByLabelText("Description")).toHaveValue(TENANT.description);

    // The code is context, not a field (RM2).
    expect(within(dialog).queryByLabelText("Code")).toBeNull();
    expect(within(dialog).getByText(TENANT.code)).toBeInTheDocument();
  });

  it("sends exactly what was typed, untrimmed", async () => {
    const state = backend();
    const { user } = await render([ROLE_READ, ROLE_MANAGE]);

    const dialog = await openDialog(user);
    await fill(user, dialog, { name: "  Access Reviewer ", description: " Reviews everything. " });
    await user.click(within(dialog).getByRole("button", { name: "Save" }));

    await waitFor(() => {
      expect(state.posts).toEqual([{ name: "  Access Reviewer ", description: " Reviews everything. " }]);
    });
  });

  it("sends nothing when the name is empty", async () => {
    const state = backend();
    const { user } = await render([ROLE_READ, ROLE_MANAGE]);

    const dialog = await openDialog(user);
    await fill(user, dialog, { name: "" });
    await user.click(within(dialog).getByRole("button", { name: "Save" }));

    expect(await within(dialog).findByText("A name is required.")).toBeInTheDocument();

    await delay(50);
    expect(state.posts).toHaveLength(0);
  });
});

// ---------------------------------------------------------------- RM-U3

describe("saving", () => {
  it("announces the stored name, re-reads and closes", async () => {
    const state = backend();
    const { user } = await render([ROLE_READ, ROLE_MANAGE]);
    const listsBefore = state.lists();

    const dialog = await openDialog(user);
    await fill(user, dialog, { name: "  Access Reviewer  " });
    await user.click(within(dialog).getByRole("button", { name: "Save" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });

    // The server's trimmed name, not the padded one that was typed.
    expect(screen.getByRole("status").textContent).toBe("Role updated: Access Reviewer.");

    await waitFor(() => {
      expect(state.lists()).toBeGreaterThan(listsBefore);
    });

    expect(await screen.findByRole("heading", { name: "Access Reviewer" })).toBeInTheDocument();
  });

  /** A save that changed nothing is still a save: the client does not predict a no-op. */
  it("treats a save with nothing changed as a save", async () => {
    const state = backend({ stored: TENANT.name });
    const { user } = await render([ROLE_READ, ROLE_MANAGE]);

    const dialog = await openDialog(user);
    await user.click(within(dialog).getByRole("button", { name: "Save" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });

    expect(state.posts).toHaveLength(1);
    expect(screen.getByRole("status").textContent).toBe(`Role updated: ${TENANT.name}.`);
  });

  it("is busy while sending, and sends once", async () => {
    const state = backend({ slow: true });
    const { user } = await render([ROLE_READ, ROLE_MANAGE]);

    const dialog = await openDialog(user);
    await fill(user, dialog, { name: "Access Reviewer" });

    const save = within(dialog).getByRole("button", { name: "Save" });
    await user.click(save);
    await user.click(save);

    expect(within(dialog).getByRole("button", { name: "Saving…" })).toBeInTheDocument();

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });
    expect(state.posts).toHaveLength(1);
  });
});

// ---------------------------------------------------------------- RM-U4

describe("a refusal", () => {
  it("is shown word for word and keeps what was typed", async () => {
    backend({ refuse: "A role name must be at most 100 characters." });
    const { user } = await render([ROLE_READ, ROLE_MANAGE]);

    const dialog = await openDialog(user);
    await fill(user, dialog, { name: "Far too long" });
    await user.click(within(dialog).getByRole("button", { name: "Save" }));

    expect(await within(dialog).findByRole("alert")).toHaveTextContent(
      "A role name must be at most 100 characters.");

    expect(within(dialog).getByLabelText("Name")).toHaveValue("Far too long");
  });
});

// ---------------------------------------------------------------- RM-U5

describe("the unsaved-changes guard", () => {
  it("asks before discarding a dirty form", async () => {
    backend();
    const { user } = await render([ROLE_READ, ROLE_MANAGE]);

    const dialog = await openDialog(user);
    await fill(user, dialog, { name: "Access Reviewer" });
    await user.click(within(dialog).getByRole("button", { name: "Cancel" }));

    expect(await screen.findByRole("heading", { name: "Discard changes?" })).toBeInTheDocument();
  });

  it("closes a clean form without asking", async () => {
    backend();
    const { user } = await render([ROLE_READ, ROLE_MANAGE]);

    const dialog = await openDialog(user);
    await user.click(within(dialog).getByRole("button", { name: "Cancel" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });
  });
});

// ---------------------------------------------------------------- RM-U7

describe("accessibility", () => {
  it("has no violations with the dialog open", async () => {
    backend();
    const { user } = await render([ROLE_READ, ROLE_MANAGE]);

    await openDialog(user);
    await expectNoAccessibilityViolations(document.body);
  });
});
