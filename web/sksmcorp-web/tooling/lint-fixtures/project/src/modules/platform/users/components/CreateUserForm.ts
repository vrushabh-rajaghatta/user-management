// Violation: a component imports an API operation instead of a hook.
import { createUser } from "../api/createUser";

export const submit = createUser;
