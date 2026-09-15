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
  const router = createMemoryRouter(routes, { initialEntries: [options.path ?? "/"] });
  const user = userEvent.setup();

  const utils = render(
    <AppProviders source={source} queryClient={queryClient}>
      <RouterProvider router={router} />
    </AppProviders>,
  );

  return { ...utils, router, user, source, queryClient };
}
