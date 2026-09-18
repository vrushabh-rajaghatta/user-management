import { act, screen, waitFor, within } from "@testing-library/react";
import { delay, http, HttpResponse } from "msw";
import type { RouteObject } from "react-router";
import { describe, expect, it } from "vitest";
import { definePermission, type PermissionCode } from "@/shared/auth/permissions";
import { expectNoAccessibilityViolations } from "@/test/axe";
import { server } from "@/test/msw/server";
import { renderWithApp } from "@/test/renderWithApp";
import { TestSessionSource } from "@/test/sessions";
import { UsersPage } from "./UsersPage";

/**
 * USR-C2's Edit profile dialog (docs/requirements.md, "USR-C2 — the Edit
 * profile UI", UI-1..UI-5, UI-8).
 *
 * THE SERVER OWNS THE NAME RULES. The form checks presence only (U3): it
 * sends whitespace, U+FEFF, control characters and 101 characters, and shows
 * the server's refusal word for word. It never trims what it sends.
 */

const at = (path: string) => new URL(path, window.location.origin).href;

const READ = definePermission("user.read");
const UPDATE = definePermission("user.update");

const ADA = {
  userId: "f1000000-0000-4000-8000-00000000ada1",
  displayName: "Ada L.",
  email: "ada@example.test",
  activationPending: false,
  status: "Active" as const,
};

const PROFILE = { userId: ADA.userId, firstName: "Ada", lastName: "Lovelace", displayName: "Ada L." };

interface Backend {
  readonly profileReads: () => number;
  readonly listReads: () => number;
  readonly posts: { path: string; body: unknown }[];
}

/** GET /api/users, GET /api/users/{id} and POST .../profile, recording what reached the server. */
function backend(options: { refuse?: string; failRead?: boolean } = {}): Backend {
  let profileReads = 0;
  let listReads = 0;
  const posts: { path: string; body: unknown }[] = [];

  server.use(
    http.get(at("/api/users"), () => {
      listReads += 1;
      return HttpResponse.json({ users: [ADA], page: 1, pageSize: 25, hasMore: false });
    }),
    http.get(at(`/api/users/${ADA.userId}`), () => {
      profileReads += 1;
      return options.failRead === true && profileReads === 1
        ? HttpResponse.json({ error: "The user does not exist." }, { status: 400 })
        : HttpResponse.json(PROFILE);
    }),
    http.post(at(`/api/users/${ADA.userId}/profile`), async ({ request }) => {
      posts.push({ path: new URL(request.url).pathname, body: await request.json() });
      return options.refuse === undefined
        ? new HttpResponse(null, { status: 204 })
        : HttpResponse.json({ error: options.refuse }, { status: 400 });
    }),
  );

  return { profileReads: () => profileReads, listReads: () => listReads, posts };
}

function routes(): RouteObject[] {
  return [{ path: "/admin/users", element: <UsersPage /> }];
}

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

async function openEdit(user: ReturnType<typeof renderWithApp>["user"]) {
  await user.click(await screen.findByRole("button", { name: /^Actions for Ada L\./ }));
  await user.click(await screen.findByRole("menuitem", { name: "Edit profile" }));

  return screen.findByRole("dialog", { name: "Edit profile for Ada L." });
}

async function loaded(dialog: HTMLElement) {
  await within(dialog).findByDisplayValue("Lovelace");
}

async function replace(user: ReturnType<typeof renderWithApp>["user"], dialog: HTMLElement, label: string, value: string) {
  const input = within(dialog).getByLabelText(label);
  await user.clear(input);

  if (value !== "") {
    // Typed through the clipboard, so control characters arrive as themselves.
    input.focus();
    await user.paste(value);
  }
}

// ---------------------------------------------------------------- UI-1

describe("who is offered Edit profile", () => {
  it("is offered with user.update, and not without it", async () => {
    backend();
    const { user } = await render(READ, UPDATE);

    await user.click(await screen.findByRole("button", { name: /^Actions for Ada L\./ }));
    expect(await screen.findByRole("menuitem", { name: "Edit profile" })).toBeInTheDocument();
  });

  it("is not offered to a caller with user.read alone", async () => {
    backend();
    await render(READ);

    expect(screen.queryByRole("button", { name: /^Actions for Ada L\./ })).toBeNull();
  });
});

// ---------------------------------------------------------------- UI-2

describe("opening the dialog", () => {
  it("reads GetUser and shows the stored names", async () => {
    const state = backend();
    const { user } = await render(READ, UPDATE);

    const dialog = await openEdit(user);
    await loaded(dialog);

    expect(state.profileReads()).toBe(1);
    expect(within(dialog).getByLabelText("First name")).toHaveValue("Ada");
    expect(within(dialog).getByLabelText("Last name")).toHaveValue("Lovelace");
    expect(within(dialog).getByLabelText("Display name")).toHaveValue("Ada L.");
  });

  it("shows no form while loading", async () => {
    backend();
    server.use(
      http.get(at(`/api/users/${ADA.userId}`), async () => {
        await delay("infinite");
        return HttpResponse.json(PROFILE);
      }),
    );
    const { user } = await render(READ, UPDATE);

    const dialog = await openEdit(user);

    expect(within(dialog).queryByLabelText("First name")).toBeNull();
  });

  it("states a failed read word for word and reads again on Try again", async () => {
    const state = backend({ failRead: true });
    const { user } = await render(READ, UPDATE);

    const dialog = await openEdit(user);

    expect(await within(dialog).findByText("The user does not exist.")).toBeInTheDocument();

    // The shared ErrorState's retry, as every error state in the app.
    await user.click(within(dialog).getByRole("button", { name: "Try again" }));
    await loaded(dialog);

    expect(state.profileReads()).toBe(2);
  });
});

// ---------------------------------------------------------------- UI-3

describe("presence only", () => {
  it("flags an empty field and sends nothing", async () => {
    const state = backend();
    const { user } = await render(READ, UPDATE);

    const dialog = await openEdit(user);
    await loaded(dialog);
    await replace(user, dialog, "Display name", "");
    await user.click(within(dialog).getByRole("button", { name: "Save" }));

    expect(await within(dialog).findByText("A display name is required.")).toBeInTheDocument();
    expect(state.posts).toEqual([]);
  });

  it.each([
    ["whitespace only", "   "],
    ["a byte-order mark", "\uFEFF"],
    ["a control character", "Ada\u0007L."],
    ["101 characters", "x".repeat(101)],
  ])("sends %s, and shows the server's refusal word for word", async (_, value) => {
    const state = backend({ refuse: "Display name is required." });
    const { user } = await render(READ, UPDATE);

    const dialog = await openEdit(user);
    await loaded(dialog);
    await replace(user, dialog, "Display name", value);
    await user.click(within(dialog).getByRole("button", { name: "Save" }));

    expect(await within(dialog).findByRole("alert")).toHaveTextContent("Display name is required.");
    expect(state.posts).toEqual([
      { path: `/api/users/${ADA.userId}/profile`, body: { firstName: "Ada", lastName: "Lovelace", displayName: value } },
    ]);
    expect(within(dialog).getByLabelText("Display name")).toHaveValue(value);
  });

  it("sets no maxLength on any field", async () => {
    backend();
    const { user } = await render(READ, UPDATE);

    const dialog = await openEdit(user);
    await loaded(dialog);

    for (const label of ["First name", "Last name", "Display name"]) {
      expect(within(dialog).getByLabelText(label)).not.toHaveAttribute("maxlength");
    }
  });
});

// ---------------------------------------------------------------- UI-4

describe("saving", () => {
  it("sends exactly what was typed, announces, refreshes, and closes", async () => {
    const state = backend();
    const { user } = await render(READ, UPDATE);
    const listReadsBefore = state.listReads();

    const dialog = await openEdit(user);
    await loaded(dialog);
    await replace(user, dialog, "Display name", "  Countess Lovelace ");
    await user.click(within(dialog).getByRole("button", { name: "Save" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });

    expect(state.posts).toEqual([
      {
        path: `/api/users/${ADA.userId}/profile`,
        body: { firstName: "Ada", lastName: "Lovelace", displayName: "  Countess Lovelace " },
      },
    ]);
    expect(screen.getByRole("status")).toHaveTextContent("Profile saved for Countess Lovelace.");

    // Read again, both: the list, and this user's GetUser. The profile query is
    // still mounted when the save succeeds, so its invalidation refetches at
    // once — which is what proves the refresh, where the flag would not.
    await waitFor(() => {
      expect(state.listReads()).toBeGreaterThan(listReadsBefore);
    });
    expect(state.profileReads()).toBe(2);
  });

  it("treats a save with nothing changed as a save", async () => {
    const state = backend();
    const { user } = await render(READ, UPDATE);

    const dialog = await openEdit(user);
    await loaded(dialog);
    await user.click(within(dialog).getByRole("button", { name: "Save" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });
    expect(state.posts).toHaveLength(1);
    expect(screen.getByRole("status")).toHaveTextContent("Profile saved for Ada L.");
  });

  it("is busy while sending, and sends once however often Save is pressed", async () => {
    const state = backend();
    server.use(
      http.post(at(`/api/users/${ADA.userId}/profile`), async ({ request }) => {
        state.posts.push({ path: new URL(request.url).pathname, body: await request.json() });
        await delay(200);
        return new HttpResponse(null, { status: 204 });
      }),
    );
    const { user } = await render(READ, UPDATE);

    const dialog = await openEdit(user);
    await loaded(dialog);
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

// ---------------------------------------------------------------- UI-5, UI-6

describe("unsaved changes in the dialog", () => {
  it.each(["Cancel", "Escape", "Close"])("asks before closing a dirty form by %s", async (how) => {
    backend();
    const { user } = await render(READ, UPDATE);

    const dialog = await openEdit(user);
    await loaded(dialog);
    await replace(user, dialog, "First name", "Augusta");

    if (how === "Escape") {
      await user.keyboard("{Escape}");
    } else {
      await user.click(within(dialog).getByRole("button", { name: how }));
    }

    const prompt = await screen.findByRole("dialog", { name: "Discard changes?" });
    await user.click(within(prompt).getByRole("button", { name: "Keep editing" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog", { name: "Discard changes?" })).toBeNull();
    });
    const editor = screen.getByRole("dialog", { name: "Edit profile for Ada L." });
    expect(within(editor).getByLabelText("First name")).toHaveValue("Augusta");

    // Found in the browser (UI-9): Keep editing returns focus inside the edit
    // dialog, not to the document body, so a keyboard user can go on.
    await waitFor(() => {
      expect(editor.contains(document.activeElement)).toBe(true);
    });

    await user.click(within(screen.getByRole("dialog", { name: "Edit profile for Ada L." })).getByRole("button", { name: "Cancel" }));
    await user.click(await screen.findByRole("button", { name: "Discard" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });
  });

  it("closes a clean form without asking", async () => {
    backend();
    const { user } = await render(READ, UPDATE);

    const dialog = await openEdit(user);
    await loaded(dialog);
    await user.click(within(dialog).getByRole("button", { name: "Cancel" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });
  });

  it("holds beforeunload while the dialog is dirty, and not after a save", async () => {
    backend();
    const { user } = await render(READ, UPDATE);
    const cancelled = () => {
      const event = new Event("beforeunload", { cancelable: true });
      window.dispatchEvent(event);
      return event.defaultPrevented;
    };

    const dialog = await openEdit(user);
    await loaded(dialog);
    expect(cancelled()).toBe(false);

    await replace(user, dialog, "First name", "Augusta");
    expect(cancelled()).toBe(true);

    await user.click(within(dialog).getByRole("button", { name: "Save" }));
    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });
    expect(cancelled()).toBe(false);
  });
});

// ---------------------------------------------------------------- UI-8

describe("accessibility", () => {
  it("has no violations with the dialog open, and with the discard prompt open", async () => {
    backend();
    const { user } = await render(READ, UPDATE);

    const dialog = await openEdit(user);
    await loaded(dialog);
    await expectNoAccessibilityViolations(document.body);

    await replace(user, dialog, "First name", "Augusta");
    await user.click(within(dialog).getByRole("button", { name: "Cancel" }));
    await screen.findByRole("dialog", { name: "Discard changes?" });

    await expectNoAccessibilityViolations(document.body);
  });
});
