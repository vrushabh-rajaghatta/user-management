// Violation: another module's internal file, through the alias.
import { useCreateUser } from "@/modules/platform/users/hooks/useCreateUser";

export const borrowed = useCreateUser;
