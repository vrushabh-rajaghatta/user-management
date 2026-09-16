import type { PermissionCode, PermissionScope } from "./permissions";

/**
 * What the client currently believes about AUTHENTICATION
 * (docs/frontend-architecture.md §8). It is not a source of authorization.
 */

export interface EffectivePermission {
  readonly code: PermissionCode;
  readonly scope?: PermissionScope;
}

/**
 * The signed-in person as a server-backed source describes them. The session
 * hint cannot describe anyone, so today the principal is always null. When a
 * source supplies effective permissions they are presentation input only.
 */
export interface Principal {
  readonly permissions: readonly EffectivePermission[];
}

/**
 * Four states, and the fourth is the one that matters
 * (docs/frontend-architecture.md §8).
 *
 * "error" is NOT "unauthenticated". A 401 is the server saying there is no
 * caller; a 5xx, a network failure or a broken response is the server failing
 * to say anything, and we do not know whether the session is valid. Collapsing
 * the second into the first would sign a live session out because a server had
 * a bad moment.
 */
export type AuthState =
  | { readonly status: "unknown" }
  | { readonly status: "authenticated"; readonly principal: Principal | null }
  | { readonly status: "unauthenticated" }
  | { readonly status: "error" };

export type PermissionState = "unknown" | "allowed" | "denied";

/** The only thing that changes when a server-backed source (B6) arrives. */
export interface AuthSessionSource {
  resolve(): Promise<AuthState>;
  signedIn(): void;
  signedOut(): void;
}

/**
 * Declared as function properties rather than methods: they are handed out
 * detached (to useSyncExternalStore, and as `can`), and none of them uses `this`.
 */
export interface AuthSession {
  readonly getState: () => AuthState;
  readonly subscribe: (listener: () => void) => () => void;
  readonly start: () => Promise<void>;
  readonly signedIn: () => void;
  readonly signedOut: () => void;
  readonly permissionState: (code: PermissionCode, scope?: PermissionScope) => PermissionState;

  /**
   * Visibility, not permission (docs/frontend-architecture.md §9). True while
   * authorization is unknown, true when allowed, false when denied. A true answer
   * never means the user may do anything: the server decides.
   */
  readonly can: (code: PermissionCode, scope?: PermissionScope) => boolean;
}

export function createAuthSession(source: AuthSessionSource): AuthSession {
  let state: AuthState = { status: "unknown" };
  let changes = 0;

  const listeners = new Set<() => void>();

  function set(next: AuthState): void {
    state = next;
    changes += 1;

    for (const listener of listeners) {
      listener();
    }
  }

  function permissionState(code: PermissionCode, scope?: PermissionScope): PermissionState {
    if (state.status !== "authenticated" || state.principal === null) {
      return "unknown";
    }

    return state.principal.permissions.some((permission) => permission.code === code && sameScope(permission.scope, scope))
      ? "allowed"
      : "denied";
  }

  return {
    getState: () => state,

    subscribe(listener) {
      listeners.add(listener);

      return () => {
        listeners.delete(listener);
      };
    },

    /**
     * Takes the source's answer — unless something already changed the session
     * while the source was answering. A sign-out during resolution is newer
     * information than the answer to a question asked before it.
     */
    async start() {
      const before = changes;
      const resolved = await source.resolve();

      if (changes === before) {
        set(resolved);
      }
    },

    signedIn() {
      source.signedIn();
      set({ status: "authenticated", principal: null });
    },

    signedOut() {
      source.signedOut();
      set({ status: "unauthenticated" });
    },

    permissionState,

    can: (code, scope) => permissionState(code, scope) !== "denied",
  };
}

/**
 * Exact matching. How a global grant relates to a scoped check is the server's
 * answer when effective permissions arrive (B6), not a rule the client invents.
 */
function sameScope(granted: PermissionScope | undefined, checked: PermissionScope | undefined): boolean {
  if (granted === undefined || checked === undefined) {
    return granted === checked;
  }

  return granted.type === checked.type && granted.id === checked.id;
}
