import { zodResolver } from "@hookform/resolvers/zod";
import { useEffect, useRef, useState, type RefObject } from "react";
import { useForm } from "react-hook-form";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import { ApiError } from "@/shared/api/errors";
import { ErrorState } from "@/shared/components/ErrorState";
import { FormField } from "@/shared/forms/FormField";
import { useUnsavedChangesGuard } from "@/shared/forms/useUnsavedChangesGuard";
import { useUpdateUserProfile, useUserProfile } from "../hooks/useUserProfile";
import { profileFormSchema, type ProfileForm, type UserProfile } from "../schemas/userProfile";
import type { UserRow } from "../schemas/users";

const UNKNOWN = "The profile could not be saved. Try again.";

interface EditProfileDialogProps {
  readonly open: boolean;
  readonly user: UserRow;
  readonly returnFocus: RefObject<HTMLElement | null>;
  readonly onClose: () => void;

  /** Called once the dialog has finished closing. */
  readonly onClosed: () => void;

  /** Called with the announcement once the server has accepted the save. */
  readonly onSaved: (announcement: string) => void;
}

/**
 * USR-C2's Edit profile dialog (docs/requirements.md, "USR-C2 — the Edit
 * profile UI"): the web client's first EDITING form, and so the first built
 * with React Hook Form (W3: "the library arrives with that form").
 *
 * GetUser → React Hook Form with the returned values as its defaults (what
 * reset(serverValues) establishes) → formState.isDirty → the unsaved-changes
 * guard → Zod, presence only → POST .../profile. The server trims, validates
 * and stores; this form never repeats those rules and never trims what it sends.
 */
export function EditProfileDialog({ open, user, returnFocus, onClose, onClosed, onSaved }: EditProfileDialogProps) {
  const profile = useUserProfile(user.userId);

  // The editor's guard, once there is an editor: Escape and the close button
  // go through it, as Cancel does. Until the form exists there is nothing to
  // lose, so closing proceeds.
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
          <DialogTitle>Edit profile for {user.displayName}</DialogTitle>
          <DialogDescription>Their email address and status are not changed here.</DialogDescription>
        </DialogHeader>

        {profile.data !== undefined ? (
          <ProfileEditor
            loaded={profile.data}
            onCancel={close}
            guardRef={guardRef}
            onSaved={(announcement) => {
              onSaved(announcement);
              onClose();
            }}
          />
        ) : profile.isError ? (
          <ErrorState
            message={profile.error instanceof ApiError ? profile.error.message : "The profile could not be loaded."}
            onRetry={() => {
              void profile.refetch();
            }}
          />
        ) : (
          <div role="group" aria-label="Loading profile" aria-busy="true" className="flex flex-col gap-3">
            {[0, 1, 2].map((row) => (
              <Skeleton key={row} className="h-9 w-full" />
            ))}
          </div>
        )}
      </DialogContent>
    </Dialog>
  );
}

interface ProfileEditorProps {
  readonly loaded: UserProfile;
  readonly onCancel: () => void;
  readonly guardRef: RefObject<((proceed: () => void) => void) | undefined>;
  readonly onSaved: (announcement: string) => void;
}

/**
 * Mounted only once GetUser has answered, so the server's values are the
 * form's defaults from the first render and isDirty means "differs from what
 * the server returned".
 */
function ProfileEditor({ loaded, onCancel, guardRef, onSaved }: ProfileEditorProps) {
  const save = useUpdateUserProfile(loaded.userId);
  const [refusal, setRefusal] = useState<string | undefined>(undefined);

  const form = useForm<ProfileForm>({
    resolver: zodResolver(profileFormSchema),
    defaultValues: { firstName: loaded.firstName, lastName: loaded.lastName, displayName: loaded.displayName },
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
      { userId: loaded.userId, ...values },
      {
        onSuccess: () => {
          // The announcement names the display name as the person will see it;
          // the list, re-read, shows the stored form.
          onSaved(`Profile saved for ${values.displayName.trim()}.`);
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

      <FormField id="profile-first-name" label="First name" error={errors.firstName?.message} required>
        {(control) => <Input {...control} {...form.register("firstName")} autoComplete="off" />}
      </FormField>

      <FormField id="profile-last-name" label="Last name" error={errors.lastName?.message} required>
        {(control) => <Input {...control} {...form.register("lastName")} autoComplete="off" />}
      </FormField>

      <FormField id="profile-display-name" label="Display name" error={errors.displayName?.message} required>
        {(control) => <Input {...control} {...form.register("displayName")} autoComplete="off" />}
      </FormField>

      <DialogFooter>
        <Button
          type="button"
          variant="outline"
          disabled={save.isPending}
          onClick={onCancel}
        >
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

