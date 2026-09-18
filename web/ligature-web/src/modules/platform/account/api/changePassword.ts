import { api } from "@/shared/api/client";

export interface ChangePasswordRequest {
  readonly currentPassword: string;
  readonly newPassword: string;
}

/**
 * CRD-C4: POST /api/account/change-password, for the account whose session
 * presents the request. 204 with no body. A wrong current password, a locked
 * account and a session the command cannot act for share ONE 400, so the
 * endpoint cannot be used to probe the current password; a policy or reuse
 * refusal is also 400. On success every OTHER session of this identity ends and
 * this one stays (A5).
 *
 * Exactly the two passwords, as typed. The confirmation never leaves the
 * browser.
 *
 * "report", the default: a 401 means this session has ended.
 */
export const changePassword = (request: ChangePasswordRequest) =>
  api.post("/api/account/change-password", {
    body: { currentPassword: request.currentPassword, newPassword: request.newPassword },
  });
