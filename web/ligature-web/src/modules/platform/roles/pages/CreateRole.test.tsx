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
 * AUT-C3's New role dialog (docs/requirements.md, "AUT-C3 CreateRole", RC7;
 * RC-U1 to RC-U6).
 *
 * PRESENCE ONLY, and the typed values are sent exactly as typed: the server
 * owns the code and name rules, and its refusal is shown word for word. The
 * created role arrives in the list by re-reading, never by patching.
 */

const at = (path: string) => new URL(path, window.location.origin).href;

const ROLE_READ = definePermission("role.read");
const ROLE_MANAGE = definePermission("role.manage");

const EXISTING = {
  roleId: "b6000000-0000-4000-8000-000000000001",
  code: "access-reviewer",
  name: "Access Reviewer",
  description: null,
  isSystemRole: true,
  isActive: true,
  agentAssignable: true,
  permissionCount: 6,
  activeHolderCount: 2,
};

const CREATED = {
  roleId: "b6000000-0000-4000-8000-000000000002",
  code: "quality-reviewer",
  name: "Quality Reviewer",
  description: "Reviews access.",
  isSystemRole: false,
  isActive: true,
  agentAssignable: true,
  permissionCount: 0,
  activeHolderCount: 0,
};

interface Backend {
  readonly lists: () => number;
  readonly posts: unknown[];
}

function backend(options: { refuse?: string; slow?: boolean } = {}): Backend {
  let lists = 0;
  let created = false;
  const posts: unknown[] = [];

  server.use(
    http.get(at("/api/roles/administration"), () => {
      lists += 1;
      return HttpResponse.json({ roles: created ? [EXISTING, CREATED] : [EXISTING] });
    }),
    http.post(at("/api/roles"), async ({ request }) => {
      posts.push(await request.json());

      if (options.slow === true) {
        await delay(200);
      }

      if (options.refuse !== undefined) {
        return HttpResponse.json({ error: options.refuse }, { status: 400 });
      }

      created = true;

      return HttpResponse.json(CREATED, { status: 201 });
    }),
  );

  return { lists: () => lists, posts };
}

type User = ReturnType<typeof renderWithApp>["user"];

async function render(codes: PermissionCode[]) {
  const source = new TestSessionSource();
  const result = renderWithApp(appRoutes, { path: "/admin/roles", source });

  await act(async () => {
    source.settle({ status: "authenticated", principal: { permissions: codes.map((code) => ({ code })) } });
    await Promise.resolve();
  });

  await screen.findByRole("table", { name: "Roles" });

  return result;
}

async function openDialog(user: User) {
  await user.click(screen.getByRole("button", { name: "New role" }));

  return screen.findByRole("dialog", { name: "New role" });
}

async function fill(user: User, dialog: HTMLElement, values: { name?: string; code?: string; description?: string }) {
  for (const [label, value] of [
    ["Name", values.name],
    ["Code", values.code],
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

// ---------------------------------------------------------------- RC-U1

describe("who is offered New role", () => {
  it("is offered to a role.manage holder", async () => {
    backend();
    await render([ROLE_READ, ROLE_MANAGE]);

    expect(screen.getByRole("button", { name: "New role" })).toBeInTheDocument();
  });

  it("is not offered with role.read alone", async () => {
    backend();
    await render([ROLE_READ]);

    expect(screen.queryByRole("button", { name: "New role" })).toBeNull();
  });
});

// ---------------------------------------------------------------- RC-U2

describe("what is sent", () => {
  it("sends exactly what was typed, untrimmed", async () => {
    const state = backend();
    const { user } = await render([ROLE_READ, ROLE_MANAGE]);

    const dialog = await openDialog(user);
    await fill(user, dialog, { name: "  Quality Reviewer ", code: " quality-reviewer", description: " Reviews access. " });
    await user.click(within(dialog).getByRole("button", { name: "Create role" }));

    await waitFor(() => {
      expect(state.posts).toEqual([
        { code: " quality-reviewer", name: "  Quality Reviewer ", description: " Reviews access. " },
      ]);
    });
  });

  it("sends nothing when the name or the code is empty", async () => {
    const state = backend();
    const { user } = await render([ROLE_READ, ROLE_MANAGE]);

    const dialog = await openDialog(user);
    await user.click(within(dialog).getByRole("button", { name: "Create role" }));

    expect(await within(dialog).findByText("A name is required.")).toBeInTheDocument();
    expect(within(dialog).getByText("A code is required.")).toBeInTheDocument();

    await delay(50);
    expect(state.posts).toHaveLength(0);
  });
});

// ---------------------------------------------------------------- RC-U3

describe("creating", () => {
  it("announces, re-reads the list and closes, and the new role is active with nothing granted", async () => {
    const state = backend();
    const { user } = await render([ROLE_READ, ROLE_MANAGE]);
    const listsBefore = state.lists();

    const dialog = await openDialog(user);
    await fill(user, dialog, { name: "Quality Reviewer", code: "quality-reviewer" });
    await user.click(within(dialog).getByRole("button", { name: "Create role" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });

    expect(screen.getByRole("status")).toHaveTextContent("Role created: Quality Reviewer.");

    await waitFor(() => {
      expect(state.lists()).toBeGreaterThan(listsBefore);
    });

    const row = await screen.findByRole("row", { name: /Quality Reviewer/ });

    expect(within(row).getByText("Active")).toBeInTheDocument();
    expect(within(row).getByText("Yes")).toBeInTheDocument();
    expect(within(row).getAllByText("0")).toHaveLength(2);
  });

  it("is busy while sending, and sends once however often Create is pressed", async () => {
    const state = backend({ slow: true });
    const { user } = await render([ROLE_READ, ROLE_MANAGE]);

    const dialog = await openDialog(user);
    await fill(user, dialog, { name: "Quality Reviewer", code: "quality-reviewer" });

    const create = within(dialog).getByRole("button", { name: "Create role" });
    await user.click(create);
    await user.click(create);

    expect(within(dialog).getByRole("button", { name: "Creating…" })).toBeInTheDocument();

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });
    expect(state.posts).toHaveLength(1);
  });

  /**
   * The announcement reports what was STORED, not what was typed. The form
   * sends the name exactly as typed (RC-U2) and the server trims it (RC2), so
   * the two differ whenever the name was padded — and only the server's answer
   * matches the row that then appears in the list.
   *
   * textContent, not toHaveTextContent: that matcher collapses whitespace, and
   * collapsed whitespace is precisely the difference this pins.
   */
  it("announces the stored name, not the typed one", async () => {
    const state = backend();
    const { user } = await render([ROLE_READ, ROLE_MANAGE]);

    const dialog = await openDialog(user);
    await fill(user, dialog, { name: "  Quality Reviewer  ", code: "quality-reviewer" });
    await user.click(within(dialog).getByRole("button", { name: "Create role" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });

    expect(state.posts).toEqual([
      { code: "quality-reviewer", name: "  Quality Reviewer  ", description: "" },
    ]);

    expect(screen.getByRole("status").textContent).toBe("Role created: Quality Reviewer.");
  });
});

// ---------------------------------------------------------------- RC-U4

describe("a refusal", () => {
  it("is shown word for word, and the dialog keeps what was typed", async () => {
    backend({ refuse: "A role with this code already exists." });
    const { user } = await render([ROLE_READ, ROLE_MANAGE]);

    const dialog = await openDialog(user);
    await fill(user, dialog, { name: "Quality Reviewer", code: "access-reviewer" });
    await user.click(within(dialog).getByRole("button", { name: "Create role" }));

    expect(await within(dialog).findByRole("alert")).toHaveTextContent("A role with this code already exists.");
    expect(within(dialog).getByLabelText("Code")).toHaveValue("access-reviewer");
    expect(within(dialog).getByLabelText("Name")).toHaveValue("Quality Reviewer");
  });
});

// ---------------------------------------------------------------- RC-U5

describe("unsaved changes", () => {
  it("asks before discarding a dirty form, and closes a clean one", async () => {
    backend();
    const { user } = await render([ROLE_READ, ROLE_MANAGE]);

    const dirty = await openDialog(user);
    await fill(user, dirty, { name: "Quality Reviewer" });
    await user.click(within(dirty).getByRole("button", { name: "Cancel" }));

    const prompt = await screen.findByRole("dialog", { name: "Discard changes?" });
    await user.click(within(prompt).getByRole("button", { name: "Discard" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });

    const clean = await openDialog(user);
    await user.click(within(clean).getByRole("button", { name: "Cancel" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });
  });
});

// ---------------------------------------------------------------- RC-U6

describe("accessibility", () => {
  it("has no violations with the dialog open", async () => {
    backend();
    const { user } = await render([ROLE_READ, ROLE_MANAGE]);

    await openDialog(user);

    await expectNoAccessibilityViolations(document.body);
  });
});
