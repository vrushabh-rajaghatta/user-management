import { useState, type RefObject } from "react";
import { ApiError } from "@/shared/api/errors";
import { ConfirmAction } from "@/shared/components/ConfirmAction";
import { FormField } from "@/shared/forms/FormField";
import { Textarea } from "@/components/ui/textarea";
import { useDeactivateRole, useReactivateRole } from "../hooks/useRoles";
import { roleReasonSchema } from "../schemas/updateRole";
import type { Role } from "../schemas/roles";

const UNKNOWN = "The role could not be saved. Try again.";

interface RoleLifecycleDialogProps {
  readonly role: Role;
  readonly open: boolean;
  readonly returnFocus: RefObject<HTMLElement | null>;
  readonly onClose: () => void;
  readonly onClosed: () => void;
  readonly onDone: (announcement: string) => void;
}

/**
 * AUT-C5/C6's confirmation (docs/requirements.md, "AUT-C5 DeactivateRole and
 * AUT-C6 ReactivateRole", RD8), built on ConfirmAction as Revoke role and
 * Deactivate user are.
 *
 * WHICH ACTION IS OFFERED IS THE ROLE'S STATE, never a choice: an active role
 * can only be deactivated, an inactive one only reactivated.
 *
 * THE COPY IS THE CONTRACT. Deactivating a role stops NEW assignments and
 * takes nobody's access away — role.IsActive is deliberately absent from the
 * authorisation predicate, and two regression tests pin that. So the
 * confirmation says exactly this, and never suggests holders lose anything
 * (RD3).
 */
export function RoleLifecycleDialog({
  role,
  open,
  returnFocus,
  onClose,
  onClosed,
  onDone,
}: RoleLifecycleDialogProps) {
  const deactivating = role.isActive;

  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | undefined>(undefined);

  const deactivate = useDeactivateRole(role.roleId);
  const reactivate = useReactivateRole(role.roleId);

  const busy = deactivate.isPending || reactivate.isPending;

  function confirm() {
    if (busy) {
      return;
    }

    setError(undefined);

    if (!deactivating) {
      reactivate.mutate(undefined, {
        onSuccess: (saved) => {
          onDone(`Role reactivated: ${saved.name}.`);
          onClose();
        },
        onError: fail,
      });

      return;
    }

    // An affordance only: the command refuses a blank reason itself, and this
    // saves the round trip (the users module's rule, reused).
    const parsed = roleReasonSchema.safeParse({ reason });

    if (!parsed.success) {
      setError(parsed.error.issues[0]?.message ?? UNKNOWN);

      return;
    }

    deactivate.mutate(parsed.data.reason, {
      onSuccess: (saved) => {
        onDone(`Role deactivated: ${saved.name}.`);
        onClose();
      },
      onError: fail,
    });
  }

  function fail(failure: unknown) {
    setError(failure instanceof ApiError ? failure.message : UNKNOWN);
  }

  return (
    <ConfirmAction
      open={open}
      onOpenChange={(next) => {
        if (!next) {
          onClose();
        }
      }}
      onClosed={onClosed}
      title={deactivating ? `Deactivate ${role.name}` : `Reactivate ${role.name}`}
      description={deactivating ? <DeactivationEffect role={role} /> : reactivationEffect}
      confirmLabel={deactivating ? "Deactivate" : "Reactivate"}
      busyLabel={deactivating ? "Deactivating\u2026" : "Reactivating\u2026"}
      busy={busy}
      destructive={deactivating}
      onConfirm={confirm}
      returnFocus={returnFocus}
    >
      {deactivating ? (
        <FormField
          id="role-lifecycle-reason"
          label="Reason"
          description="Recorded with the deactivation and in the audit trail."
          error={error}
          required
        >
          {(control) => (
            <Textarea
              {...control}
              value={reason}
              onChange={(event) => {
                setReason(event.target.value);
              }}
            />
          )}
        </FormField>
      ) : error === undefined ? null : (
        <p role="alert" className="text-sm text-destructive">
          {error}
        </p>
      )}
    </ConfirmAction>
  );
}

/**
 * The whole point, said plainly. The holder count is the one AUT-Q5 already
 * derives (RD2); with no holders the sentence about them is simply absent.
 */
function DeactivationEffect({ role }: { readonly role: Role }) {
  const holders = role.activeHolderCount;

  return (
    <>
      {holders === 0 ? null : (
        <>
          This role has{" "}
          <strong>
            {holders} active {holders === 1 ? "holder" : "holders"}
          </strong>
          .{" "}
        </>
      )}
      Deactivating it will prevent new assignments, but existing holders will keep their current access. The role is
      not deleted, and it can be reactivated later.
    </>
  );
}

const reactivationEffect =
  "The role can be assigned again. Existing holders were never affected by its deactivation.";
