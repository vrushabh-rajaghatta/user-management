import { QueryClientProvider } from "@tanstack/react-query";
import { act, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { afterEach, describe, expect, it } from "vitest";
import { createQueryClient } from "@/app/queryClient";
import { api, onUnauthorized } from "@/shared/api/client";
import { server } from "@/test/msw/server";
import { TestSessionSource } from "@/test/sessions";
import type { AuthSessionSource } from "./AuthSession";
import { AuthProvider } from "./AuthProvider";
import { SESSION_HINT_KEY, SessionHintSource } from "./SessionHintSource";
import { useAuthSession } from "./useAuthSession";

const at = (path: string) => new URL(path, window.location.origin).href;

afterEach(() => {
  sessionStorage.clear();
});

function Session() {
  const { state, signedIn, signedOut } = useAuthSession();

  return (
    <div>
      <p>status {state.status}</p>
      <button type="button" onClick={signedIn}>
        sign in
      </button>
      <button type="button" onClick={signedOut}>
        sign out
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

    await user.click(screen.getByRole("button", { name: "sign in" }));
    expect(screen.getByText("status authenticated")).toBeInTheDocument();
    expect(source.signedInCalls).toBe(1);

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

  it("treats a hand-set hint as an empty authenticated shell that the first reported 401 undoes", async () => {
    sessionStorage.setItem(SESSION_HINT_KEY, "1");

    mount(new SessionHintSource());

    expect(await screen.findByText("status authenticated")).toBeInTheDocument();

    await answerUnauthorized();

    expect(screen.getByText("status unauthenticated")).toBeInTheDocument();
    expect(sessionStorage.getItem(SESSION_HINT_KEY)).toBeNull();
  });
});
