import { z } from "zod";

/**
 * IDN-Q1's response, as amended (docs/requirements.md, "IDN-Q1
 * GetUserIdentities and Unlock on the User detail page"): exactly these eight
 * fields per identity, oldest first.
 *
 * `locked` is the SERVER's verdict at the moment of the read — a lock in force
 * — and `lockedUntil` is set only when it is true. The client never compares
 * instants to decide whether an identity is locked.
 */
export const userIdentitySchema = z.object({
  userIdentityId: z.string().min(1),
  type: z.enum(["Local", "External"]),
  provider: z.string(),
  username: z.string().nullable(),
  status: z.enum(["Active", "Inactive"]),
  deactivatedAt: z.string().nullable(),
  locked: z.boolean(),
  lockedUntil: z.string().nullable(),
});

export const userIdentitiesSchema = z.object({ identities: z.array(userIdentitySchema) });

export type UserIdentity = z.infer<typeof userIdentitySchema>;
