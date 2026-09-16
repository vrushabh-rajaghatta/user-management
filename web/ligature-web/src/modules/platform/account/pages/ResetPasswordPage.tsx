import { useEffect, useState, type ReactNode } from "react";
import { Link } from "react-router";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { SessionNotEndedError, SIGN_IN_PATH } from "@/modules/platform/auth";
import { ApiError } from "@/shared/api/errors";
import { Page } from "@/shared/components/Page";
import { FormField } from "@/shared/forms/FormField";
import { useResetPassword } from "../hooks/useResetPassword";

/**
 * CRD-C3 (docs/frontend-architecture.md §13).
 *
 * /reset-password is a BACKEND CONTRACT path, built by NotificationTemplates
 * for both the self-service reset and the administrator-initiated one.
 *
 * The fragment reading below is deliberately a SECOND COPY of the activation
 * page's, not a shared helper. §13 forbids one: it would hide the security
 * reasoning, and a single change to it would weaken both pages at once.
 */

const MISMATCH = "The passwords do not match.";

const NO_TOKEN = "This link is not valid. Ask for a new one from the sign-in page.";

const UNKNOWN = "The request could not be completed.";

/**
 * A successful reset does NOT revoke existing sessions: CRD-C3 writes the
 * credential, the token and the password history and clears the lockout, and
 * touches no session. Change-password does end other sessions, by A5, and the
 * difference is deliberate in the specification.
 *
 * So this says nothing about other sessions. Telling someone resetting a
 * compromised account that they had shut the attacker out would be false. That
 * improvement is parked as a backend change-control item.
 */
const CHANGED = "Your password has been changed. Sign in with your new password.";

function readTokenFromFragment(): string | null {
  const fragment = window.location.hash;

  if (fragment.length <= 1) {
    return null;
  }

  const tokens = new URLSearchParams(fragment.slice(1)).getAll("token");

  if (tokens.length !== 1) {
    return null;
  }

  const token = tokens[0];

  return token === undefined || token.trim().length === 0 ? null : token;
}

function messageFor(failure: unknown): string {
  if (failure instanceof SessionNotEndedError) {
    return failure.message;
  }

  return failure instanceof ApiError ? failure.message : UNKNOWN;
}

export function ResetPasswordPage() {
  const [token] = useState(readTokenFromFragment);
  const [password, setPassword] = useState("");
  const [confirmation, setConfirmation] = useState("");
  const [error, setError] = useState<string | undefined>(undefined);

  const reset = useResetPassword();

  useEffect(() => {
    window.history.replaceState(null, "", `${window.location.pathname}${window.location.search}`);
  }, []);

  function submit() {
    setError(undefined);

    if (token === null) {
      return;
    }

    if (password !== confirmation) {
      setError(MISMATCH);

      return;
    }

    reset.mutate(
      { token, newPassword: password },
      {
        onError: (failure: unknown) => {
          setError(messageFor(failure));
        },
      },
    );
  }

  let body: ReactNode;

  if (token === null) {
    body = (
      <p role="alert" className="text-sm">
        {NO_TOKEN}
      </p>
    );
  } else if (reset.isSuccess) {
    body = (
      <div className="flex flex-col gap-4">
        <p>{CHANGED}</p>
        <p className="text-sm">
          <Link to={SIGN_IN_PATH} className="underline underline-offset-4">
            Sign in
          </Link>
        </p>
      </div>
    );
  } else {
    body = (
      <form
        onSubmit={(event) => {
          event.preventDefault();
          submit();
        }} noValidate className="flex max-w-sm flex-col gap-4">
        <FormField id="new-password" label="New password" required>
          {(control) => (
            <Input
              {...control}
              type="password"
              autoComplete="new-password"
              value={password}
              onChange={(event) => {
                setPassword(event.target.value);
              }}
            />
          )}
        </FormField>

        <FormField id="confirm-new-password" label="Confirm new password" required>
          {(control) => (
            <Input
              {...control}
              type="password"
              autoComplete="new-password"
              value={confirmation}
              onChange={(event) => {
                setConfirmation(event.target.value);
              }}
            />
          )}
        </FormField>

        {error === undefined ? null : (
          <p role="alert" className="text-sm text-destructive">
            {error}
          </p>
        )}

        <Button type="submit" disabled={reset.isPending}>
          {reset.isPending ? "Setting..." : "Set new password"}
        </Button>
      </form>
    );
  }

  return <Page title="Choose a new password">{body}</Page>;
}
