import { QueryClient } from "@tanstack/react-query";
import { ApiContractError, ApiError } from "@/shared/api/errors";

/** Retries after the first failure; a query runs at most three times. */
export const MAX_QUERY_RETRIES = 2;

/**
 * A request the server refused (4xx) is refused again: retrying only repeats it,
 * and for a 401 would repeat an unauthorized report. A contract violation is a
 * defect that another attempt will not fix. Anything else may be transient.
 */
export function shouldRetryQuery(failureCount: number, error: unknown): boolean {
  if (error instanceof ApiContractError) {
    return false;
  }

  if (error instanceof ApiError && error.status !== null && error.status >= 400 && error.status < 500) {
    return false;
  }

  return failureCount < MAX_QUERY_RETRIES;
}

/** Mutations change state, so they are never repeated behind the user's back. */
export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: { retry: shouldRetryQuery },
      mutations: { retry: false },
    },
  });
}
