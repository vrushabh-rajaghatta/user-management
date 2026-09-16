import { api } from "@/shared/api/client";
import { accountChangedSchema } from "../schemas/account";

export interface ResetPasswordRequest {
  readonly token: string;
  readonly newPassword: string;
}

/**
 * CRD-C3. An invalid, expired, consumed or unparseable token all return the
 * same 400, which is what stops this being an oracle for which tokens exist. A
 * password the policy refuses is also 400, and leaves the token usable.
 */
export const resetPassword = (request: ResetPasswordRequest) =>
  api.post("/api/account/reset-password", { body: request, response: accountChangedSchema, unauthorized: "return" });
