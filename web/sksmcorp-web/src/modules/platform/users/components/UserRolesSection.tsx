import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { ApiError } from "@/shared/api/errors";
import { DataTable, type DataTableColumn } from "@/shared/components/DataTable";
import { EmptyState } from "@/shared/components/EmptyState";
import { ErrorState } from "@/shared/components/ErrorState";
import { formatInstant } from "@/shared/format/formatInstant";
import { useRoleAssignments } from "../hooks/useRoleAssignments";
import type { RoleAssignment } from "../schemas/roleAssignments";

interface UserRolesSectionProps {
  readonly userId: string;

  /** Opens the existing Manage roles dialog. */
  readonly onManage: () => void;
}

const columns: DataTableColumn<RoleAssignment>[] = [
  { header: "Role", cell: (assignment) => assignment.roleName },
  { header: "From", cell: (assignment) => formatInstant(assignment.effectiveFrom) },
  {
    header: "To",
    cell: (assignment) => (assignment.effectiveTo === null ? "No end date" : formatInstant(assignment.effectiveTo)),
  },
  // THE STATE IS THE SERVER'S (AUT-Q2): shown exactly as sent, never derived
  // here from the dates.
  { header: "State", cell: (assignment) => assignment.state },
];

/**
 * The User detail page's Roles section (docs/requirements.md, "USR-Q1 GetUser
 * v2 and the User detail page", G6). MOUNTED ONLY FOR role.read HOLDERS by the
 * page, so without that permission this component — and its AUT-Q2 request —
 * never exists (G2, DV-7).
 *
 * Current assignments only: AUT-Q2's default (Active and Future). Four columns;
 * provenance (who granted it, when and why) is Manage roles' to show. Its
 * loading, error and retry are its own, so a failure here leaves the page
 * standing.
 */
export function UserRolesSection({ userId, onManage }: UserRolesSectionProps) {
  const assignments = useRoleAssignments(userId, false);

  let body;

  if (assignments.data !== undefined) {
    body =
      assignments.data.assignments.length === 0 ? (
        <EmptyState title="No current roles." />
      ) : (
        <DataTable
          caption="Current roles"
          rows={assignments.data.assignments}
          rowKey={(assignment) => assignment.assignmentId}
          columns={columns}
        />
      );
  } else if (assignments.isError) {
    body = (
      <ErrorState
        message={
          assignments.error instanceof ApiError ? assignments.error.message : "The roles could not be loaded."
        }
        onRetry={() => {
          void assignments.refetch();
        }}
      />
    );
  } else {
    body = (
      <div role="group" aria-label="Loading roles" aria-busy="true">
        <Skeleton className="h-12 w-full" />
      </div>
    );
  }

  return (
    <section aria-labelledby="user-roles-heading" className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id="user-roles-heading" className="text-lg font-semibold">
          Roles
        </h2>
        <Button variant="outline" onClick={onManage}>
          Manage roles
        </Button>
      </div>
      {body}
    </section>
  );
}
