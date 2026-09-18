import type { ReactNode } from "react";

/**
 * docs/frontend-architecture.md §12, "Unsaved changes". Stub — not yet
 * implemented: never blocks, never prompts.
 */
export interface UnsavedChangesGuard {
  /** Runs `proceed` now when clean; asks "Discard changes?" first when dirty. */
  readonly confirm: (proceed: () => void) => void;

  /** The prompt, rendered once by the form. */
  readonly prompt: ReactNode;
}

export function useUnsavedChangesGuard(dirty: boolean): UnsavedChangesGuard {
  // Stub: the flag is accepted and ignored.
  if (dirty) {
    // never blocks
  }

  return {
    confirm: (proceed) => {
      proceed();
    },
    prompt: null,
  };
}
