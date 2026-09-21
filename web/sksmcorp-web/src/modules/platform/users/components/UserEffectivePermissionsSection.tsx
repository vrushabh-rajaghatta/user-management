import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { ApiError } from "@/shared/api/errors";
import { EmptyState } from "@/shared/components/EmptyState";
import { ErrorState } from "@/shared/components/ErrorState";
import { useEffectivePermissions } from "../hooks/useEffectivePermissions";

interface UserEffectivePermissionsSectionProps {
  readonly userId: string;
}

/**
 * The User detail page's Effective permissions section (docs/requirements.md,
 * "USR-Q3 GetUserAccessSummary", UA8; UA-U1 to UA-U6).
 *
 * It completes what the page shows: Profile is who they are, Roles is what was
 * granted, and this is what is IN EFFECT. The three are independently
 * authorised reads (G2).
 *
 * MOUNTED ONLY FOR A CALLER HOLDING BOTH user.read AND role.read by the page
 * (UA2), so without either this component — and its request — never exists.
 * That guard is not the protection; the server refuses the read as well. It is
 * here because a caller who can read the profile must not be shown a section
 * they will only be refused.
 *
 * READ-ONLY. Nothing here is a control: the way to change this set is to
 * change the user's roles, which is the Roles section's business.
 */
export function UserEffectivePermissionsSection({ userId }: UserEffectivePermissionsSectionProps) {
  const effective = useEffectivePermissions(userId);

  let body;

  if (effective.data !== undefined) {
    const { status, permissions } = effective.data;

    body =
      permissions.length === 0 ? (
        // THE EMPTY SET EXPLAINS ITSELF (UA4). An inactive user holding roles
        // and an active user holding none both answer nothing, and only the
        // first is worth an access reviewer's attention.
        <EmptyState
          title={
            status === "Inactive"
              ? "This user is inactive, so they can currently do nothing."
              : "No effective permissions."
          }
        />
      ) : (
        <Table aria-label="Effective permissions">
          <TableHeader>
            <TableRow>
              <TableHead>Permission</TableHead>
              <TableHead>Scope</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {permissions.map((permission) => (
              <TableRow key={`${permission.code}:${permission.scopeType}:${permission.scopeId ?? ""}`}>
                <TableCell>{permission.code}</TableCell>
                <TableCell>
                  {permission.scopeId === null
                    ? permission.scopeType
                    : `${permission.scopeType} · ${permission.scopeId}`}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      );
  } else if (effective.isError) {
    body = (
      <ErrorState
        message={
          effective.error instanceof ApiError
            ? effective.error.message
            : "The effective permissions could not be loaded."
        }
        onRetry={() => {
          void effective.refetch();
        }}
      />
    );
  } else {
    body = (
      <div role="group" aria-label="Loading effective permissions" aria-busy="true">
        <Skeleton className="h-12 w-full" />
      </div>
    );
  }

  return (
    <section aria-labelledby="effective-permissions-heading" className="flex flex-col gap-4">
      <h2 id="effective-permissions-heading" className="text-lg font-semibold">
        Effective permissions
      </h2>
      {body}
    </section>
  );
}
