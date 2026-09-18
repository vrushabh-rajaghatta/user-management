import { render, screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createQueryClient } from "@/app/queryClient";
import { authKeys } from "@/shared/auth/me";
import { definePermission } from "@/shared/auth/permissions";
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

/**
 * THE WHOLE PAGE, not the render container. axe applies its "all content is
 * inside a landmark" rule only when auditing the page, which is what a browser
 * extension or a screen-reader user meets; the container audits above never ran
 * it. Found by the My account story: the shell's brand and its footer (the
 * caller's name, My account, Sign out) sat outside every landmark.
 */
describe("the signed-in shell, audited as a whole page", () => {
  const READ = definePermission("user.read");

  const cases = [
    ["a caller whose permissions are not yet known", null],
    ["a caller holding no permissions, so the primary navigation is absent", { permissions: [] }],
    ["a caller holding user.read", { permissions: [{ code: READ }] }],
  ] as const;

  it.each(cases)("has no violations for %s, with the caller's name shown", async (_, principal) => {
    const queryClient = createQueryClient();

    queryClient.setQueryData(authKeys.me(), {
      identity: { userIdentityId: "i-1", username: "ada", displayName: "Ada Lovelace" },
      permissions: [],
      session: { expiresAt: "2026-09-17T20:00:00Z", idleExpiresAt: "2026-09-17T12:15:00Z" },
    });

    renderWithApp(appRoutes, {
      path: "/",
      source: new TestSessionSource({ status: "authenticated", principal }),
      queryClient,
    });

    await screen.findByRole("heading", { level: 1, name: "Home" });
    await screen.findByText("Ada Lovelace");
    await expectNoAccessibilityViolations(document.body);
  });

  it("puts the brand in the banner and the caller's own controls in an Account navigation", async () => {
    renderWithApp(appRoutes, {
      path: "/",
      source: new TestSessionSource({ status: "authenticated", principal: { permissions: [] } }),
    });

    await screen.findByRole("heading", { level: 1, name: "Home" });

    const banner = screen.getByRole("banner");
    expect(within(banner).getByRole("link", { name: "Ligature" })).toBeInTheDocument();

    const account = screen.getByRole("navigation", { name: "Account" });
    expect(within(account).getByRole("link", { name: "My account" })).toBeInTheDocument();
    expect(within(account).getByRole("button", { name: "Sign out" })).toBeInTheDocument();
  });
});
