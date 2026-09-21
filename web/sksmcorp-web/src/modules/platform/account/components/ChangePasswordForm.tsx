import { zodResolver } from "@hookform/resolvers/zod";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { ApiError } from "@/shared/api/errors";
import { FormField } from "@/shared/forms/FormField";
import { useUnsavedChangesGuard } from "@/shared/forms/useUnsavedChangesGuard";
import { useChangePassword } from "../hooks/useChangePassword";
import { changePasswordFormSchema, type ChangePasswordForm as FormValues } from "../schemas/changePassword";

const EMPTY: FormValues = { currentPassword: "", newPassword: "", confirmation: "" };

const UNKNOWN = "The request could not be completed.";

/**
 * A5, as the catalogue asks for it: "with clear messaging". The 204 does not
 * say whether another session existed, so the sentence is true either way.
 */
const CHANGED =
  "Your password has been changed. Any other sessions on this account have been signed out; you remain signed in here.";

/**
 * CRD-C4's form (docs/requirements.md, "CRD-C4 and SES-C4 (self) — the My
 * account page", M4–M6).
 *
 * React Hook Form starting EMPTY, so isDirty is "any field is non-empty" —
 * exactly M6's rule — and the shared guard needs nothing more. Zod checks
 * presence and the confirmation only; the request carries the two passwords
 * exactly as typed.
 *
 * On 204 the fields are cleared (no password stays in state longer than it
 * must), which also turns the guard off. A refusal keeps what was typed, so it
 * can be corrected, and is shown word for word. A 401 is not handled here: the
 * API boundary reports it and the application signs the caller out.
 */
export function ChangePasswordForm() {
  const change = useChangePassword();
  const [refusal, setRefusal] = useState<string | undefined>(undefined);
  const [announcement, setAnnouncement] = useState("");

  const form = useForm<FormValues>({ resolver: zodResolver(changePasswordFormSchema), defaultValues: EMPTY });

  const guard = useUnsavedChangesGuard(form.formState.isDirty);

  const errors = form.formState.errors;

  const submit = form.handleSubmit((values) => {
    if (change.isPending) {
      return;
    }

    setRefusal(undefined);
    setAnnouncement("");

    change.mutate(
      { currentPassword: values.currentPassword, newPassword: values.newPassword },
      {
        onSuccess: () => {
          form.reset(EMPTY);
          setAnnouncement(CHANGED);
        },
        onError: (failure: unknown) => {
          if (failure instanceof ApiError && failure.status === 401) {
            return;
          }

          setRefusal(failure instanceof ApiError ? failure.message : UNKNOWN);
        },
      },
    );
  });

  return (
    <form
      noValidate
      className="flex max-w-md flex-col gap-4"
      onSubmit={(event) => {
        void submit(event);
      }}
    >
      <p role="status" className="text-sm empty:hidden">
        {announcement}
      </p>

      {refusal === undefined ? null : (
        <p role="alert" className="text-sm text-destructive">
          {refusal}
        </p>
      )}

      <FormField id="current-password" label="Current password" error={errors.currentPassword?.message} required>
        {(control) => (
          <Input {...control} {...form.register("currentPassword")} type="password" autoComplete="current-password" />
        )}
      </FormField>

      <FormField id="new-password" label="New password" error={errors.newPassword?.message} required>
        {(control) => <Input {...control} {...form.register("newPassword")} type="password" autoComplete="new-password" />}
      </FormField>

      <FormField id="confirm-new-password" label="Confirm new password" error={errors.confirmation?.message} required>
        {(control) => <Input {...control} {...form.register("confirmation")} type="password" autoComplete="new-password" />}
      </FormField>

      <div>
        <Button type="submit" disabled={change.isPending}>
          {change.isPending ? "Changing…" : "Change password"}
        </Button>
      </div>

      {guard.prompt}
    </form>
  );
}
