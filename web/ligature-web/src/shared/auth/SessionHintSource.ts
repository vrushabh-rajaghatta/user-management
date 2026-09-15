import type { AuthSessionSource, AuthState } from "./AuthSession";

/**
 * NOT A SECURITY CONTROL.
 *
 * A flag in sessionStorage recording that this tab signed in
 * (docs/frontend-architecture.md §8). It decides only what to render first.
 * Setting it by hand gets an empty shell and, on the first request, a 401 that
 * signs the session out. Every request is still decided by the server.
 *
 * It is not an identity-discovery mechanism, and cannot establish whether the
 * browser's carrier cookie represents a live session. A tab opened on its own
 * starts without it while the shared cookie may still be live; identity-
 * establishing flows therefore end any existing session first (§13). That
 * mitigates the ambiguity; a server-backed source (B6) resolves it.
 *
 * The only file the lint rules allow to touch web storage.
 */

export const SESSION_HINT_KEY = "ligature.session-hint";

export class SessionHintSource implements AuthSessionSource {
  readonly #storage: Storage | undefined;

  constructor(storage?: Storage) {
    this.#storage = storage ?? browserSessionStorage();
  }

  resolve(): Promise<AuthState> {
    const hinted = this.#attempt((storage) => storage.getItem(SESSION_HINT_KEY) !== null) ?? false;

    return Promise.resolve(hinted ? { status: "authenticated", principal: null } : { status: "unauthenticated" });
  }

  signedIn(): void {
    this.#attempt((storage) => {
      storage.setItem(SESSION_HINT_KEY, "1");
    });
  }

  signedOut(): void {
    this.#attempt((storage) => {
      storage.removeItem(SESSION_HINT_KEY);
    });
  }

  /** Storage can be disabled or full. Without it there is simply no hint. */
  #attempt<T>(operation: (storage: Storage) => T): T | undefined {
    if (this.#storage === undefined) {
      return undefined;
    }

    try {
      return operation(this.#storage);
    } catch {
      return undefined;
    }
  }
}

function browserSessionStorage(): Storage | undefined {
  try {
    return sessionStorage;
  } catch {
    return undefined;
  }
}
