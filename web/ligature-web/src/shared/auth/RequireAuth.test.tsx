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
});
