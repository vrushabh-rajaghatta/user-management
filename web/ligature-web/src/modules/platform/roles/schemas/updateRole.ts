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

/**
 * An administrator's reason for retiring a role (AUT-C5). An affordance
 * (docs/frontend-architecture.md §12): the command refuses a blank reason
 * itself, and this only saves the round trip.
 *
 * The users module has its own identical copy. Modules do not import each
 * other (the boundaries rule), and the shared form primitives are forbidden
 * to know about schemas at all (§12), so three lines are duplicated rather
 * than a new shared layer invented for them. The sentence matches the
 * server's family deliberately.
 */
export const roleReasonSchema = z.object({
  reason: z.string().trim().min(1, "A reason is required."),
});
