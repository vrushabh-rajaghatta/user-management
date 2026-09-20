import { useParams } from "react-router";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Skeleton } from "@/components/ui/skeleton";
import { ApiError } from "@/shared/api/errors";
import { ErrorState } from "@/shared/components/ErrorState";
import { Page } from "@/shared/components/Page";
import { useRolePermissions, useRoles } from "../hooks/useRoles";

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

  return (
    <Page title={role.name}>
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

        <dt className="text-muted-foreground">Holders</dt>
        <dd>{role.activeHolderCount}</dd>
      </dl>

      <Table aria-label="Permissions">
        <TableHeader>
          <TableRow>
            <TableHead>Code</TableHead>
            <TableHead>Name</TableHead>
            <TableHead>Resource</TableHead>
            <TableHead>Action</TableHead>
            <TableHead>Actor</TableHead>
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
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </Page>
  );
}
