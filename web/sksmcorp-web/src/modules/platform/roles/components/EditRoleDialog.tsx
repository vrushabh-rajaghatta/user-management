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
import { useUpdateRoleMetadata } from "../hooks/useRoles";
import { updateRoleFormSchema, type UpdateRoleForm } from "../schemas/updateRole";
import type { Role } from "../schemas/roles";

const UNKNOWN = "The role could not be saved. Try again.";

interface EditRoleDialogProps {
  readonly role: Role;
  readonly open: boolean;
  readonly returnFocus: RefObject<HTMLElement | null>;
  readonly onClose: () => void;
  readonly onClosed: () => void;
  readonly onSaved: (announcement: string) => void;
}

/**
 * AUT-C4's Edit role dialog (docs/requirements.md, "AUT-C4
 * UpdateRoleMetadata", RM7), built as New role and Edit profile are.
 *
 * THE FORM OPENS ON THE ROLE'S STORED VALUES, so "dirty" means "differs from
 * what the server returned". Unlike Edit profile it needs no read of its own:
 * the detail page already holds the role, from AUT-Q5 (RM9).
 *
 * The code is shown, never edited (RM2). Ownership is not here at all: this
 * dialog is only ever reached for a tenant role (RM4).
 */
export function EditRoleDialog({ role, open, returnFocus, onClose, onClosed, onSaved }: EditRoleDialogProps) {
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
          <DialogTitle>Edit role</DialogTitle>
          <DialogDescription>
            The name and description are this role&apos;s to change. Its code is not; its permissions are changed below.
          </DialogDescription>
        </DialogHeader>

        <RoleEditor
          role={role}
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

interface RoleEditorProps {
  readonly role: Role;
  readonly onCancel: () => void;
  readonly guardRef: RefObject<((proceed: () => void) => void) | undefined>;
  readonly onSaved: (announcement: string) => void;
}

function RoleEditor({ role, onCancel, guardRef, onSaved }: RoleEditorProps) {
  const save = useUpdateRoleMetadata(role.roleId);
  const [refusal, setRefusal] = useState<string | undefined>(undefined);

  const form = useForm<UpdateRoleForm>({
    resolver: zodResolver(updateRoleFormSchema),
    defaultValues: { name: role.name, description: role.description ?? "" },
  });

  const guard = useUnsavedChangesGuard(form.formState.isDirty && !save.isSuccess);

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

    save.mutate(values, {
      onSuccess: (saved) => {
        // The server's stored name, not the typed one: it trimmed it (RM6).
        onSaved(`Role updated: ${saved.name}.`);
      },
      onError: (failure: unknown) => {
        setRefusal(failure instanceof ApiError ? failure.message : UNKNOWN);
      },
    });
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
        Code: <span className="font-medium">{role.code}</span>
      </p>

      <FormField id="edit-role-name" label="Name" error={errors.name?.message} required>
        {(control) => <Input {...control} {...form.register("name")} autoComplete="off" />}
      </FormField>

      <FormField id="edit-role-description" label="Description">
        {(control) => <Textarea {...control} {...form.register("description")} />}
      </FormField>

      <DialogFooter>
        <Button type="button" variant="outline" disabled={save.isPending} onClick={onCancel}>
          Cancel
        </Button>
        <Button type="submit" disabled={save.isPending}>
          {save.isPending ? "Saving\u2026" : "Save"}
        </Button>
      </DialogFooter>

      {guard.prompt}
    </form>
  );
}
