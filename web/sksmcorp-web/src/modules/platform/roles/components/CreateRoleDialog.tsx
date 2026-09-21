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
import { useCreateRole } from "../hooks/useRoles";
import { createRoleFormSchema, type CreateRoleForm } from "../schemas/createRole";

const UNKNOWN = "The role could not be created. Try again.";

interface CreateRoleDialogProps {
  readonly open: boolean;
  readonly returnFocus: RefObject<HTMLElement | null>;
  readonly onClose: () => void;
  readonly onClosed: () => void;
  readonly onCreated: (announcement: string) => void;
}

/**
 * AUT-C3's New role dialog (docs/requirements.md, "AUT-C3 CreateRole", RC7),
 * built as Edit profile and Change email are: React Hook Form, the
 * unsaved-changes guard on every close path, Zod for presence only, and the
 * server's refusal word for word.
 *
 * No permissions here (RC8): a new role carries none, and granting them is
 * AUT-C7's story.
 */
export function CreateRoleDialog({ open, returnFocus, onClose, onClosed, onCreated }: CreateRoleDialogProps) {
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
          <DialogTitle>New role</DialogTitle>
          <DialogDescription>
            The role is created active, with no permissions and no holders. Its code cannot be changed afterwards.
          </DialogDescription>
        </DialogHeader>

        <RoleEditor
          onCancel={close}
          guardRef={guardRef}
          onCreated={(announcement) => {
            onCreated(announcement);
            onClose();
          }}
        />
      </DialogContent>
    </Dialog>
  );
}

interface RoleEditorProps {
  readonly onCancel: () => void;
  readonly guardRef: RefObject<((proceed: () => void) => void) | undefined>;
  readonly onCreated: (announcement: string) => void;
}

function RoleEditor({ onCancel, guardRef, onCreated }: RoleEditorProps) {
  const create = useCreateRole();
  const [refusal, setRefusal] = useState<string | undefined>(undefined);

  const form = useForm<CreateRoleForm>({
    resolver: zodResolver(createRoleFormSchema),
    defaultValues: { name: "", code: "", description: "" },
  });

  const guard = useUnsavedChangesGuard(form.formState.isDirty && !create.isSuccess);

  useEffect(() => {
    guardRef.current = guard.confirm;

    return () => {
      guardRef.current = undefined;
    };
  });

  const errors = form.formState.errors;

  const submit = form.handleSubmit((values) => {
    if (create.isPending) {
      return;
    }

    setRefusal(undefined);

    create.mutate(values, {
      onSuccess: (role) => {
        // The server's stored name, not the typed one: it trimmed it.
        onCreated(`Role created: ${role.name}.`);
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

      <FormField id="new-role-name" label="Name" error={errors.name?.message} required>
        {(control) => <Input {...control} {...form.register("name")} autoComplete="off" />}
      </FormField>

      <FormField
        id="new-role-code"
        label="Code"
        description="The stable identifier other systems use. It cannot be changed later."
        error={errors.code?.message}
        required
      >
        {(control) => <Input {...control} {...form.register("code")} autoComplete="off" />}
      </FormField>

      <FormField id="new-role-description" label="Description">
        {(control) => <Textarea {...control} {...form.register("description")} />}
      </FormField>

      <DialogFooter>
        <Button type="button" variant="outline" disabled={create.isPending} onClick={onCancel}>
          Cancel
        </Button>
        <Button type="submit" disabled={create.isPending}>
          {create.isPending ? "Creating…" : "Create role"}
        </Button>
      </DialogFooter>

      {guard.prompt}
    </form>
  );
}
