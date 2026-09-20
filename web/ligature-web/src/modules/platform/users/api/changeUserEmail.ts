import { api } from "@/shared/api/client";
import type { ChangeEmailForm } from "../schemas/changeUserEmail";

/**
 * USR-C3: POST /api/users/{userId}/email, user.update. Sends exactly what was
 * typed; an empty reason is omitted rather than sent. 204 with no body, change
 * or not.
 */
export const changeUserEmail = ({ userId, email, reason }: { userId: string } & ChangeEmailForm) =>
  api.post(`/api/users/${encodeURIComponent(userId)}/email`, {
    body: reason === "" ? { email } : { email, reason },
  });
