import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import type { RouteObject } from "react-router";
import { renderWithApp } from "@/test/renderWithApp";
import { TestSessionSource } from "@/test/sessions";
import { definePermission } from "./permissions";
import { RequirePermission } from "./RequirePermission";

/**
 * Route authorization (B6-B, §5, §9).
 *
 * The distinction this file exists to keep: <Can> decides whether a navigation
 * entry is SHOWN; this decides whether a page may be REACHED. They are separate
 * mechanisms, and W4 produced concrete evidence that conflating them in a test
 * hides which one broke.
 *
 * A denial renders an EXPLICIT denied state. Never a blank page, and never a
 * redirect that makes the route look as though it does not exist: a hidden
 * route is not an authorization outcome, a stated refusal is.
 */

const CREATE = definePermission("user.create");

const routes: RouteObject[] = [
  {
    path: "/users/new",
    element: (
      <RequirePermission permission={CREATE}>
        <h1>Create user</h1>
      </RequirePermission>
    ),
  },
];

function render(source: TestSessionSource) {
  return renderWithApp(routes, { path: "/users/new", source });
}

const holding = () =>
  new TestSessionSource({ status: "authenticated", principal: { permissions: [{ code: CREATE }] } });

const denied = () => new TestSessionSource({ status: "authenticated", principal: { permissions: [] } });

const unresolved = () => new TestSessionSource({ status: "authenticated", principal: null });

describe("a permission-gated route", () => {
  it("renders the page for a caller who holds the permission", async () => {
    render(holding());

    expect(await screen.findByRole("heading", { level: 1, name: "Create user" })).toBeInTheDocument();
  });

  /**
   * Optimistic visibility while resolution is in flight (§9): the capability is
   * shown, and the server still refuses the operation if it must.
   */
  it("renders the page while the caller's permissions are not yet known", async () => {
    render(unresolved());

    expect(await screen.findByRole("heading", { level: 1, name: "Create user" })).toBeInTheDocument();
  });

  it("refuses a caller whose permission is known to be denied", async () => {
    render(denied());

    expect(await screen.findByRole("heading", { level: 1, name: "Not available" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Create user" })).toBeNull();
  });

  /**
   * The three things a denial must NOT be. Each has been a real product bug
   * somewhere: a blank screen reads as breakage, a 404 lies about the route,
   * and a silent redirect leaves the person wondering what happened.
   */
  it("states the refusal rather than rendering nothing", async () => {
    const { container } = render(denied());

    await screen.findByRole("heading", { level: 1, name: "Not available" });

    expect(container.textContent.trim()).not.toBe("");
  });

  it("does not pretend the page is missing", async () => {
    render(denied());

    await screen.findByRole("heading", { level: 1, name: "Not available" });

    expect(screen.queryByRole("heading", { name: "Page not found" })).toBeNull();
  });

  it("stays on the route rather than redirecting away from it", async () => {
    const { router } = render(denied());

    await screen.findByRole("heading", { level: 1, name: "Not available" });

    expect(router.state.location.pathname).toBe("/users/new");
  });
});
