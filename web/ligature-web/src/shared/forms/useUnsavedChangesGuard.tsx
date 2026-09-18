import { useEffect, useState, type ReactNode } from "react";
import { useBlocker } from "react-router";
import { ConfirmAction } from "@/shared/components/ConfirmAction";

/**
 * The unsaved-changes guard (docs/frontend-architecture.md §12, "Unsaved
 * changes"; USR-C2 UI, U2 and U7).
 *
 * TAKES A BOOLEAN, never a form library's object: an RHF form passes
 * `formState.isDirty && !saved`, a useState form its own comparison, and the
 * guard neither knows nor cares which.
 *
 * While dirty:
 *   - in-app navigation is held by React Router's useBlocker;
 *   - reloading or closing the tab is held by beforeunload, and the browser
 *     shows its own prompt;
 *   - `confirm(proceed)` — for a dialog's Cancel, Escape and close — asks
 *     first instead of proceeding.
 * Each asks "Discard changes?": Keep editing stays, Discard goes on. Clean,
 * everything proceeds at once and nothing is registered.
 */
export interface UnsavedChangesGuard {
  /** Runs `proceed` now when clean; asks "Discard changes?" first when dirty. */
  readonly confirm: (proceed: () => void) => void;

  /** The prompt. Rendered once by the form, inside its dialog if it has one. */
  readonly prompt: ReactNode;
}

export function useUnsavedChangesGuard(dirty: boolean): UnsavedChangesGuard {
  const blocker = useBlocker(dirty);
  const [pending, setPending] = useState<(() => void) | undefined>(undefined);

  useEffect(() => {
    if (!dirty) {
      return;
    }

    const hold = (event: BeforeUnloadEvent) => {
      event.preventDefault();
    };

    window.addEventListener("beforeunload", hold);

    return () => {
      window.removeEventListener("beforeunload", hold);
    };
  }, [dirty]);

  const blocked = blocker.state === "blocked";

  function keepEditing() {
    if (blocked) {
      blocker.reset();
    }

    setPending(undefined);
  }

  function discard() {
    if (blocked) {
      blocker.proceed();
      return;
    }

    const proceed = pending;
    setPending(undefined);
    proceed?.();
  }

  return {
    confirm: (proceed) => {
      if (dirty) {
        setPending(() => proceed);
      } else {
        proceed();
      }
    },
    prompt: (
      <ConfirmAction
        open={blocked || pending !== undefined}
        onOpenChange={(next) => {
          if (!next) {
            keepEditing();
          }
        }}
        title="Discard changes?"
        description="Your changes have not been saved."
        confirmLabel="Discard"
        busyLabel="Discarding…"
        cancelLabel="Keep editing"
        onConfirm={discard}
      />
    ),
  };
}
