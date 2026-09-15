// Control: an API operation is the caller of the API client, and uses its module's schema.
import { client } from "@/shared/api/client";
import { createUserResponseSchema } from "../schemas/createUser";

export const createUser = () => [client.post("/api/users"), createUserResponseSchema];
