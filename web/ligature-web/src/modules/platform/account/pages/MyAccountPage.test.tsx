import { act, screen, waitFor, within } from "@testing-library/react";
import { delay, http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { appRoutes } from "@/app/router";
import { expectNoAccessibilityViolations } from "@/test/axe";
import { server } from "@/test/msw/server";
import { renderWithApp } from "@/test/renderWithApp";
import { TestSessionSource } from "@/test/sessions";

/**
 * The My account page (docs/requirements.md, "CRD-C4 and SES-C4 (self) — the
 * My account page", UI-1..UI-9). Rendered through the application's own
 * routes, so the footer link, RequireAuth and the sign-in redirect are the
 * ones that ship.
 *
 * THE SERVER OWNS THE PASSWORD POLICY (M4). The client checks presence and the
 * confirmation match only, sends exactly what was typed, and shows every
 * refusal word for word.
 *
 * A SIGN-OUT FAILURE IS NOT A SIGN-OUT (M9). Unlike the Sign out button, a
 * failed Sign out everywhere leaves the caller signed in and on the page,
 * because what was revoked is not known. A 401 means the session had already
 * ended.
 */

const at = (path: string) => new URL(path, window.location.origin).href;

const CHANGE = at("/api/account/change-password");
const EVERYWHERE = at("/api/account/sign-out-everywhere");

const CHANGED =
  "Your password has been changed. Any other sessions on this account have been signed out; you remain signed in here.";
const OTHERS_ENDED = "Your other sessions have been signed out.";
const MISMATCH = "The passwords do not match.";

interface Backend {
  readonly changes: unknown[];
  readonly signOuts: unknown[];
}

/** Records every body that reached the two endpoints; each answers 204 unless told otherwise. */
function backend(options: { change?: () => Response | Promise<Response>; everywhere?: () => Response } = {}): Backend {
  const changes: unknown[] = [];
  const signOuts: unknown[] = [];

  server.use(
    // SES-Q2's list reads on arrival; its own tests are in MyAccountSessions.test.tsx.
    http.get(at("/api/account/sessions"), () => HttpResponse.json({ sessions: [] })),
    http.post(CHANGE, async ({ request }) => {
      changes.push(await request.json());
      return options.change === undefined ? new HttpResponse(null, { status: 204 }) : options.change();
    }),
    http.post(EVERYWHERE, async ({ request }) => {
      const text = await request.text();
      signOuts.push(text === "" ? undefined : JSON.parse(text));
      return options.everywhere === undefined ? new HttpResponse(null, { status: 204 }) : options.everywhere();
    }),
  );

  return { changes, signOuts };
}

/** Authenticated with NO permissions: My account belongs to every caller (UI-1). */
function render(path = "/account") {
  const source = new TestSessionSource({ status: "authenticated", principal: { permissions: [] } });

  return { ...renderWithApp(appRoutes, { path, source }), source };
}

async function page() {
  return screen.findByRole("heading", { level: 1, name: "My account" });
}

function passwordForm() {
  return screen.getByRole("region", { name: "Change password" });
}

function sessions() {
  return screen.getByRole("region", { name: "Sessions" });
}

type User = ReturnType<typeof renderWithApp>["user"];

async function fill(user: User, values: { current?: string; next?: string; confirm?: string }) {
  const form = passwordForm();

  if (values.current !== undefined) await user.type(within(form).getByLabelText("Current password"), values.current);
  if (values.next !== undefined) await user.type(within(form).getByLabelText("New password"), values.next);
  if (values.confirm !== undefined) await user.type(within(form).getByLabelText("Confirm new password"), values.confirm);
}

async function submit(user: User) {
  await user.click(within(passwordForm()).getByRole("button", { name: "Change password" }));
}

async function announced(text: string) {
  await waitFor(() => {
    expect(screen.getAllByRole("status").some((region) => region.textContent.includes(text))).toBe(true);
  });
}

/**
 * Whether "Discard changes?" appears at ANY point, not only at the end. A
 * prompt that flashes up and is then unmounted by the sign-out would pass a
 * check of the final state, so every change to the document is watched.
 */
function watchForDiscardPrompt() {
  let seen = false;

  // The ADDED nodes are inspected, not the document when the callback runs: a
  // prompt mounted and unmounted within one batch is gone by then.
  const observer = new MutationObserver((records) => {
    for (const record of records) {
      for (const node of Array.from(record.addedNodes)) {
        if (node.textContent?.includes("Discard changes?") === true) {
          seen = true;
        }
      }
    }
  });
  observer.observe(document.body, { childList: true, subtree: true });

  return {
    seen: () => {
      for (const record of observer.takeRecords()) {
        for (const node of Array.from(record.addedNodes)) {
          if (node.textContent?.includes("Discard changes?") === true) {
            seen = true;
          }
        }
      }
      observer.disconnect();
      return seen;
    },
  };
}

const unloadCancelled = () => {
  const event = new Event("beforeunload", { cancelable: true });
  window.dispatchEvent(event);
  return event.defaultPrevented;
};

// ---------------------------------------------------------------- UI-1

describe("reaching My account", () => {
  it("renders for an authenticated caller holding no permissions", async () => {
    render();

    expect(await page()).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Change password" })).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Sessions" })).toBeInTheDocument();
  });

  it("sends an unauthenticated visitor to sign-in, returning to /account", async () => {
    const { router } = renderWithApp(appRoutes, { path: "/account" });

    expect(await screen.findByRole("heading", { level: 1, name: "Sign in" })).toBeInTheDocument();
    expect(router.state.location.state).toMatchObject({ returnTo: "/account" });
  });
});

// ---------------------------------------------------------------- UI-2

describe("the footer link", () => {
  it("is offered beside Sign out to every signed-in caller, and leads to /account", async () => {
    const { user, router } = render("/");

    await screen.findByRole("heading", { level: 1, name: "Home" });
    const link = screen.getByRole("link", { name: "My account" });
    expect(screen.getByRole("button", { name: "Sign out" })).toBeInTheDocument();

    await user.click(link);

    expect(await page()).toBeInTheDocument();
    expect(router.state.location.pathname).toBe("/account");
  });

  it("is marked as the current page on /account", async () => {
    render();

    await page();
    expect(screen.getByRole("link", { name: "My account" })).toHaveAttribute("aria-current", "page");
  });

  it("is not in the primary navigation", async () => {
    render();

    await page();
    const main = screen.queryByRole("navigation", { name: "Main" });
    expect(main === null ? null : within(main).queryByRole("link", { name: "My account" })).toBeNull();
  });
});

// ---------------------------------------------------------------- UI-3

describe("change password: what is sent", () => {
  it("sends exactly the current and new password, untrimmed, and never the confirmation", async () => {
    const state = backend();
    const { user } = render();
    await page();

    await fill(user, { current: " old secret ", next: " new secret ", confirm: " new secret " });
    await submit(user);

    await waitFor(() => {
      expect(state.changes).toHaveLength(1);
    });
    expect(state.changes[0]).toStrictEqual({ currentPassword: " old secret ", newPassword: " new secret " });
  });

  it.each([
    ["the current password", { next: "new secret", confirm: "new secret" }],
    ["the new password", { current: "old secret", confirm: "new secret" }],
    ["the confirmation", { current: "old secret", next: "new secret" }],
  ])("sends nothing when %s is empty", async (_, values) => {
    const state = backend();
    const { user } = render();
    await page();

    await fill(user, values);
    await submit(user);

    expect(await within(passwordForm()).findAllByText(/required/i)).not.toHaveLength(0);
    expect(state.changes).toHaveLength(0);
  });

  it("sends nothing when the confirmation does not match, and says so", async () => {
    const state = backend();
    const { user } = render();
    await page();

    await fill(user, { current: "old secret", next: "new secret", confirm: "new secreT" });
    await submit(user);

    expect(await within(passwordForm()).findByText(MISMATCH)).toBeInTheDocument();
    expect(state.changes).toHaveLength(0);
  });

  it("is busy while sending, and sends once however often it is pressed", async () => {
    const state = backend({
      change: async () => {
        await delay(200);
        return new HttpResponse(null, { status: 204 });
      },
    });
    const { user } = render();
    await page();

    await fill(user, { current: "old secret", next: "new secret", confirm: "new secret" });
    const button = within(passwordForm()).getByRole("button", { name: "Change password" });
    await user.click(button);
    await user.click(button);

    expect(within(passwordForm()).getByRole("button", { name: "Changing…" })).toBeInTheDocument();
    await announced(CHANGED);
    expect(state.changes).toHaveLength(1);
  });

  it("leaves the policy to the server: no length limits, and the autocomplete hints", async () => {
    render();
    await page();
    const form = passwordForm();

    const current = within(form).getByLabelText("Current password");
    const next = within(form).getByLabelText("New password");
    const confirm = within(form).getByLabelText("Confirm new password");

    expect(current).toHaveAttribute("type", "password");
    expect(current).toHaveAttribute("autocomplete", "current-password");
    expect(next).toHaveAttribute("type", "password");
    expect(next).toHaveAttribute("autocomplete", "new-password");
    expect(confirm).toHaveAttribute("type", "password");
    expect(confirm).toHaveAttribute("autocomplete", "new-password");

    for (const input of [current, next, confirm]) {
      expect(input).not.toHaveAttribute("minlength");
      expect(input).not.toHaveAttribute("maxlength");
    }

    expect(within(form).queryByRole("button", { name: /show/i })).toBeNull();
  });
});

// ---------------------------------------------------------------- UI-4

describe("change password: the outcome", () => {
  it("clears every field and announces the A5 sentence on 204", async () => {
    backend();
    const { user } = render();
    await page();

    await fill(user, { current: "old secret", next: "new secret", confirm: "new secret" });
    await submit(user);

    await announced(CHANGED);
    const form = passwordForm();
    expect(within(form).getByLabelText("Current password")).toHaveValue("");
    expect(within(form).getByLabelText("New password")).toHaveValue("");
    expect(within(form).getByLabelText("Confirm new password")).toHaveValue("");
  });

  it.each([
    "The password could not be changed.",
    "The password must be at least 12 characters.",
    "The new password must not match a recently used password.",
  ])("shows %s word for word and keeps what was typed", async (message) => {
    backend({ change: () => HttpResponse.json({ error: message }, { status: 400 }) });
    const { user } = render();
    await page();

    await fill(user, { current: "old secret", next: "new secret", confirm: "new secret" });
    await submit(user);

    expect(await within(passwordForm()).findByText(message)).toBeInTheDocument();
    expect(within(passwordForm()).getByLabelText("Current password")).toHaveValue("old secret");
    expect(within(passwordForm()).getByLabelText("New password")).toHaveValue("new secret");
    expect(within(passwordForm()).getByLabelText("Confirm new password")).toHaveValue("new secret");
    expect(unloadCancelled()).toBe(true);
  });

  it("goes to sign-in when the server answers 401: this session has ended", async () => {
    backend({ change: () => HttpResponse.json({ error: "Authentication is required." }, { status: 401 }) });
    const { user } = render();
    await page();

    await fill(user, { current: "old secret", next: "new secret", confirm: "new secret" });
    await submit(user);

    expect(await screen.findByRole("heading", { level: 1, name: "Sign in" })).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------- UI-5

describe("change password: unsaved changes", () => {
  it.each(["Current password", "New password", "Confirm new password"])(
    "holds beforeunload once %s alone has been typed into, and releases it when emptied",
    async (label) => {
      const { user } = render();
      await page();
      const field = within(passwordForm()).getByLabelText(label);

      expect(unloadCancelled()).toBe(false);

      await user.type(field, "x");
      expect(unloadCancelled()).toBe(true);

      await user.clear(field);
      expect(unloadCancelled()).toBe(false);
    },
  );

  it("asks before an in-app link leaves a started form", async () => {
    const { user, router } = render();
    await page();

    await fill(user, { current: "old secret" });
    await user.click(screen.getByRole("link", { name: "Ligature" }));

    expect(await screen.findByRole("dialog", { name: "Discard changes?" })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe("/account");
  });

  it("does not ask after a successful change", async () => {
    backend();
    const { user, router } = render();
    await page();

    await fill(user, { current: "old secret", next: "new secret", confirm: "new secret" });
    await submit(user);
    await announced(CHANGED);

    expect(unloadCancelled()).toBe(false);
    await user.click(screen.getByRole("link", { name: "Ligature" }));

    await screen.findByRole("heading", { level: 1, name: "Home" });
    expect(screen.queryByRole("dialog", { name: "Discard changes?" })).toBeNull();
    expect(router.state.location.pathname).toBe("/");
  });
});

// ---------------------------------------------------------------- UI-6

describe("sign out other sessions", () => {
  it("asks first, and Cancel sends nothing", async () => {
    const state = backend();
    const { user } = render();
    await page();

    await user.click(within(sessions()).getByRole("button", { name: "Sign out other sessions" }));
    const dialog = await screen.findByRole("dialog", { name: "Sign out other sessions?" });
    expect(dialog).toHaveTextContent("Every other session on your account will be signed out. You stay signed in here.");

    await user.click(within(dialog).getByRole("button", { name: "Cancel" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });
    expect(state.signOuts).toHaveLength(0);
  });

  it("keeps this session, sends no reason, stays on the page and announces", async () => {
    const state = backend();
    const { user, router, source } = render();
    await page();

    await user.click(within(sessions()).getByRole("button", { name: "Sign out other sessions" }));
    const dialog = await screen.findByRole("dialog", { name: "Sign out other sessions?" });
    await user.click(within(dialog).getByRole("button", { name: "Sign out other sessions" }));

    await announced(OTHERS_ENDED);
    expect(state.signOuts).toStrictEqual([{ keepCurrentSession: true }]);
    expect(router.state.location.pathname).toBe("/account");
    expect(source.signedOutCalls).toBe(0);
  });
});

// ---------------------------------------------------------------- UI-7

describe("sign out everywhere", () => {
  it("asks first, and Cancel sends nothing", async () => {
    const state = backend();
    const { user } = render();
    await page();

    await user.click(within(sessions()).getByRole("button", { name: "Sign out everywhere" }));
    const dialog = await screen.findByRole("dialog", { name: "Sign out everywhere?" });
    expect(dialog).toHaveTextContent(
      "Every session on your account will be signed out, including this one. You will need to sign in again.",
    );

    await user.click(within(dialog).getByRole("button", { name: "Cancel" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });
    expect(state.signOuts).toHaveLength(0);
  });

  it("includes this session, sends no reason, signs out locally and goes to sign-in", async () => {
    const state = backend();
    const { user, router, source, queryClient } = render();
    await page();
    queryClient.setQueryData(["users"], [{ username: "ada.lovelace" }]);

    await user.click(within(sessions()).getByRole("button", { name: "Sign out everywhere" }));
    const dialog = await screen.findByRole("dialog", { name: "Sign out everywhere?" });
    await user.click(within(dialog).getByRole("button", { name: "Sign out everywhere" }));

    expect(await screen.findByRole("heading", { level: 1, name: "Sign in" })).toBeInTheDocument();
    expect(state.signOuts).toStrictEqual([{ keepCurrentSession: false }]);
    expect(source.signedOutCalls).toBeGreaterThan(0);
    expect(queryClient.getQueryData(["users"])).toBeUndefined();
    expect(router.state.location.pathname).toBe("/sign-in");
  });

  it("does not ask to discard a started password form once the session has ended", async () => {
    backend();
    const { user } = render();
    await page();

    await fill(user, { current: "old secret" });
    await user.click(within(sessions()).getByRole("button", { name: "Sign out everywhere" }));
    const dialog = await screen.findByRole("dialog", { name: "Sign out everywhere?" });
    const prompt = watchForDiscardPrompt();
    await user.click(within(dialog).getByRole("button", { name: "Sign out everywhere" }));

    expect(await screen.findByRole("heading", { level: 1, name: "Sign in" })).toBeInTheDocument();
    expect(prompt.seen()).toBe(false);
  });

  it("does not ask to discard a started password form when the Sign out button is used", async () => {
    server.use(http.post(at("/api/auth/sign-out"), () => new HttpResponse(null, { status: 204 })));
    const { user } = render();
    await page();

    await fill(user, { current: "old secret" });
    const prompt = watchForDiscardPrompt();
    await user.click(screen.getByRole("button", { name: "Sign out" }));

    expect(await screen.findByRole("heading", { level: 1, name: "Sign in" })).toBeInTheDocument();
    expect(prompt.seen()).toBe(false);
  });
});

// ---------------------------------------------------------------- UI-8

describe("sign-out failures (M9)", () => {
  const actions = [
    ["Sign out other sessions", "Sign out other sessions?"],
    ["Sign out everywhere", "Sign out everywhere?"],
  ] as const;

  /**
   * Each failure's message as the application shows it: the server's own
   * sentence for a 500, and the API boundary's for a request that never
   * reached the server.
   */
  const failures = [
    [
      "the host fails",
      () => HttpResponse.json({ error: "The request could not be completed." }, { status: 500 }),
      "The request could not be completed.",
    ],
    ["the host cannot be reached", () => HttpResponse.error(), "The server could not be reached."],
  ] as const;

  for (const [action, title] of actions) {
    for (const [failure, respond, message] of failures) {
      it(`${action}: when ${failure}, says so and leaves the caller signed in on /account`, async () => {
        const state = backend({ everywhere: respond });
        const { user, router, source } = render();
        await page();

        await user.click(within(sessions()).getByRole("button", { name: action }));
        const dialog = await screen.findByRole("dialog", { name: title });
        await user.click(within(dialog).getByRole("button", { name: action }));

        expect(await screen.findByText(message)).toBeInTheDocument();
        expect(state.signOuts).toHaveLength(1);
        expect(source.signedOutCalls).toBe(0);
        expect(router.state.location.pathname).toBe("/account");
        expect(screen.queryByRole("heading", { level: 1, name: "Sign in" })).toBeNull();
        expect(screen.queryByText(OTHERS_ENDED)).toBeNull();
      });
    }

    it(`${action}: a 401 means the session had already ended, so it goes to sign-in`, async () => {
      backend({ everywhere: () => HttpResponse.json({ error: "Authentication is required." }, { status: 401 }) });
      const { user } = render();
      await page();

      await user.click(within(sessions()).getByRole("button", { name: action }));
      const dialog = await screen.findByRole("dialog", { name: title });
      await user.click(within(dialog).getByRole("button", { name: action }));

      expect(await screen.findByRole("heading", { level: 1, name: "Sign in" })).toBeInTheDocument();
    });
  }
});

// ---------------------------------------------------------------- UI-9

describe("accessibility", () => {
  it("has no violations on the page, with each confirmation open, and with the discard prompt open", async () => {
    const { user } = render();
    await page();
    await expectNoAccessibilityViolations(document.body);

    for (const [action, title] of [
      ["Sign out other sessions", "Sign out other sessions?"],
      ["Sign out everywhere", "Sign out everywhere?"],
    ] as const) {
      await user.click(within(sessions()).getByRole("button", { name: action }));
      const dialog = await screen.findByRole("dialog", { name: title });
      await expectNoAccessibilityViolations(document.body);
      await user.click(within(dialog).getByRole("button", { name: "Cancel" }));
      await waitFor(() => {
        expect(screen.queryByRole("dialog")).toBeNull();
      });
    }

    await fill(user, { current: "old secret" });
    await act(async () => {
      await user.click(screen.getByRole("link", { name: "Ligature" }));
    });
    await screen.findByRole("dialog", { name: "Discard changes?" });
    await expectNoAccessibilityViolations(document.body);
  });
});
