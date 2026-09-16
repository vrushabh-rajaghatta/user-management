// Violation: app/ imports a module internal instead of its routes.
import { page } from "@/modules/platform/users/pages/CreateUserPage";

export const route = page;
