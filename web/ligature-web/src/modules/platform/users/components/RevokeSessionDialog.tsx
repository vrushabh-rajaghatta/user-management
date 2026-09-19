import { useState, type RefObject } from "react";
import { Textarea } from "@/components/ui/textarea";
import { ApiError } from "@/shared/api/errors";
import { ConfirmAction } from "@/shared/components/ConfirmAction";
import { FormField } from "@/shared/forms/FormField";
import { useRevokeSession } from "../hooks/useUserSessions";
import { reasonSchema } from "../schemas/users";

const UNKNOWN = "The action could not be completed. Try again.";

interface RevokeSessionDialogProps {
  readonly open: boolean;
  readonly userId: string;
  readonly sessionId: string;
  readonly returnFocus: RefObject<HTMLElement | null>;
  readonly onClose: () => void;
  readonly onClosed: () => void;

  /** Called with the announcement once the server has accepted the revocation. */
  readonly onDone: (announcement: string) => void;
}

/**
 * SES-C3 from the User detail page (docs/requirements.md, "SES-Q1
 * GetActiveSessions and Revoke on the User detail page", SS7): the existing
 * confirmation-with-reason pattern, and the reason is required.
 *
 * THE SERVER DECIDES. A refusal keeps the dialog open with the message word
 * for word (§7), and the sessions are read again either way
 * (useRevokeSession). Nothing here branches on a message's text.
 */
export function RevokeSessionDialog({
  open,
  userId,
  sessionId,
  returnFocus,
  onClose,
  onClosed,
  onDone,
}: RevokeSessionDialogProps) {
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | undefined>(undefined);
  const revoke = useRevokeSession(userId);

  function confirm() {
    if (revoke.isPending) {
      return;
    }

    setError(undefined);

    const parsed = reasonSchema.safeParse({ reason });

    if (!parsed.success) {
      setError(parsed.error.issues[0]?.message ?? UNKNOWN);
      return;
    }

    revoke.mutate(
      { sessionId, reason: parsed.data.reason },
      {
        onSuccess: () => {
          onDone("The session was revoked.");
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
      title="Revoke session"
      description="This signs the person out on that browser now. Their other sessions, password and roles are unchanged."
      confirmLabel="Revoke"
      busyLabel="Revoking…"
      busy={revoke.isPending}
      onConfirm={confirm}
      returnFocus={returnFocus}
    >
      <FormField
        id="revoke-session-reason"
        label="Reason"
        description="Recorded with the revocation in the audit trail."
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
