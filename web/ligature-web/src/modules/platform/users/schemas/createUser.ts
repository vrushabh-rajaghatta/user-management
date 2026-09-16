import { z } from "zod";

/**
 * An affordance, not a rule (docs/frontend-architecture.md §12). It saves a
 * round trip; the command stays the authority.
 *
 * Presence only. There is deliberately NO email format check: the server's own
 * address rules are looser than any pattern we would reach for here, and a
 * schema that rejected input the server would accept is exactly what §12
 * forbids. The input's type="email" is a keyboard and autofill affordance, and
 * the form does not let the browser block submission on it either.
 */
export const createUserSchema = z.object({
  firstName: z.string().trim().min(1, "A first name is required."),
  lastName: z.string().trim().min(1, "A last name is required."),
  displayName: z.string().trim().min(1, "A display name is required."),
  email: z.string().trim().min(1, "An email address is required."),
  initialUsername: z.string().trim().min(1, "A username is required."),
});

export type CreateUserRequest = z.infer<typeof createUserSchema>;

/**
 * USR-C1 answers 201 with both identifiers. Declared because the response
 * carries a body and §6 requires a schema whenever one does — not because
 * anything is shown: neither identifier means anything to the person reading
 * the page, and there is no read endpoint to use them against.
 */
export const createdUserSchema = z.object({
  userId: z.string().min(1),
  userIdentityId: z.string().min(1),
});
