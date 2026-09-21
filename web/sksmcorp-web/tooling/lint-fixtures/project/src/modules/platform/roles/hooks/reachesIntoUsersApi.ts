// Violation: another module's API operation, through a relative path.
import { createUser } from "../../users/api/createUser";

export const borrowed = createUser;
