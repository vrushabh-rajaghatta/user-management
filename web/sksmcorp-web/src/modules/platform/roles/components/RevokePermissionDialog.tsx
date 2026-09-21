import { useState, type RefObject } from "react";
import { ApiError } from "@/shared/api/errors";
import { ConfirmAction } from "@/shared/components/ConfirmAction";
import { FormField } from "@/shared/forms/FormField";
import { Textarea } from "@/components/ui/textarea";
import { useRevokePermission } from "../hooks/useRoles";
import { roleReasonSchema } from "../schemas/updateRole";
import type { RolePermissions } from "../schemas/roles";

const UNKNOWN = "The permission could not be revoked. Try again.";

type Grant = RolePermissions["permissions"][number];

interface RevokePermissionDialogProps {
  readonly grant: Grant;
  readonly roleName: string;
  readonly open: boolean;
  readonly returnFocus: RefObject<HTMLElement | null>;
  readonly onClose: () => void;
  readonly onClosed: () => void;
  readonly onRevoked: (announcement: string) => void;
}

/**
 * AUT-C8's confirmation (RG9). The reason is required, as Revoke role's is,
 * and it reaches the audit record rather than the grant: the frozen entity has
 * no column for it.
 *
 * The grant is closed, not deleted, which the copy says — a reader should not
 * think history is being erased.
 */
export function RevokePermissionDialog({
  grant,
  roleName,
  open,
  returnFocus,
  onClose,
  onClosed,
  onRevoked,
}: RevokePermissionDialogProps) {
  const revoke = useRevokePermission();

  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | undefined>(undefined);

  function confirm() {
    if (revoke.isPending) {
      return;
    }

    setError(undefined);

    // An affordance only: the command refuses a blank reason itself.
    const parsed = roleReasonSchema.safeParse({ reason });

    if (!parsed.success) {
      setError(parsed.error.issues[0]?.message ?? UNKNOWN);

      return;
    }

    revoke.mutate(
      { rolePermissionId: grant.rolePermissionId, reason: parsed.data.reason },
      {
        onSuccess: () => {
          onRevoked(`Permission revoked: ${grant.code}.`);
          onClose();
        },
        onError: (failure: unknown) => {
          setError(failure instanceof ApiError ? failure.message : UNKNOWN);
        },
      },
    );
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
      title={`Revoke ${grant.code} from ${roleName}`}
      description="Everyone who holds this role loses it immediately. The grant is closed rather than deleted, so what the role could do, and when, stays answerable."
      confirmLabel="Revoke permission"
      busyLabel={"Revoking…"}
      busy={revoke.isPending}
      destructive
      onConfirm={confirm}
      returnFocus={returnFocus}
    >
      <FormField
        id="revoke-permission-reason"
        label="Reason"
        description="Recorded with the revocation and in the audit trail."
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
    </ConfirmAction>
  );
}
