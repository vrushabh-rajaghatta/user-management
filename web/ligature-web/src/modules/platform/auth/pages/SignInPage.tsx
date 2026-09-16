import { useState } from "react";
import { Link, useNavigate } from "react-router";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { ApiError } from "@/shared/api/errors";
import { useReturnPath } from "@/shared/auth/useReturnPath";
import { Page } from "@/shared/components/Page";
import { FormField } from "@/shared/forms/FormField";
import { useSignIn } from "../hooks/useSignIn";
import { SessionNotEndedError } from "../hooks/useEndSession";

/**
 * SES-C1 (docs/frontend-architecture.md §7, §13).
 *
 * The host answers every failed sign-in with one 401 and one sentence —
 * unknown user, wrong password, locked, inactive, and a request that already
 * presents a live session are deliberately indistinguishable. That sentence is
 * written for an API caller, so this page shows its own fixed text instead.
 * The decision is made on the STATUS; no message is ever interpreted.
 */

const REJECTED = "Invalid username or password.";

const UNKNOWN = "The request could not be completed.";

function messageFor(failure: unknown): string {
  // The session could not be ended, so nothing was submitted.
  if (failure instanceof SessionNotEndedError) {
    return failure.message;
  }

  if (failure instanceof ApiError) {
    return failure.status === 401 ? REJECTED : failure.message;
  }

  return UNKNOWN;
}

export function SignInPage() {
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | undefined>(undefined);

  const returnTo = useReturnPath();
  const navigate = useNavigate();
  const signIn = useSignIn();

  function submit() {
    setError(undefined);

    signIn.mutate(
      { username, password },
      {
        onSuccess: () => {
          void navigate(returnTo, { replace: true });
        },
        onError: (failure: unknown) => {
          setError(messageFor(failure));
        },
      },
    );
  }

  return (
    <Page title="Sign in">
      <form
        onSubmit={(event) => {
          event.preventDefault();
          submit();
        }} noValidate className="flex max-w-sm flex-col gap-4">
        <FormField id="username" label="Username" required>
          {(control) => (
            <Input
              {...control}
              autoComplete="username"
              value={username}
              onChange={(event) => {
                setUsername(event.target.value);
              }}
            />
          )}
        </FormField>

        <FormField id="password" label="Password" required>
          {(control) => (
            <Input
              {...control}
              type="password"
              autoComplete="current-password"
              value={password}
              onChange={(event) => {
                setPassword(event.target.value);
              }}
            />
          )}
        </FormField>

        {error === undefined ? null : (
          <p role="alert" className="text-sm text-destructive">
            {error}
          </p>
        )}

        <Button type="submit" disabled={signIn.isPending}>
          {signIn.isPending ? "Signing in..." : "Sign in"}
        </Button>
      </form>

      <p className="text-sm">
        <Link to="/forgot-password" className="underline underline-offset-4">
          Forgot your password?
        </Link>
      </p>
    </Page>
  );
}
