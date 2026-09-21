import { useState } from "react";
import { RouterProvider } from "react-router/dom";
import { ServerSessionSource } from "@/shared/auth/ServerSessionSource";
import { AppProviders } from "./providers";
import { createQueryClient } from "./queryClient";
import { createAppRouter } from "./router";

/**
 * The composition root (docs/frontend-architecture.md §2). It chooses the
 * session source and owns nothing else.
 *
 * The source is the server (B6): GET /me is asked who the caller is, through
 * the application's own query client, so the answer is held and retried under
 * the same policy as every other read.
 */
export function App() {
  const [queryClient] = useState(createQueryClient);
  const [source] = useState(() => new ServerSessionSource(queryClient));
  const [router] = useState(createAppRouter);

  return (
    <AppProviders source={source} queryClient={queryClient}>
      <RouterProvider router={router} />
    </AppProviders>
  );
}
