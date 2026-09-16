import { screen } from "@testing-library/react";
import { useLocation, type RouteObject } from "react-router";
import { describe, expect, it } from "vitest";
import { renderWithApp } from "@/test/renderWithApp";
import { TestSessionSource } from "@/test/sessions";
import { RequireAuth } from "./RequireAuth";

function SignInProbe() {
  const state = useLocation().state as { returnTo?: unknown } | null;

  return <h1>sign in, return to {String(state?.returnTo)}</h1>;
}

const routes: RouteObject[] = [
  {
    element: <RequireAuth signInPath="/sign-in" />,
    children: [{ path: "/users/new", element: <h1>protected page</h1> }],
  },
  { path: "/sign-in", element: <SignInProbe /> },
];

describe("RequireAuth", () => {
  it("renders nothing while authentication is unknown", async () => {
    renderWithApp(routes, { path: "/users/new", source: new TestSessionSource() });

    await Promise.resolve();

    expect(screen.queryByRole("heading")).toBeNull();
  });

  it("renders the page when authenticated", async () => {
    renderWithApp(routes, {
      path: "/users/new",
      source: new TestSessionSource({ status: "authenticated", principal: null }),
    });

    expect(await screen.findByRole("heading", { name: "protected page" })).toBeInTheDocument();
  });

  it("redirects to sign-in with the path and query as the return path, never the fragment", async () => {
    renderWithApp(routes, {
      path: "/users/new?step=2#secret-fragment",
      source: new TestSessionSource({ status: "unauthenticated" }),
    });

    expect(await screen.findByRole("heading", { name: "sign in, return to /users/new?step=2" })).toBeInTheDocument();
  });

  /**
   * RequireAuth passes the return path on; it does not decide whether it is safe
   * to follow. The sign-in flow (W3) consumes it and must accept only a
   * same-origin, relative path — otherwise it is an open redirect.
   */
  it("passes the return path on unvalidated, leaving its validation to the sign-in flow", async () => {
    renderWithApp(routes, {
      path: "/users/new",
      source: new TestSessionSource({ status: "unauthenticated" }),
    });

    expect(await screen.findByRole("heading", { name: "sign in, return to /users/new" })).toBeInTheDocument();
  });

  /**
   * The fourth state (B6-B, §8). A failed resolution is not a signed-out
   * session: the server did not say there is no caller, it failed to say
   * anything. Sending this person to sign-in would end a session that may well
   * be live, so the boundary says what happened and offers to ask again.
   */
  describe("when authentication could not be resolved", () => {
    it("states that the session could not be checked, and offers to try again", async () => {
      renderWithApp(routes, { path: "/users/new", source: new TestSessionSource({ status: "error" }) });

      expect(await screen.findByRole("alert")).toHaveTextContent("We could not check your session");
      expect(screen.getByRole("button", { name: "Try again" })).toBeInTheDocument();
    });

    it("does not send anyone to sign-in, because nothing established that they are signed out", async () => {
      renderWithApp(routes, { path: "/users/new", source: new TestSessionSource({ status: "error" }) });

      await screen.findByRole("alert");

      expect(screen.queryByRole("heading", { name: /^sign in/ })).toBeNull();
    });

    it("does not render the protected page either, because nothing established a caller", async () => {
      renderWithApp(routes, { path: "/users/new", source: new TestSessionSource({ status: "error" }) });

      await screen.findByRole("alert");

      expect(screen.queryByRole("heading", { name: "protected page" })).toBeNull();
    });

    it("resolves the session again when the retry is used", async () => {
      const source = new TestSessionSource({ status: "error" });
      const { user } = renderWithApp(routes, { path: "/users/new", source });

      await screen.findByRole("alert");

      source.answerNext({ status: "authenticated", principal: null });

      await user.click(screen.getByRole("button", { name: "Try again" }));

      expect(await screen.findByRole("heading", { name: "protected page" })).toBeInTheDocument();
      expect(source.resolveCalls).toBe(2);
    });

    it("stays on the route it was asked to guard rather than navigating away", async () => {
      const { router } = renderWithApp(routes, { path: "/users/new", source: new TestSessionSource({ status: "error" }) });

      await screen.findByRole("alert");

      expect(router.state.location.pathname).toBe("/users/new");
    });
  });
});
