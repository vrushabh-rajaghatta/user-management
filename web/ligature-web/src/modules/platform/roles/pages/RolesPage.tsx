import { useState } from "react";
import { Link } from "react-router";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { ApiError } from "@/shared/api/errors";
import { ErrorState } from "@/shared/components/ErrorState";
import { Page } from "@/shared/components/Page";
import { Skeleton } from "@/components/ui/skeleton";
import { useRoles } from "../hooks/useRoles";

/**
 * AUT-Q5's screen (docs/requirements.md, "Role administration read", RA-U2).
 *
 * READ-ONLY: no row action, no dialog, nothing to press but the inactive-roles
 * control, which re-reads. Every value in the table is the server's, including
 * the three derived ones.
 */
export function RolesPage() {
  const [includeInactive, setIncludeInactive] = useState(false);
  const roles = useRoles(includeInactive);

  return (
    <Page title="Roles">
      <label className="flex items-center gap-2 text-sm">
        <input
          type="checkbox"
          className="size-4"
          checked={includeInactive}
          onChange={(event) => {
            setIncludeInactive(event.target.checked);
          }}
        />
        Show inactive roles
      </label>

      {roles.isError ? (
        <ErrorState
          message={roles.error instanceof ApiError ? roles.error.message : "The roles could not be read."}
          onRetry={() => {
            void roles.refetch();
          }}
        />
      ) : roles.data === undefined ? (
        <div role="group" aria-label="Loading roles" aria-busy="true" className="flex flex-col gap-3">
          {[0, 1, 2].map((row) => (
            <Skeleton key={row} className="h-9 w-full" />
          ))}
        </div>
      ) : (
        <Table aria-label="Roles">
          <TableHeader>
            <TableRow>
              <TableHead>Name</TableHead>
              <TableHead>Code</TableHead>
              <TableHead>Status</TableHead>
              <TableHead>Agent-assignable</TableHead>
              <TableHead>Permissions</TableHead>
              <TableHead>Holders</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {roles.data.roles.map((role) => (
              <TableRow key={role.roleId}>
                <TableCell>
                  <Link to={`/admin/roles/${role.roleId}`} className="underline underline-offset-4">
                    {role.name}
                  </Link>
                </TableCell>
                <TableCell>{role.code}</TableCell>
                <TableCell>{role.isActive ? "Active" : "Inactive"}</TableCell>
                <TableCell>{role.agentAssignable ? "Yes" : "No"}</TableCell>
                <TableCell>{role.permissionCount}</TableCell>
                <TableCell>{role.activeHolderCount}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </Page>
  );
}
