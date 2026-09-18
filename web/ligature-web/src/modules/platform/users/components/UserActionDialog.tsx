import { useState, type RefObject } from "react";
import { Textarea } from "@/components/ui/textarea";
import { ApiError } from "@/shared/api/errors";
import { ConfirmAction } from "@/shared/components/ConfirmAction";
import { FormField } from "@/shared/forms/FormField";
import {
  useDeactivateUser,
  useReactivateUser,
  useReissueActivationLink,
  useResetUserPassword,
  useSignOutUserEverywhere,
} from "../hooks/useUserActions";
import { reasonSchema, type UserRow } from "../schemas/users";

export type UserAction = "resend-activation" | "reset-password" | "sign-out-everywhere" | "deactivate" | "reactivate";

const UNKNOWN = "The action could not be completed. Try again.";

interface ActionCopy {
  readonly title: (name: string) => string;
  readonly description: string;
  readonly confirm: string;
  readonly busy: string;
  readonly done: (name: string) => string;
}

/**
 * What each action says, and what it announces when it succeeds. The wording
 * does not claim more than the server confirmed: a reset is INITIATED — the link
 * is mailed after the response — and the client cannot tell whether a row is
 * the caller, so sign-out says what happens if it is.
 */
const COPY: Record<UserAction, ActionCopy> = {
  "resend-activation": {
    title: (name) => `Resend activation link to ${name}`,
    description:
      "They'll be emailed a new link to activate their account. Any earlier activation link stops working. You won't see the link.",
    confirm: "Resend link",
    busy: "Sending…",
    done: (name) => `A new activation link has been issued for ${name}.`,
  },
  "reset-password": {
    title: (name) => `Reset password for ${name}`,
    description:
      "They'll be emailed a link to choose a new password. Any earlier reset link stops working. You won't see the link or their password.",
    confirm: "Reset password",
    busy: "Resetting…",
    done: (name) => `Password reset initiated for ${name}.`,
  },
  "sign-out-everywhere": {
    title: (name) => `Sign out ${name} everywhere`,
    description: "Ends every active session of this user. If this is your own account, you will be signed out too.",
    confirm: "Sign out everywhere",
    busy: "Signing out…",
    done: (name) => `All active sessions for ${name} have been signed out.`,
  },

  // USR-C4 / USR-C5 (U6). Neither is a toggle: the copy states the cascade,
  // and what reactivation does not bring back, before either is confirmed.
  // The self case is not mentioned: the server refuses it, and the refusal is
  // shown word for word (U4).
  deactivate: {
    title: (name) => `Deactivate ${name}`,
    description:
      "They'll be signed out everywhere and won't be able to sign in. All of their current and future roles are revoked, and reactivating them later will not restore any of them. Any activation or password-reset link they have stops working.",
    confirm: "Deactivate",
    busy: "Deactivating…",
    done: (name) => `${name} has been deactivated.`,
  },
  reactivate: {
    title: (name) => `Reactivate ${name}`,
    description:
      "They'll be able to sign in again, but they will have no roles: grant any access they need afresh. If they had activated their account, they sign in with their existing password. If they had not, send them a new activation link.",
    confirm: "Reactivate",
    busy: "Reactivating…",
    done: (name) => `${name} has been reactivated.`,
  },
};

interface UserActionDialogProps {
  readonly open: boolean;
  readonly user: UserRow;
  readonly action: UserAction;
  readonly returnFocus: RefObject<HTMLElement | null>;
  readonly onClose: () => void;

  /** Called once the dialog has finished closing. */
  readonly onClosed: () => void;

  /** Called with the announcement once the server has accepted the action. */
  readonly onDone: (announcement: string) => void;
}

/**
 * The confirmation for one row action, with the reason every one of them requires.
 *
 * Mounted per opening and kept mounted while it closes, so a reason or a
 * refusal from an earlier opening never carries over. A server 400 keeps the
 * dialog open with the message word for word (§7); success closes it and hands
 * the announcement to the page.
 */
export function UserActionDialog({ open, user, action, returnFocus, onClose, onClosed, onDone }: UserActionDialogProps) {
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | undefined>(undefined);

  const mutations = {
    "resend-activation": useReissueActivationLink(),
    "reset-password": useResetUserPassword(),
    "sign-out-everywhere": useSignOutUserEverywhere(),
    deactivate: useDeactivateUser(),
    reactivate: useReactivateUser(),
  };
  const mutation = mutations[action];

  const copy = COPY[action];

  function confirm() {
    if (mutation.isPending) {
      return;
    }

    setError(undefined);

    const parsed = reasonSchema.safeParse({ reason });

    if (!parsed.success) {
      setError(parsed.error.issues[0]?.message ?? UNKNOWN);
      return;
    }

    mutation.mutate(
      { userId: user.userId, reason: parsed.data.reason },
      {
        onSuccess: () => {
          onDone(copy.done(user.displayName));
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
      title={copy.title(user.displayName)}
      description={copy.description}
      confirmLabel={copy.confirm}
      busyLabel={copy.busy}
      busy={mutation.isPending}
      onConfirm={confirm}
      returnFocus={returnFocus}
    >
      <FormField
        id="user-action-reason"
        label="Reason"
        description="Recorded with the action in the audit trail."
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
