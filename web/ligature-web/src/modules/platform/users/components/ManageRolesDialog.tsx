import { useId, useRef, useState, type RefObject } from "react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { NativeSelect, NativeSelectOption } from "@/components/ui/native-select";
import { Skeleton } from "@/components/ui/skeleton";
import { Switch } from "@/components/ui/switch";
import { Textarea } from "@/components/ui/textarea";
import { ApiError } from "@/shared/api/errors";
import { useCan } from "@/shared/auth/useCan";
import { ConfirmAction } from "@/shared/components/ConfirmAction";
import { DataTable, type DataTableColumn } from "@/shared/components/DataTable";
import { ErrorState } from "@/shared/components/ErrorState";
import { FormField } from "@/shared/forms/FormField";
import { useGrantRole, useGrantableRoles, useRevokeRole, useRoleAssignments } from "../hooks/useRoleAssignments";
import { UserPermissions } from "../permissions";
import { grantFormSchema, type RoleAssignment } from "../schemas/roleAssignments";
import { reasonSchema, type UserRow } from "../schemas/users";

const UNKNOWN = "The action could not be completed. Try again.";

const format = new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" });

const when = (instant: string) => format.format(new Date(instant));

/**
 * A browser-local "YYYY-MM-DDTHH:mm" as a UTC instant, or undefined when
 * empty. The browser's own time zone is what the administrator meant; the
 * server stores and judges UTC.
 */
function toUtc(local: string): string | undefined {
  return local === "" ? undefined : new Date(local).toISOString();
}

/** Only an assignment the server calls Active or Future has anything left to close. */
const revocable = (assignment: RoleAssignment) => assignment.state === "Active" || assignment.state === "Future";

interface ManageRolesDialogProps {
  readonly open: boolean;
  readonly user: UserRow;
  readonly returnFocus: RefObject<HTMLElement | null>;
  readonly onClose: () => void;
  readonly onClosed: () => void;
}

/**
 * A user's role assignments (AUT-Q2), with Grant (AUT-C1) and Revoke (AUT-C2)
 * where the caller may (docs/requirements.md, "AUT-Q2").
 *
 * WHAT IS OFFERED COMES FROM EFFECTIVE PERMISSIONS, never from role names:
 * role.read shows this at all (the row decides), role.grant the grant form,
 * role.revoke a Revoke on each Active or Future assignment. Hidden, never
 * disabled.
 *
 * THE STATE IS THE SERVER'S. Each row shows `state` exactly as sent; nothing
 * here derives one from the dates. After a grant or a revocation the
 * assignments are read again — nothing is manufactured client-side.
 */
export function ManageRolesDialog({ open, user, returnFocus, onClose, onClosed }: ManageRolesDialogProps) {
  const canGrant = useCan(UserPermissions.grantRoles);
  const canRevoke = useCan(UserPermissions.revokeRoles);

  const [includeInactive, setIncludeInactive] = useState(false);
  const [revoking, setRevoking] = useState<{ assignment: RoleAssignment; open: boolean } | undefined>(undefined);
  const revokeFocus = useRef<HTMLElement | null>(null);
  const title = useRef<HTMLHeadingElement | null>(null);
  const historyLabel = useId();

  const assignments = useRoleAssignments(user.userId, includeInactive);

  const columns: DataTableColumn<RoleAssignment>[] = [
    {
      header: "Role",
      className: "whitespace-normal",
      cell: (assignment) => (
        <div className="flex flex-col">
          <span className="font-medium">{assignment.roleName}</span>
          <span>{assignment.state}</span>
        </div>
      ),
    },
    {
      header: "Period",
      className: "whitespace-normal",
      cell: (assignment) => (
        <span>
          {when(assignment.effectiveFrom)} – {assignment.effectiveTo === null ? "no end" : when(assignment.effectiveTo)}
        </span>
      ),
    },
    {
      header: "Granted",
      className: "whitespace-normal",
      cell: (assignment) => (
        <div className="flex flex-col">
          <span>
            {assignment.assignedBy.displayName}, {when(assignment.assignedAt)}
          </span>
          <span className="text-muted-foreground">{assignment.assignmentReason}</span>
        </div>
      ),
    },
    {
      header: "Revoked",
      className: "whitespace-normal",
      cell: (assignment) =>
        assignment.revokedAt === null ? null : (
          <div className="flex flex-col">
            <span>
              {assignment.revokedBy?.displayName}, {when(assignment.revokedAt)}
            </span>
            <span className="text-muted-foreground">{assignment.revocationReason}</span>
          </div>
        ),
    },
  ];

  if (canRevoke) {
    columns.push({
      header: "Actions",
      className: "text-right",
      cell: (assignment) =>
        revocable(assignment) ? (
          <Button
            variant="outline"
            size="sm"
            onClick={(event) => {
              revokeFocus.current = event.currentTarget;
              setRevoking({ assignment, open: true });
            }}
          >
            Revoke {assignment.roleName}
          </Button>
        ) : null,
    });
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next) {
          onClose();
        }
      }}
      onOpenChangeComplete={(isOpen) => {
        if (!isOpen) {
          onClosed();
        }
      }}
    >
      <DialogContent
        className="max-h-[90vh] overflow-y-auto sm:max-w-3xl"
        finalFocus={() => {
          const target = returnFocus.current;

          return target?.isConnected === true ? target : false;
        }}
      >
        <DialogHeader>
          {/* Focusable by script only: where focus goes when the element that
              should get it back has left with a revoked row. */}
          <DialogTitle ref={title} tabIndex={-1}>
            Roles for {user.displayName}
          </DialogTitle>
          <DialogDescription>
            Each assignment&apos;s state is as the server reports it now. Changes take effect on the next request.
          </DialogDescription>
        </DialogHeader>

        <div className="flex items-center gap-2">
          <Switch
            aria-labelledby={historyLabel}
            checked={includeInactive}
            onCheckedChange={(checked) => {
              setIncludeInactive(checked);
            }}
          />
          <span id={historyLabel} className="text-sm">
            Show history
          </span>
        </div>

        {assignments.data === undefined ? (
          assignments.isError ? (
            <ErrorState
              message={
                assignments.error instanceof ApiError ? assignments.error.message : "The assignments could not be loaded."
              }
              onRetry={() => {
                void assignments.refetch();
              }}
            />
          ) : (
            <div role="group" aria-label="Loading role assignments" aria-busy="true">
              <Skeleton className="h-12 w-full" />
            </div>
          )
        ) : (
          <DataTable
            caption="Role assignments"
            rows={assignments.data.assignments}
            rowKey={(assignment) => assignment.assignmentId}
            columns={columns}
          />
        )}

        {canGrant ? <GrantRoleForm user={user} /> : null}

        {revoking === undefined ? null : (
          <RevokeRoleConfirmation
            key={revoking.assignment.assignmentId}
            open={revoking.open}
            user={user}
            assignment={revoking.assignment}
            returnFocus={revokeFocus}
            onRevoked={() => {
              // The Revoke button leaves with its row when the assignments are
              // read again, so focus returns to this dialog's title rather than
              // falling to the page behind the modal.
              revokeFocus.current = title.current;
            }}
            onClose={() => {
              setRevoking({ ...revoking, open: false });
            }}
            onClosed={() => {
              setRevoking(undefined);
            }}
          />
        )}
      </DialogContent>
    </Dialog>
  );
}

/**
 * AUT-C1's form. The server is authoritative: overlap, a past start and an
 * empty period are refused there, and the refusal is shown word for word.
 * Nothing about overlap is checked here.
 */
function GrantRoleForm({ user }: { readonly user: UserRow }) {
  const roles = useGrantableRoles(true);
  const grant = useGrantRole(user.userId);

  const [roleId, setRoleId] = useState("");
  const [starts, setStarts] = useState("");
  const [ends, setEnds] = useState("");
  const [reason, setReason] = useState("");
  const [errors, setErrors] = useState<{ roleId?: string; reason?: string }>({});
  const [refusal, setRefusal] = useState<string | undefined>(undefined);

  function submit() {
    if (grant.isPending) {
      return;
    }

    setRefusal(undefined);

    const parsed = grantFormSchema.safeParse({ roleId, reason });

    if (!parsed.success) {
      const first = (field: "roleId" | "reason") => parsed.error.issues.find((x) => x.path[0] === field)?.message;
      setErrors({ roleId: first("roleId"), reason: first("reason") });
      return;
    }

    setErrors({});

    grant.mutate(
      {
        userId: user.userId,
        roleId: parsed.data.roleId,
        effectiveFrom: toUtc(starts),
        effectiveTo: toUtc(ends),
        reason: parsed.data.reason,
      },
      {
        onSuccess: () => {
          setRoleId("");
          setStarts("");
          setEnds("");
          setReason("");
        },
        onError: (failure: unknown) => {
          setRefusal(failure instanceof ApiError ? failure.message : UNKNOWN);
        },
      },
    );
  }

  return (
    <form
      aria-labelledby="grant-role-heading"
      className="flex flex-col gap-3 border-t pt-4"
      // The server and the schema decide validity, and say so accessibly; the
      // browser's own bubbles would pre-empt both.
      noValidate
      onSubmit={(event) => {
        event.preventDefault();
        submit();
      }}
    >
      <h3 id="grant-role-heading" className="font-medium">
        Grant a role
      </h3>

      <FormField id="grant-role" label="Role" error={errors.roleId} required>
        {(control) => (
          <NativeSelect
            {...control}
            className="w-full"
            value={roleId}
            onChange={(event) => {
              setRoleId(event.target.value);
            }}
          >
            <NativeSelectOption value="">Choose a role</NativeSelectOption>
            {(roles.data?.roles ?? []).map((role) => (
              <NativeSelectOption key={role.roleId} value={role.roleId}>
                {role.name}
              </NativeSelectOption>
            ))}
          </NativeSelect>
        )}
      </FormField>

      <div className="grid gap-3 sm:grid-cols-2">
        <FormField id="grant-starts" label="Starts" description="Optional. Now if left empty; your local time.">
          {(control) => (
            <Input
              {...control}
              type="datetime-local"
              value={starts}
              onChange={(event) => {
                setStarts(event.target.value);
              }}
            />
          )}
        </FormField>

        <FormField id="grant-ends" label="Ends" description="Optional. No end if left empty; your local time.">
          {(control) => (
            <Input
              {...control}
              type="datetime-local"
              value={ends}
              onChange={(event) => {
                setEnds(event.target.value);
              }}
            />
          )}
        </FormField>
      </div>

      <FormField
        id="grant-reason"
        label="Reason"
        description="Recorded with the assignment and in the audit trail."
        error={errors.reason}
        required
      >
        {(control) => (
          <Textarea
            {...control}
            value={reason}
            onChange={(event) => {
              setReason(event.target.value);
            }}
          />
        )}
      </FormField>

      {refusal === undefined ? null : (
        <p role="alert" className="text-sm text-destructive">
          {refusal}
        </p>
      )}

      <div>
        <Button type="submit" disabled={grant.isPending}>
          {grant.isPending ? "Granting…" : "Grant role"}
        </Button>
      </div>
    </form>
  );
}

interface RevokeRoleConfirmationProps {
  readonly open: boolean;
  readonly user: UserRow;
  readonly assignment: RoleAssignment;
  readonly returnFocus: RefObject<HTMLElement | null>;
  readonly onRevoked: () => void;
  readonly onClose: () => void;
  readonly onClosed: () => void;
}

/** AUT-C2's confirmation, with the reason it requires. */
function RevokeRoleConfirmation({
  open,
  user,
  assignment,
  returnFocus,
  onRevoked,
  onClose,
  onClosed,
}: RevokeRoleConfirmationProps) {
  const revoke = useRevokeRole(user.userId);
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | undefined>(undefined);

  function confirm() {
    if (revoke.isPending) {
      return;
    }

    setError(undefined);

    const parsed = reasonSchema.safeParse({ reason });

    if (!parsed.success) {
      setError(parsed.error.issues[0]?.message ?? UNKNOWN);
      return;
    }

    revoke.mutate(
      { assignmentId: assignment.assignmentId, reason: parsed.data.reason },
      {
        onSuccess: () => {
          onRevoked();
          onClose();
        },
        onError: (failure: unknown) => {
          setError(failure instanceof ApiError ? failure.message : UNKNOWN);
        },
      },
    );
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
      title={`Revoke ${assignment.roleName} from ${user.displayName}`}
      description={
        assignment.state === "Future"
          ? "It has not started yet, so it will never take effect."
          : "It stops granting access from now."
      }
      confirmLabel="Revoke role"
      busyLabel="Revoking…"
      busy={revoke.isPending}
      onConfirm={confirm}
      returnFocus={returnFocus}
    >
      <FormField
        id="revoke-role-reason"
        label="Reason"
        description="Recorded with the revocation and in the audit trail."
        error={error}
        required
      >
        {(control) => (
          <Textarea
            {...control}
            value={reason}
            onChange={(event) => {
              setReason(event.target.value);
            }}
          />
        )}
      </FormField>
    </ConfirmAction>
  );
}
