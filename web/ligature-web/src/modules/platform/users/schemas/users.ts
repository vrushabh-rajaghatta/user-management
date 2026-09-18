import { z } from "zod";

/**
 * USR-Q1's response (docs/requirements.md): exactly userId, displayName and
 * email per row, and page, pageSize and hasMore. No total.
 *
 * email is nullable because the column is. userId is the only identifier; the
 * name and email are for people to read, never to key or match on (P4).
 */
export const usersPageSchema = z.object({
  users: z.array(
    z.object({
      userId: z.string().min(1),
      displayName: z.string(),
      email: z.string().nullable(),
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
