import { z } from "zod";

/**
 * USR-Q2's response (docs/requirements.md): exactly userId, displayName, email
 * and activationPending per row, and page, pageSize and hasMore. No total.
 *
 * email is nullable because the column is. userId is the only identifier; the
 * name and email are for people to read, never to key or match on (P4).
 *
 * activationPending (amendment 1) decides which actions a row offers and
 * nothing else. False means only "not pending": never read it as "activated",
 * "has a password" or "can sign in".
 *
 * status (amendment 2) is the stored lifecycle status, "Active" or "Inactive",
 * and is independent of activationPending: all four combinations occur. It
 * decides the Inactive marker and which actions a row offers — an affordance,
 * never authorization. Any other value is a contract error, not a guess.
 */
export const usersPageSchema = z.object({
  users: z.array(
    z.object({
      userId: z.string().min(1),
      displayName: z.string(),
      email: z.string().nullable(),
      activationPending: z.boolean(),
      status: z.enum(["Active", "Inactive"]),
    }),
  ),
  page: z.number().int().positive(),
  pageSize: z.number().int().positive(),
  hasMore: z.boolean(),
});

export type UsersPage = z.infer<typeof usersPageSchema>;

export type UserRow = UsersPage["users"][number];

/**
 * The administrator's reason for a row action. An affordance (§12): the command
 * refuses a blank reason itself, and this only saves the round trip.
 */
export const reasonSchema = z.object({
  reason: z.string().trim().min(1, "A reason is required."),
});
