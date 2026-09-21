import { useState, type RefObject } from "react";
import { ApiError } from "@/shared/api/errors";
import { ConfirmAction } from "@/shared/components/ConfirmAction";
import { ErrorState } from "@/shared/components/ErrorState";
import { FormField } from "@/shared/forms/FormField";
import { NativeSelect, NativeSelectOption } from "@/components/ui/native-select";
import { Skeleton } from "@/components/ui/skeleton";
import { usePermissionCatalogue, useAddPermission } from "../hooks/useRoles";
import type { Role } from "../schemas/roles";

const UNKNOWN = "The permission could not be added. Try again.";

interface AddPermissionDialogProps {
  readonly role: Role;
  readonly held: readonly string[];
  readonly open: boolean;
  readonly returnFocus: RefObject<HTMLElement | null>;
  readonly onClose: () => void;
  readonly onClosed: () => void;
  readonly onAdded: (announcement: string) => void;
}

/**
 * AUT-C7's picker (docs/requirements.md, RG9), built on ConfirmAction as the
 * lifecycle confirmation is.
 *
 * WHAT IT OFFERS IS THE CATALOGUE MINUS WHAT THE ROLE HOLDS, and minus retired
 * entries, because a new authorization edge may only be created against the
 * current catalogue (RG4). Both exclusions are affordances: the server refuses
 * either case itself.
 *
 * Each option says whether the permission is human-only, because that is what
 * makes a role un-agent-assignable — and, if an agent already holds the role,
 * what the server will refuse under RP6.
 */
export function AddPermissionDialog({
  role,
  held,
  open,
  returnFocus,
  onClose,
  onClosed,
  onAdded,
}: AddPermissionDialogProps) {
  const catalogue = usePermissionCatalogue(open);
  const add = useAddPermission(role.roleId);

  const [permissionId, setPermissionId] = useState("");
  const [error, setError] = useState<string | undefined>(undefined);

  // Destructured, not read as a member: ".permissions" is reserved for a
  // caller's effective permissions (frontend-architecture §9), and this is the
  // release's catalogue.
  const { permissions: catalogued = [] } = catalogue.data ?? {};

  const offered = catalogued
    .filter((permission) => permission.isActive && !held.includes(permission.permissionId));

  function confirm() {
    if (add.isPending) {
      return;
    }

    setError(undefined);

    if (permissionId === "") {
      setError("Choose a permission.");

      return;
    }

    add.mutate(permissionId, {
      onSuccess: () => {
        const chosen = offered.find((permission) => permission.permissionId === permissionId);

        onAdded(`Permission added: ${chosen?.code ?? "the permission"}.`);
        onClose();
      },
      onError: (failure: unknown) => {
        setError(failure instanceof ApiError ? failure.message : UNKNOWN);
      },
    });
  }

  return (
    <ConfirmAction
      open={open}
      onOpenChange={(next) => {
        if (!next) {
          onClose();
        }
      }}
      onClosed={onClosed}
      title={`Add a permission to ${role.name}`}
      description="Everyone who holds this role gains it immediately."
      confirmLabel="Add permission"
      busyLabel={"Adding…"}
      busy={add.isPending}
      onConfirm={confirm}
      returnFocus={returnFocus}
    >
      {catalogue.isError ? (
        <ErrorState
          message={catalogue.error instanceof ApiError ? catalogue.error.message : UNKNOWN}
          onRetry={() => {
            void catalogue.refetch();
          }}
        />
      ) : catalogue.data === undefined ? (
        <div role="group" aria-label="Loading permissions" aria-busy="true" className="flex flex-col gap-2">
          <Skeleton className="h-9 w-full" />
        </div>
      ) : (
        <FormField id="add-permission" label="Permission" error={error} required>
          {(control) => (
            <NativeSelect
              {...control}
              value={permissionId}
              onChange={(event) => {
                setPermissionId(event.target.value);
              }}
            >
              <NativeSelectOption value="">Choose a permission</NativeSelectOption>
              {offered.map((permission) => (
                <NativeSelectOption key={permission.permissionId} value={permission.permissionId}>
                  {permission.code} — {permission.name}
                  {permission.requiresHumanActor ? " (human only)" : ""}
                </NativeSelectOption>
              ))}
            </NativeSelect>
          )}
        </FormField>
      )}
    </ConfirmAction>
  );
}
