import { delay, http, HttpResponse } from "msw";
import { afterEach, describe, expect, it } from "vitest";
import { z } from "zod";
import { server } from "@/test/msw/server";
import { api, onUnauthorized, type HttpMethod } from "./client";
import { ApiContractError, ApiError } from "./errors";

const at = (path: string) => new URL(path, window.location.origin).href;

const created = z.object({ userId: z.string(), userIdentityId: z.string() });

let unregister: (() => void) | undefined;

afterEach(() => {
  unregister?.();
  unregister = undefined;
});

function report(handler: () => void): void {
  unregister = onUnauthorized(handler);
}

/** The rejection of a request that must fail; a request that succeeds fails the test. */
function failure(promise: Promise<unknown>): Promise<unknown> {
  return promise.then(
    () => {
      throw new Error("Expected the request to fail.");
    },
    (error: unknown) => error,
  );
}

describe("the HTTP operation boundary", () => {
  it.each([
    ["GET", "get"],
    ["POST", "post"],
    ["PUT", "put"],
    ["PATCH", "patch"],
    ["DELETE", "delete"],
  ] as const)("sends a %s request through api.%s", async (method, helper) => {
    let seen: string | undefined;

    server.use(
      http.all(at("/api/probe"), ({ request }) => {
        seen = request.method;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    await api[helper]("/api/probe");

    expect(seen).toBe(method);
  });

  it("sends a JSON body and parses the JSON response through api.request", async () => {
    let received: unknown;
    let contentType: string | null = null;

    server.use(
      http.post(at("/api/users"), async ({ request }) => {
        received = await request.json();
        contentType = request.headers.get("content-type");
        return HttpResponse.json({ userId: "u1", userIdentityId: "i1" }, { status: 201 });
      }),
    );

    const result = await api.request("POST" satisfies HttpMethod, "/api/users", {
      body: { firstName: "Ada" },
      response: created,
    });

    expect(result).toEqual({ userId: "u1", userIdentityId: "i1" });
    expect(received).toEqual({ firstName: "Ada" });
    expect(contentType).toBe("application/json");
  });

  it("sends no content type when there is no body", async () => {
    let contentType: string | null = "unset";

    server.use(
      http.post(at("/api/auth/sign-out"), ({ request }) => {
        contentType = request.headers.get("content-type");
        return new HttpResponse(null, { status: 204 });
      }),
    );

    await api.post("/api/auth/sign-out");

    expect(contentType).toBeNull();
  });

  it("carries same-origin credentials and never sets an Authorization header", async () => {
    let credentials: RequestCredentials | undefined;
    let authorization: string | null = "unset";

    server.use(
      http.post(at("/api/probe"), ({ request }) => {
        credentials = request.credentials;
        authorization = request.headers.get("authorization");
        return new HttpResponse(null, { status: 204 });
      }),
    );

    await api.post("/api/probe");

    expect(credentials).toBe("same-origin");
    expect(authorization).toBeNull();
  });

  it.each([
    ["an absolute URL", "https://evil.example/api/users"],
    ["a protocol-relative URL", "//evil.example/api/users"],
    ["a path without a leading slash", "api/users"],
  ])("refuses %s without sending anything", async (_, path) => {
    let requests = 0;

    server.use(
      http.all("*", () => {
        requests += 1;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const error = await failure(api.post(path));

    expect(error).toBeInstanceOf(Error);
    expect(String(error)).toMatch(/relative path/);
    expect(requests).toBe(0);
  });

  it.each(["get", "delete"] as const)("refuses a body on api.%s without sending anything", async (helper) => {
    let requests = 0;

    server.use(
      http.all("*", () => {
        requests += 1;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const error = await failure(api[helper]("/api/probe", { body: { unexpected: true } }));

    expect(String(error)).toMatch(/body/);
    expect(requests).toBe(0);
  });
});

describe("the response contract", () => {
  it("returns the parsed body when it matches the declared schema", async () => {
    server.use(http.post(at("/api/users"), () => HttpResponse.json({ userId: "u1", userIdentityId: "i1" }, { status: 201 })));

    await expect(api.post("/api/users", { response: created })).resolves.toEqual({ userId: "u1", userIdentityId: "i1" });
  });

  it("treats a 204 as a violation when a response schema is declared", async () => {
    server.use(http.post(at("/api/users"), () => new HttpResponse(null, { status: 204 })));

    const error = await failure(api.post("/api/users", { response: created }));

    expect(error).toBeInstanceOf(ApiContractError);
    expect((error as ApiContractError).method).toBe("POST");
    expect((error as ApiContractError).path).toBe("/api/users");

    // Refused as a 204 — the wrong kind of response — not merely as a missing body.
    expect((error as ApiContractError).issues.join(" | ")).toMatch(/204 No Content/);
  });

  it("treats a missing body as a violation when a response schema is declared", async () => {
    server.use(http.post(at("/api/users"), () => new HttpResponse("", { status: 200 })));

    const error = await failure(api.post("/api/users", { response: created }));

    expect(error).toBeInstanceOf(ApiContractError);
    expect((error as ApiContractError).issues.join(" | ")).toMatch(/has no body/);
  });

  it("treats a body that is not JSON as a violation when a response schema is declared", async () => {
    server.use(http.post(at("/api/users"), () => new HttpResponse("<html>not json</html>", { status: 200 })));

    expect(await failure(api.post("/api/users", { response: created }))).toBeInstanceOf(ApiContractError);
  });

  it("names the field that does not match the declared schema", async () => {
    server.use(http.post(at("/api/users"), () => HttpResponse.json({ userId: "u1" }, { status: 201 })));

    const error = await failure(api.post("/api/users", { response: created }));

    expect(error).toBeInstanceOf(ApiContractError);
    expect((error as ApiContractError).issues.join(" | ")).toMatch(/userIdentityId/);
  });

  it("accepts a 204 when no response schema is declared", async () => {
    server.use(http.post(at("/api/auth/sign-out"), () => new HttpResponse(null, { status: 204 })));

    await expect(api.post("/api/auth/sign-out")).resolves.toBeUndefined();
  });

  it("treats any body as a violation when no response schema is declared", async () => {
    server.use(http.post(at("/api/auth/sign-in"), () => HttpResponse.json({ accessToken: "carrier" })));

    const error = await failure(api.post("/api/auth/sign-in"));

    expect(error).toBeInstanceOf(ApiContractError);
    expect(String((error as ApiContractError).issues)).not.toMatch(/carrier/);
  });
});

describe("normalised errors", () => {
  it("reports a 400 with the server's message word for word", async () => {
    server.use(http.post(at("/api/users"), () => HttpResponse.json({ error: "An email address is required." }, { status: 400 })));

    const error = await failure(api.post("/api/users", { response: created }));

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).kind).toBe("http");
    expect((error as ApiError).status).toBe(400);
    expect((error as ApiError).message).toBe("An email address is required.");
  });

  it.each([403, 500])("reports a %i as an error without an unauthorized report", async (status) => {
    let reports = 0;
    report(() => {
      reports += 1;
    });

    server.use(http.post(at("/api/probe"), () => HttpResponse.json({ error: "Refused." }, { status })));

    const error = await failure(api.post("/api/probe"));

    expect((error as ApiError).status).toBe(status);
    expect((error as ApiError).message).toBe("Refused.");
    expect(reports).toBe(0);
  });

  it.each([
    ["is not JSON", () => new HttpResponse("<html>at Ligature.Host secret stack</html>", { status: 500 })],
    ["has the wrong shape", () => HttpResponse.json({ message: "secret detail" }, { status: 500 })],
  ])("shows a fixed message, never the raw response, when an error body %s", async (_, respond) => {
    server.use(http.post(at("/api/probe"), respond));

    const error = await failure(api.post("/api/probe"));

    expect((error as ApiError).status).toBe(500);
    expect((error as ApiError).message).toBe("The request could not be completed.");
  });

  it("reports a network failure as a network error", async () => {
    server.use(http.post(at("/api/probe"), () => HttpResponse.error()));

    const error = await failure(api.post("/api/probe"));

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).kind).toBe("network");
    expect((error as ApiError).status).toBeNull();
    expect((error as ApiError).message).toBe("The server could not be reached.");
  });
});

describe("unauthorized reporting", () => {
  const unauthorized = () => HttpResponse.json({ error: "Authentication is required." }, { status: 401 });

  it("reports a 401 once by default, and the caller still receives the error", async () => {
    let reports = 0;
    report(() => {
      reports += 1;
    });

    server.use(http.post(at("/api/probe"), unauthorized));

    const error = await failure(api.post("/api/probe"));

    expect((error as ApiError).status).toBe(401);
    expect(reports).toBe(1);
  });

  it("reports a 401 once when the operation declares report", async () => {
    let reports = 0;
    report(() => {
      reports += 1;
    });

    server.use(http.post(at("/api/probe"), unauthorized));

    await failure(api.post("/api/probe", { unauthorized: "report" }));

    expect(reports).toBe(1);
  });

  it("does not report a 401 when the operation declares return, and the caller still receives the error", async () => {
    let reports = 0;
    report(() => {
      reports += 1;
    });

    server.use(http.post(at("/api/auth/sign-in"), unauthorized));

    const error = await failure(api.post("/api/auth/sign-in", { body: {}, unauthorized: "return" }));

    expect((error as ApiError).status).toBe(401);
    expect(reports).toBe(0);
  });

  it("still returns the error when nothing is listening", async () => {
    server.use(http.post(at("/api/probe"), unauthorized));

    expect(((await failure(api.post("/api/probe"))) as ApiError).status).toBe(401);
  });

  it("holds exactly one registration", () => {
    report(() => undefined);

    expect(() => onUnauthorized(() => undefined)).toThrow(/already/);
  });

  it("accepts a new registration once the previous one is removed", () => {
    const first = onUnauthorized(() => undefined);
    first();

    expect(() => {
      unregister = onUnauthorized(() => undefined);
    }).not.toThrow();
  });
});

describe("aborts", () => {
  it("rethrows the abort untouched and never reports it", async () => {
    let reports = 0;
    report(() => {
      reports += 1;
    });

    server.use(
      http.post(at("/api/slow"), async () => {
        await delay("infinite");
        return new HttpResponse(null, { status: 401 });
      }),
    );

    const controller = new AbortController();
    const pending = failure(api.post("/api/slow", { signal: controller.signal }));

    controller.abort();

    const error = await pending;

    expect((error as Error).name).toBe("AbortError");
    expect(error).not.toBeInstanceOf(ApiError);
    expect(reports).toBe(0);
  });
});
