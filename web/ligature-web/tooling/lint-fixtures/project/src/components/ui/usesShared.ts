// Violation: a vendored primitive imports shared/.
import { readHint } from "@/shared/auth/SessionHintSource";

export const primitive = readHint;
