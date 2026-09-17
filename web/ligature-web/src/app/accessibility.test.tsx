import { render, screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, it, vi } from "vitest";
import { ErrorState } from "@/shared/components/ErrorState";
import { PageHeader } from "@/shared/components/PageHeader";
import { AppShell } from "@/shared/layout/AppShell";
import { expectNoAccessibilityViolations } from "@/test/axe";
import { renderWithApp } from "@/test/renderWithApp";
import { server } from "@/test/msw/server";
import { TestSessionSource } from "@/test/sessions";
import { appRoutes, composeRoutes } from "./router";

/**
 * Rendered accessibility semantics for everything the foundation itself renders
 * (docs/frontend-architecture.md §15). Colour contrast is proved by the token
 * contrast test instead, because jsdom cannot compute it.
 */
/** The Users page reads the list; these tests are not about its contents. */
const listUsersHandler = () =>
  http.get(new URL("/api/users", window.location.origin).href, () =>
    HttpResponse.json({ users: [], page: 1, pageSize: 25, hasMore: false }),
  );

beforeEach(() => {
  server.use(listUsersHandler());
});

describe("the foundation's rendered accessibility", () => {
  it("has no violations on the home page", async () => {
    const { container } = renderWithApp(appRoutes, {
      path: "/",
      source: new TestSessionSource({ status: "authenticated", principal: null }),
    });

    await screen.findByRole("heading", { level: 1, name: "Home" });
    await expectNoAccessibilityViolations(container);
  });

  it("has no violations on an Administration page, with both navigation levels", async () => {
    const { container } = renderWithApp(appRoutes, {
      path: "/admin/users",
      source: new TestSessionSource({ status: "authenticated", principal: null }),
    });

    await screen.findByRole("navigation", { name: "Administration" });
    await expectNoAccessibilityViolations(container);
  });

  it("has no violations on the sign-in page", async () => {
    const { container } = renderWithApp(appRoutes, { path: "/sign-in" });

    await screen.findByRole("heading", { level: 1, name: "Sign in" });
    await expectNoAccessibilityViolations(container);
  });

  it("has no violations on the not-found page", async () => {
    const { container } = renderWithApp(appRoutes, { path: "/missing" });

    await screen.findByRole("heading", { level: 1, name: "Page not found" });
    await expectNoAccessibilityViolations(container);
  });

  it("has no violations on the route error page", async () => {
    vi.spyOn(console, "error").mockImplementation(() => undefined);

    function Boom(): never {
      throw new Error("failure");
    }

    const { container } = renderWithApp(composeRoutes([{ path: "/", element: <Boom /> }], []));

    await screen.findByRole("heading", { level: 1, name: "Something went wrong" });
    await expectNoAccessibilityViolations(container);
  });

  it("has no violations in the application shell", async () => {
    const { container } = renderWithApp([
      {
        element: <AppShell actions={<button type="button">Sign out</button>} />,
        children: [{ path: "/", element: <PageHeader title="Signed in" /> }],
      },
    ]);

    await screen.findByRole("heading", { level: 1, name: "Signed in" });
    await expectNoAccessibilityViolations(container);
  });

  it("has no violations in an error state with a retry", async () => {
    const { container } = render(
      <main>
        <ErrorState message="The server could not be reached." onRetry={() => undefined} />
      </main>,
    );

    await expectNoAccessibilityViolations(container);
  });

  it("has no violations in a page header with actions", async () => {
    const { container } = render(
      <main>
        <PageHeader title="Users" description="People who can sign in." actions={<button type="button">Create user</button>} />
      </main>,
    );

    await expectNoAccessibilityViolations(container);
  });
});
