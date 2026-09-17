import { api } from "@/shared/api/client";
import { usersPageSchema } from "../schemas/users";

/**
 * USR-Q1: GET /api/users. Requires a carrier and user.read.
 *
 * Only the page is sent. The page size is the server's default, and no other
 * parameter exists: the list has no filtering or sorting to ask for.
 */
export const listUsers = (page: number, signal?: AbortSignal) =>
  api.get(`/api/users?page=${String(page)}`, { response: usersPageSchema, signal });
