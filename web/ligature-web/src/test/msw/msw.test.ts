import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { server } from "./server";

/**
 * The web test project's network boundary. The same request is made twice: once
 * with no handler, once with one. The second passing is what shows the first
 * failed because MSW refused it, and not because the request could never have
 * worked.
 */
describe("the web test network boundary", () => {
  const probe = new URL("/api/probe", window.location.origin).toString();

  it("fails a request that no handler answers instead of letting it reach the network", async () => {
    const failure: unknown = await fetch(probe, { method: "POST" }).then(
      () => undefined,
      (error: unknown) => error,
    );

    expect(failure).toBeInstanceOf(Error);
    expect(describeError(failure)).toMatch(/onUnhandledRequest/);
  });

  it("answers the same request once a test declares a handler for it", async () => {
    server.use(http.post(probe, () => new HttpResponse(null, { status: 204 })));

    const response = await fetch(probe, { method: "POST" });

    expect(response.status).toBe(204);
  });
});

/** The error's message and every cause beneath it, so the reason can be matched. */
function describeError(error: unknown): string {
  const messages: string[] = [];

  for (let current = error; current instanceof Error; current = current.cause) {
    messages.push(current.message);
  }

  return messages.join(" | ");
}
