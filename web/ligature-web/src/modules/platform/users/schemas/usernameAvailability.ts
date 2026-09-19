import { z } from "zod";

/**
 * IDN-Q3's answer (docs/requirements.md, "IDN-Q3 CheckUsernameAvailable on
 * Create user"): whether USR-C1 would accept this username now. Nothing else —
 * never who holds it.
 */
export const usernameAvailabilitySchema = z.object({ available: z.boolean() });
