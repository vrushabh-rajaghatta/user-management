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
 * USR-C3's Change email dialog (docs/requirements.md, "USR-C3 ChangeUserEmail",
 * CE-U1 to CE-U7).
 *
 * THE SERVER OWNS THE ADDRESS RULES. The form checks presence only and sends
 * exactly what was typed; a refusal is shown word for word. For a user whose
 * activation is pending it says what happens to the old link — guidance only:
 * the command issues nothing (CE7).
 */

const at = (path: string) => new URL(path, window.location.origin).href;

const READ = definePermission("user.read");
const UPDATE = definePermission("user.update");
const DEACTIVATE = definePermission("user.deactivate");

const TAKEN = "A user with this email address already exists.";
const GUIDANCE = "Their current activation link will stop working. Use Resend activation to send a new one to the new address.";

type Status = "Active" | "Inactive";

interface Row {
  readonly userId: string;
  readonly displayName: string;
  readonly email: string | null;
  readonly activationPending: boolean;
  readonly status: Status;
}

const ADA: Row = {
  userId: "f1000000-0000-4000-8000-00000000c3a1",
  displayName: "Ada L.",
  email: "ada@example.test",
  activationPending: false,
  status: "Active",
};

interface Backend {
  readonly listReads: () => number;
  readonly detailReads: () => number;
  readonly posts: { path: string; body: unknown }[];
}

function backend(options: { row?: Partial<Row>; refuse?: string; slow?: boolean } = {}): Backend {
  const row = { ...ADA, ...options.row };
  let listReads = 0;
  let detailReads = 0;
  const posts: { path: string; body: unknown }[] = [];

  server.use(
    http.get(at("/api/users"), () => {
      listReads += 1;
      return HttpResponse.json({ users: [row], page: 1, pageSize: 25, hasMore: false });
    }),
    http.get(at(`/api/users/${row.userId}`), () => {
      detailReads += 1;
      return HttpResponse.json({ ...row, firstName: "Ada", lastName: "Lovelace" });
    }),
    http.post(at(`/api/users/${row.userId}/email`), async ({ request }) => {
      posts.push({ path: new URL(request.url).pathname, body: await request.json() });
      if (options.slow === true) {
        await delay(200);
      }
      return options.refuse === undefined
        ? new HttpResponse(null, { status: 204 })
        : HttpResponse.json({ error: options.refuse }, { status: 400 });
    }),
  );

  return { listReads: () => listReads, detailReads: () => detailReads, posts };
}

type User = ReturnType<typeof renderWithApp>["user"];

async function render(codes: PermissionCode[], path = "/admin/users") {
  const source = new TestSessionSource();
  const result = renderWithApp(appRoutes, { path, source });

  await act(async () => {
    source.settle({ status: "authenticated", principal: { permissions: codes.map((code) => ({ code })) } });
    await Promise.resolve();
  });

  return result;
}

async function menu(user: User): Promise<string[]> {
  await user.click(await screen.findByRole("button", { name: /^Actions for Ada L\./ }));
  const items = await screen.findAllByRole("menuitem");

  return items.map((item) => item.textContent);
}

async function openChange(user: User) {
  await user.click(await screen.findByRole("button", { name: /^Actions for Ada L\./ }));
  await user.click(await screen.findByRole("menuitem", { name: "Change email" }));

  return screen.findByRole("dialog", { name: "Change email for Ada L." });
}

async function type(user: User, dialog: HTMLElement, label: string, value: string) {
  const input = within(dialog).getByLabelText(label);
  await user.clear(input);
  input.focus();
  await user.paste(value);
}

// ---------------------------------------------------------------- CE-U1

describe("who is offered Change email", () => {
  it.each<Status>(["Active", "Inactive"])("is offered with user.update on an %s row, after Edit profile and before Deactivate", async (status) => {
    backend({ row: { status } });
    const { user } = await render([READ, UPDATE, DEACTIVATE]);

    const items = await menu(user);

    expect(items).toContain("Change email");
    expect(items.indexOf("Change email")).toBe(items.indexOf("Edit profile") + 1);
    if (status === "Active") {
      expect(items.indexOf("Change email")).toBeLessThan(items.indexOf("Deactivate"));
    }
  });

  it("is not offered without user.update", async () => {
    backend();
    const { user } = await render([READ, DEACTIVATE]);

    expect(await menu(user)).not.toContain("Change email");
  });

  it("is offered on User detail too", async () => {
    backend();
    const { user } = await render([READ, UPDATE], `/admin/users/${ADA.userId}`);

    await screen.findByRole("heading", { level: 1, name: ADA.displayName });
    await user.click(screen.getByRole("button", { name: "Actions" }));
    await user.click(await screen.findByRole("menuitem", { name: "Change email" }));

    expect(await screen.findByRole("dialog", { name: "Change email for Ada L." })).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------- CE-U2

describe("what is sent", () => {
  it("shows the current address, and sends exactly the typed address with no reason when none was typed", async () => {
    const state = backend();
    const { user } = await render([READ, UPDATE]);

    const dialog = await openChange(user);
    expect(within(dialog).getByText("ada@example.test")).toBeInTheDocument();

    await type(user, dialog, "New email", "  Ada.King@Example.test ");
    await user.click(within(dialog).getByRole("button", { name: "Save" }));

    await waitFor(() => {
      expect(state.posts).toEqual([{ path: `/api/users/${ADA.userId}/email`, body: { email: "  Ada.King@Example.test " } }]);
    });
  });

  it("sends the reason exactly as typed when there is one", async () => {
    const state = backend();
    const { user } = await render([READ, UPDATE]);

    const dialog = await openChange(user);
    await type(user, dialog, "New email", "ada.king@example.test");
    await type(user, dialog, "Reason (optional)", " Married; HR ticket 5120. ");
    await user.click(within(dialog).getByRole("button", { name: "Save" }));

    await waitFor(() => {
      expect(state.posts).toEqual([
        { path: `/api/users/${ADA.userId}/email`, body: { email: "ada.king@example.test", reason: " Married; HR ticket 5120. " } },
      ]);
    });
  });

  it("sends nothing for an empty address, and says so", async () => {
    const state = backend();
    const { user } = await render([READ, UPDATE]);

    const dialog = await openChange(user);
    await user.click(within(dialog).getByRole("button", { name: "Save" }));

    expect(await within(dialog).findByText("An email address is required.")).toBeInTheDocument();
    await delay(50);
    expect(state.posts).toHaveLength(0);
  });
});

// ---------------------------------------------------------------- CE-U3

describe("saving", () => {
  it("announces, refreshes the list, and closes", async () => {
    const state = backend();
    const { user } = await render([READ, UPDATE]);
    await screen.findByRole("table", { name: "Users" });
    const listReadsBefore = state.listReads();

    const dialog = await openChange(user);
    await type(user, dialog, "New email", "ada.king@example.test");
    await user.click(within(dialog).getByRole("button", { name: "Save" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });
    expect(screen.getByRole("status")).toHaveTextContent("Email changed for Ada L.");
    await waitFor(() => {
      expect(state.listReads()).toBeGreaterThan(listReadsBefore);
    });
  });

  it("refreshes GetUser on User detail", async () => {
    const state = backend();
    const { user } = await render([READ, UPDATE], `/admin/users/${ADA.userId}`);
    await screen.findByRole("heading", { level: 1, name: ADA.displayName });
    const detailReadsBefore = state.detailReads();

    await user.click(screen.getByRole("button", { name: "Actions" }));
    await user.click(await screen.findByRole("menuitem", { name: "Change email" }));
    const dialog = await screen.findByRole("dialog", { name: "Change email for Ada L." });
    await type(user, dialog, "New email", "ada.king@example.test");
    await user.click(within(dialog).getByRole("button", { name: "Save" }));

    await waitFor(() => {
      expect(state.detailReads()).toBeGreaterThan(detailReadsBefore);
    });
  });

  it("is busy while sending, and sends once however often Save is pressed", async () => {
    const state = backend({ slow: true });
    const { user } = await render([READ, UPDATE]);

    const dialog = await openChange(user);
    await type(user, dialog, "New email", "ada.king@example.test");
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

// ---------------------------------------------------------------- CE-U4

describe("a refusal", () => {
  it("is shown word for word, and the dialog keeps what was typed", async () => {
    backend({ refuse: TAKEN });
    const { user } = await render([READ, UPDATE]);

    const dialog = await openChange(user);
    await type(user, dialog, "New email", "grace@example.test");
    await type(user, dialog, "Reason (optional)", "Swap.");
    await user.click(within(dialog).getByRole("button", { name: "Save" }));

    expect(await within(dialog).findByRole("alert")).toHaveTextContent(TAKEN);
    expect(within(dialog).getByLabelText("New email")).toHaveValue("grace@example.test");
    expect(within(dialog).getByLabelText("Reason (optional)")).toHaveValue("Swap.");
  });
});

// ---------------------------------------------------------------- CE-U5

describe("the activation guidance", () => {
  it.each([
    [true, "shown"],
    [false, "not shown"],
  ])("with activation pending %s, is %s", async (pending) => {
    backend({ row: { activationPending: pending } });
    const { user } = await render([READ, UPDATE]);

    const dialog = await openChange(user);

    if (pending) {
      expect(within(dialog).getByText(GUIDANCE)).toBeInTheDocument();
    } else {
      expect(within(dialog).queryByText(GUIDANCE)).toBeNull();
    }
  });
});

// ---------------------------------------------------------------- CE-U6

describe("unsaved changes in the dialog", () => {
  it.each(["Cancel", "Escape", "Close"])("asks before closing a dirty form by %s", async (how) => {
    backend();
    const { user } = await render([READ, UPDATE]);

    const dialog = await openChange(user);
    await type(user, dialog, "New email", "ada.king@example.test");

    if (how === "Escape") {
      await user.keyboard("{Escape}");
    } else {
      await user.click(within(dialog).getByRole("button", { name: how }));
    }

    const prompt = await screen.findByRole("dialog", { name: "Discard changes?" });
    await user.click(within(prompt).getByRole("button", { name: "Discard" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });
  });

  it("closes a clean form without asking", async () => {
    backend();
    const { user } = await render([READ, UPDATE]);

    const dialog = await openChange(user);
    await user.click(within(dialog).getByRole("button", { name: "Cancel" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });
  });

  it("returns focus to the row's Actions button when it closes", async () => {
    backend();
    const { user } = await render([READ, UPDATE]);

    const dialog = await openChange(user);
    await user.click(within(dialog).getByRole("button", { name: "Cancel" }));

    await waitFor(() => {
      expect(document.activeElement).toBe(screen.getByRole("button", { name: /^Actions for Ada L\./ }));
    });
  });
});

// ---------------------------------------------------------------- CE-U7

describe("accessibility", () => {
  it("has no violations with the dialog open, guidance included", async () => {
    backend({ row: { activationPending: true } });
    const { user } = await render([READ, UPDATE]);

    await openChange(user);

    await expectNoAccessibilityViolations(document.body);
  });
});
