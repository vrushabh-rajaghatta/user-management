import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import type { RouteObject } from "react-router";
import { renderWithApp } from "@/test/renderWithApp";
import { server } from "@/test/msw/server";
import { expectNoAccessibilityViolations } from "@/test/axe";
import { ActivatePage } from "./ActivatePage";

/**
 * Activation: the emailed token in the URL fragment
 * (docs/frontend-architecture.md §13), ending any session first (F3), and no
 * copied password policy (F6).
 *
 * /activate is a BACKEND CONTRACT path: NotificationTemplates builds
 * "{base}/activate#token={escaped}". Renaming it breaks every link already
 * sent.
 */

const at = (path: string) => new URL(path, window.location.origin).href;

const SIGN_OUT = at("/api/auth/sign-out");
const ACTIVATE = at("/api/account/activate");

const routes: RouteObject[] = [
  { path: "/activate", element: <ActivatePage /> },
  { path: "/sign-in", element: <h1>Sign in</h1> },
];

/** Opens the page as the emailed link does: a real address bar, with a fragment. */
function open(fragment: string, search = "") {
  window.history.replaceState(null, "", `/activate${search}${fragment}`);

  return renderWithApp(routes, { path: "/activate" });
}

/** Every request the page makes, in order. */
function record(activate: () => Response = () => HttpResponse.json({ userIdentityId: "11111111-1111-1111-1111-111111111111" })) {
  const calls: string[] = [];

  server.use(
    http.post(SIGN_OUT, () => {
      calls.push("sign-out");
      return new HttpResponse(null, { status: 204 });
    }),
    http.post(ACTIVATE, () => {
      calls.push("activate");
      return activate();
    }),
  );

  return calls;
}

async function submit(user: ReturnType<typeof renderWithApp>["user"], password = "a new password") {
  await user.type(await screen.findByLabelText("New password"), password);
  await user.type(screen.getByLabelText("Confirm new password"), password);
  await user.click(screen.getByRole("button", { name: "Activate account" }));
}

beforeEach(() => {
  window.history.replaceState(null, "", "/");
});

afterEach(() => {
  window.history.replaceState(null, "", "/");
});

describe("the activation page", () => {
  it("shows the form when the link carries exactly one token", async () => {
    open("#token=abc.def");

    expect(await screen.findByLabelText("New password")).toBeInTheDocument();
  });

  /**
   * Asserted with no submission at all: the fragment must be gone because the
   * page removed it, not because a later request happened to succeed.
   */
  it("removes the token from the address bar on arrival, before anything is submitted", async () => {
    open("#token=abc.def");

    expect(await screen.findByLabelText("New password")).toBeInTheDocument();
    expect(window.location.hash).toBe("");
  });

  it("keeps the path and query while removing the fragment", async () => {
    open("#token=abc.def", "?ref=email");

    expect(await screen.findByLabelText("New password")).toBeInTheDocument();
    expect(window.location.pathname).toBe("/activate");
    expect(window.location.search).toBe("?ref=email");
    expect(window.location.hash).toBe("");
  });

  it("leaves the fragment gone after the request is refused", async () => {
    record(() => HttpResponse.json({ error: "That link is no longer valid." }, { status: 400 }));

    const { user } = open("#token=abc.def");

    await submit(user);

    expect(await screen.findByRole("alert")).toBeInTheDocument();
    expect(window.location.hash).toBe("");
  });

  it.each([
    ["no fragment at all", ""],
    ["a fragment with no token", "#ref=email"],
    ["a blank token", "#token="],
    ["a token of only whitespace", "#token=%20%20"],
    ["two tokens, which must not be resolved by taking the first", "#token=first&token=second"],
  ])("shows a message rather than a form for %s", async (_, fragment) => {
    const calls = record();

    open(fragment);

    expect(await screen.findByRole("alert")).toBeInTheDocument();
    expect(screen.queryByLabelText("New password")).toBeNull();
    expect(calls).toEqual([]);
  });

  it("reads the token beside other fragment parameters, which it ignores", async () => {
    let body: unknown;

    server.use(
      http.post(SIGN_OUT, () => new HttpResponse(null, { status: 204 })),
      http.post(ACTIVATE, async ({ request }) => {
        body = await request.json();
        return HttpResponse.json({ userIdentityId: "11111111-1111-1111-1111-111111111111" });
      }),
    );

    const { user } = open("#ref=email&token=abc.def");

    await submit(user);

    expect(body).toEqual({ token: "abc.def", newPassword: "a new password" });
  });

  it("sends the token exactly as it was delivered, unaltered", async () => {
    let body: { token?: string } | undefined;

    server.use(
      http.post(SIGN_OUT, () => new HttpResponse(null, { status: 204 })),
      http.post(ACTIVATE, async ({ request }) => {
        body = (await request.json()) as { token?: string };
        return HttpResponse.json({ userIdentityId: "11111111-1111-1111-1111-111111111111" });
      }),
    );

    // The host escapes the token with EscapeDataString, so a "+" arrives as %2B
    // and must not be decoded into a space.
    const { user } = open("#token=aaa.bbb%2Bccc");

    await submit(user);

    expect(body?.token).toBe("aaa.bbb+ccc");
  });

  it("ends any existing session before activating, in that order", async () => {
    const calls = record();
    const { user } = open("#token=abc.def");

    await submit(user);

    expect(calls).toEqual(["sign-out", "activate"]);
  });

  it("does not end the session merely because the link was opened", async () => {
    const calls = record();

    open("#token=abc.def");

    expect(await screen.findByLabelText("New password")).toBeInTheDocument();
    expect(calls).toEqual([]);
  });

  it.each([400, 403, 500])("does not activate when ending the session fails with %i", async (status) => {
    const calls: string[] = [];

    server.use(
      http.post(SIGN_OUT, () => {
        calls.push("sign-out");
        return HttpResponse.json({ error: "Refused." }, { status });
      }),
      http.post(ACTIVATE, () => {
        calls.push("activate");
        return HttpResponse.json({ userIdentityId: "11111111-1111-1111-1111-111111111111" });
      }),
    );

    const { user } = open("#token=abc.def");

    await submit(user);

    expect(await screen.findByRole("alert")).toBeInTheDocument();
    expect(calls).toEqual(["sign-out"]);
  });

  /**
   * F6: the policy belongs to the tenant and can change, so the client never
   * copies it. A password the server would refuse is still sent to the server.
   */
  it("sends a short password rather than judging it, because the policy is the server's", async () => {
    const calls = record(() => HttpResponse.json({ error: "That password is too short." }, { status: 400 }));

    const { user } = open("#token=abc.def");

    await submit(user, "short");

    expect(calls).toEqual(["sign-out", "activate"]);
  });

  it("does not submit when the confirmation does not match", async () => {
    const calls = record();

    const { user } = open("#token=abc.def");

    await user.type(await screen.findByLabelText("New password"), "a new password");
    await user.type(screen.getByLabelText("Confirm new password"), "a different password");
    await user.click(screen.getByRole("button", { name: "Activate account" }));

    expect(await screen.findByRole("alert")).toBeInTheDocument();
    expect(calls).toEqual([]);
  });

  it("shows the host's refusal word for word and keeps the form open, because the token still works", async () => {
    record(() => HttpResponse.json({ error: "That password has been used before." }, { status: 400 }));

    const { user } = open("#token=abc.def");

    await submit(user);

    expect(await screen.findByRole("alert")).toHaveTextContent("That password has been used before.");
    expect(screen.getByLabelText("New password")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Activate account" })).toBeInTheDocument();
  });

  /** RL-17 (behaviour 11): the refusal is the host's, word for word, and the form stays. */
  it("shows the rate-limit refusal word for word and keeps the form open", async () => {
    record(() => HttpResponse.json({ error: "Too many attempts. Try again later." }, { status: 429, headers: { "Retry-After": "900" } }));

    const { user } = open("#token=abc.def");

    await submit(user);

    expect(await screen.findByRole("alert")).toHaveTextContent("Too many attempts. Try again later.");
    expect(screen.getByLabelText("New password")).toBeInTheDocument();
  });

  it("offers a link to sign in on success, and does not navigate on its own", async () => {
    record();

    const { user } = open("#token=abc.def");

    await submit(user);

    expect(await screen.findByRole("link", { name: /sign in/i })).toHaveAttribute("href", "/sign-in");
    expect(screen.queryByRole("heading", { name: "Sign in" })).toBeNull();
  });

  it("echoes nothing the host returned about the account", async () => {
    record();

    const { user } = open("#token=abc.def");

    await submit(user);

    expect(await screen.findByRole("link", { name: /sign in/i })).toBeInTheDocument();
    expect(screen.queryByText(/11111111/)).toBeNull();
  });

  it("has no accessibility violations, with a token and without one", async () => {
    const { container, unmount } = open("#token=abc.def");

    expect(await screen.findByLabelText("New password")).toBeInTheDocument();
    await expectNoAccessibilityViolations(container);

    unmount();

    const without = open("#ref=email");

    expect(await screen.findByRole("alert")).toBeInTheDocument();
    await expectNoAccessibilityViolations(without.container);
  });
});
