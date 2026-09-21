// Violation: shared/ reaches a vendored hook instead of using the primitive.
import { useIsMobile } from "@/hooks/use-mobile";

export const layout = useIsMobile;
