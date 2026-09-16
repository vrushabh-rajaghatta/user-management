import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { ServerSessionSource } from "@/shared/auth/ServerSessionSource";
import { server } from "@/test/msw/server";
import { renderWithApp } from "@/test/renderWithApp";
import { createQueryClient } from "./queryClient";
import { appRoutes } from "./router";

/**
 * The authentication boundary, driven through the REAL sign-in flow
 * (docs/frontend-architecture.md §8, §9, §13).
 *
 * Every other test in this suite injects an already-resolved session source,
 * which is the right seam for those tests and precisely the seam that hid this
 * defect: a successful sign-in declared the caller authenticated without ever
 * asking the server who they were, so every permission stayed unknown and
 * optimistic visibility became permanent instead of applying only while
 * resolution was in flight.
 *
 * So these tests use the real ServerSessionSource against a real HTTP
 * conversation, and assert on what the server was asked.
 */

const at = (path: string) => new URL(path, window.location.origin).href;

const caller = (permissions: { code: string; scopeType: string; scopeId: string | null }[]) => ({
  identity: { userIdentityId: "11111111-1111-4000-8000-000000000001", username: "ada.lovelace", displayName: "Ada Lovelace" },
  permissions,
  session: { expiresAt: "2026-09-17T00:00:00Z", idleExpiresAt: "2026-09-16T12:15:00Z" },
});

const CREATE = { code: "user.create", scopeType: "Global", scopeId: null };

interface Conversation {
  /** Every GET /me, in order, recorded as whether a session was live at the time. */
  readonly me: boolean[];

  /** What /me answers once a session is live. Deferred so a test can hold it pending. */
  answer: () => Promise<Response>;
}

/**
 * The host as this flow meets it: /me answers 401 until sign-in succeeds, the
 * pre-flight sign-out answers 401 because there is nothing to end (§13), and
 * sign-in answers 204 with no body.
 */
function host(answer?: Conversation["answer"]): Conversation {
  let live = false;

  const conversation: Conversation = {
    me: [],
    answer: answer ?? (() => Promise.resolve(HttpResponse.json(caller([CREATE])))),
  };

  server.use(
    http.get(at("/api/me"), async () => {
      conversation.me.push(live);

      return live
        ? await conversation.answer()
        : HttpResponse.json({ error: "Authentication is required." }, { status: 401 });
    }),
    http.post(at("/api/auth/sign-out"), () =>
      HttpResponse.json({ error: "Authentication is required." }, { status: 401 }),
    ),
    http.post(at("/api/auth/sign-in"), () => {
      live = true;

      return new HttpResponse(null, { status: 204 });
    }),
  );

  return conversation;
}

function arrive(path: string) {
  const queryClient = createQueryClient();

  return renderWithApp(appRoutes, { path, source: new ServerSessionSource(queryClient), queryClient });
}

async function signIn(user: ReturnType<typeof arrive>["user"]) {
  await screen.findByRole("heading", { level: 1, name: "Sign in" });

  await user.type(screen.getByLabelText(/Username/), "ada.lovelace");
  await user.type(screen.getByLabelText(/Password/), "a-sufficiently-long-password");
  await user.click(screen.getByRole("button", { name: "Sign in" }));
}

describe("signing in", () => {
  it("asks the server who the caller is, because a successful sign-in is an authentication boundary", async () => {
    const conversation = host();
    const { user } = arrive("/");

    await signIn(user);

    await screen.findByRole("heading", { level: 1, name: "Home" });

    expect(conversation.me).toEqual([false, true]);
  });

  /**
   * THE REGRESSION. The whole chain in one test: sign in, ask /me, receive a
   * caller holding NOTHING, and be refused the page.
   *
   * Without the /me call after sign-in the principal stays null, every
   * permission stays unknown, and optimistic visibility renders the page to a
   * caller the server has just said holds no permission at all.
   */
  it("refuses a permission-gated page to a caller the server reports with no permissions", async () => {
    host(() => Promise.resolve(HttpResponse.json(caller([]))));

    const { user } = arrive("/users/new");

    await signIn(user);

    expect(await screen.findByRole("heading", { level: 1, name: "Not available" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Create user" })).toBeNull();
  });

  it("renders the page to a caller the server reports as holding the permission", async () => {
    const conversation = host();

    const { user } = arrive("/users/new");

    await signIn(user);

    expect(await screen.findByRole("heading", { level: 1, name: "Create user" })).toBeInTheDocument();

    // Optimistic visibility would render this page without asking anyone, so
    // the assertion above alone would pass with the defect in place.
    expect(conversation.me).toEqual([false, true]);
  });

  /**
   * The intermediate state is NOT "authenticated with an unknown principal":
   * nothing has been established about the caller until the server answers. So
   * the sign-in surface stays put while the resolution is in flight, rather than
   * entering the application and rendering it blank or optimistic.
   */
  it("stays on the sign-in page while the caller is being resolved", async () => {
    let release: (() => void) | undefined;

    const held = new Promise<void>((resolve) => {
      release = resolve;
    });

    host(async () => {
      await held;

      return HttpResponse.json(caller([CREATE]));
    });

    const { user } = arrive("/users/new");

    await signIn(user);

    expect(screen.getByRole("heading", { level: 1, name: "Sign in" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Signing in..." })).toBeInTheDocument();

    release?.();

    expect(await screen.findByRole("heading", { level: 1, name: "Create user" })).toBeInTheDocument();
  });

  /**
   * Credentials were accepted and the caller still could not be resolved. That
   * is an authentication-resolution failure, and it is stated: entering the
   * application on the strength of the 204 alone would claim an authenticated
   * caller nobody has confirmed.
   *
   * The generous timeout is not slack. /me is a query like any other, so a 5xx
   * is retried under createQueryClient's policy before it is reported — this
   * test waits out that backoff, and in doing so shows the policy reaches /me.
   */
  it("does not enter the application when the caller cannot be resolved", async () => {
    host(() => Promise.resolve(HttpResponse.json({ error: "Nope." }, { status: 500 })));

    const { user } = arrive("/users/new");

    await signIn(user);

    expect(await screen.findByRole("alert", undefined, { timeout: 8_000 })).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 1, name: "Sign in" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Create user" })).toBeNull();
  }, 15_000);

  it("does not claim a caller when the server answers that there is none", async () => {
    host(() => Promise.resolve(HttpResponse.json({ error: "Authentication is required." }, { status: 401 })));

    const { user } = arrive("/users/new");

    await signIn(user);

    expect(await screen.findByRole("alert")).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 1, name: "Sign in" })).toBeInTheDocument();
  });
});
