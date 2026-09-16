import { screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { renderWithApp } from "@/test/renderWithApp";
import { definePermission } from "@/shared/auth/permissions";
import { TestSessionSource } from "@/test/sessions";
import { AppShell } from "./AppShell";
import type { NavigationSection } from "./navigation";
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
 * The shell renders sections and items. It never learns what a section MEANS —
 * "Users" originates in the module that owns that vocabulary, and shared/layout
 * may not import a module (docs/frontend-architecture.md §2, §3).
 */
const CREATE = definePermission("user.create");

const NAVIGATION: readonly NavigationSection[] = [
  { label: "Users", items: [{ label: "Create user", to: "/users/new", permission: CREATE }] },
];

function shell(source?: TestSessionSource) {
  return renderWithApp(
    [
      {
        element: <AppShell navigation={NAVIGATION} />,
        children: [{ path: "/", element: <p>main content</p> }],
      },
    ],
    { source: source ?? new TestSessionSource({ status: "authenticated", principal: null }) },
  );
}

describe("the application navigation", () => {
  it("is a navigation landmark", async () => {
    shell();

    expect(await screen.findByRole("navigation")).toBeInTheDocument();
  });

  it("shows the section it was given", async () => {
    shell();

    expect(await screen.findByText("Users")).toBeInTheDocument();
  });

  it("links to the page the item names", async () => {
    shell();

    expect(await screen.findByRole("link", { name: "Create user" })).toHaveAttribute("href", "/users/new");
  });

  /**
   * Optimistic visibility (§9): until a source supplies effective permissions,
   * every permission is unknown and the capability is shown. A caller without it
   * still gets the server's refusal.
   */
  it("shows an item whose permission is not yet known", async () => {
    shell(new TestSessionSource({ status: "authenticated", principal: null }));

    expect(await screen.findByRole("link", { name: "Create user" })).toBeInTheDocument();
  });

  it("shows an item whose permission is held", async () => {
    shell(new TestSessionSource({ status: "authenticated", principal: { permissions: [{ code: CREATE }] } }));

    expect(await screen.findByRole("link", { name: "Create user" })).toBeInTheDocument();
  });

  /**
   * Hiding navigation a caller cannot use is PRESENTATION. It is not access
   * control, and the route behind it is deliberately not gated (F8).
   */
  it("hides an item whose permission is known to be denied", async () => {
    shell(new TestSessionSource({ status: "authenticated", principal: { permissions: [] } }));

    await screen.findByText("main content");

    expect(screen.queryByRole("link", { name: "Create user" })).toBeNull();
  });

  it("renders no navigation landmark when it is given none", async () => {
    renderWithApp(
      [{ element: <AppShell />, children: [{ path: "/", element: <p>main content</p> }] }],
      { source: new TestSessionSource({ status: "authenticated", principal: null }) },
    );

    await screen.findByText("main content");

    expect(screen.queryByRole("navigation")).toBeNull();
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
