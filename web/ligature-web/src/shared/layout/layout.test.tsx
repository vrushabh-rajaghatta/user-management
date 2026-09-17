import type { QueryClient } from "@tanstack/react-query";
import { screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { createQueryClient } from "@/app/queryClient";
import { authKeys } from "@/shared/auth/me";
import { definePermission, type PermissionCode } from "@/shared/auth/permissions";
import { renderWithApp } from "@/test/renderWithApp";
import { TestSessionSource } from "@/test/sessions";
import { setViewportWidth } from "@/test/viewport";
import { AppShell } from "./AppShell";
import type { NavigationArea, NavigationGroup } from "./navigation";
import { NotFound } from "./NotFound";
import { PublicShell } from "./PublicShell";
import { RouteError } from "./RouteError";

function Boom(): never {
  throw new Error("secret detail at Ligature.Host");
}

describe("AppShell", () => {
  it("lets keyboard users skip straight to the main content", async () => {
    const { user } = renderWithApp([
      { element: <AppShell />, children: [{ path: "/", element: <p>main content</p> }] },
    ]);

    await screen.findByText("main content");

    await user.tab();
    const skip = screen.getByRole("link", { name: "Skip to content" });
    expect(document.activeElement).toBe(skip);

    await user.keyboard("{Enter}");
    expect(document.activeElement).toBe(screen.getByRole("main"));
  });
});

/**
 * The shell lists AREAS in labelled groups (docs/frontend-architecture.md §5,
 * "Administration shell"). It never learns what an area MEANS — the label comes
 * from the module that owns the vocabulary — and an area has no permission of
 * its own: it is shown when at least one of its items is.
 */
const READ = definePermission("user.read");
const CREATE = definePermission("user.create");

const AREA: NavigationArea = {
  label: "Area",
  to: "/area",
  title: "Area",
  items: [
    { label: "First", to: "/area/first", permission: READ },
    { label: "Second", to: "/area/second", permission: CREATE },
  ],
};

const NAVIGATION: readonly NavigationGroup[] = [{ label: "Group", areas: [AREA] }];

const unknown = () => new TestSessionSource({ status: "authenticated", principal: null });
const holding = (...codes: PermissionCode[]) =>
  new TestSessionSource({ status: "authenticated", principal: { permissions: codes.map((code) => ({ code })) } });

function shell(options: { source?: TestSessionSource; path?: string; queryClient?: QueryClient } = {}) {
  return renderWithApp(
    [
      {
        element: <AppShell navigation={NAVIGATION} actions={<button type="button">Sign out</button>} />,
        children: [{ path: "*", element: <p>main content</p> }],
      },
    ],
    { source: options.source ?? unknown(), path: options.path ?? "/", queryClient: options.queryClient },
  );
}

const mainNavigation = () => screen.findByRole("navigation", { name: "Main" });

describe("the application navigation", () => {
  it("is a navigation landmark named Main", async () => {
    shell();

    expect(await mainNavigation()).toBeInTheDocument();
  });

  it("shows the group it was given, and links the area to its root", async () => {
    shell();

    const nav = await mainNavigation();

    expect(within(nav).getByText("Group")).toBeInTheDocument();
    expect(within(nav).getByRole("link", { name: "Area" })).toHaveAttribute("href", "/area");
  });

  it("links the brand to the home page", async () => {
    shell();

    expect(await screen.findByRole("link", { name: "Ligature" })).toHaveAttribute("href", "/");
  });

  /**
   * Optimistic visibility (§9): until a source supplies effective permissions,
   * every permission is unknown and the capability is shown.
   */
  it("shows an area whose items' permissions are not yet known", async () => {
    shell({ source: unknown() });

    expect(within(await mainNavigation()).getByRole("link", { name: "Area" })).toBeInTheDocument();
  });

  it("shows an area when only one of its items is visible", async () => {
    shell({ source: holding(CREATE) });

    expect(within(await mainNavigation()).getByRole("link", { name: "Area" })).toBeInTheDocument();
  });

  /** Presentation, not access control: the routes behind it guard themselves. */
  it("hides an area none of whose items is visible, and the landmark with it", async () => {
    shell({ source: holding() });

    await screen.findByText("main content");

    expect(screen.queryByRole("link", { name: "Area" })).toBeNull();
    expect(screen.queryByRole("navigation", { name: "Main" })).toBeNull();
  });

  it("renders no navigation landmark when it is given none", async () => {
    renderWithApp([{ element: <AppShell />, children: [{ path: "/", element: <p>main content</p> }] }], {
      source: unknown(),
    });

    await screen.findByText("main content");

    expect(screen.queryByRole("navigation")).toBeNull();
  });

  /** The URL is the state: an area is current anywhere beneath its root. */
  it.each(["/area", "/area/first", "/area/first/deeper"])("marks the area current at %s", async (path) => {
    shell({ path });

    expect(within(await mainNavigation()).getByRole("link", { name: "Area" })).toHaveAttribute("aria-current", "page");
  });

  it("does not mark the area current outside it", async () => {
    shell({ path: "/elsewhere" });

    expect(within(await mainNavigation()).getByRole("link", { name: "Area" })).not.toHaveAttribute("aria-current");
  });

  it("follows the area's link to its root", async () => {
    const { router, user } = shell();

    await user.click(within(await mainNavigation()).getByRole("link", { name: "Area" }));

    expect(router.state.location.pathname).toBe("/area");
  });
});

describe("the shell's header and footer", () => {
  it("offers a sidebar toggle at desktop width", async () => {
    shell();

    expect(await screen.findByRole("button", { name: "Toggle Sidebar" })).toBeInTheDocument();
  });

  it("puts the actions it is given in the footer", async () => {
    shell();

    expect(await screen.findByRole("button", { name: "Sign out" })).toBeInTheDocument();
  });

  /**
   * The name comes from the /me answer caller resolution already holds. The
   * shell reads it; it never asks the server again, because /me is not a
   * heartbeat (§8).
   */
  it("shows the caller's display name from the resolved /me answer", async () => {
    const queryClient = createQueryClient();

    queryClient.setQueryData(authKeys.me(), {
      identity: { userIdentityId: "i-1", username: "ada", displayName: "Ada Lovelace" },
      permissions: [],
      session: { expiresAt: "2026-09-17T20:00:00Z", idleExpiresAt: "2026-09-17T12:15:00Z" },
    });

    shell({ queryClient });

    expect(await screen.findByText("Ada Lovelace")).toBeInTheDocument();
  });

  it("shows no name when no /me answer is held", async () => {
    shell();

    await screen.findByText("main content");

    expect(screen.queryByText("Ada Lovelace")).toBeNull();
  });
});

/** Below md the primary sidebar is a sheet opened from the header (§14). */
describe("the application navigation on a phone", () => {
  it("is not shown until the sheet is opened", async () => {
    setViewportWidth(375);

    shell();

    await screen.findByText("main content");

    expect(screen.queryByRole("navigation", { name: "Main" })).toBeNull();
  });

  it("opens from the header, and closes when an entry is followed", async () => {
    setViewportWidth(375);

    const { router, user } = shell();

    await user.click(await screen.findByRole("button", { name: "Toggle Sidebar" }));

    const dialog = await screen.findByRole("dialog");

    await user.click(within(within(dialog).getByRole("navigation", { name: "Main" })).getByRole("link", { name: "Area" }));

    expect(router.state.location.pathname).toBe("/area");
    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });
  });

  it("closes with Escape", async () => {
    setViewportWidth(375);

    const { user } = shell();

    await user.click(await screen.findByRole("button", { name: "Toggle Sidebar" }));
    await screen.findByRole("dialog");

    await user.keyboard("{Escape}");

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });
  });
});

describe("PublicShell", () => {
  it("renders its route inside the main landmark", async () => {
    renderWithApp([{ element: <PublicShell />, children: [{ path: "/", element: <p>public content</p> }] }]);

    expect(await screen.findByRole("main")).toHaveTextContent("public content");
  });
});

describe("RouteError", () => {
  it("shows fixed text and nothing of the error that was thrown", async () => {
    vi.spyOn(console, "error").mockImplementation(() => undefined);

    renderWithApp([
      {
        element: <PublicShell />,
        children: [{ errorElement: <RouteError />, children: [{ path: "/", element: <Boom /> }] }],
      },
    ]);

    expect(await screen.findByRole("heading", { level: 1, name: "Something went wrong" })).toBeInTheDocument();
    expect(screen.queryByText(/secret detail/)).toBeNull();
    expect(screen.getByRole("link", { name: "Go to the home page" })).toHaveAttribute("href", "/");
  });
});

describe("NotFound", () => {
  it("says the page does not exist and sets the title", async () => {
    renderWithApp([{ path: "*", element: <NotFound /> }], { path: "/nowhere" });

    expect(await screen.findByRole("heading", { level: 1, name: "Page not found" })).toBeInTheDocument();
    expect(document.title).toBe("Page not found · Ligature");
  });
});
