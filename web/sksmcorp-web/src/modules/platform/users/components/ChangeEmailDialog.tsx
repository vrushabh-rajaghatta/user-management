import { zodResolver } from "@hookform/resolvers/zod";
import { useEffect, useRef, useState, type RefObject } from "react";
import { useForm } from "react-hook-form";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { ApiError } from "@/shared/api/errors";
import { FormField } from "@/shared/forms/FormField";
import { useUnsavedChangesGuard } from "@/shared/forms/useUnsavedChangesGuard";
import { useChangeUserEmail } from "../hooks/useChangeUserEmail";
import { changeEmailFormSchema, type ChangeEmailForm } from "../schemas/changeUserEmail";
import type { UserRow } from "../schemas/users";

const UNKNOWN = "The email address could not be changed. Try again.";

/** CE12: guidance only. The command issues nothing; Resend activation does (CE7). */
const ACTIVATION_GUIDANCE =
  "Their current activation link will stop working. Use Resend activation to send a new one to the new address.";

interface ChangeEmailDialogProps {
  readonly open: boolean;
  readonly user: UserRow;
  readonly returnFocus: RefObject<HTMLElement | null>;
  readonly onClose: () => void;

  /** Called once the dialog has finished closing. */
  readonly onClosed: () => void;

  /** Called with the announcement once the server has accepted the change. */
  readonly onSaved: (announcement: string) => void;
}

/**
 * USR-C3's Change email dialog (docs/requirements.md, "USR-C3 ChangeUserEmail",
 * The UI). Built as Edit profile is: React Hook Form, the unsaved-changes guard
 * on every close path, Zod for presence only, and the server's refusal word
 * for word. The current address comes from the row the caller chose.
 */
export function ChangeEmailDialog({ open, user, returnFocus, onClose, onClosed, onSaved }: ChangeEmailDialogProps) {
  // The editor's guard: Escape and the close button go through it, as Cancel does.
  const guardRef = useRef<((proceed: () => void) => void) | undefined>(undefined);

  function close() {
    if (guardRef.current === undefined) {
      onClose();
    } else {
      guardRef.current(onClose);
    }
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next) {
          close();
        }
      }}
      onOpenChangeComplete={(isOpen) => {
        if (!isOpen) {
          onClosed();
        }
      }}
    >
      <DialogContent
        className="sm:max-w-lg"
        finalFocus={() => {
          const target = returnFocus.current;

          return target?.isConnected === true ? target : false;
        }}
      >
        <DialogHeader>
          <DialogTitle>Change email for {user.displayName}</DialogTitle>
          <DialogDescription>
            The change takes effect at once. Links already sent to the current address stop working.
          </DialogDescription>
        </DialogHeader>

        <EmailEditor
          user={user}
          onCancel={close}
          guardRef={guardRef}
          onSaved={(announcement) => {
            onSaved(announcement);
            onClose();
          }}
        />
      </DialogContent>
    </Dialog>
  );
}

interface EmailEditorProps {
  readonly user: UserRow;
  readonly onCancel: () => void;
  readonly guardRef: RefObject<((proceed: () => void) => void) | undefined>;
  readonly onSaved: (announcement: string) => void;
}

function EmailEditor({ user, onCancel, guardRef, onSaved }: EmailEditorProps) {
  const save = useChangeUserEmail(user.userId);
  const [refusal, setRefusal] = useState<string | undefined>(undefined);

  const form = useForm<ChangeEmailForm>({
    resolver: zodResolver(changeEmailFormSchema),
    defaultValues: { email: "", reason: "" },
  });

  const guard = useUnsavedChangesGuard(form.formState.isDirty && !save.isSuccess);

  // The dialog's close paths use this guard too, always the current one.
  useEffect(() => {
    guardRef.current = guard.confirm;

    return () => {
      guardRef.current = undefined;
    };
  });

  const errors = form.formState.errors;

  const submit = form.handleSubmit((values) => {
    if (save.isPending) {
      return;
    }

    setRefusal(undefined);

    save.mutate(
      { userId: user.userId, ...values },
      {
        onSuccess: () => {
          onSaved(`Email changed for ${user.displayName}.`);
        },
        onError: (failure: unknown) => {
          setRefusal(failure instanceof ApiError ? failure.message : UNKNOWN);
        },
      },
    );
  });

  return (
    <form
      noValidate
      className="flex flex-col gap-4"
      onSubmit={(event) => {
        void submit(event);
      }}
    >
      {refusal === undefined ? null : (
        <p role="alert" className="text-sm text-destructive">
          {refusal}
        </p>
      )}

      <p className="text-sm">
        Current email: <span className="font-medium">{user.email ?? "none"}</span>
      </p>

      {user.activationPending ? <p className="text-sm text-muted-foreground">{ACTIVATION_GUIDANCE}</p> : null}

      {/* Text with an email keyboard, NOT type="email": the browser sanitises an
          email input's value (surrounding whitespace is stripped), and CE12
          sends exactly what was typed. The server owns the address rules. */}
      <FormField id="change-email-address" label="New email" error={errors.email?.message} required>
        {(control) => <Input {...control} {...form.register("email")} inputMode="email" autoComplete="off" />}
      </FormField>

      <FormField id="change-email-reason" label="Reason (optional)">
        {(control) => <Textarea {...control} {...form.register("reason")} />}
      </FormField>

      <DialogFooter>
        <Button type="button" variant="outline" disabled={save.isPending} onClick={onCancel}>
          Cancel
        </Button>
        <Button type="submit" disabled={save.isPending}>
          {save.isPending ? "Saving…" : "Save"}
        </Button>
      </DialogFooter>

      {guard.prompt}
    </form>
  );
}
