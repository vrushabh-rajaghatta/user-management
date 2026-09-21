import { z } from "zod";

/**
 * The New role form (docs/requirements.md, "AUT-C3 CreateRole", RC7): PRESENCE
 * ONLY. The server owns the code rule, the name rule and uniqueness, and its
 * refusal is shown word for word; the form never trims what it sends.
 */
export const createRoleFormSchema = z.object({
  name: z.string().min(1, "A name is required."),
  code: z.string().min(1, "A code is required."),
  description: z.string(),
});

export type CreateRoleForm = z.infer<typeof createRoleFormSchema>;

/** AUT-C3's answer: the created role, as stored (RC4). */
export const createdRoleSchema = z.object({
  roleId: z.string().min(1),
  code: z.string().min(1),
  name: z.string().min(1),
  description: z.string().nullable(),
  isSystemRole: z.boolean(),
  isActive: z.boolean(),
});
