import type { HttpMethod } from "./client";

/**
 * A request the server refused, or one that never reached it
 * (docs/frontend-architecture.md §7).
 *
 * The message is safe to show: it is either the server's own single sentence
 * from its uniform error body, or fixed client text. Logic branches on status
 * and kind, never on the message.
 */
export class ApiError extends Error {
  readonly kind: "http" | "network";

  /** The HTTP status, or null when the request never reached the server. */
  readonly status: number | null;

  constructor(kind: "http" | "network", status: number | null, message: string) {
    super(message);
    this.name = "ApiError";
    this.kind = kind;
    this.status = status;
  }
}

/**
 * A response that broke its operation's contract (docs/frontend-architecture.md
 * §6) — a defect between client and server, never something a user did.
 *
 * The issues describe what is wrong with the shape of the response. They never
 * repeat the response itself, which could carry a credential.
 */
export class ApiContractError extends Error {
  readonly method: HttpMethod;

  readonly path: string;

  readonly issues: readonly string[];

  constructor(method: HttpMethod, path: string, issues: readonly string[]) {
    super(`${method} ${path} broke its response contract: ${issues.join("; ")}`);
    this.name = "ApiContractError";
    this.method = method;
    this.path = path;
    this.issues = issues;
  }
}
