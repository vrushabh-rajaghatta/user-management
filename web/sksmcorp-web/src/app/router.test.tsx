import { screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { definePermission, type PermissionCode } from "@/shared/auth/permissions";
import { renderWithApp } from "@/test/renderWithApp";
import { server } from "@/test/msw/server";
import { TestSessionSource } from "@/test/sessions";
import { appRoutes, composeRoutes } from "./router";

function Boom(): never {
  throw new Error("secret detail at SKSMCorp.Host");
}

const signedIn = () => new TestSessionSource({ status: "authenticated", principal: null });

/** Authenticated, with effective permissions that do NOT include user.create. */
const denied = () => new TestSessionSource({ status: "authenticated", principal: { permissions: [] } });

const CREATE = definePermission("user.create");
const READ = definePermission("user.read");

/** Authenticated, holding user.create. */
const holding = () =>
  new TestSessionSource({ status: "authenticated", principal: { permissions: [{ code: CREATE }] } });

/** Authenticated, holding exactly the permissions named. */
const holdingOnly = (...codes: PermissionCode[]) =>
  new TestSessionSource({ status: "authenticated", principal: { permissions: codes.map((code) => ({ code })) } });

/** The Users page reads the list; these tests are not about its contents. */
const listUsersHandler = () =>
  http.get(new URL("/api/users", window.location.origin).href, () =>
    HttpResponse.json({ users: [], page: 1, pageSize: 25, hasMore: false }),
  );

beforeEach(() => {
  server.use(listUsersHandler());
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


  // ------------------------------------------------------------ Administration

  it("redirect /admin to its first page, /admin/users", async () => {
    const { router } = renderWithApp(appRoutes, { path: "/admin", source: signedIn() });

    expect(await screen.findByRole("heading", { level: 1, name: "Users" })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe("/admin/users");
  });

  it("load the Users page for a visitor who holds user.read", async () => {
    renderWithApp(appRoutes, { path: "/admin/users", source: holdingOnly(READ) });

    expect(await screen.findByRole("heading", { level: 1, name: "Users" })).toBeInTheDocument();
  });

  it("refuse the Users page to a visitor without user.read, even one who holds user.create", async () => {
    renderWithApp(appRoutes, { path: "/admin/users", source: holdingOnly(CREATE) });

    expect(await screen.findByRole("heading", { level: 1, name: "Not available" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Users" })).toBeNull();
  });

  it("send a signed-out visitor from an Administration page to sign-in", async () => {
    renderWithApp(appRoutes, { path: "/admin/users" });

    expect(await screen.findByRole("heading", { level: 1, name: "Sign in" })).toBeInTheDocument();
  });

  it("load the create user page at /admin/users/new for a signed-in visitor whose permissions are not yet known", async () => {
    renderWithApp(appRoutes, { path: "/admin/users/new", source: signedIn() });

    expect(await screen.findByRole("heading", { level: 1, name: "Create user" })).toBeInTheDocument();
  });

  it("load the create user page for a visitor who holds the permission", async () => {
    renderWithApp(appRoutes, { path: "/admin/users/new", source: holding() });

    expect(await screen.findByRole("heading", { level: 1, name: "Create user" })).toBeInTheDocument();
  });

  it("send a signed-out visitor from the create user page to sign-in", async () => {
    renderWithApp(appRoutes, { path: "/admin/users/new" });

    expect(await screen.findByRole("heading", { level: 1, name: "Sign in" })).toBeInTheDocument();
  });

  /** Moved, not aliased: nothing linked to the old path, and no redirect is kept. */
  it("no longer serve the create user page at /users/new", async () => {
    renderWithApp(appRoutes, { path: "/users/new", source: signedIn() });

    expect(await screen.findByRole("heading", { level: 1, name: "Page not found" })).toBeInTheDocument();
  });

  /**
   * The B6 amendment to §5, stated as a test: the route guards itself, and a
   * denial renders an EXPLICIT denied state rather than a blank page or a 404
   * that lies about the route. The server still authorises POST /api/users.
   */
  it("refuse the create user page to a visitor whose permission is denied", async () => {
    renderWithApp(appRoutes, { path: "/admin/users/new", source: denied() });

    expect(await screen.findByRole("heading", { level: 1, name: "Not available" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Create user" })).toBeNull();
  });

  it("offer Administration in a Platform group of the main navigation", async () => {
    renderWithApp(appRoutes, { path: "/", source: signedIn() });

    const nav = await screen.findByRole("navigation", { name: "Main" });

    expect(within(nav).getByText("Platform")).toBeInTheDocument();
    expect(within(nav).getByRole("link", { name: "Administration" })).toHaveAttribute("href", "/admin");
  });

  it("show both navigation landmarks on an Administration page, with Users current in each sense", async () => {
    renderWithApp(appRoutes, { path: "/admin/users/new", source: signedIn() });

    const main = await screen.findByRole("navigation", { name: "Main" });
    const administration = screen.getByRole("navigation", { name: "Administration" });

    expect(within(main).getByRole("link", { name: "Administration" })).toHaveAttribute("aria-current", "page");
    expect(within(administration).getByRole("link", { name: "Users" })).toHaveAttribute("href", "/admin/users");
    expect(within(administration).getByRole("link", { name: "Users" })).toHaveAttribute("aria-current", "page");
  });

  /** Only real destinations: no read capability exists for either yet. */
  it("list no Audit trail or Notifications entry", async () => {
    renderWithApp(appRoutes, { path: "/admin/users", source: signedIn() });

    await screen.findByRole("navigation", { name: "Administration" });

    expect(screen.queryByRole("link", { name: "Audit trail" })).toBeNull();
    expect(screen.queryByRole("link", { name: "Notifications" })).toBeNull();
  });

  it.each(["/admin/audit-trail", "/admin/notifications"])("serve no page at %s", async (path) => {
    renderWithApp(appRoutes, { path, source: signedIn() });

    expect(await screen.findByRole("heading", { level: 1, name: "Page not found" })).toBeInTheDocument();
  });

  /**
   * Deliberately SEPARATE from the route-guard tests above. Removing the
   * navigation filter must fail this one and leave those passing, and the
   * reverse (W4).
   */
  it("hide Administration from a visitor who holds user.create but not user.read", async () => {
    renderWithApp(appRoutes, { path: "/", source: holdingOnly(CREATE) });

    expect(await screen.findByRole("heading", { level: 1, name: "Home" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Administration" })).toBeNull();
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
