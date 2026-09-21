import { describe, expect, it } from "vitest";
import { TestSessionSource } from "@/test/sessions";
import { createAuthSession } from "./AuthSession";

describe("the authentication session", () => {
  it("is unknown until the source answers, then takes the source's answer", async () => {
    const source = new TestSessionSource();
    const session = createAuthSession(source);

    const started = session.start();

    expect(session.getState()).toEqual({ status: "unknown" });

    source.settle({ status: "authenticated", principal: null });
    await started;

    expect(session.getState()).toEqual({ status: "authenticated", principal: null });
  });

  it("keeps a sign-out that happens while the source is still answering", async () => {
    const source = new TestSessionSource();
    const session = createAuthSession(source);

    const started = session.start();

    session.signedOut();
    source.settle({ status: "authenticated", principal: null });
    await started;

    expect(session.getState()).toEqual({ status: "unauthenticated" });
  });

  /**
   * Sign-in RESOLVES; it does not declare (§8). Accepted credentials say the
   * password was right, not who the caller is, so the session asks — and the
   * state it settles on is the server's answer, principal and all.
   */
  it("resolves the caller on sign-in, and tells the source", async () => {
    const source = new TestSessionSource({ status: "unauthenticated" });
    const session = createAuthSession(source);

    source.answerNext({ status: "authenticated", principal: { permissions: [] } });

    const state = await session.signedIn();

    expect(state).toEqual({ status: "authenticated", principal: { permissions: [] } });
    expect(session.getState()).toEqual({ status: "authenticated", principal: { permissions: [] } });
    expect(source.signedInCalls).toBe(1);
    expect(source.resolveCalls).toBe(1);
  });

  /**
   * The answer is reported, never softened. A sign-in the server will not stand
   * behind leaves the session exactly where the server put it.
   */
  it("reports the answer when the caller cannot be established after sign-in", async () => {
    const source = new TestSessionSource({ status: "unauthenticated" });
    const session = createAuthSession(source);

    source.answerNext({ status: "error" });

    expect(await session.signedIn()).toEqual({ status: "error" });
    expect(session.getState()).toEqual({ status: "error" });
  });

  it("moves to unauthenticated on sign-out and tells the source", async () => {
    const source = new TestSessionSource({ status: "authenticated", principal: null });
    const session = createAuthSession(source);

    await session.signedIn();
    session.signedOut();

    expect(session.getState()).toEqual({ status: "unauthenticated" });
    expect(source.signedOutCalls).toBe(1);
  });

  it("notifies subscribers of each change, and stops after they unsubscribe", async () => {
    const session = createAuthSession(new TestSessionSource({ status: "unauthenticated" }));
    let notifications = 0;

    const unsubscribe = session.subscribe(() => {
      notifications += 1;
    });

    await session.signedIn();
    unsubscribe();
    session.signedOut();

    expect(notifications).toBe(1);
  });
});
