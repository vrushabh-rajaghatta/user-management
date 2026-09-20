import { z } from "zod";

/**
 * The Edit role form (docs/requirements.md, "AUT-C4 UpdateRoleMetadata", RM7):
 * PRESENCE ONLY, as New role is. The server owns the name and description
 * rules, its refusal is shown word for word, and the form never trims what it
 * sends.
 *
 * NO CODE (RM2). It is shown as context on the dialog, never as a field.
 */
export const updateRoleFormSchema = z.object({
  name: z.string().min(1, "A name is required."),
  description: z.string(),
});

export type UpdateRoleForm = z.infer<typeof updateRoleFormSchema>;
