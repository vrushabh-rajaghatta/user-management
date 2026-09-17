import type { ReactNode, RefObject } from "react";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";

interface ConfirmActionProps {
  readonly open: boolean;
  readonly onOpenChange: (open: boolean) => void;
  readonly title: string;
  readonly description?: ReactNode;

  /** The action, named: "Reset password", never "OK". */
  readonly confirmLabel: string;

  /** What is in progress: "Resetting…". */
  readonly busyLabel: string;

  /** While true, confirming and dismissing are both refused. */
  readonly busy?: boolean;

  readonly onConfirm: () => void;

  /**
   * Where focus goes when the dialog closes. A dialog opened from a menu has no
   * trigger of its own, so the caller names the element that should get focus
   * back. If that element has left the document meanwhile, focus is left where
   * it is rather than an error thrown.
   */
  readonly returnFocus?: RefObject<HTMLElement | null>;

  /**
   * Called once the dialog has finished closing, after any exit animation. The
   * page behind a modal dialog is hidden from assistive technology until then, so
   * an announcement made earlier can be missed; this is when to make it.
   */
  readonly onClosed?: () => void;

  /** Anything the confirmation needs besides its description, such as a reason field. */
  readonly children?: ReactNode;
}

/**
 * A confirmation before a consequential action (docs/frontend-architecture.md
 * §4, §15), built with its first user. The vendored Base UI dialog traps focus
 * and closes on Escape; this adds the named confirm, the busy state that makes a
 * second submission impossible, and focus returned to a named element.
 *
 * It does not submit anything. The caller runs the action in onConfirm and
 * decides whether the dialog closes — a refusal keeps it open so its message
 * can be read beside the action that failed (§7).
 */
export function ConfirmAction({
  open,
  onOpenChange,
  title,
  description,
  confirmLabel,
  busyLabel,
  busy = false,
  onConfirm,
  returnFocus,
  onClosed,
  children,
}: ConfirmActionProps) {
  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!busy) {
          onOpenChange(next);
        }
      }}
      onOpenChangeComplete={(isOpen) => {
        if (!isOpen) {
          onClosed?.();
        }
      }}
    >
      <DialogContent
        showCloseButton={false}
        finalFocus={() => {
          const target = returnFocus?.current;

          return target?.isConnected === true ? target : false;
        }}
      >
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          {description === undefined ? null : <DialogDescription>{description}</DialogDescription>}
        </DialogHeader>
        {children}
        <DialogFooter>
          <Button
            variant="outline"
            disabled={busy}
            onClick={() => {
              onOpenChange(false);
            }}
          >
            Cancel
          </Button>
          <Button
            disabled={busy}
            onClick={() => {
              if (!busy) {
                onConfirm();
              }
            }}
          >
            {busy ? busyLabel : confirmLabel}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
