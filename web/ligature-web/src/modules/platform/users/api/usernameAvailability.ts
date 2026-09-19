import { api } from "@/shared/api/client";
import { usernameAvailabilitySchema } from "../schemas/usernameAvailability";

/**
 * IDN-Q3: POST /api/identities/username-availability, identity.read. A read,
 * carried in the body so the typed username never reaches a request URL (UN1).
 */
export const checkUsernameAvailability = (username: string) =>
  api.post("/api/identities/username-availability", {
    body: { username },
    response: usernameAvailabilitySchema,
  });
