import { QueryClient } from "@tanstack/react-query";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it } from "vitest";
import { server } from "@/test/msw/server";
import { authKeys } from "./me";
import { ServerSessionSource } from "./ServerSessionSource";

/**
 * The server-backed source (B6-B, §8, §11). It asks GET /me who the caller is,
 * through the query layer, and turns the answer into one of four states.
 *
 * The distinction these tests exist for: a 401 means there is NO CALLER, and a
 * 5xx or a network failure means WE DO NOT KNOW. Collapsing the second into the
 * first would end a live session because a server had a bad moment.
 *
 * Retries are off in this client on purpose. The application's retry policy
 * belongs to createQueryClient and is proved in app/queryClient.test.ts; these
 * tests are about the mapping, and waiting out a backoff would prove nothing
 * about it.
 */

const at = (path: string) => new URL(path, window.location.origin).href;

const ME = at("/api/me");

const answer = {
  identity: {
    userIdentityId: "11111111-1111-4000-8000-000000000001",
    username: "ada.lovelace",
    displayName: "Ada Lovelace",
  },
  permissions: [{ code: "user.create", scopeType: "Global", scopeId: null }],
  session: {
    expiresAt: "2026-09-17T00:00:00Z",
    idleExpiresAt: "2026-09-16T12:15:00Z",
  },
};

let queryClient: QueryClient;

beforeEach(() => {
  queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
});

const source = () => new ServerSessionSource(queryClient);

describe("resolving the caller", () => {
  it("is authenticated when the server describes a caller", async () => {
    server.use(http.get(ME, () => HttpResponse.json(answer)));

    const state = await source().resolve();

    expect(state.status).toBe("authenticated");
  });

  it("carries the effective permissions the server reported", async () => {
    server.use(http.get(ME, () => HttpResponse.json(answer)));

    const state = await source().resolve();

    expect(state.status === "authenticated" && state.principal?.permissions).toEqual([
      { code: "user.create", scope: undefined },
    ]);
  });

  it("is unauthenticated when the server says there is no caller", async () => {
    server.use(http.get(ME, () => HttpResponse.json({ error: "Authentication is required." }, { status: 401 })));

    const state = await source().resolve();

    expect(state.status).toBe("unauthenticated");
  });

  /**
   * THE DISTINCTION. A server failure is not an answer about the session, so it
   * must not be read as one.
   */
  it.each([500, 502, 503])("is an error, not unauthenticated, when the server fails with %i", async (status) => {
    server.use(http.get(ME, () => HttpResponse.json({ error: "Nope." }, { status })));

    const state = await source().resolve();

    expect(state.status).toBe("error");
    expect(state.status).not.toBe("unauthenticated");
  });

  it("is an error, not unauthenticated, when the server cannot be reached", async () => {
    server.use(http.get(ME, () => HttpResponse.error()));

    const state = await source().resolve();

    expect(state.status).toBe("error");
  });

  /**
   * A response that does not match the contract is a defect, not a statement
   * about the session — so it is an error too, never a sign-out.
   */
  it("is an error when the response breaks its contract", async () => {
    server.use(http.get(ME, () => HttpResponse.json({ identity: { username: "ada" } })));

    const state = await source().resolve();

    expect(state.status).toBe("error");
  });

  /**
   * §8: /me is an authenticated request and therefore session activity. The
   * source asks once when asked to; anything periodic would keep an idle
   * session alive for as long as a tab stayed open.
   */
  it("asks the server exactly once per resolution", async () => {
    let calls = 0;

    server.use(
      http.get(ME, () => {
        calls += 1;
        return HttpResponse.json(answer);
      }),
    );

    await source().resolve();

    expect(calls).toBe(1);
  });

  /**
   * §11: resolution consumes the /me QUERY rather than reaching past the query
   * layer to the transport. The cache holding the answer under the key factory's
   * key is what distinguishes the two.
   */
  it("consumes the query, so the answer is held in the cache under the /me key", async () => {
    server.use(http.get(ME, () => HttpResponse.json(answer)));

    await source().resolve();

    expect(queryClient.getQueryData(authKeys.me())).toEqual(answer);
  });

  /**
   * §11: a stale cached /me is not proof of authentication after a fresh
   * resolution. Resolving asks the server again rather than answering from what
   * the cache happens to hold.
   */
  it("asks the server again on a later resolution rather than answering from the cache", async () => {
    let calls = 0;

    server.use(
      http.get(ME, () => {
        calls += 1;
        return HttpResponse.json(answer);
      }),
    );

    await source().resolve();
    await source().resolve();

    expect(calls).toBe(2);
  });
});
