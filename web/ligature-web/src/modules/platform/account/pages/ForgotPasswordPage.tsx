import { useState } from "react";
import { Link } from "react-router";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { SIGN_IN_PATH } from "@/modules/platform/auth";
import { ApiError } from "@/shared/api/errors";
import { Page } from "@/shared/components/Page";
import { FormField } from "@/shared/forms/FormField";
import { useRequestPasswordReset } from "../hooks/useRequestPasswordReset";

/**
 * CRD-C2, whose whole design is that it reveals nothing.
 *
 * The host answers 200 with one fixed sentence whatever was typed — there is no
 * 400 and no 404 — so the response cannot be used to discover which addresses
 * or usernames are registered. This page keeps that property: it shows the
 * host's sentence and nothing derived from what was entered, and it does not
 * navigate, because where it went would itself be an answer.
 *
 * It ends no session first: it establishes no identity and is refused by
 * nothing, so signing out here would end the session of someone who only
 * mistyped their own address.
 */

const UNKNOWN = "The request could not be completed.";

export function ForgotPasswordPage() {
  const [emailOrUsername, setEmailOrUsername] = useState("");
  const [error, setError] = useState<string | undefined>(undefined);

  const request = useRequestPasswordReset();

  function submit() {
    setError(undefined);

    request.mutate(emailOrUsername, {
      onError: (failure: unknown) => {
        setError(failure instanceof ApiError ? failure.message : UNKNOWN);
      },
    });
  }

  return (
    <Page title="Forgot your password?">
      {request.isSuccess ? (
        // The host's own sentence, word for word.
        <p>{request.data.message}</p>
      ) : (
        <form
        onSubmit={(event) => {
          event.preventDefault();
          submit();
        }} noValidate className="flex max-w-sm flex-col gap-4">
          <FormField
            id="email-or-username"
            label="Email address or username"
            description="We will send a reset link if an account matches."
            required
          >
            {(control) => (
              <Input
                {...control}
                autoComplete="username"
                value={emailOrUsername}
                onChange={(event) => {
                  setEmailOrUsername(event.target.value);
                }}
              />
            )}
          </FormField>

          {error === undefined ? null : (
            <p role="alert" className="text-sm text-destructive">
              {error}
            </p>
          )}

          <Button type="submit" disabled={request.isPending}>
            {request.isPending ? "Sending..." : "Send reset link"}
          </Button>
        </form>
      )}

      <p className="text-sm">
        <Link to={SIGN_IN_PATH} className="underline underline-offset-4">
          Sign in
        </Link>
      </p>
    </Page>
  );
}
