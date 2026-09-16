// Control: app/ composes a module through its routes and its public surface.
import { users } from "@/modules/platform/users";
import { userRoutes } from "@/modules/platform/users/routes";

export const router = [users, userRoutes];
