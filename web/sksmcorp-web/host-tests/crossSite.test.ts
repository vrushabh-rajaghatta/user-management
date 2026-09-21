import { request } from "node:http";
import { describe, expect, inject, it } from "vitest";

/**
 * The cross-site refusal, header by header (docs/architecture.md §17,
 * CrossSiteMiddleware).
 *
 * node:http rather than fetch, because Node's fetch SILENTLY DROPS a Host
 * header, and Host is half of what the Origin rule compares. These requests go
 * straight to the host: the middleware is what is under test, and the
 * development server's preservation of Host is proved separately by
 * tooling/dev-server.test.ts.
 *
 * Every row asserts "not the cross-site refusal" rather than "succeeded". These
 * requests may still fail on their own merits with 400 or 401, and a proof that
 * accepted any non-403 would pass for the wrong reason.
 */

const hostOrigin = inject("hostOrigin");

const REFUSAL = "Cross-site requests are not accepted.";

const WEB_HOST = "localhost:5173";

const EVIL = "https://evil.example";

interface Answer {
  readonly status: number;
  readonly body: string;
}

function send(headers: Record<string, string>, method = "POST"): Promise<Answer> {
  return new Promise((resolve, reject) => {
    const body = JSON.stringify({ username: "someone", password: "something" });

    const attempt = request(
      `${hostOrigin}/api/auth/sign-in`,
      { method, headers: { "Content-Type": "application/json", "Content-Length": Buffer.byteLength(body), ...headers } },
      (response) => {
        let text = "";

        response.setEncoding("utf8");
        response.on("data", (chunk: string) => (text += chunk));
        response.on("end", () => {
          resolve({ status: response.statusCode ?? 0, body: text });
        });
      },
    );

    attempt.on("error", reject);

    if (method !== "GET") {
      attempt.write(body);
    }

    attempt.end();
  });
}

function refused(answer: Answer): boolean {
  return answer.status === 403 && answer.body.includes(REFUSAL);
}

describe("a state-changing request the browser says is cross-site", () => {
  it("is refused, even beside an Origin that matches the Host", async () => {
    const answer = await send({ Host: WEB_HOST, Origin: `https://${WEB_HOST}`, "Sec-Fetch-Site": "cross-site" });

    expect(refused(answer)).toBe(true);
  });

  it.each(["same-site", "none", "SAME-ORIGIN", "unrecognised"])(
    "is refused when Sec-Fetch-Site is %s, which is not exactly same-origin",
    async (value) => {
      const answer = await send({ Host: WEB_HOST, Origin: `https://${WEB_HOST}`, "Sec-Fetch-Site": value });

      expect(refused(answer)).toBe(true);
    },
  );
});

describe("Sec-Fetch-Site, when the browser sends it", () => {
  /**
   * Stronger evidence is never rescued or overruled by weaker evidence. The
   * browser computes Sec-Fetch-Site against the URL it actually requested and
   * page script cannot set it, so Origin is not consulted at all.
   */
  it("decides alone, so same-origin is accepted beside an Origin from anywhere", async () => {
    const answer = await send({ Host: WEB_HOST, Origin: EVIL, "Sec-Fetch-Site": "same-origin" });

    expect(refused(answer)).toBe(false);
  });
});

describe("a request with no Sec-Fetch-Site, as an older browser sends", () => {
  it("is refused when its Origin does not match the Host it reached", async () => {
    const answer = await send({ Host: WEB_HOST, Origin: EVIL });

    expect(refused(answer)).toBe(true);
  });

  it("is accepted when its Origin matches the Host it reached", async () => {
    const answer = await send({ Host: WEB_HOST, Origin: `https://${WEB_HOST}` });

    expect(refused(answer)).toBe(false);
  });

  /**
   * Neither header means it is not a browser request that anyone could have
   * been made to forge. This is what keeps non-browser and bearer callers
   * working without the middleware inspecting how a caller authenticates.
   */
  it("is accepted when it carries neither header", async () => {
    const answer = await send({ Host: WEB_HOST });

    expect(refused(answer)).toBe(false);
  });

  it("is accepted when its Origin is blank, which carries no evidence", async () => {
    const answer = await send({ Host: WEB_HOST, Origin: "" });

    expect(refused(answer)).toBe(false);
  });
});

describe("a safe method", () => {
  it("is never refused, because it is not supposed to change anything", async () => {
    const answer = await send({ Host: WEB_HOST, Origin: EVIL, "Sec-Fetch-Site": "cross-site" }, "GET");

    expect(refused(answer)).toBe(false);
  });
});
