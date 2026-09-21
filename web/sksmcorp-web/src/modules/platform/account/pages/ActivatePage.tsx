import { useEffect, useState, type ReactNode } from "react";
import { Link } from "react-router";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { SessionNotEndedError, SIGN_IN_PATH } from "@/modules/platform/auth";
import { ApiError } from "@/shared/api/errors";
import { Page } from "@/shared/components/Page";
import { FormField } from "@/shared/forms/FormField";
import { useActivate } from "../hooks/useActivate";

/**
 * CRD-C1 (docs/frontend-architecture.md §13).
 *
 * /activate is a BACKEND CONTRACT path: NotificationTemplates builds
 * "{base}/activate#token={escaped}" and every link already sent points here.
 *
 * The fragment reading below is deliberately NOT shared with the reset page.
 * §13 forbids a generic token utility: a shared helper would hide the security
 * reasoning, and one change to it would weaken every page at once. The two
 * copies are each tested on their own.
 */

const MISMATCH = "The passwords do not match.";

const NO_TOKEN = "This link is not valid. Ask your administrator to send a new one.";

const UNKNOWN = "The request could not be completed.";

/**
 * Exactly one token, with a value. Duplicated, blank or absent is refused
 * rather than resolved by taking the first, which is what URLSearchParams.get
 * would do. The value is never trimmed: it is an opaque credential, and the
 * host percent-escapes it when building the link.
 */
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

export function ActivatePage() {
  // Read once, during the first render, before the effect below removes it.
  const [token] = useState(readTokenFromFragment);
  const [password, setPassword] = useState("");
  const [confirmation, setConfirmation] = useState("");
  const [error, setError] = useState<string | undefined>(undefined);

  const activateAccount = useActivate();

  // Out of the address bar and out of history straight away, whether or not a
  // token was found, and whatever happens to the request afterwards. The path
  // and query are kept; only the fragment goes.
  useEffect(() => {
    window.history.replaceState(null, "", `${window.location.pathname}${window.location.search}`);
  }, []);

  function submit() {
    setError(undefined);

    if (token === null) {
      return;
    }

    // A confirmation field guards against mistyping a value that cannot be read
    // back. It is not part of the request, and it is not a password rule: the
    // policy belongs to the tenant and stays the server's.
    if (password !== confirmation) {
      setError(MISMATCH);

      return;
    }

    activateAccount.mutate(
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
  } else if (activateAccount.isSuccess) {
    body = (
      <div className="flex flex-col gap-4">
        <p>Your account is ready.</p>
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

        <Button type="submit" disabled={activateAccount.isPending}>
          {activateAccount.isPending ? "Activating..." : "Activate account"}
        </Button>
      </form>
    );
  }

  return <Page title="Activate your account">{body}</Page>;
}
