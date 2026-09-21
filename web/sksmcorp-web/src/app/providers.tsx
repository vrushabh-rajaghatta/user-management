import { QueryClientProvider, type QueryClient } from "@tanstack/react-query";
import type { ReactNode } from "react";
import type { AuthSessionSource } from "@/shared/auth/AuthSession";
import { AuthProvider } from "@/shared/auth/AuthProvider";

interface AppProvidersProps {
  readonly source: AuthSessionSource;
  readonly queryClient: QueryClient;
  readonly children: ReactNode;
}

/**
 * The application's providers, in order: server state, then authentication
 * (which clears server state when a session ends). Tests render routes inside
 * this same component, so they exercise the wiring the application ships with.
 */
export function AppProviders({ source, queryClient, children }: AppProvidersProps) {
  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider source={source}>{children}</AuthProvider>
    </QueryClientProvider>
  );
}
