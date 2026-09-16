// Violation: the return path is attacker-influenced, and only shared/auth reads
// and validates it (docs/frontend-architecture.md §13).
export const destination = (state: { returnTo?: string }) => state.returnTo ?? "/";
