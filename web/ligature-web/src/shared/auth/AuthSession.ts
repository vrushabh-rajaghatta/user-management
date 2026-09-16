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
 * The signed-in person as the server describes them (B6). It is null only while
 * no source has described anyone yet; the effective permissions it carries are
 * presentation input, never authorization (§9).
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

/** Where the answer comes from. B6 makes it the server. */
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

  /**
   * Credentials were accepted. That is NOT the same as knowing who the caller
   * is, so this RESOLVES rather than declaring the session authenticated
   * (docs/frontend-architecture.md §8): a successful sign-in is an
   * authentication boundary, and the server answers at every one of them.
   *
   * It returns the state that resulted, so the flow that signed in can decide
   * what to do about an answer that was not "authenticated".
   */
  readonly signedIn: () => Promise<AuthState>;

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

  /**
   * Takes the source's answer — unless something already changed the session
   * while the source was answering. A sign-out during resolution is newer
   * information than the answer to a question asked before it.
   *
   * It returns the state as it NOW STANDS, which is not always the answer that
   * came back, for exactly that reason.
   */
  async function resolve(): Promise<AuthState> {
    const before = changes;
    const resolved = await source.resolve();

    if (changes === before) {
      set(resolved);
    }

    return state;
  }

  return {
    getState: () => state,

    subscribe(listener) {
      listeners.add(listener);

      return () => {
        listeners.delete(listener);
      };
    },

    async start() {
      await resolve();
    },

    async signedIn() {
      source.signedIn();

      return await resolve();
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
