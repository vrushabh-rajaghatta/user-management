import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import type { RouteObject } from "react-router";
import type { AuthSessionSource, AuthState } from "@/shared/auth/AuthSession";
import { RequireAuth } from "@/shared/auth/RequireAuth";
import { renderWithApp } from "@/test/renderWithApp";
import { TestSessionSource } from "@/test/sessions";
import { server } from "@/test/msw/server";
import { SignInPage } from "./SignInPage";

/**
 * Sign-in: F3 (end the session first), F4 (the fixed 401 text) and F5 (where
 * success goes), against docs/frontend-architecture.md §7 and §13.
 *
 * Everything is asserted through the page and through the requests the host
 * actually receives. Nothing here reaches into the page's internals.
 */

const at = (path: string) => new URL(path, window.location.origin).href;

const SIGN_OUT = at("/api/auth/sign-out");
const SIGN_IN = at("/api/auth/sign-in");

/** The host's answer to every failed sign-in, whatever the cause. */
const REJECTED = { error: "Authentication is required." };

const routes: RouteObject[] = [
  { path: "/sign-in", element: <SignInPage /> },
  {
    element: <RequireAuth signInPath="/sign-in" />,
    children: [
      { path: "/", element: <h1>home</h1> },
      { path: "/users/new", element: <h1>protected page</h1> },
    ],
  },
];

/** Records every request the page makes, in order, so sequence can be asserted. */
function record(): string[] {
  const calls: string[] = [];

  server.use(
    http.post(SIGN_OUT, () => {
      calls.push("sign-out");
      return new HttpResponse(null, { status: 204 });
    }),
    http.post(SIGN_IN, () => {
      calls.push("sign-in");
      return new HttpResponse(null, { status: 204 });
    }),
  );

  return calls;
}

/**
 * A source that answers the way the server does: no caller until a session is
 * established, an established caller afterwards.
 *
 * A fixed-answer source will not do here any more. A successful sign-in resolves
 * the session at the authentication boundary (§8), so a source that kept saying
 * "no caller" would — correctly — refuse to enter the application, and these
 * tests are about where a SUCCESSFUL sign-in goes.
 */
class EstablishedSessionSource implements AuthSessionSource {
  #live = false;

  resolve(): Promise<AuthState> {
    return Promise.resolve(this.#live ? { status: "authenticated", principal: null } : { status: "unauthenticated" });
  }

  signedIn(): void {
    this.#live = true;
  }

  signedOut(): void {
    this.#live = false;
  }
}

async function signIn(user: ReturnType<typeof renderWithApp>["user"]) {
  await user.type(await screen.findByLabelText("Username"), "ada.lovelace");
  await user.type(screen.getByLabelText("Password"), "correct horse battery staple");
  await user.click(screen.getByRole("button", { name: "Sign in" }));
}

describe("the sign-in page", () => {
  it("ends any existing session before presenting credentials, in that order", async () => {
    const calls = record();
    const { user } = renderWithApp(routes, { path: "/sign-in" });

    await signIn(user);

    expect(calls).toEqual(["sign-out", "sign-in"]);
  });

  it("does not end the session merely because the page was opened", async () => {
    const calls = record();

    renderWithApp(routes, { path: "/sign-in" });

    expect(await screen.findByLabelText("Username")).toBeInTheDocument();
    expect(calls).toEqual([]);
  });

  it("presents the credentials when no session was live, which the host answers with 401", async () => {
    const calls: string[] = [];

    server.use(
      http.post(SIGN_OUT, () => {
        calls.push("sign-out");
        return HttpResponse.json(REJECTED, { status: 401 });
      }),
      http.post(SIGN_IN, () => {
        calls.push("sign-in");
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const { user } = renderWithApp(routes, { path: "/sign-in" });

    await signIn(user);

    expect(calls).toEqual(["sign-out", "sign-in"]);
  });

  /**
   * The one case that must not proceed: whether a revocation happened cannot be
   * known, so presenting credentials anyway could establish a second session
   * beside a live one.
   */
  it.each([400, 403, 500])("stops without presenting credentials when ending the session fails with %i", async (status) => {
    const calls: string[] = [];

    server.use(
      http.post(SIGN_OUT, () => {
        calls.push("sign-out");
        return HttpResponse.json({ error: "Refused." }, { status });
      }),
      http.post(SIGN_IN, () => {
        calls.push("sign-in");
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const { user } = renderWithApp(routes, { path: "/sign-in" });

    await signIn(user);

    expect(await screen.findByRole("alert")).toBeInTheDocument();
    expect(calls).toEqual(["sign-out"]);
    expect(screen.getByLabelText("Username")).toBeInTheDocument();
  });

  it("shows fixed text for a rejected sign-in, never the host's own 401 wording", async () => {
    server.use(
      http.post(SIGN_OUT, () => new HttpResponse(null, { status: 204 })),
      http.post(SIGN_IN, () => HttpResponse.json(REJECTED, { status: 401 })),
    );

    const { user } = renderWithApp(routes, { path: "/sign-in" });

    await signIn(user);

    expect(await screen.findByRole("alert")).toHaveTextContent("Invalid username or password.");
    expect(screen.queryByText(/Authentication is required/)).toBeNull();
  });

  it("shows the host's message word for word when the request was malformed", async () => {
    server.use(
      http.post(SIGN_OUT, () => new HttpResponse(null, { status: 204 })),
      http.post(SIGN_IN, () => HttpResponse.json({ error: "A username and password are required." }, { status: 400 })),
    );

    const { user } = renderWithApp(routes, { path: "/sign-in" });

    await signIn(user);

    expect(await screen.findByRole("alert")).toHaveTextContent("A username and password are required.");
  });

  it("stays on the page after a rejection, so the credentials can be corrected", async () => {
    server.use(
      http.post(SIGN_OUT, () => new HttpResponse(null, { status: 204 })),
      http.post(SIGN_IN, () => HttpResponse.json(REJECTED, { status: 401 })),
    );

    const { user } = renderWithApp(routes, { path: "/sign-in" });

    await signIn(user);

    expect(await screen.findByRole("alert")).toBeInTheDocument();
    expect(screen.getByLabelText("Username")).toHaveValue("ada.lovelace");
  });

  it("sends the credentials as the host names them", async () => {
    let body: unknown;

    server.use(
      http.post(SIGN_OUT, () => new HttpResponse(null, { status: 204 })),
      http.post(SIGN_IN, async ({ request }) => {
        body = await request.json();
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const { user } = renderWithApp(routes, { path: "/sign-in" });

    await signIn(user);

    expect(body).toEqual({ username: "ada.lovelace", password: "correct horse battery staple" });
  });

  it("goes to the home page on success when there is nowhere to return to", async () => {
    record();

    const { user } = renderWithApp(routes, { path: "/sign-in", source: new EstablishedSessionSource() });

    await signIn(user);

    expect(await screen.findByRole("heading", { name: "home" })).toBeInTheDocument();
  });

  it("returns to the page that sent the visitor to sign in", async () => {
    record();

    const { user } = renderWithApp(routes, { path: "/users/new", source: new EstablishedSessionSource() });

    await signIn(user);

    expect(await screen.findByRole("heading", { name: "protected page" })).toBeInTheDocument();
  });

  /**
   * The return path arrives in router state, which a crafted link can set. The
   * page validates it rather than trusting the router to refuse it.
   */
  it.each([
    ["a protocol-relative URL", "//evil.example/"],
    ["an absolute URL", "https://evil.example/"],
    ["a backslash-relative URL", "/\\evil.example/"],
  ])("refuses %s as a return path and goes to the home page instead", async (_, returnTo) => {
    record();

    const { user } = renderWithApp(routes, {
      path: "/sign-in",
      state: { returnTo },
      source: new EstablishedSessionSource(),
    });

    await signIn(user);

    expect(await screen.findByRole("heading", { name: "home" })).toBeInTheDocument();
  });

  it("gives no reason when a visitor was redirected here from a protected page", async () => {
    renderWithApp(routes, {
      path: "/users/new",
      source: new TestSessionSource({ status: "unauthenticated" }),
    });

    expect(await screen.findByLabelText("Username")).toBeInTheDocument();
    expect(screen.queryByRole("alert")).toBeNull();
  });
});
