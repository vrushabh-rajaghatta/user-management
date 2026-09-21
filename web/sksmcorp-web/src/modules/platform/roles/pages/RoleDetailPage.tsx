import { useRef, useState } from "react";
import { useParams } from "react-router";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { ApiError } from "@/shared/api/errors";
import { ErrorState } from "@/shared/components/ErrorState";
import { Page } from "@/shared/components/Page";
import { useCan } from "@/shared/auth/useCan";
// One module using another's public surface, as RA8 established in the
// other direction: AUT-Q4 needs user.read as well as role.read (RH9).
import { UserPermissions } from "@/modules/platform/users";
import { EditRoleDialog } from "../components/EditRoleDialog";
import { AddPermissionDialog } from "../components/AddPermissionDialog";
import { RevokePermissionDialog } from "../components/RevokePermissionDialog";
import { RoleHoldersSection } from "../components/RoleHoldersSection";
import { RoleLifecycleDialog } from "../components/RoleLifecycleDialog";
import { useRolePermissions, useRoles } from "../hooks/useRoles";
// Aliased: the module also exports a RolePermissions CONST of permission
// codes, and this is the schema type for one role's grants.
import type { RolePermissions as RoleGrants } from "../schemas/roles";
import { RolePermissions } from "../permissions";

type Grant = RoleGrants["permissions"][number];

const NOT_FOUND = "The role does not exist.";

/**
 * AUT-Q3's screen (docs/requirements.md, "Role administration read", RA-U4 and
 * RA-U5).
 *
 * The metadata comes from AUT-Q5 — the catalogue has no GetRole — read with
 * inactive roles included, so an inactive role's page is not a dead end. The
 * grants come from AUT-Q3, in its CURRENT-STATE form: asOf belongs to the
 * contract, not to this screen (RA5).
 */
export function RoleDetailPage() {
  const { roleId = "" } = useParams();
  const roles = useRoles(true);
  const grants = useRolePermissions(roleId);

  // AUT-C4 (RM-U1). An affordance, never authorization: the command decides.
  const canManage = useCan(RolePermissions.manage);

  // RH9: AUT-Q4 requires BOTH codes, so the section is mounted only for a
  // caller holding both — and the request is never made without them.
  const canReadUsers = useCan(UserPermissions.read);
  const [editing, setEditing] = useState<{ open: boolean } | undefined>(undefined);
  const [lifecycle, setLifecycle] = useState<{ open: boolean } | undefined>(undefined);
  const [announcement, setAnnouncement] = useState("");
  const [queued, setQueued] = useState<string | undefined>(undefined);
  const edit = useRef<HTMLButtonElement | null>(null);
  const lifecycleAction = useRef<HTMLButtonElement | null>(null);
  const addPermission = useRef<HTMLButtonElement | null>(null);
  const permissionsHeading = useRef<HTMLHeadingElement | null>(null);
  const [adding, setAdding] = useState<{ open: boolean } | undefined>(undefined);
  const [revoking, setRevoking] = useState<{ grant: Grant; open: boolean } | undefined>(undefined);

  const role = roles.data?.roles.find((candidate) => candidate.roleId === roleId);
  const missing = grants.error instanceof ApiError && grants.error.status === 404;

  if (missing || (roles.data !== undefined && role === undefined)) {
    return (
      <Page title="Role">
        <ErrorState title="Not found" message={NOT_FOUND} />
      </Page>
    );
  }

  if (roles.isError || grants.isError) {
    return (
      <Page title="Role">
        <ErrorState
          message={
            grants.error instanceof ApiError
              ? grants.error.message
              : roles.error instanceof ApiError
                ? roles.error.message
                : "The role could not be read."
          }
          onRetry={() => {
            void roles.refetch();
            void grants.refetch();
          }}
        />
      </Page>
    );
  }

  if (role === undefined || grants.data === undefined) {
    return (
      <Page title="Role">
        <div role="group" aria-label="Loading role" aria-busy="true" className="flex flex-col gap-3">
          <Skeleton className="h-6 w-64" />
          <Skeleton className="h-4 w-48" />
        </div>
      </Page>
    );
  }

  // Destructured, not read as a member: ".permissions" is reserved for a
  // caller's effective permissions (frontend-architecture §9), and these are
  // one role's grants.
  const { permissions: rows } = grants.data;

  // A release-owned role is nobody's to edit (RM4), so nobody is offered it.
  const editable = canManage && !role.isSystemRole;

  return (
    <Page
      title={role.name}
      actions={
        editable ? (
          <>
            <Button
              ref={edit}
              variant="outline"
              onClick={() => {
                setAnnouncement("");
                setEditing({ open: true });
              }}
            >
              Edit role
            </Button>

            {/* RD8: one action, and which one is the role's state. */}
            <Button
              ref={lifecycleAction}
              variant={role.isActive ? "destructive" : "default"}
              onClick={() => {
                setAnnouncement("");
                setLifecycle({ open: true });
              }}
            >
              {role.isActive ? "Deactivate" : "Reactivate"}
            </Button>
          </>
        ) : undefined
      }
    >
      <p role="status" className="text-sm empty:hidden">
        {announcement}
      </p>

      <dl className="grid gap-2 text-sm sm:grid-cols-[10rem_1fr]">
        <dt className="text-muted-foreground">Code</dt>
        <dd>{role.code}</dd>

        <dt className="text-muted-foreground">Description</dt>
        <dd>{role.description ?? "None"}</dd>

        <dt className="text-muted-foreground">Defined by</dt>
        <dd>{role.isSystemRole ? "The platform" : "This tenant"}</dd>

        <dt className="text-muted-foreground">Status</dt>
        <dd>{role.isActive ? "Active" : "Inactive"}</dd>

        <dt className="text-muted-foreground">Agent-assignable</dt>
        <dd>{role.agentAssignable ? "Yes" : "No"}</dd>
      </dl>

      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 ref={permissionsHeading} tabIndex={-1} className="text-lg font-semibold">
          Permissions
        </h2>

        {editable ? (
          <Button
            ref={addPermission}
            variant="outline"
            onClick={() => {
              setAnnouncement("");
              setAdding({ open: true });
            }}
          >
            Add permission
          </Button>
        ) : null}
      </div>

      <Table aria-label="Permissions">
        <TableHeader>
          <TableRow>
            <TableHead>Code</TableHead>
            <TableHead>Name</TableHead>
            <TableHead>Resource</TableHead>
            <TableHead>Action</TableHead>
            <TableHead>Actor</TableHead>
            {editable ? <TableHead className="text-right">Actions</TableHead> : null}
          </TableRow>
        </TableHeader>
        <TableBody>
          {rows.map((permission) => (
            <TableRow key={permission.rolePermissionId}>
              <TableCell>{permission.code}</TableCell>
              <TableCell>{permission.name}</TableCell>
              <TableCell>{permission.resource}</TableCell>
              <TableCell>{permission.action}</TableCell>
              <TableCell>{permission.requiresHumanActor ? "Human only" : "Any actor"}</TableCell>
              {editable ? (
                <TableCell className="text-right">
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={() => {
                      setAnnouncement("");
                      setRevoking({ grant: permission, open: true });
                    }}
                  >
                    Revoke {permission.code}
                  </Button>
                </TableCell>
              ) : null}
            </TableRow>
          ))}
        </TableBody>
      </Table>

      {/* RH10: the count that used to sit dead in the metadata list now names
          the people, read at one instant with them. RH11: read-only. */}
      {canReadUsers ? <RoleHoldersSection roleId={roleId} canOpenUsers={canReadUsers} /> : null}

      {adding === undefined ? null : (
        <AddPermissionDialog
          role={role}
          held={rows.map((grant) => grant.permissionId)}
          open={adding.open}
          returnFocus={addPermission}
          onClose={() => {
            setAdding({ open: false });
          }}
          onAdded={(message) => {
            setQueued(message);
          }}
          onClosed={() => {
            setAdding(undefined);

            if (queued !== undefined) {
              setAnnouncement(queued);
              setQueued(undefined);
            }
          }}
        />
      )}

      {revoking === undefined ? null : (
        <RevokePermissionDialog
          key={revoking.grant.rolePermissionId}
          grant={revoking.grant}
          roleName={role.name}
          open={revoking.open}
          returnFocus={permissionsHeading}
          onClose={() => {
            setRevoking({ grant: revoking.grant, open: false });
          }}
          onRevoked={(message) => {
            setQueued(message);
          }}
          onClosed={() => {
            setRevoking(undefined);

            if (queued !== undefined) {
              setAnnouncement(queued);
              setQueued(undefined);
            }
          }}
        />
      )}

      {lifecycle === undefined ? null : (
        <RoleLifecycleDialog
          role={role}
          open={lifecycle.open}
          returnFocus={lifecycleAction}
          onClose={() => {
            setLifecycle({ open: false });
          }}
          onDone={(message) => {
            setQueued(message);
          }}
          onClosed={() => {
            setLifecycle(undefined);

            if (queued !== undefined) {
              setAnnouncement(queued);
              setQueued(undefined);
            }
          }}
        />
      )}

      {editing === undefined ? null : (
        <EditRoleDialog
          role={role}
          open={editing.open}
          returnFocus={edit}
          onClose={() => {
            setEditing({ open: false });
          }}
          onSaved={(message) => {
            setQueued(message);
          }}
          onClosed={() => {
            setEditing(undefined);

            if (queued !== undefined) {
              setAnnouncement(queued);
              setQueued(undefined);
            }
          }}
        />
      )}
    </Page>
  );
}
