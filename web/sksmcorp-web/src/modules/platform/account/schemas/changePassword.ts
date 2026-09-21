import { z } from "zod";

export const PASSWORDS_DO_NOT_MATCH = "The passwords do not match.";

/**
 * The Change password form (My account, M4): PRESENCE, and the confirmation
 * matching, and nothing else. The password policy belongs to the tenant and can
 * change, so it is never copied here (docs/frontend-architecture.md §12); the
 * server's refusal is shown word for word. Nothing is trimmed.
 *
 * The confirmation is a client affordance against mistyping a value the person
 * cannot read back. It is not part of the request, so checking it does not
 * reject anything the server would accept (§12, W3).
 */
export const changePasswordFormSchema = z
  .object({
    currentPassword: z.string().min(1, "The current password is required."),
    newPassword: z.string().min(1, "A new password is required."),
    confirmation: z.string().min(1, "The confirmation is required."),
  })
  .refine((form) => form.newPassword === form.confirmation, {
    message: PASSWORDS_DO_NOT_MATCH,
    path: ["confirmation"],
  });

export type ChangePasswordForm = z.infer<typeof changePasswordFormSchema>;
