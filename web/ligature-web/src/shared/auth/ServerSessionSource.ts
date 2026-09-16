import type { QueryClient } from "@tanstack/react-query";
import { ApiError } from "@/shared/api/errors";
import type { AuthSessionSource, AuthState } from "./AuthSession";
import { authKeys, me } from "./me";
import { definePermission, type PermissionCode } from "./permissions";

/**
 * The server-backed session source (docs/architecture.md §17, B6).
 *
 * It replaces the sessionStorage hint entirely. The hint could never establish
 * whether the browser's carrier cookie represented a live session; this asks,
 * and the server answers.
 *
 * THE MAPPING IS THE CONTRACT:
 *
 *   200   a caller, with their effective permissions   -> authenticated
 *   401   there is no caller                           -> unauthenticated
 *   5xx   the server did not answer                    -> error
 *   network / broken contract                          -> error
 *
 * Only a 401 is a statement about the session. Everything else means we do not
 * know, and an unknown must never be rendered as a sign-out.
 *
 * It resolves through the QUERY LAYER rather than reaching past it to the
 * transport (§11), so the answer is held under /me's own key like any other
 * read. staleTime is 0 deliberately: a cached /me is a record of what the server
 * said, never proof of authentication now, so a resolution always asks again.
 */
export class ServerSessionSource implements AuthSessionSource {
  readonly #queryClient: QueryClient;

  constructor(queryClient: QueryClient) {
    this.#queryClient = queryClient;
  }

  async resolve(): Promise<AuthState> {
    try {
      const caller = await this.#queryClient.query({
        queryKey: authKeys.me(),
        queryFn: () => me(),
        staleTime: 0,
      });

      return {
        status: "authenticated",
        principal: {
          permissions: caller.permissions.map((permission) => ({
            code: definePermission(permission.code) satisfies PermissionCode,

            // V1 assigns Global with no id, and the client does not invent
            // scope semantics: an entry with no scope id is a global one.
            scope:
              permission.scopeId === null
                ? undefined
                : { type: permission.scopeType, id: permission.scopeId },
          })),
        },
      };
    } catch (failure) {
      if (failure instanceof ApiError && failure.status === 401) {
        return { status: "unauthenticated" };
      }

      return { status: "error" };
    }
  }

  /**
   * Nothing to remember. The server is the source, and sign-in and sign-out
   * change what it will say next rather than anything held here.
   */
  signedIn(): void {
    // Intentionally empty.
  }

  signedOut(): void {
    // Intentionally empty.
  }
}
