import { screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { definePermission } from "@/shared/auth/permissions";
import { renderWithApp } from "@/test/renderWithApp";
import { TestSessionSource } from "@/test/sessions";
import { appRoutes, composeRoutes } from "./router";

function Boom(): never {
  throw new Error("secret detail at Ligature.Host");
}

const signedIn = () => new TestSessionSource({ status: "authenticated", principal: null });

/** Authenticated, with effective permissions that do NOT include user.create. */
const denied = () => new TestSessionSource({ status: "authenticated", principal: { permissions: [] } });

/** Authenticated, holding user.create. */
const holding = () =>
  new TestSessionSource({
    status: "authenticated",
    principal: { permissions: [{ code: definePermission("user.create") }] },
  });

describe("the application routes", () => {
  it("load the placeholder home page lazily at /, for a signed-in visitor", async () => {
    renderWithApp(appRoutes, { path: "/", source: signedIn() });

    expect(await screen.findByRole("heading", { level: 1, name: "Home" })).toBeInTheDocument();
    expect(screen.getByRole("main")).toHaveTextContent("Nothing can be done here yet.");
  });

  /**
   * RequireAuth is mounted from W3 onwards, now that a sign-in route exists for
   * a redirect to land on (docs/frontend-architecture.md §5, §8).
   */
  it("send a signed-out visitor from a protected page to sign-in", async () => {
    renderWithApp(appRoutes, { path: "/" });

    expect(await screen.findByRole("heading", { level: 1, name: "Sign in" })).toBeInTheDocument();
  });

  /**
   * Named headings, not merely "a heading": the not-found page has one too, so a
   * weaker assertion would pass for a route that does not exist at all.
   */
  it.each([
    ["/sign-in", "Sign in"],
    ["/forgot-password", "Forgot your password?"],
    ["/activate", "Activate your account"],
    ["/reset-password", "Choose a new password"],
  ])("serve %s without a session, because none of them can require one", async (path, heading) => {
    renderWithApp(appRoutes, { path });

    expect(await screen.findByRole("heading", { level: 1, name: heading })).toBeInTheDocument();
    expect(screen.getByRole("main")).toBeInTheDocument();
  });

  it("show the not-found page for an unknown path without asking anyone to sign in", async () => {
    renderWithApp(appRoutes, { path: "/no/such/page" });

    expect(await screen.findByRole("heading", { level: 1, name: "Page not found" })).toBeInTheDocument();
    expect(screen.getByRole("main")).toBeInTheDocument();
  });

  it("offer a way to sign out of a signed-in page", async () => {
    renderWithApp(appRoutes, { path: "/", source: signedIn() });

    expect(await screen.findByRole("heading", { level: 1, name: "Home" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Sign out" })).toBeInTheDocument();
  });


  it("load the create user page for a signed-in visitor whose permissions are not yet known", async () => {
    renderWithApp(appRoutes, { path: "/users/new", source: signedIn() });

    expect(await screen.findByRole("heading", { level: 1, name: "Create user" })).toBeInTheDocument();
  });

  it("load the create user page for a visitor who holds the permission", async () => {
    renderWithApp(appRoutes, { path: "/users/new", source: holding() });

    expect(await screen.findByRole("heading", { level: 1, name: "Create user" })).toBeInTheDocument();
  });

  it("send a signed-out visitor from the create user page to sign-in", async () => {
    renderWithApp(appRoutes, { path: "/users/new" });

    expect(await screen.findByRole("heading", { level: 1, name: "Sign in" })).toBeInTheDocument();
  });

  it("offer the Users area in the navigation of a signed-in page", async () => {
    renderWithApp(appRoutes, { path: "/", source: signedIn() });

    expect(await screen.findByRole("heading", { level: 1, name: "Home" })).toBeInTheDocument();
    expect(screen.getByRole("navigation")).toBeInTheDocument();
    expect(screen.getByText("Users")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Create user" })).toHaveAttribute("href", "/users/new");
  });

  /**
   * The B6 amendment to §5, stated as a test. W4 left this route ungated for one
   * reason — without a server-backed source every permission was permanently
   * unknown, so a gate would have been unreachable and untestable. /me removes
   * that reason, so the route guards itself, and a denial renders an EXPLICIT
   * denied state rather than a blank page or a 404 that lies about the route.
   *
   * The server still authorises POST /api/users either way. This decides what a
   * person is shown, not what they are permitted to do.
   */
  it("refuse the create user page to a visitor whose permission is denied", async () => {
    renderWithApp(appRoutes, { path: "/users/new", source: denied() });

    expect(await screen.findByRole("heading", { level: 1, name: "Not available" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Create user" })).toBeNull();
  });

  /**
   * Deliberately a SEPARATE test from the one above, and the separation is the
   * point. Removing the NAVIGATION gate must fail this one and leave that one
   * passing; removing the ROUTE guard must fail that one and leave this one
   * passing. W4 produced concrete evidence that a single test covering both
   * reports the wrong mechanism as broken.
   */
  it("hide the create user navigation from a visitor whose permission is denied", async () => {
    renderWithApp(appRoutes, { path: "/", source: denied() });

    expect(await screen.findByRole("heading", { level: 1, name: "Home" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Create user" })).toBeNull();
  });

  it("show the route error inside the shell when a public page throws, without its detail", async () => {
    vi.spyOn(console, "error").mockImplementation(() => undefined);

    renderWithApp(composeRoutes([{ path: "/", element: <Boom /> }], []), { path: "/" });

    expect(await screen.findByRole("heading", { level: 1, name: "Something went wrong" })).toBeInTheDocument();
    expect(screen.getByRole("main")).toBeInTheDocument();
    expect(screen.queryByText(/secret detail/)).toBeNull();
  });

  it("show the route error inside the app shell when a signed-in page throws", async () => {
    vi.spyOn(console, "error").mockImplementation(() => undefined);

    renderWithApp(composeRoutes([], [{ path: "/", element: <Boom /> }]), { path: "/", source: signedIn() });

    expect(await screen.findByRole("heading", { level: 1, name: "Something went wrong" })).toBeInTheDocument();
    expect(screen.getByRole("main")).toBeInTheDocument();
    expect(screen.queryByText(/secret detail/)).toBeNull();
  });
});
