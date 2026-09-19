import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import type { RouteObject } from "react-router";
import { renderWithApp } from "@/test/renderWithApp";
import { server } from "@/test/msw/server";
import { expectNoAccessibilityViolations } from "@/test/axe";
import { ResetPasswordPage } from "./ResetPasswordPage";

/**
 * Reset password: the same fragment-token rules as activation
 * (docs/frontend-architecture.md §13), ending any session first (F3), and a
 * success message that claims only what CRD-C3 actually does.
 *
 * /reset-password is a BACKEND CONTRACT path: NotificationTemplates builds it
 * for both the self-service reset and the administrator-initiated one.
 */

const at = (path: string) => new URL(path, window.location.origin).href;

const SIGN_OUT = at("/api/auth/sign-out");
const RESET = at("/api/account/reset-password");

/** The locked wording. It says nothing about any other session, deliberately. */
const SUCCESS = "Your password has been changed. Sign in with your new password.";

const routes: RouteObject[] = [
  { path: "/reset-password", element: <ResetPasswordPage /> },
  { path: "/sign-in", element: <h1>Sign in</h1> },
];

function open(fragment: string, search = "") {
  window.history.replaceState(null, "", `/reset-password${search}${fragment}`);

  return renderWithApp(routes, { path: "/reset-password" });
}

function record(reset: () => Response = () => HttpResponse.json({ userIdentityId: "11111111-1111-1111-1111-111111111111" })) {
  const calls: string[] = [];

  server.use(
    http.post(SIGN_OUT, () => {
      calls.push("sign-out");
      return new HttpResponse(null, { status: 204 });
    }),
    http.post(RESET, () => {
      calls.push("reset");
      return reset();
    }),
  );

  return calls;
}

async function submit(user: ReturnType<typeof renderWithApp>["user"], password = "a new password") {
  await user.type(await screen.findByLabelText("New password"), password);
  await user.type(screen.getByLabelText("Confirm new password"), password);
  await user.click(screen.getByRole("button", { name: "Set new password" }));
}

beforeEach(() => {
  window.history.replaceState(null, "", "/");
});

afterEach(() => {
  window.history.replaceState(null, "", "/");
});

describe("the reset password page", () => {
  it("shows the form when the link carries exactly one token", async () => {
    open("#token=abc.def");

    expect(await screen.findByLabelText("New password")).toBeInTheDocument();
  });

  it("removes the token from the address bar on arrival, before anything is submitted", async () => {
    open("#token=abc.def");

    expect(await screen.findByLabelText("New password")).toBeInTheDocument();
    expect(window.location.hash).toBe("");
  });

  it("keeps the path and query while removing the fragment", async () => {
    open("#token=abc.def", "?ref=email");

    expect(await screen.findByLabelText("New password")).toBeInTheDocument();
    expect(window.location.pathname).toBe("/reset-password");
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

  it("sends the token exactly as it was delivered, unaltered", async () => {
    let body: unknown;

    server.use(
      http.post(SIGN_OUT, () => new HttpResponse(null, { status: 204 })),
      http.post(RESET, async ({ request }) => {
        body = await request.json();
        return HttpResponse.json({ userIdentityId: "11111111-1111-1111-1111-111111111111" });
      }),
    );

    const { user } = open("#token=aaa.bbb%2Bccc");

    await submit(user);

    expect(body).toEqual({ token: "aaa.bbb+ccc", newPassword: "a new password" });
  });

  it("ends any existing session before resetting, in that order", async () => {
    const calls = record();
    const { user } = open("#token=abc.def");

    await submit(user);

    expect(calls).toEqual(["sign-out", "reset"]);
  });

  it.each([400, 403, 500])("does not reset when ending the session fails with %i", async (status) => {
    const calls: string[] = [];

    server.use(
      http.post(SIGN_OUT, () => {
        calls.push("sign-out");
        return HttpResponse.json({ error: "Refused." }, { status });
      }),
      http.post(RESET, () => {
        calls.push("reset");
        return HttpResponse.json({ userIdentityId: "11111111-1111-1111-1111-111111111111" });
      }),
    );

    const { user } = open("#token=abc.def");

    await submit(user);

    expect(await screen.findByRole("alert")).toBeInTheDocument();
    expect(calls).toEqual(["sign-out"]);
  });

  /**
   * Ending the session locally is not proof the server revoked anything. If a
   * session is still live, the host refuses with 401 and that answer stands.
   */
  it("shows the host's refusal when a session is still established, and keeps the form open", async () => {
    record(() => HttpResponse.json({ error: "Authentication is required." }, { status: 401 }));

    const { user } = open("#token=abc.def");

    await submit(user);

    expect(await screen.findByRole("alert")).toHaveTextContent("Authentication is required.");
    expect(screen.getByLabelText("New password")).toBeInTheDocument();
  });

  it("sends a short password rather than judging it, because the policy is the server's", async () => {
    const calls = record(() => HttpResponse.json({ error: "That password is too short." }, { status: 400 }));

    const { user } = open("#token=abc.def");

    await submit(user, "short");

    expect(calls).toEqual(["sign-out", "reset"]);
  });

  it("does not submit when the confirmation does not match", async () => {
    const calls = record();

    const { user } = open("#token=abc.def");

    await user.type(await screen.findByLabelText("New password"), "a new password");
    await user.type(screen.getByLabelText("Confirm new password"), "a different password");
    await user.click(screen.getByRole("button", { name: "Set new password" }));

    expect(await screen.findByRole("alert")).toBeInTheDocument();
    expect(calls).toEqual([]);
  });

  it("shows the host's refusal word for word and keeps the form open, because the token still works", async () => {
    record(() => HttpResponse.json({ error: "That password has been used before." }, { status: 400 }));

    const { user } = open("#token=abc.def");

    await submit(user);

    expect(await screen.findByRole("alert")).toHaveTextContent("That password has been used before.");
    expect(screen.getByLabelText("New password")).toBeInTheDocument();
  });

  /** RL-17 (behaviour 11): the refusal is the host's, word for word, and the form stays. */
  it("shows the rate-limit refusal word for word and keeps the form open", async () => {
    record(() => HttpResponse.json({ error: "Too many attempts. Try again later." }, { status: 429, headers: { "Retry-After": "900" } }));

    const { user } = open("#token=abc.def");

    await submit(user);

    expect(await screen.findByRole("alert")).toHaveTextContent("Too many attempts. Try again later.");
    expect(screen.getByLabelText("New password")).toBeInTheDocument();
  });

  it("confirms success in exactly the words the contract allows", async () => {
    record();

    const { user } = open("#token=abc.def");

    await submit(user);

    expect(await screen.findByText(SUCCESS)).toBeInTheDocument();
  });

  /**
   * CRD-C3 does NOT revoke existing sessions, unlike CRD-C4. Claiming otherwise
   * would tell someone resetting a compromised account that they had shut the
   * attacker out, which is not what happened. The improvement is parked as a
   * backend change-control item; until then the page must not imply it.
   */
  it("claims nothing about other sessions, which a reset does not end", async () => {
    record();

    const { user } = open("#token=abc.def");

    await submit(user);

    expect(await screen.findByText(SUCCESS)).toBeInTheDocument();
    expect(screen.queryByText(/other session|everywhere|all devices|signed out of|logged out of/i)).toBeNull();
  });

  it("offers a link to sign in on success, and does not navigate on its own", async () => {
    record();

    const { user } = open("#token=abc.def");

    await submit(user);

    expect(await screen.findByRole("link", { name: /sign in/i })).toHaveAttribute("href", "/sign-in");
    expect(screen.queryByRole("heading", { name: "Sign in" })).toBeNull();
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
