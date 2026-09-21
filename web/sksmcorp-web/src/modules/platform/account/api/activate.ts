import { api } from "@/shared/api/client";
import { accountChangedSchema } from "../schemas/account";

export interface ActivateRequest {
  readonly token: string;
  readonly newPassword: string;
}

/**
 * CRD-C1. Anonymous: the emailed token IS the authorisation, because holding it
 * proves control of the mailbox.
 *
 * "return", because a 401 here means a session is already established — an
 * answer about this request, not evidence that some other session ended.
 */
export const activate = (request: ActivateRequest) =>
  api.post("/api/account/activate", { body: request, response: accountChangedSchema, unauthorized: "return" });
