import { useState } from "react";
import { RouterProvider } from "react-router/dom";
import { SessionHintSource } from "@/shared/auth/SessionHintSource";
import { AppProviders } from "./providers";
import { createQueryClient } from "./queryClient";
import { createAppRouter } from "./router";

/**
 * The composition root (docs/frontend-architecture.md §2). It chooses the
 * session source — the one line that changes when a server-backed source (B6)
 * arrives — and owns nothing else.
 */
export function App() {
  const [queryClient] = useState(createQueryClient);
  const [source] = useState(() => new SessionHintSource());
  const [router] = useState(createAppRouter);

  return (
    <AppProviders source={source} queryClient={queryClient}>
      <RouterProvider router={router} />
    </AppProviders>
  );
}
