import { screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { renderWithApp } from "@/test/renderWithApp";
import { TestSessionSource } from "@/test/sessions";
import { appRoutes, composeRoutes } from "./router";

function Boom(): never {
  throw new Error("secret detail at Ligature.Host");
}

const signedIn = () => new TestSessionSource({ status: "authenticated", principal: null });

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
