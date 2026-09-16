import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import type { RouteObject } from "react-router";
import { renderWithApp } from "@/test/renderWithApp";
import { TestSessionSource } from "@/test/sessions";
import { server } from "@/test/msw/server";
import { SignOutButton } from "./SignOutButton";

/**
 * Sign-out, which is NOT the end-session-first sequence of §13 even though it
 * calls the same endpoint.
 *
 * Here the visitor asked to leave, so the session ends locally and the page
 * goes to sign-in whether or not the call succeeded
 * (docs/frontend-architecture.md §8). Ending a session first before
 * establishing an identity is the opposite: a failure there stops everything,
 * because a second session must not be established beside a live one.
 */

const at = (path: string) => new URL(path, window.location.origin).href;

const SIGN_OUT = at("/api/auth/sign-out");

const routes: RouteObject[] = [
  { path: "/", element: <SignOutButton /> },
  { path: "/sign-in", element: <h1>Sign in</h1> },
];

function render() {
  const source = new TestSessionSource({ status: "authenticated", principal: null });

  // Returned alongside, because renderWithApp hands back the AuthSessionSource
  // interface and the counters belong to this implementation of it.
  return { ...renderWithApp(routes, { path: "/", source }), source };
}

describe("signing out", () => {
  it("ends the session and goes to sign-in", async () => {
    server.use(http.post(SIGN_OUT, () => new HttpResponse(null, { status: 204 })));

    const { user, source } = render();

    await user.click(await screen.findByRole("button", { name: "Sign out" }));

    expect(await screen.findByRole("heading", { name: "Sign in" })).toBeInTheDocument();
    expect(source.signedOutCalls).toBe(1);
  });

  /**
   * A 401 means the session had already ended. The visitor asked to leave, and
   * they have: there is nothing to report and nowhere else to send them.
   */
  it.each([
    ["the session had already ended", 401],
    ["the host refused", 400],
    ["the host failed", 500],
  ])("still ends the session and goes to sign-in when %s", async (_, status) => {
    server.use(http.post(SIGN_OUT, () => HttpResponse.json({ error: "Refused." }, { status })));

    const { user, source } = render();

    await user.click(await screen.findByRole("button", { name: "Sign out" }));

    expect(await screen.findByRole("heading", { name: "Sign in" })).toBeInTheDocument();
    expect(source.signedOutCalls).toBe(1);
  });

  it("still ends the session and goes to sign-in when the host cannot be reached", async () => {
    server.use(http.post(SIGN_OUT, () => HttpResponse.error()));

    const { user, source } = render();

    await user.click(await screen.findByRole("button", { name: "Sign out" }));

    expect(await screen.findByRole("heading", { name: "Sign in" })).toBeInTheDocument();
    expect(source.signedOutCalls).toBe(1);
  });

  it("empties the query cache, so the next person on this browser sees nothing of the last", async () => {
    server.use(http.post(SIGN_OUT, () => new HttpResponse(null, { status: 204 })));

    const { user, queryClient } = render();

    queryClient.setQueryData(["users"], [{ username: "ada.lovelace" }]);

    await user.click(await screen.findByRole("button", { name: "Sign out" }));

    expect(await screen.findByRole("heading", { name: "Sign in" })).toBeInTheDocument();
    expect(queryClient.getQueryData(["users"])).toBeUndefined();
  });
});
