import { api } from "@/shared/api/client";
import { resetRequestedSchema } from "../schemas/account";

/**
 * CRD-C2. One outcome, deliberately: always 200 with the same body, whether or
 * not an account matches, so the response cannot be used to discover which
 * addresses or usernames are registered.
 *
 * Whether the value is an address or a username is not the caller's to declare:
 * usernames are unconstrained labels and may look exactly like an address, so
 * the host tries both.
 */
export const requestPasswordReset = (emailOrUsername: string) =>
  api.post("/api/account/password-reset-request", {
    body: { emailOrUsername },
    response: resetRequestedSchema,
    unauthorized: "return",
  });
