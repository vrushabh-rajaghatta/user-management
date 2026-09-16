import type { QueryClient } from "@tanstack/react-query";
import { render } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createMemoryRouter, type RouteObject } from "react-router";
import { RouterProvider } from "react-router/dom";
import { AppProviders } from "@/app/providers";
import { createQueryClient } from "@/app/queryClient";
import type { AuthSessionSource } from "@/shared/auth/AuthSession";
import { TestSessionSource } from "./sessions";

interface RenderOptions {
  path?: string;

  /**
   * Router state for the first entry. RequireAuth only ever produces a path and
   * query, so the hostile return paths validateReturnPath exists to refuse can
   * only be constructed here — a test limited to what RequireAuth emits would
   * exercise trusted input alone.
   */
  state?: unknown;

  source?: AuthSessionSource;
  queryClient?: QueryClient;
}

/**
 * Renders routes inside the application's real providers, with a memory router,
 * a fresh query client and a test session source. The providers are the ones the
 * application uses, so a test exercises the same wiring it ships with.
 */
export function renderWithApp(routes: RouteObject[], options: RenderOptions = {}) {
  const source = options.source ?? new TestSessionSource({ status: "unauthenticated" });
  const queryClient = options.queryClient ?? createQueryClient();
  const path = options.path ?? "/";
  const router = createMemoryRouter(routes, { initialEntries: [entry(path, options.state)] });
  const user = userEvent.setup();

  const utils = render(
    <AppProviders source={source} queryClient={queryClient}>
      <RouterProvider router={router} />
    </AppProviders>,
  );

  return { ...utils, router, user, source, queryClient };
}

/** Keeps the query and fragment of the path when state has to be carried too. */
function entry(path: string, state: unknown) {
  if (state === undefined) {
    return path;
  }

  const url = new URL(path, "http://localhost");

  return { pathname: url.pathname, search: url.search, hash: url.hash, state };
}
