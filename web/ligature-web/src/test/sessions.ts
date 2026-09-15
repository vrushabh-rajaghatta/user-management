import type { AuthSessionSource, AuthState } from "@/shared/auth/AuthSession";

/**
 * An in-memory session source for tests. Given a state, it resolves with it at
 * once; given nothing, it stays unresolved until the test calls settle(), which
 * is how a test holds a session in "unknown".
 *
 * It records the signedIn/signedOut notifications it receives, so a test can
 * prove the provider told the source, not only that the UI changed.
 */
export class TestSessionSource implements AuthSessionSource {
  signedInCalls = 0;

  signedOutCalls = 0;

  readonly #answer: Promise<AuthState>;

  #settle: ((state: AuthState) => void) | undefined;

  constructor(initial?: AuthState) {
    this.#answer =
      initial === undefined
        ? new Promise<AuthState>((resolve) => {
            this.#settle = resolve;
          })
        : Promise.resolve(initial);
  }

  settle(state: AuthState): void {
    if (this.#settle === undefined) {
      throw new Error("This source was created already resolved.");
    }

    this.#settle(state);
  }

  resolve(): Promise<AuthState> {
    return this.#answer;
  }

  signedIn(): void {
    this.signedInCalls += 1;
  }

  signedOut(): void {
    this.signedOutCalls += 1;
  }
}
