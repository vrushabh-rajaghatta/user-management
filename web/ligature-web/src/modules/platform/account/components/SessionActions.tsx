import { useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { useSignOutEverywhere } from "@/modules/platform/auth";
import { ApiError } from "@/shared/api/errors";
import { ConfirmAction } from "@/shared/components/ConfirmAction";

const UNKNOWN = "The request could not be completed.";

const OTHERS_ENDED = "Your other sessions have been signed out.";

type Choice = "others" | "everywhere";

/** Each action as the frozen contract words it (M7). */
const ACTIONS: Record<Choice, { label: string; title: string; description: string; keepCurrentSession: boolean }> = {
  others: {
    label: "Sign out other sessions",
    title: "Sign out other sessions?",
    description: "Every other session on your account will be signed out. You stay signed in here.",
    keepCurrentSession: true,
  },
  everywhere: {
    label: "Sign out everywhere",
    title: "Sign out everywhere?",
    description:
      "Every session on your account will be signed out, including this one. You will need to sign in again.",
    keepCurrentSession: false,
  },
};

/**
 * SES-C4's self form on My account (M7–M9), through the auth module's hook:
 * session lifecycle is that module's (M3).
 *
 * Both actions are confirmed. Neither sends a reason (M8). A failure keeps the
 * confirmation open with the message beside the action that failed, and
 * changes nothing about authentication (M9): what was revoked is not known.
 * A 401 is left to the application, which signs the caller out.
 *
 * No list of sessions: that needs SES-Q2, which is not built.
 */
export function SessionActions() {
  const signOut = useSignOutEverywhere();
  const [open, setOpen] = useState<Choice | undefined>(undefined);
  const [error, setError] = useState<string | undefined>(undefined);
  const [announcement, setAnnouncement] = useState("");

  // Set on success, announced once the confirmation has closed: until then
  // the page behind the modal is hidden from assistive technology.
  const pendingAnnouncement = useRef<string | undefined>(undefined);

  const triggers = {
    others: useRef<HTMLButtonElement>(null),
    everywhere: useRef<HTMLButtonElement>(null),
  };

  // The last one opened, so its text stays put while the dialog animates out.
  const [shown, setShown] = useState<Choice>("others");
  const action = ACTIONS[shown];

  function start(choice: Choice) {
    setError(undefined);
    setAnnouncement("");
    setShown(choice);
    setOpen(choice);
  }

  function confirm() {
    if (signOut.isPending) {
      return;
    }

    setError(undefined);

    signOut.mutate(
      { keepCurrentSession: action.keepCurrentSession },
      {
        onSuccess: () => {
          if (action.keepCurrentSession) {
            pendingAnnouncement.current = OTHERS_ENDED;
            setOpen(undefined);
          }
        },
        onError: (failure: unknown) => {
          if (failure instanceof ApiError && failure.status === 401) {
            return;
          }

          setError(failure instanceof ApiError ? failure.message : UNKNOWN);
        },
      },
    );
  }

  return (
    <div className="flex flex-col gap-4">
      <p className="text-sm text-muted-foreground">
        Sign out other sessions ends every session on your account except this one. Sign out everywhere ends this
        one too.
      </p>

      <p role="status" className="text-sm empty:hidden">
        {announcement}
      </p>

      <div className="flex flex-wrap gap-2">
        {(["others", "everywhere"] as const).map((choice) => (
          <Button
            key={choice}
            ref={triggers[choice]}
            variant="outline"
            onClick={() => {
              start(choice);
            }}
          >
            {ACTIONS[choice].label}
          </Button>
        ))}
      </div>

      <ConfirmAction
        open={open !== undefined}
        onOpenChange={(next) => {
          if (!next) {
            setOpen(undefined);
          }
        }}
        title={action.title}
        description={action.description}
        confirmLabel={action.label}
        busyLabel="Signing out…"
        busy={signOut.isPending}
        returnFocus={triggers[shown]}
        onConfirm={confirm}
        onClosed={() => {
          if (pendingAnnouncement.current !== undefined) {
            setAnnouncement(pendingAnnouncement.current);
            pendingAnnouncement.current = undefined;
          }
        }}
      >
        {error === undefined ? null : (
          <p role="alert" className="text-sm text-destructive">
            {error}
          </p>
        )}
      </ConfirmAction>
    </div>
  );
}
