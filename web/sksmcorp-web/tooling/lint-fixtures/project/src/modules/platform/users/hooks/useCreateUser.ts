// Control: a hook calls its own module's API operation.
import { createUser } from "../api/createUser";

export const useCreateUser = () => createUser;
