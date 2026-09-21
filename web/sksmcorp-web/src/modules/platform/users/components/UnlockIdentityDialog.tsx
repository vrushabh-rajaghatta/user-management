import { useState, type RefObject } from "react";
import { Textarea } from "@/components/ui/textarea";
import { ApiError } from "@/shared/api/errors";
import { ConfirmAction } from "@/shared/components/ConfirmAction";
import { FormField } from "@/shared/forms/FormField";
import { useUnlockIdentity } from "../hooks/useUserIdentities";
import { reasonSchema } from "../schemas/users";

const UNKNOWN = "The action could not be completed. Try again.";

interface UnlockIdentityDialogProps {
  readonly open: boolean;
  readonly userId: string;
  readonly identityId: string;
  readonly username: string;
  readonly returnFocus: RefObject<HTMLElement | null>;
  readonly onClose: () => void;
  readonly onClosed: () => void;

  /** Called with the announcement once the server has unlocked the identity. */
  readonly onDone: (announcement: string) => void;
}

/**
 * CRD-C6 from the User detail page (docs/requirements.md, "IDN-Q1
 * GetUserIdentities and Unlock on the User detail page", I7): the existing
 * confirmation-with-reason pattern, and the reason is required.
 *
 * THE SERVER DECIDES. A refusal — the account cannot be unlocked, is not
 * currently locked, or is the administrator's own — keeps the dialog open with
 * the message word for word (§7), and the identities are read again either way
 * (useUnlockIdentity), so an expired lock stops being offered. Nothing here
 * branches on a message's text.
 */
export function UnlockIdentityDialog({
  open,
  userId,
  identityId,
  username,
  returnFocus,
  onClose,
  onClosed,
  onDone,
}: UnlockIdentityDialogProps) {
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | undefined>(undefined);
  const unlock = useUnlockIdentity(userId);

  function confirm() {
    if (unlock.isPending) {
      return;
    }

    setError(undefined);

    const parsed = reasonSchema.safeParse({ reason });

    if (!parsed.success) {
      setError(parsed.error.issues[0]?.message ?? UNKNOWN);
      return;
    }

    unlock.mutate(
      { identityId, reason: parsed.data.reason },
      {
        onSuccess: () => {
          onDone(`${username} was unlocked.`);
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
      title={`Unlock ${username}`}
      description="This clears the lock now, so the person can sign in again. Nothing else changes: not the password, and no session or token."
      confirmLabel="Unlock"
      busyLabel="Unlocking…"
      busy={unlock.isPending}
      onConfirm={confirm}
      returnFocus={returnFocus}
    >
      <FormField
        id="unlock-identity-reason"
        label="Reason"
        description="Recorded with the unlock in the audit trail."
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
