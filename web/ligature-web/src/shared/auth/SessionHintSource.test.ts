import { afterEach, describe, expect, it } from "vitest";
import { SESSION_HINT_KEY, SessionHintSource } from "./SessionHintSource";

afterEach(() => {
  sessionStorage.clear();
  localStorage.clear();
});

describe("the session hint source", () => {
  it("resolves unauthenticated when there is no hint", async () => {
    await expect(new SessionHintSource().resolve()).resolves.toEqual({ status: "unauthenticated" });
  });

  it("writes the hint on sign-in, and resolves authenticated with no principal", async () => {
    const source = new SessionHintSource();

    source.signedIn();

    expect(sessionStorage.getItem(SESSION_HINT_KEY)).not.toBeNull();
    await expect(source.resolve()).resolves.toEqual({ status: "authenticated", principal: null });
  });

  it("removes the hint on sign-out", async () => {
    const source = new SessionHintSource();

    source.signedIn();
    source.signedOut();

    expect(sessionStorage.getItem(SESSION_HINT_KEY)).toBeNull();
    await expect(source.resolve()).resolves.toEqual({ status: "unauthenticated" });
  });

  it("keeps the hint in sessionStorage, never localStorage", () => {
    new SessionHintSource().signedIn();

    expect(localStorage.length).toBe(0);
    expect(sessionStorage.getItem(SESSION_HINT_KEY)).not.toBeNull();
  });

  it("treats storage that cannot be used as no hint, rather than failing", async () => {
    const unusable = {
      getItem: () => {
        throw new Error("storage disabled");
      },
      setItem: () => {
        throw new Error("storage disabled");
      },
      removeItem: () => {
        throw new Error("storage disabled");
      },
    } as unknown as Storage;

    const source = new SessionHintSource(unusable);

    expect(() => {
      source.signedIn();
      source.signedOut();
    }).not.toThrow();
    await expect(source.resolve()).resolves.toEqual({ status: "unauthenticated" });
  });
});
