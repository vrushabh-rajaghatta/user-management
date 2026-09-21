import { QueryClientProvider } from "@tanstack/react-query";
import { act, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { createQueryClient } from "@/app/queryClient";
import { api, onUnauthorized } from "@/shared/api/client";
import { server } from "@/test/msw/server";
import { TestSessionSource } from "@/test/sessions";
import type { AuthSessionSource } from "./AuthSession";
import { AuthProvider } from "./AuthProvider";
import { useAuthSession } from "./useAuthSession";

const at = (path: string) => new URL(path, window.location.origin).href;

function Session() {
  const { state, signedIn, signedOut, retry } = useAuthSession();

  return (
    <div>
      <p>status {state.status}</p>
      <button
        type="button"
        onClick={() => {
          void signedIn();
        }}
      >
        sign in
      </button>
      <button type="button" onClick={signedOut}>
        sign out
      </button>
      <button type="button" onClick={retry}>
        retry
      </button>
    </div>
  );
}

function mount(source: AuthSessionSource) {
  const queryClient = createQueryClient();

  queryClient.setQueryData(["previous", "person"], { name: "someone" });

  const utils = render(
    <QueryClientProvider client={queryClient}>
      <AuthProvider source={source}>
        <Session />
      </AuthProvider>
    </QueryClientProvider>,
  );

  return { ...utils, queryClient, user: userEvent.setup() };
}

async function answerUnauthorized(mode?: "report" | "return") {
  server.use(http.post(at("/api/probe"), () => HttpResponse.json({ error: "Authentication is required." }, { status: 401 })));

  await act(async () => {
    await api.post("/api/probe", mode === undefined ? {} : { unauthorized: mode }).catch(() => undefined);
  });
}

describe("the authentication provider", () => {
  it("starts unknown and settles on the source's answer", async () => {
    const source = new TestSessionSource();

    mount(source);

    expect(screen.getByText("status unknown")).toBeInTheDocument();

    await act(async () => {
      source.settle({ status: "authenticated", principal: null });
      await Promise.resolve();
    });

    expect(await screen.findByText("status authenticated")).toBeInTheDocument();
  });

  it("signs in and out through the hook, telling the source, and clears the query cache on sign-out", async () => {
    const source = new TestSessionSource({ status: "unauthenticated" });
    const { user, queryClient } = mount(source);

    await screen.findByText("status unauthenticated");

    // Sign-in resolves rather than declares (§8), so the state it reaches is
    // the one the source answers with at that boundary.
    source.answerNext({ status: "authenticated", principal: null });

    await user.click(screen.getByRole("button", { name: "sign in" }));
    expect(await screen.findByText("status authenticated")).toBeInTheDocument();
    expect(source.signedInCalls).toBe(1);
    expect(source.resolveCalls).toBe(2);

    await user.click(screen.getByRole("button", { name: "sign out" }));
    expect(screen.getByText("status unauthenticated")).toBeInTheDocument();
    expect(source.signedOutCalls).toBe(1);
    expect(queryClient.getQueryData(["previous", "person"])).toBeUndefined();
  });

  it("signs the session out and clears the query cache on a reported 401", async () => {
    const source = new TestSessionSource({ status: "authenticated", principal: null });
    const { queryClient } = mount(source);

    await screen.findByText("status authenticated");
    await answerUnauthorized();

    expect(screen.getByText("status unauthenticated")).toBeInTheDocument();
    expect(source.signedOutCalls).toBe(1);
    expect(queryClient.getQueryData(["previous", "person"])).toBeUndefined();
  });

  it("changes neither the session nor the query cache on a returned 401", async () => {
    const source = new TestSessionSource({ status: "authenticated", principal: null });
    const { queryClient } = mount(source);

    await screen.findByText("status authenticated");
    await answerUnauthorized("return");

    expect(screen.getByText("status authenticated")).toBeInTheDocument();
    expect(source.signedOutCalls).toBe(0);
    expect(queryClient.getQueryData(["previous", "person"])).toEqual({ name: "someone" });
  });

  it("stops listening for unauthorized reports when it unmounts", async () => {
    const { unmount } = mount(new TestSessionSource({ status: "unauthenticated" }));

    await screen.findByText("status unauthenticated");
    unmount();

    expect(() => {
      onUnauthorized(() => undefined)();
    }).not.toThrow();
  });

  it("settles on error when the source could not determine the caller", async () => {
    mount(new TestSessionSource({ status: "error" }));

    expect(await screen.findByText("status error")).toBeInTheDocument();
  });

  /**
   * THE DISTINCTION, at the provider (B6-B, §8). A failed resolution establishes
   * nothing about the session, so nothing may be torn down on the strength of
   * it: the source is not told the session ended, and the query cache — which
   * may hold another person's data only if a session actually ended — stays.
   */
  it("does not sign out or clear the query cache when resolution fails", async () => {
    const source = new TestSessionSource({ status: "error" });
    const { queryClient } = mount(source);

    await screen.findByText("status error");

    expect(source.signedOutCalls).toBe(0);
    expect(queryClient.getQueryData(["previous", "person"])).toEqual({ name: "someone" });
  });

  it("re-asks the source when the session is retried, and takes the new answer", async () => {
    const source = new TestSessionSource({ status: "error" });
    const { user } = mount(source);

    await screen.findByText("status error");
    expect(source.resolveCalls).toBe(1);

    source.answerNext({ status: "authenticated", principal: null });

    await user.click(screen.getByRole("button", { name: "retry" }));

    expect(await screen.findByText("status authenticated")).toBeInTheDocument();
    expect(source.resolveCalls).toBe(2);
  });

  /** A retry is not a sign-out (§8): it asks again, and destroys nothing. */
  it("retries without signing out or clearing the query cache", async () => {
    const source = new TestSessionSource({ status: "error" });
    const { user, queryClient } = mount(source);

    await screen.findByText("status error");

    source.answerNext({ status: "error" });

    await user.click(screen.getByRole("button", { name: "retry" }));

    expect(source.resolveCalls).toBe(2);
    expect(source.signedOutCalls).toBe(0);
    expect(queryClient.getQueryData(["previous", "person"])).toEqual({ name: "someone" });
  });
});
