import { chromium, type Browser, type BrowserContext, type Page } from "@playwright/test";
import { afterAll, beforeAll, describe, expect, inject, it } from "vitest";

/**
 * The carrier cookie over a real browser, a real HTTPS origin and a real host
 * (docs/architecture.md §17, docs/frontend-architecture.md §16).
 *
 * Every request here is issued BY THE PAGE, so the browser applies its own
 * cookie rules — Secure, SameSite and the __Host- prefix — and its own Fetch
 * Metadata. page.request shares the cookie jar but its handling of those rules
 * is unstated, and an unstated behaviour cannot be evidence.
 *
 * The certificate is the mkcert one and is NOT ignored: whether a browser
 * accepts this cookie over this certificate is part of what is being proved.
 */

const webOrigin = inject("webOrigin");
const activationToken = inject("activationToken");
const username = inject("username");

const PASSWORD = "Correct Horse Battery Staple 42";

const COOKIE = "__Host-sksmcorp";

interface Answer {
  readonly status: number;
  readonly body: string;
}

let browser: Browser | undefined;
let activation: Answer;

async function open(): Promise<{ context: BrowserContext; page: Page }> {
  if (browser === undefined) {
    throw new Error("The browser was not launched, so the environment never became ready.");
  }

  const context = await browser.newContext();
  const page = await context.newPage();

  await page.goto(`${webOrigin}/sign-in`);

  return { context, page };
}

function post(page: Page, path: string, body?: unknown): Promise<Answer> {
  return page.evaluate(
    async ([target, payload]: [string, string | undefined]) => {
      const response = await fetch(target, {
        method: "POST",
        credentials: "same-origin",
        headers: payload === undefined ? undefined : { "Content-Type": "application/json" },
        body: payload,
      });

      return { status: response.status, body: await response.text() };
    },
    [path, body === undefined ? undefined : JSON.stringify(body)] as [string, string | undefined],
  );
}

async function carrier(context: BrowserContext) {
  return (await context.cookies()).find((cookie) => cookie.name === COOKIE);
}

beforeAll(async () => {
  browser = await chromium.launch();

  // The one activation this token allows, through the browser, the HTTPS
  // development server and its proxy — the whole path, not just the transport.
  const { context, page } = await open();

  activation = await post(page, "/api/account/activate", { token: activationToken, newPassword: PASSWORD });

  await context.close();
}, 180_000);

afterAll(async () => {
  await browser?.close();
});

describe("the account created by the provisioning tool", () => {
  it("is activated through the real HTTPS path with its emailed token", () => {
    expect(activation.status).toBe(200);
    expect(activation.body).toContain("userIdentityId");
  });
});

describe("the carrier cookie", () => {
  it("is set by a successful sign-in, with no body of its own", async () => {
    const { context, page } = await open();

    try {
      const answer = await post(page, "/api/auth/sign-in", { username, password: PASSWORD });

      expect(answer.status).toBe(204);
      expect(answer.body).toBe("");

      const cookie = await carrier(context);

      expect(cookie).toBeDefined();
      expect(cookie?.httpOnly).toBe(true);
      expect(cookie?.secure).toBe(true);
      expect(cookie?.sameSite).toBe("Strict");
      expect(cookie?.path).toBe("/");

      // A session cookie: it has no lifetime of its own, so the server's
      // session is the only thing that decides how long it is good for.
      expect(cookie?.expires).toBe(-1);
    } finally {
      await context.close();
    }
  });

  it("is not set by a failed sign-in", async () => {
    const { context, page } = await open();

    try {
      const answer = await post(page, "/api/auth/sign-in", { username, password: "the wrong password" });

      expect(answer.status).toBe(401);
      expect(await carrier(context)).toBeUndefined();
    } finally {
      await context.close();
    }
  });

  /**
   * The cookie is HttpOnly, so no test can read it and present it deliberately.
   * What can be shown is that a request the browser sends afterwards is treated
   * as authenticated — and an invalid activation token proves it without
   * needing a second real token: an established caller is refused BEFORE the
   * token is examined.
   */
  it("authenticates the requests the browser sends afterwards", async () => {
    const signedIn = await open();
    const anonymous = await open();

    try {
      expect((await post(signedIn.page, "/api/auth/sign-in", { username, password: PASSWORD })).status).toBe(204);

      const withCookie = await post(signedIn.page, "/api/account/activate", {
        token: "not-a-real-token",
        newPassword: PASSWORD,
      });

      const withoutCookie = await post(anonymous.page, "/api/account/activate", {
        token: "not-a-real-token",
        newPassword: PASSWORD,
      });

      expect(withCookie.status).toBe(401);
      expect(withoutCookie.status).toBe(400);
    } finally {
      await signedIn.context.close();
      await anonymous.context.close();
    }
  });

  it("is cleared by signing out", async () => {
    const { context, page } = await open();

    try {
      expect((await post(page, "/api/auth/sign-in", { username, password: PASSWORD })).status).toBe(204);
      expect(await carrier(context)).toBeDefined();

      const answer = await post(page, "/api/auth/sign-out");

      expect(answer.status).toBe(204);
      expect(await carrier(context)).toBeUndefined();
    } finally {
      await context.close();
    }
  });
});

describe("a request that already presents a session", () => {
  it("cannot sign in again", async () => {
    const { context, page } = await open();

    try {
      expect((await post(page, "/api/auth/sign-in", { username, password: PASSWORD })).status).toBe(204);

      const again = await post(page, "/api/auth/sign-in", { username, password: PASSWORD });

      expect(again.status).toBe(401);
    } finally {
      await context.close();
    }
  });

  it("cannot reset a password", async () => {
    const { context, page } = await open();

    try {
      expect((await post(page, "/api/auth/sign-in", { username, password: PASSWORD })).status).toBe(204);

      const reset = await post(page, "/api/account/reset-password", {
        token: "not-a-real-token",
        newPassword: PASSWORD,
      });

      expect(reset.status).toBe(401);
    } finally {
      await context.close();
    }
  });
});

describe("a token that is not valid", () => {
  /**
   * The token is checked BEFORE the password policy, so an invalid token can
   * never become an oracle for what the policy is.
   *
   * Proved by offering the same invalid token with two passwords that any
   * policy would judge differently, and requiring the two answers to be
   * identical. Searching the message for the word "password" would not prove
   * it: the reset token is legitimately called "the password reset token", and
   * a test that reads wording would pass or fail on a phrase rather than on the
   * ordering it is meant to check.
   */
  it.each([
    ["activation", "/api/account/activate"],
    ["reset", "/api/account/reset-password"],
  ])("is refused by %s identically whatever password accompanies it", async (_, path) => {
    const { context, page } = await open();

    try {
      const token = "not-a-real-token";

      const withUnacceptable = await post(page, path, { token, newPassword: "x" });
      const withAcceptable = await post(page, path, {
        token,
        newPassword: "a long and entirely unremarkable passphrase",
      });

      expect(withUnacceptable.status).toBe(400);
      expect(withAcceptable.status).toBe(400);

      // Byte for byte: any difference at all would be the oracle.
      expect(withUnacceptable.body).toBe(withAcceptable.body);

      // And it says the token is the problem, rather than describing the policy.
      expect(withUnacceptable.body).toMatch(/token/i);
    } finally {
      await context.close();
    }
  });
});
