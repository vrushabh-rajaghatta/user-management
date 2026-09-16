// Violation: a platform module imports a business module.
import { submissions } from "@/modules/regulatory/submissions";

export const upward = submissions;
