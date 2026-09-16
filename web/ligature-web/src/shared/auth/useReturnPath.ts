import { useLocation } from "react-router";
import { validateReturnPath } from "./validateReturnPath";

/**
 * Where to go after signing in: the page that sent the visitor here, or the
 * home page (docs/frontend-architecture.md §13).
 *
 * Reading the value and validating it live together, in shared/auth, so no
 * feature ever holds an unvalidated return path. Lint enforces that.
 *
 * Deliberately narrow. It knows about the return path and nothing else — it is
 * not a place for tokens or other URL handling, which §13 keeps in the page
 * that needs them, in plain sight.
 */
export function useReturnPath(): string {
  // Typed `any` by the router, so it is narrowed to unknown before it is read:
  // this value is attacker-influenced and nothing may be assumed about it.
  const state: unknown = useLocation().state;

  return validateReturnPath((state as { returnTo?: unknown } | null)?.returnTo);
}
