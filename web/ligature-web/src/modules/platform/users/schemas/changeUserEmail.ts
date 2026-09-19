import { z } from "zod";

/**
 * The Change email form (docs/requirements.md, "USR-C3 ChangeUserEmail",
 * CE12): PRESENCE ONLY. The server trims, checks and compares the address;
 * copying its rules here could refuse an address it accepts (§12), so the form
 * never does. The reason is optional and never checked.
 */
export const changeEmailFormSchema = z.object({
  email: z.string().min(1, "An email address is required."),
  reason: z.string(),
});

export type ChangeEmailForm = z.infer<typeof changeEmailFormSchema>;
