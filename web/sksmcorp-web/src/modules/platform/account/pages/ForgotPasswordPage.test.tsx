import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import type { RouteObject } from "react-router";
import { renderWithApp } from "@/test/renderWithApp";
import { server } from "@/test/msw/server";
import { expectNoAccessibilityViolations } from "@/test/axe";
import { ForgotPasswordPage } from "./ForgotPasswordPage";

/**
 * Forgot password (CRD-C2), whose whole design is that it reveals nothing.
 *
 * The host answers 200 with one fixed sentence whatever was typed — there is no
 * 400 and no 404 — so that the response cannot be used to discover which
 * addresses or usernames are registered. The page keeps that property: it shows
 * the host's sentence and nothing derived from the input.
 *
 * It establishes no identity, so unlike sign-in, activation and reset it does
 * NOT end an existing session first.
 */

const at = (path: string) => new URL(path, window.location.origin).href;

const SIGN_OUT = at("/api/auth/sign-out");
const REQUEST = at("/api/account/password-reset-request");

/** The host's fixed literal, which nothing may vary. */
const ANSWER = "If the account exists, a reset link has been sent.";

const routes: RouteObject[] = [
  { path: "/forgot-password", element: <ForgotPasswordPage /> },
  { path: "/sign-in", element: <h1>Sign in</h1> },
];

function record() {
  const calls: string[] = [];

  server.use(
    http.post(SIGN_OUT, () => {
      calls.push("sign-out");
      return new HttpResponse(null, { status: 204 });
    }),
    http.post(REQUEST, () => {
      calls.push("password-reset-request");
      return HttpResponse.json({ message: ANSWER });
    }),
  );

  return calls;
}

async function ask(user: ReturnType<typeof renderWithApp>["user"], value: string) {
  await user.type(await screen.findByLabelText("Email address or username"), value);
  await user.click(screen.getByRole("button", { name: "Send reset link" }));
}

describe("the forgot password page", () => {
  it("asks the host for a reset link", async () => {
    let body: unknown;

    server.use(
      http.post(REQUEST, async ({ request }) => {
        body = await request.json();
        return HttpResponse.json({ message: ANSWER });
      }),
    );

    const { user } = renderWithApp(routes, { path: "/forgot-password" });

    await ask(user, "ada@example.test");

    expect(body).toEqual({ emailOrUsername: "ada@example.test" });
  });

  /**
   * It establishes no identity and is refused by nothing, so there is no
   * session to end. Signing out here would end the session of someone who only
   * mistyped their own address.
   */
  it("does not end the current session, because it establishes no identity", async () => {
    const calls = record();
    const { user } = renderWithApp(routes, { path: "/forgot-password" });

    await ask(user, "ada@example.test");

    expect(await screen.findByText(ANSWER)).toBeInTheDocument();
    expect(calls).toEqual(["password-reset-request"]);
  });

  it("shows the host's sentence word for word", async () => {
    record();

    const { user } = renderWithApp(routes, { path: "/forgot-password" });

    await ask(user, "ada@example.test");

    expect(await screen.findByText(ANSWER)).toBeInTheDocument();
  });

  it("stays on the page rather than navigating, because there is nothing to move on to", async () => {
    record();

    const { user, router } = renderWithApp(routes, { path: "/forgot-password" });

    await ask(user, "ada@example.test");

    expect(await screen.findByText(ANSWER)).toBeInTheDocument();
    expect(router.state.location.pathname).toBe("/forgot-password");
  });

  /** RL-17 (behaviour 11): the only refusal this page can meet, shown as the host words it. */
  it("shows the rate-limit refusal word for word and stays on the form", async () => {
    server.use(
      http.post(REQUEST, () =>
        HttpResponse.json({ error: "Too many attempts. Try again later." }, { status: 429, headers: { "Retry-After": "3600" } }),
      ),
    );

    const { user } = renderWithApp(routes, { path: "/forgot-password" });

    await ask(user, "ada@example.test");

    expect(await screen.findByRole("alert")).toHaveTextContent("Too many attempts. Try again later.");
    expect(screen.queryByText(ANSWER)).toBeNull();
    expect(screen.getByRole("button", { name: "Send reset link" })).toBeInTheDocument();
  });

  it("never repeats what was typed, which would confirm it to whoever typed it", async () => {
    record();

    const { user } = renderWithApp(routes, { path: "/forgot-password" });

    await ask(user, "someone.else@example.test");

    expect(await screen.findByText(ANSWER)).toBeInTheDocument();
    expect(screen.queryByText(/someone\.else@example\.test/)).toBeNull();
  });

  it("answers an unknown account exactly as it answers a known one", async () => {
    record();

    const first = renderWithApp(routes, { path: "/forgot-password" });

    await ask(first.user, "ada@example.test");

    const known = (await screen.findByText(ANSWER)).textContent;

    first.unmount();

    const second = renderWithApp(routes, { path: "/forgot-password" });

    await ask(second.user, "nobody@example.test");

    expect((await screen.findByText(ANSWER)).textContent).toBe(known);
  });

  it("offers a way back to sign in", async () => {
    renderWithApp(routes, { path: "/forgot-password" });

    expect(await screen.findByRole("link", { name: /sign in/i })).toHaveAttribute("href", "/sign-in");
  });

  it("has no accessibility violations, before and after asking", async () => {
    record();

    const { container, user } = renderWithApp(routes, { path: "/forgot-password" });

    expect(await screen.findByLabelText("Email address or username")).toBeInTheDocument();
    await expectNoAccessibilityViolations(container);

    await ask(user, "ada@example.test");

    expect(await screen.findByText(ANSWER)).toBeInTheDocument();
    await expectNoAccessibilityViolations(container);
  });
});
