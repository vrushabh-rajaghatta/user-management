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
 * USR-Q1's page: the Users table, its paging, and the two row actions that
 * already exist — CRD-C5 (reset password) and SES-C4's administrator form
 * (sign out everywhere).
 *
 * The backend contract is fixed (docs/requirements.md, USR-Q1): rows of
 * userId, displayName and email; page, pageSize and hasMore; a fixed order; no
 * total, filtering or client sorting. These tests are about how that contract
 * is presented and driven, never about reopening it.
 */

const at = (path: string) => new URL(path, window.location.origin).href;

const CREATE = definePermission("user.create");
const READ = definePermission("user.read");
const RESET = definePermission("user.resetpassword");
const REVOKE = definePermission("session.revoke");

const ADA = { userId: "a0000000-0000-4000-8000-00000000ada1", displayName: "Ada Lovelace", email: "ada@example.test" };
const GRACE = { userId: "b0000000-0000-4000-8000-00000000c0de", displayName: "Grace Hopper", email: null };

type Row = { userId: string; displayName: string; email: string | null };

interface Listing {
  readonly users: readonly Row[];
  readonly hasMore: boolean;
}

/**
 * Answers GET /api/users per requested page, and records every query string
 * the page sent, so a test can prove what reached the server — not only what
 * was rendered.
 */
function list(pages: Record<number, Listing>, options: { hold?: Promise<void> } = {}) {
  const requested: string[] = [];

  server.use(
    http.get(at("/api/users"), async ({ request }) => {
      const url = new URL(request.url);
      requested.push(url.search);

      if (options.hold !== undefined) {
        await options.hold;
      }

      const page = Number(url.searchParams.get("page"));
      const listing = pages[page] ?? { users: [], hasMore: false };

      return HttpResponse.json({ users: listing.users, page, pageSize: 25, hasMore: listing.hasMore });
    }),
  );

  return requested;
}

function routes(): RouteObject[] {
  return [
    { path: "/admin/users", element: <UsersPage /> },
    { path: "/admin/users/new", element: <h1>Create user</h1> },
  ];
}

/**
 * Renders with the session UNRESOLVED, then settles it before any assertion.
 * Until a session resolves every permission is unknown, and unknown is shown
 * (§9) — so an assertion that an action IS offered, made before resolution,
 * would pass whatever permission gated it.
 */
async function render(path: string, ...codes: PermissionCode[]) {
  const source = new TestSessionSource();

  const result = renderWithApp(routes(), { path, source });

  await act(async () => {
    source.settle({ status: "authenticated", principal: { permissions: codes.map((code) => ({ code })) } });
    await Promise.resolve();
  });

  return result;
}

const table = () => screen.findByRole("table", { name: "Users" });

const actionsFor = (name: string) => screen.findByRole("button", { name: new RegExp(`^Actions for ${name}`) });

// ---------------------------------------------------------------- the header

describe("the Users page header", () => {
  it("is titled Users", async () => {
    list({ 1: { users: [], hasMore: false } });

    await render("/admin/users", READ);

    expect(await screen.findByRole("heading", { level: 1, name: "Users" })).toBeInTheDocument();
    expect(document.title).toBe("Users · Ligature");
  });

  it("offers New user, linking to the create user page, to a caller who holds user.create", async () => {
    list({ 1: { users: [], hasMore: false } });

    await render("/admin/users", READ, CREATE);

    expect(await screen.findByRole("link", { name: "New user" })).toHaveAttribute("href", "/admin/users/new");
  });

  it("does not offer New user to a caller who does not", async () => {
    list({ 1: { users: [], hasMore: false } });

    await render("/admin/users", READ);

    await screen.findByRole("heading", { level: 1, name: "Users" });

    expect(screen.queryByRole("link", { name: "New user" })).toBeNull();
  });
});

// ---------------------------------------------------------------- the table

describe("the Users table", () => {
  it("asks for page 1, with no page size, when the URL names no page", async () => {
    const requested = list({ 1: { users: [ADA], hasMore: false } });

    await render("/admin/users", READ);

    await table();

    expect(requested).toEqual(["?page=1"]);
  });

  it("shows each user's display name with their email beneath it", async () => {
    list({ 1: { users: [ADA], hasMore: false } });

    await render("/admin/users", READ);

    const row = within(await table()).getByRole("row", { name: /Ada Lovelace/ });

    expect(within(row).getByText("Ada Lovelace")).toBeInTheDocument();
    expect(within(row).getByText("ada@example.test")).toBeInTheDocument();
  });

  it("says so when a user has no email address, rather than leaving a gap", async () => {
    list({ 1: { users: [GRACE], hasMore: false } });

    await render("/admin/users", READ);

    const row = within(await table()).getByRole("row", { name: /Grace Hopper/ });

    expect(within(row).getByText("No email address")).toBeInTheDocument();
  });

  it("shows the rows in the order the server returned them", async () => {
    list({ 1: { users: [GRACE, ADA], hasMore: false } });

    await render("/admin/users", READ);

    const rows = within(await table()).getAllByRole("row").slice(1);

    expect(rows.map((row) => within(row).getAllByRole("cell")[0]?.textContent)).toEqual([
      "Grace HopperNo email address",
      "Ada Lovelaceada@example.test",
    ]);
  });

  it("is marked busy while the first page loads", async () => {
    let release: (() => void) | undefined;
    const hold = new Promise<void>((resolve) => {
      release = resolve;
    });

    list({ 1: { users: [ADA], hasMore: false } }, { hold });

    await render("/admin/users", READ);

    expect(await screen.findByLabelText("Loading users")).toBeInTheDocument();
    expect(screen.queryByRole("table")).toBeNull();

    release?.();

    await table();
    expect(screen.queryByLabelText("Loading users")).toBeNull();
  });

  it("states the server's message, and retries, when the list cannot be read", async () => {
    let fail = true;

    server.use(
      http.get(at("/api/users"), () =>
        fail
          ? HttpResponse.json({ error: "The page number must be 1 or greater." }, { status: 400 })
          : HttpResponse.json({ users: [ADA], page: 1, pageSize: 25, hasMore: false }),
      ),
    );

    const { user } = await render("/admin/users", READ);

    const alert = await screen.findByRole("alert");

    expect(alert).toHaveTextContent("The page number must be 1 or greater.");

    fail = false;
    await user.click(within(alert).getByRole("button", { name: "Try again" }));

    expect(await table()).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------- empty pages

describe("an empty page", () => {
  it("on page 1 says there are no users", async () => {
    list({ 1: { users: [], hasMore: false } });

    await render("/admin/users", READ);

    expect(await screen.findByText("No users")).toBeInTheDocument();
    expect(screen.queryByRole("table")).toBeNull();
    expect(screen.queryByText("This page is past the end")).toBeNull();
  });

  /**
   * A valid response, not an error: the offset simply lies beyond the
   * collection. It is not announced as an alert.
   */
  it("past page 1 says the page is past the end, and links back to page 1", async () => {
    list({ 7: { users: [], hasMore: false } });

    await render("/admin/users?page=7", READ);

    expect(await screen.findByText("This page is past the end")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Go to page 1" })).toHaveAttribute("href", "/admin/users");
    expect(screen.queryByRole("alert")).toBeNull();
    expect(screen.queryByText("No users")).toBeNull();
  });
});

// ---------------------------------------------------------------- paging

describe("paging", () => {
  it("takes the page from the URL", async () => {
    const requested = list({ 2: { users: [GRACE], hasMore: false } });

    await render("/admin/users?page=2", READ);

    await table();

    expect(requested).toEqual(["?page=2"]);
    expect(screen.getByText("Page 2")).toBeInTheDocument();
  });

  it.each(["abc", "0", "-1", "1.5", "2e1", ""])(
    "replaces page=%s with page 1, and never sends it",
    async (value) => {
      const requested = list({ 1: { users: [ADA], hasMore: false } });

      const { router } = await render(`/admin/users?page=${value}`, READ);

      await table();

      expect(router.state.location.search).toBe("");
      expect(requested).toEqual(["?page=1"]);
    },
  );

  it("offers Next, as a link to the following page, only while there is more", async () => {
    list({ 1: { users: [ADA], hasMore: true }, 2: { users: [GRACE], hasMore: false } });

    const { user, router } = await render("/admin/users", READ);

    await table();

    const next = screen.getByRole("link", { name: /Next/ });
    expect(next).toHaveAttribute("href", "/admin/users?page=2");

    await user.click(next);

    await screen.findByText("Grace Hopper");
    expect(router.state.location.search).toBe("?page=2");
    expect(screen.queryByRole("link", { name: /Next/ })).toBeNull();
  });

  it("offers Previous from page 2, linking to page 1 without a page parameter", async () => {
    list({ 2: { users: [GRACE], hasMore: false } });

    await render("/admin/users?page=2", READ);

    await table();

    expect(screen.getByRole("link", { name: /Previous/ })).toHaveAttribute("href", "/admin/users");
  });

  it("offers no Previous on page 1", async () => {
    list({ 1: { users: [ADA], hasMore: true } });

    await render("/admin/users", READ);

    await table();

    expect(screen.queryByRole("link", { name: /Previous/ })).toBeNull();
  });

  it("keeps the current page on screen while the next one loads", async () => {
    let release: (() => void) | undefined;
    const hold = new Promise<void>((resolve) => {
      release = resolve;
    });

    server.use(
      http.get(at("/api/users"), async ({ request }) => {
        const page = new URL(request.url).searchParams.get("page");

        if (page === "2") {
          await hold;
          return HttpResponse.json({ users: [GRACE], page: 2, pageSize: 25, hasMore: false });
        }

        return HttpResponse.json({ users: [ADA], page: 1, pageSize: 25, hasMore: true });
      }),
    );

    const { user } = await render("/admin/users", READ);

    await table();
    await user.click(screen.getByRole("link", { name: /Next/ }));

    expect(screen.getByText("Ada Lovelace")).toBeInTheDocument();
    expect(screen.queryByLabelText("Loading users")).toBeNull();

    release?.();

    expect(await screen.findByText("Grace Hopper")).toBeInTheDocument();
  });

  it("returns to the previous page with Back", async () => {
    list({ 1: { users: [ADA], hasMore: true }, 2: { users: [GRACE], hasMore: false } });

    const { user, router } = await render("/admin/users", READ);

    await table();
    await user.click(screen.getByRole("link", { name: /Next/ }));
    await screen.findByText("Grace Hopper");

    await act(async () => {
      await router.navigate(-1);
    });

    expect(await screen.findByText("Ada Lovelace")).toBeInTheDocument();
    expect(router.state.location.search).toBe("");
  });
});

// ---------------------------------------------------------------- row actions

describe("a row's actions", () => {
  it("offers both actions to a caller who holds both permissions", async () => {
    list({ 1: { users: [ADA], hasMore: false } });

    const { user } = await render("/admin/users", READ, RESET, REVOKE);

    await user.click(await actionsFor("Ada Lovelace"));

    const menu = await screen.findByRole("menu");

    expect(within(menu).getByRole("menuitem", { name: "Reset password" })).toBeInTheDocument();
    expect(within(menu).getByRole("menuitem", { name: "Sign out everywhere" })).toBeInTheDocument();
  });

  it("names the row's user, and their email, in the actions button's label", async () => {
    list({ 1: { users: [ADA, GRACE], hasMore: false } });

    await render("/admin/users", READ, RESET);

    expect(await screen.findByRole("button", { name: "Actions for Ada Lovelace (ada@example.test)" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Actions for Grace Hopper" })).toBeInTheDocument();
  });

  it("offers only reset password to a caller who holds only user.resetpassword", async () => {
    list({ 1: { users: [ADA], hasMore: false } });

    const { user } = await render("/admin/users", READ, RESET);

    await user.click(await actionsFor("Ada Lovelace"));

    const menu = await screen.findByRole("menu");

    expect(within(menu).getByRole("menuitem", { name: "Reset password" })).toBeInTheDocument();
    expect(within(menu).queryByRole("menuitem", { name: "Sign out everywhere" })).toBeNull();
  });

  it("offers only sign out everywhere to a caller who holds only session.revoke", async () => {
    list({ 1: { users: [ADA], hasMore: false } });

    const { user } = await render("/admin/users", READ, REVOKE);

    await user.click(await actionsFor("Ada Lovelace"));

    const menu = await screen.findByRole("menu");

    expect(within(menu).getByRole("menuitem", { name: "Sign out everywhere" })).toBeInTheDocument();
    expect(within(menu).queryByRole("menuitem", { name: "Reset password" })).toBeNull();
  });

  /** The access reviewer's case: the list, and nothing to do with it. */
  it("offers no actions button to a caller who holds neither", async () => {
    list({ 1: { users: [ADA], hasMore: false } });

    await render("/admin/users", READ);

    await table();

    expect(screen.queryByRole("button", { name: /^Actions for/ })).toBeNull();
  });
});

// ---------------------------------------------------------------- reset password

describe("resetting a user's password", () => {
  async function openReset() {
    list({ 1: { users: [ADA, GRACE], hasMore: false } });

    const rendered = await render("/admin/users", READ, RESET, REVOKE);

    await rendered.user.click(await actionsFor("Ada Lovelace"));
    await rendered.user.click(await screen.findByRole("menuitem", { name: "Reset password" }));

    const dialog = await screen.findByRole("dialog", { name: "Reset password for Ada Lovelace" });

    return { ...rendered, dialog };
  }

  it("explains what happens, and what the administrator never sees", async () => {
    const { dialog } = await openReset();

    expect(dialog).toHaveTextContent("They'll be emailed a link to choose a new password.");
    expect(dialog).toHaveTextContent("Any earlier reset link stops working.");
    expect(dialog).toHaveTextContent("You won't see the link or their password.");
  });

  it("requires a reason before sending anything", async () => {
    let calls = 0;
    server.use(
      http.post(at(`/api/users/${ADA.userId}/password-reset`), () => {
        calls += 1;
        return new HttpResponse(null, { status: 202 });
      }),
    );

    const { user, dialog } = await openReset();

    await user.type(within(dialog).getByLabelText("Reason"), "   ");
    await user.click(within(dialog).getByRole("button", { name: "Reset password" }));

    expect(within(dialog).getByRole("alert")).toHaveTextContent("A reason is required.");
    expect(calls).toBe(0);
  });

  it("sends exactly the reason to the row's own userId, then confirms and returns focus to the row", async () => {
    let path: string | undefined;
    let body: unknown;

    server.use(
      http.post(at("/api/users/:userId/password-reset"), async ({ request }) => {
        path = new URL(request.url).pathname;
        body = await request.json();
        return new HttpResponse(null, { status: 202 });
      }),
    );

    const { user, dialog } = await openReset();

    await user.type(within(dialog).getByLabelText("Reason"), "Suspected compromise.");
    await user.click(within(dialog).getByRole("button", { name: "Reset password" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });

    expect(path).toBe(`/api/users/${ADA.userId}/password-reset`);
    expect(body).toEqual({ reason: "Suspected compromise." });
    expect(screen.getByRole("status")).toHaveTextContent("Password reset initiated for Ada Lovelace.");

    await waitFor(() => {
      expect(document.activeElement).toBe(screen.getByRole("button", { name: /^Actions for Ada Lovelace/ }));
    });
  });

  it("keeps the dialog open and shows the server's refusal word for word", async () => {
    server.use(
      http.post(at(`/api/users/${ADA.userId}/password-reset`), () =>
        HttpResponse.json({ error: "This user's password cannot be reset by an administrator." }, { status: 400 }),
      ),
    );

    const { user, dialog } = await openReset();

    await user.type(within(dialog).getByLabelText("Reason"), "Asked by phone.");
    await user.click(within(dialog).getByRole("button", { name: "Reset password" }));

    expect(await within(dialog).findByRole("alert")).toHaveTextContent(
      "This user's password cannot be reset by an administrator.",
    );
    expect(screen.getByRole("dialog", { name: "Reset password for Ada Lovelace" })).toBeInTheDocument();

    // The live region exists before anything is announced — that is what makes
    // an announcement reliable — so a refusal leaves it present and empty. It
    // is behind the open modal, hidden from assistive technology, so it is
    // found with hidden: true.
    expect(screen.getByRole("status", { hidden: true })).toBeEmptyDOMElement();
  });

  it("is busy while sending, and sends once however often confirm is pressed", async () => {
    let calls = 0;
    let release: (() => void) | undefined;
    const hold = new Promise<void>((resolve) => {
      release = resolve;
    });

    server.use(
      http.post(at(`/api/users/${ADA.userId}/password-reset`), async () => {
        calls += 1;
        await hold;
        return new HttpResponse(null, { status: 202 });
      }),
    );

    const { user, dialog } = await openReset();

    await user.type(within(dialog).getByLabelText("Reason"), "Suspected compromise.");
    await user.click(within(dialog).getByRole("button", { name: "Reset password" }));

    const busy = await within(dialog).findByRole("button", { name: "Resetting…" });

    expect(busy).toBeDisabled();

    await user.click(busy);
    await delay(20);

    expect(calls).toBe(1);

    release?.();

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });
  });

  it("cancels without sending anything, and returns focus to the row", async () => {
    let calls = 0;
    server.use(
      http.post(at(`/api/users/${ADA.userId}/password-reset`), () => {
        calls += 1;
        return new HttpResponse(null, { status: 202 });
      }),
    );

    const { user, dialog } = await openReset();

    await user.click(within(dialog).getByRole("button", { name: "Cancel" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });

    expect(calls).toBe(0);
    await waitFor(() => {
      expect(document.activeElement).toBe(screen.getByRole("button", { name: /^Actions for Ada Lovelace/ }));
    });
  });

  /**
   * The row an action started from can leave the screen while its dialog is
   * open. The action still completes and is still confirmed; focus simply has
   * nowhere to return to, and that is not an error.
   */
  it("still confirms when the row has left the screen while the dialog was open", async () => {
    list({ 1: { users: [ADA], hasMore: true }, 2: { users: [GRACE], hasMore: false } });

    server.use(
      http.post(at(`/api/users/${ADA.userId}/password-reset`), () => new HttpResponse(null, { status: 202 })),
    );

    const rendered = await render("/admin/users", READ, RESET);

    await rendered.user.click(await actionsFor("Ada Lovelace"));
    await rendered.user.click(await screen.findByRole("menuitem", { name: "Reset password" }));

    const dialog = await screen.findByRole("dialog", { name: "Reset password for Ada Lovelace" });

    await rendered.user.type(within(dialog).getByLabelText("Reason"), "Suspected compromise.");

    await act(async () => {
      await rendered.router.navigate("/admin/users?page=2");
    });

    await screen.findByText("Grace Hopper");

    await rendered.user.click(within(dialog).getByRole("button", { name: "Reset password" }));

    expect(await screen.findByRole("status")).toHaveTextContent("Password reset initiated for Ada Lovelace.");
    expect(screen.queryByRole("dialog")).toBeNull();
  });
});

// ---------------------------------------------------------------- sign out everywhere

describe("signing a user out everywhere", () => {
  async function openSignOut() {
    list({ 1: { users: [ADA], hasMore: false } });

    const rendered = await render("/admin/users", READ, REVOKE);

    await rendered.user.click(await actionsFor("Ada Lovelace"));
    await rendered.user.click(await screen.findByRole("menuitem", { name: "Sign out everywhere" }));

    const dialog = await screen.findByRole("dialog", { name: "Sign out Ada Lovelace everywhere" });

    return { ...rendered, dialog };
  }

  /** The client cannot tell whether this row is the caller, so it says so. */
  it("warns that signing out one's own account signs the caller out too", async () => {
    const { dialog } = await openSignOut();

    expect(dialog).toHaveTextContent("Ends every active session of this user.");
    expect(dialog).toHaveTextContent("If this is your own account, you will be signed out too.");
  });

  it("sends exactly the reason to the row's own userId, then confirms", async () => {
    let path: string | undefined;
    let body: unknown;

    server.use(
      http.post(at("/api/users/:userId/sign-out-everywhere"), async ({ request }) => {
        path = new URL(request.url).pathname;
        body = await request.json();
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const { user, dialog } = await openSignOut();

    await user.type(within(dialog).getByLabelText("Reason"), "Lost laptop.");
    await user.click(within(dialog).getByRole("button", { name: "Sign out everywhere" }));

    expect(await screen.findByRole("status")).toHaveTextContent(
      "All active sessions for Ada Lovelace have been signed out.",
    );
    expect(path).toBe(`/api/users/${ADA.userId}/sign-out-everywhere`);
    expect(body).toEqual({ reason: "Lost laptop." });
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("keeps the dialog open and shows the server's refusal word for word", async () => {
    server.use(
      http.post(at(`/api/users/${ADA.userId}/sign-out-everywhere`), () =>
        HttpResponse.json({ error: "The user does not exist." }, { status: 400 }),
      ),
    );

    const { user, dialog } = await openSignOut();

    await user.type(within(dialog).getByLabelText("Reason"), "Lost laptop.");
    await user.click(within(dialog).getByRole("button", { name: "Sign out everywhere" }));

    expect(await within(dialog).findByRole("alert")).toHaveTextContent("The user does not exist.");
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------- accessibility

describe("the Users page's accessibility", () => {
  it("has no violations with rows and actions", async () => {
    list({ 1: { users: [ADA, GRACE], hasMore: true } });

    const { container } = await render("/admin/users", READ, CREATE, RESET, REVOKE);

    await table();
    await expectNoAccessibilityViolations(container);
  });

  it("has no violations with a confirmation open", async () => {
    list({ 1: { users: [ADA], hasMore: false } });

    const { user } = await render("/admin/users", READ, RESET);

    await user.click(await actionsFor("Ada Lovelace"));
    await user.click(await screen.findByRole("menuitem", { name: "Reset password" }));
    await screen.findByRole("dialog");

    await expectNoAccessibilityViolations(document.body);
  });
});
