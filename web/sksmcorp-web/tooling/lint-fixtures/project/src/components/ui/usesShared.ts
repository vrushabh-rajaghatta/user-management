// Violation: a vendored primitive imports shared/.
import { holds } from "@/shared/auth/checksPermissions";

export const primitive = holds;
