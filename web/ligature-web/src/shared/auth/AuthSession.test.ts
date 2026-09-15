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

  it("moves to authenticated on sign-in and tells the source", () => {
    const source = new TestSessionSource({ status: "unauthenticated" });
    const session = createAuthSession(source);

    session.signedIn();

    expect(session.getState()).toEqual({ status: "authenticated", principal: null });
    expect(source.signedInCalls).toBe(1);
  });

  it("moves to unauthenticated on sign-out and tells the source", () => {
    const source = new TestSessionSource({ status: "authenticated", principal: null });
    const session = createAuthSession(source);

    session.signedIn();
    session.signedOut();

    expect(session.getState()).toEqual({ status: "unauthenticated" });
    expect(source.signedOutCalls).toBe(1);
  });

  it("notifies subscribers of each change, and stops after they unsubscribe", () => {
    const session = createAuthSession(new TestSessionSource({ status: "unauthenticated" }));
    let notifications = 0;

    const unsubscribe = session.subscribe(() => {
      notifications += 1;
    });

    session.signedIn();
    unsubscribe();
    session.signedOut();

    expect(notifications).toBe(1);
  });
});
