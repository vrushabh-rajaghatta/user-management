import { api } from "@/shared/api/client";
import { createdUserSchema, type CreateUserRequest } from "../schemas/createUser";

/**
 * USR-C1 over HTTP. Requires a carrier and the 'user.create' permission.
 *
 * The default "report" applies to its 401, deliberately, and this is the
 * OPPOSITE of the authentication flows: there a 401 answered a question about
 * the credentials presented, so it was returned. Here the caller already had an
 * established session, so a 401 is evidence that session has ended, and
 * AuthProvider should act on it.
 *
 * A caller without the permission receives 400 — the same status a validation
 * failure gets, from the same exception type. Nothing here tries to tell them
 * apart.
 */
export const createUser = (request: CreateUserRequest) =>
  api.post("/api/users", { body: request, response: createdUserSchema });
