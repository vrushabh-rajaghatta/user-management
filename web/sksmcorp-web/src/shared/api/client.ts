import { z } from "zod";
import { ApiContractError, ApiError } from "./errors";

/**
 * The HTTP operation boundary (docs/frontend-architecture.md §6).
 *
 * Feature code reaches the backend only through here. The boundary owns
 * relative same-origin paths, credential transport, JSON, the response
 * contract, normalised errors, unauthorized reporting and aborts.
 *
 * It does NOT own authentication state. It reports a 401 to whoever registered
 * with onUnauthorized — AuthProvider — and changes nothing itself, which is why
 * this file never imports shared/auth.
 */

export type HttpMethod = "GET" | "POST" | "PUT" | "PATCH" | "DELETE";

/**
 * "report": a 401 is evidence the session has ended, and is reported.
 * "return": a 401 is an expected answer (sign-in, activation, reset, sign-out),
 * and is only returned to the caller.
 */
export type UnauthorizedMode = "report" | "return";

export interface RequestOptions {
  readonly body?: unknown;
  readonly unauthorized?: UnauthorizedMode;
  readonly signal?: AbortSignal;
}

export interface ResponseOptions<T> extends RequestOptions {
  /** The operation returns a JSON body that must match this schema. */
  readonly response: z.ZodType<T>;
}

/** Shown when a failure's body cannot be read: never the raw response. */
const UNREADABLE_ERROR = "The request could not be completed.";

const UNREACHABLE = "The server could not be reached.";

/** The host's uniform error body. */
const errorBody = z.object({ error: z.string().min(1) });

let unauthorizedHandler: (() => void) | undefined;

/**
 * Registers the single listener for reported 401s and returns its removal.
 * A second registration is a defect — two listeners would mean two opinions
 * about the session — so it throws rather than replacing the first.
 */
export function onUnauthorized(handler: () => void): () => void {
  if (unauthorizedHandler !== undefined) {
    throw new Error("An unauthorized handler is already registered; the API boundary accepts exactly one.");
  }

  unauthorizedHandler = handler;

  return () => {
    if (unauthorizedHandler === handler) {
      unauthorizedHandler = undefined;
    }
  };
}

function request<T>(method: HttpMethod, path: string, options: ResponseOptions<T>): Promise<T>;
function request(method: HttpMethod, path: string, options?: RequestOptions): Promise<undefined>;
function request<T>(method: HttpMethod, path: string, options: Partial<ResponseOptions<T>> = {}): Promise<T | undefined> {
  return send(method, path, options);
}

async function send<T>(method: HttpMethod, path: string, options: Partial<ResponseOptions<T>>): Promise<T | undefined> {
  const url = sameOriginUrl(path);

  if (options.body !== undefined && (method === "GET" || method === "DELETE")) {
    throw new Error(`A ${method} request cannot carry a body (${path}).`);
  }

  const init: RequestInit = { method, credentials: "same-origin", signal: options.signal ?? null };

  if (options.body !== undefined) {
    init.headers = { "Content-Type": "application/json" };
    init.body = JSON.stringify(options.body);
  }

  let response: Response;
  let text: string;

  try {
    response = await fetch(url, init);
    text = await response.text();
  } catch (error) {
    if (isAbort(error)) {
      throw error;
    }

    throw new ApiError("network", null, UNREACHABLE);
  }

  if (!response.ok) {
    const failure = new ApiError("http", response.status, readErrorMessage(text));

    if (response.status === 401 && (options.unauthorized ?? "report") === "report") {
      unauthorizedHandler?.();
    }

    throw failure;
  }

  return readBody(method, path, response.status, text, options.response);
}

/**
 * Only a path on this origin. Checked before and after resolution: a path that
 * does not start with a single "/" is refused, and so is anything that still
 * resolves elsewhere (a backslash is read as a slash by the URL parser).
 */
function sameOriginUrl(path: string): URL {
  const refused = new Error(`API requests take a relative path beginning with "/"; ${path} is not one.`);

  if (!path.startsWith("/") || path.startsWith("//")) {
    throw refused;
  }

  const url = new URL(path, window.location.origin);

  if (url.origin !== window.location.origin) {
    throw refused;
  }

  return url;
}

function readBody<T>(method: HttpMethod, path: string, status: number, text: string, schema: z.ZodType<T> | undefined): T | undefined {
  if (schema === undefined) {
    if (text.length > 0) {
      throw new ApiContractError(method, path, ["the response has a body, but the operation declares none"]);
    }

    return undefined;
  }

  // Status and body are separate rules (docs/frontend-architecture.md §6), so a
  // violation says which one was broken. A 204 has no body by definition, but it
  // is refused here for what it is — the wrong kind of response — rather than
  // only for the body it lacks.
  if (status === 204) {
    throw new ApiContractError(method, path, ["the operation declares a JSON response body, but the response is 204 No Content"]);
  }

  if (text.length === 0) {
    throw new ApiContractError(method, path, ["the operation declares a JSON response body, but the response has no body"]);
  }

  let json: unknown;

  try {
    json = JSON.parse(text);
  } catch {
    throw new ApiContractError(method, path, ["the response body is not JSON"]);
  }

  const parsed = schema.safeParse(json);

  if (!parsed.success) {
    throw new ApiContractError(
      method,
      path,
      parsed.error.issues.map((issue) => `${issue.path.map(String).join(".") || "(body)"}: ${issue.message}`),
    );
  }

  return parsed.data;
}

function readErrorMessage(text: string): string {
  try {
    const parsed = errorBody.safeParse(JSON.parse(text));

    return parsed.success ? parsed.data.error : UNREADABLE_ERROR;
  } catch {
    return UNREADABLE_ERROR;
  }
}

function isAbort(error: unknown): boolean {
  return error instanceof Error && error.name === "AbortError";
}

interface MethodHelper {
  <T>(path: string, options: ResponseOptions<T>): Promise<T>;
  (path: string, options?: RequestOptions): Promise<undefined>;
}

function helper(method: HttpMethod): MethodHelper {
  return ((path: string, options?: Partial<ResponseOptions<unknown>>) => send(method, path, options ?? {})) as MethodHelper;
}

export const api = {
  request,
  get: helper("GET"),
  post: helper("POST"),
  put: helper("PUT"),
  patch: helper("PATCH"),
  delete: helper("DELETE"),
};
