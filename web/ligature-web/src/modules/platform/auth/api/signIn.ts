import { api } from "@/shared/api/client";

export interface SignInCredentials {
  readonly username: string;
  readonly password: string;
}

/**
 * SES-C1 over HTTP. Success is 204 with no body: the carrier arrives as the
 * __Host-ligature cookie, which script cannot read, so there is nothing to
 * parse and no response schema to declare.
 *
 * "return", because a 401 here is an answer about the credentials presented,
 * not evidence that an established session has ended
 * (docs/frontend-architecture.md §6).
 */
export const signIn = (credentials: SignInCredentials) =>
  api.post("/api/auth/sign-in", { body: credentials, unauthorized: "return" });
