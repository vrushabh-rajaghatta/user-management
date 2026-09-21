// Violation: the form presentation primitives know nothing of schemas, form
// libraries or API errors (docs/frontend-architecture.md §12).
import { z } from "zod";

export const schema = z.object({ username: z.string() });
