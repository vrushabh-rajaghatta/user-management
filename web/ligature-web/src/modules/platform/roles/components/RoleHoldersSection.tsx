import { Link } from "react-router";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { ApiError } from "@/shared/api/errors";
import { EmptyState } from "@/shared/components/EmptyState";
import { ErrorState } from "@/shared/components/ErrorState";
import { formatInstant } from "@/shared/format/formatInstant";
import { useRoleMembers } from "../hooks/useRoles";

interface RoleHoldersSectionProps {
  readonly roleId: string;

  /** Whether the caller may follow a holder to their user detail page. */
  readonly canOpenUsers: boolean;
}

/**
 * The Role detail page's Holders section (docs/requirements.md, "AUT-Q4
 * GetRoleMembers", RH10, RH-U1 to RH-U7).
 *
 * MOUNTED ONLY FOR A CALLER HOLDING BOTH role.read AND user.read by the page,
 * so without either this component — and its AUT-Q4 request — never exists
 * (RH9). The section's own guard is not the only thing between a caller and
 * the user directory: the server refuses the read as well.
 *
 * READ-ONLY, DELIBERATELY (RH11). Revoking a holding is AUT-C2's, and it lives
 * in the Users module with role.revoke. This section reports; it does not act.
 *
 * The count is this read's own, derived from these rows. The roles list's
 * activeHolderCount is computed at that query's instant and is NOT shown here,
 * because a page displaying two numbers from two instants can display two
 * different numbers.
 */
export function RoleHoldersSection({ roleId, canOpenUsers }: RoleHoldersSectionProps) {
  const members = useRoleMembers(roleId, true);

  let body;

  if (members.data !== undefined) {
    body =
      members.data.members.length === 0 ? (
        <EmptyState title="No current holders." />
      ) : (
        <Table aria-label="Holders">
          <TableHeader>
            <TableRow>
              <TableHead>Holder</TableHead>
              <TableHead>Email</TableHead>
              <TableHead>Status</TableHead>
              <TableHead>From</TableHead>
              <TableHead>To</TableHead>
              <TableHead>Reason</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {members.data.members.map((member) => (
              <TableRow key={member.assignmentId}>
                <TableCell>
                  {canOpenUsers ? (
                    <Link
                      to={`/admin/users/${member.userId}`}
                      className="underline underline-offset-4"
                    >
                      {member.displayName}
                    </Link>
                  ) : (
                    member.displayName
                  )}
                </TableCell>
                <TableCell>{member.email ?? "None"}</TableCell>
                <TableCell>{member.status}</TableCell>
                <TableCell>{formatInstant(member.effectiveFrom)}</TableCell>
                <TableCell>
                  {member.effectiveTo === null ? "No end date" : formatInstant(member.effectiveTo)}
                </TableCell>
                <TableCell>{member.assignmentReason}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      );
  } else if (members.isError) {
    body = (
      <ErrorState
        message={members.error instanceof ApiError ? members.error.message : "The holders could not be loaded."}
        onRetry={() => {
          void members.refetch();
        }}
      />
    );
  } else {
    body = (
      <div role="group" aria-label="Loading holders" aria-busy="true">
        <Skeleton className="h-12 w-full" />
      </div>
    );
  }

  return (
    <section aria-labelledby="role-holders-heading" className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id="role-holders-heading" className="text-lg font-semibold">
          Holders
        </h2>

        {members.data !== undefined ? (
          <p className="text-muted-foreground text-sm">
            {members.data.activeHolderCount === 1
              ? "1 holder"
              : `${String(members.data.activeHolderCount)} holders`}
          </p>
        ) : null}
      </div>
      {body}
    </section>
  );
}
