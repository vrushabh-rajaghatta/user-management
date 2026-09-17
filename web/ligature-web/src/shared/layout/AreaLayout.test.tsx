import { screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import type { RouteObject } from "react-router";
import { definePermission, type PermissionCode } from "@/shared/auth/permissions";
import { expectNoAccessibilityViolations } from "@/test/axe";
import { renderWithApp } from "@/test/renderWithApp";
import { TestSessionSource } from "@/test/sessions";
import { AppShell } from "./AppShell";
import { AreaLayout } from "./AreaLayout";
import type { NavigationArea } from "./navigation";

/**
 * An area's secondary navigation (docs/frontend-architecture.md §5,
 * "Administration shell"). AreaLayout is generic: it renders the area it is
 * given beside its page, and knows no area's name, pages or permissions.
 */

const READ = definePermission("area.read");
const WRITE = definePermission("area.write");
const OTHER = definePermission("area.other");

const AREA: NavigationArea = {
  label: "Area",
  to: "/area",
  title: "Area title",
  description: "What this area is for.",
  items: [
    { label: "Reading", to: "/area/reading", permission: READ },
    { label: "Writing", to: "/area/writing", permission: WRITE },
    { label: "Other", to: "/area/other", permission: OTHER },
  ],
};

const holding = (...codes: PermissionCode[]) =>
  new TestSessionSource({ status: "authenticated", principal: { permissions: codes.map((code) => ({ code })) } });

const unknown = () => new TestSessionSource({ status: "authenticated", principal: null });

function areaRoutes(): RouteObject[] {
  return [
    {
      path: "/area",
      element: <AreaLayout area={AREA} />,
      children: [{ path: "*", element: <p>page content</p> }],
    },
  ];
}

function renderArea(path: string, source: TestSessionSource = unknown()) {
  return renderWithApp(areaRoutes(), { path, source });
}

const areaNavigation = () => screen.findByRole("navigation", { name: "Area title" });

describe("an area's navigation", () => {
  it("is a navigation landmark named by the area's title, beside the page", async () => {
    renderArea("/area/reading");

    expect(await areaNavigation()).toBeInTheDocument();
    expect(screen.getByText("page content")).toBeInTheDocument();
  });

  it("shows the area's title and description", async () => {
    renderArea("/area/reading");

    await areaNavigation();

    expect(screen.getByText("Area title")).toBeInTheDocument();
    expect(screen.getByText("What this area is for.")).toBeInTheDocument();
  });

  it("links each visible item to its page", async () => {
    renderArea("/area/reading");

    const nav = await areaNavigation();

    expect(within(nav).getByRole("link", { name: "Reading" })).toHaveAttribute("href", "/area/reading");
    expect(within(nav).getByRole("link", { name: "Writing" })).toHaveAttribute("href", "/area/writing");
  });

  it("hides an item whose permission is known to be denied", async () => {
    renderArea("/area/reading", holding(READ));

    const nav = await areaNavigation();

    expect(within(nav).getByRole("link", { name: "Reading" })).toBeInTheDocument();
    expect(within(nav).queryByRole("link", { name: "Writing" })).toBeNull();
  });

  it("renders no navigation landmark when no item is visible, and still renders the page", async () => {
    renderArea("/area/reading", holding());

    await screen.findByText("page content");

    expect(screen.queryByRole("navigation")).toBeNull();
  });

  /** The URL is the state: an item is current on its page and beneath it. */
  it.each(["/area/reading", "/area/reading/new"])("marks Reading current at %s", async (path) => {
    renderArea(path);

    const nav = await areaNavigation();

    expect(within(nav).getByRole("link", { name: "Reading" })).toHaveAttribute("aria-current", "page");
    expect(within(nav).getByRole("link", { name: "Writing" })).not.toHaveAttribute("aria-current");
  });

  it("navigates by URL when an item is followed", async () => {
    const { router, user } = renderArea("/area/reading");

    await user.click(within(await areaNavigation()).getByRole("link", { name: "Writing" }));

    expect(router.state.location.pathname).toBe("/area/writing");
  });

  it("has no accessibility violations", async () => {
    const { container } = renderArea("/area/reading");

    await areaNavigation();
    await expectNoAccessibilityViolations(container);
  });
});

/**
 * THE INVARIANT the two-level model introduces: the primary navigation and the
 * area's own navigation are read from the same items with the same can(), so
 * they cannot disagree. The shell offers the area exactly when the area has
 * something to show, and what the area shows is exactly the visible items.
 *
 * Asserted over several permission states against the rendered output of BOTH
 * levels at once, rather than by naming the area twice.
 */
describe("the two navigation levels", () => {
  const states: [name: string, source: () => TestSessionSource, visible: string[]][] = [
    ["permissions not yet known", unknown, ["Reading", "Writing", "Other"]],
    ["every item's permission held", () => holding(READ, WRITE, OTHER), ["Reading", "Writing", "Other"]],
    ["one item's permission held", () => holding(WRITE), ["Writing"]],
    ["two items' permissions held", () => holding(READ, OTHER), ["Reading", "Other"]],
    ["no item's permission held", () => holding(), []],
  ];

  it.each(states)("agree when %s", async (_name, source, visible) => {
    renderWithApp(
      [
        {
          element: <AppShell navigation={[{ label: "Group", areas: [AREA] }]} />,
          children: areaRoutes(),
        },
      ],
      { path: "/area/reading", source: source() },
    );

    await screen.findByText("page content");

    const primary = screen.queryByRole("navigation", { name: "Main" });
    const secondary = screen.queryByRole("navigation", { name: "Area title" });

    const offered = primary !== null && within(primary).queryByRole("link", { name: "Area" }) !== null;
    const shown = secondary === null ? [] : within(secondary).getAllByRole("link").map((link) => link.textContent);

    expect(shown).toEqual(visible);
    expect(offered).toBe(visible.length > 0);
  });
});
