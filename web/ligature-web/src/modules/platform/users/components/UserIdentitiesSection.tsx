import { useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { ApiError } from "@/shared/api/errors";
import { useCallerIdentityId } from "@/shared/auth/useCallerIdentityId";
import { useCan } from "@/shared/auth/useCan";
import { DataTable, type DataTableColumn } from "@/shared/components/DataTable";
import { EmptyState } from "@/shared/components/EmptyState";
import { ErrorState } from "@/shared/components/ErrorState";
import { useUserIdentities } from "../hooks/useUserIdentities";
import { UserPermissions } from "../permissions";
import type { UserIdentity } from "../schemas/identities";
import { formatInstant } from "./formatInstant";
import { unlockOffered } from "./identityActions";
import { UnlockIdentityDialog } from "./UnlockIdentityDialog";

interface UserIdentitiesSectionProps {
  readonly userId: string;

  /** The user's lifecycle status, from GetUser: Unlock is offered only for an active user. */
  readonly userStatus: "Active" | "Inactive";
}

type Unlocking = { readonly identity: UserIdentity; readonly open: boolean };

/**
 * The User detail page's Sign-in identities (IDN-Q1, as amended). MOUNTED ONLY
 * FOR identity.read HOLDERS by the page, so without that permission this
 * component — and its request — never exists (I5).
 *
 * The lock state is shown as the server decided it at the read; nothing here
 * compares instants. Its loading, error and retry are its own.
 */
export function UserIdentitiesSection({ userId, userStatus }: UserIdentitiesSectionProps) {
  const identities = useUserIdentities(userId);
  const holdsUnlock = useCan(UserPermissions.unlock);
  const callerIdentityId = useCallerIdentityId();

  const [unlocking, setUnlocking] = useState<Unlocking | undefined>(undefined);
  const [announcement, setAnnouncement] = useState("");
  const [queued, setQueued] = useState<string | undefined>(undefined);
  const returnFocus = useRef<HTMLElement | null>(null);

  const ownPage =
    callerIdentityId !== undefined &&
    (identities.data?.identities.some((x) => x.userIdentityId === callerIdentityId) ?? false);

  const columns: DataTableColumn<UserIdentity>[] = [
    { header: "Type", cell: (identity) => identity.type },
    { header: "Username", cell: (identity) => identity.username ?? "—" },
    { header: "Status", cell: (identity) => identity.status },
    {
      // The fourth and last column: the lock state as the server decided it,
      // and Unlock beside it where every I6 condition holds.
      header: "Lock",
      cell: (identity) => (
        <span className="flex flex-wrap items-center gap-2">
          <span>
            {identity.locked && identity.lockedUntil !== null
              ? `Locked until ${formatInstant(identity.lockedUntil)}`
              : "Not locked"}
          </span>
          {unlockOffered({ identity, userStatus, ownPage, holdsUnlock }) && identity.username !== null ? (
            <Button
              variant="outline"
              size="sm"
              onClick={(event) => {
                setAnnouncement("");
                returnFocus.current = event.currentTarget;
                setUnlocking({ identity, open: true });
              }}
            >
              {`Unlock ${identity.username}`}
            </Button>
          ) : null}
        </span>
      ),
    },
  ];

  let body;

  if (identities.data !== undefined) {
    body =
      identities.data.identities.length === 0 ? (
        <EmptyState title="No sign-in identities." />
      ) : (
        <DataTable
          caption="Sign-in identities"
          rows={identities.data.identities}
          rowKey={(identity) => identity.userIdentityId}
          columns={columns}
        />
      );
  } else if (identities.isError) {
    body = (
      <ErrorState
        message={
          identities.error instanceof ApiError ? identities.error.message : "The identities could not be loaded."
        }
        onRetry={() => {
          void identities.refetch();
        }}
      />
    );
  } else {
    body = (
      <div role="group" aria-label="Loading sign-in identities" aria-busy="true">
        <Skeleton className="h-12 w-full" />
      </div>
    );
  }

  return (
    <section aria-labelledby="user-identities-heading" className="flex flex-col gap-4">
      <h2 id="user-identities-heading" className="text-lg font-semibold">
        Sign-in identities
      </h2>

      <p role="status" className="text-sm empty:hidden">
        {announcement}
      </p>

      {body}

      {unlocking === undefined || unlocking.identity.username === null ? null : (
        <UnlockIdentityDialog
          key={unlocking.identity.userIdentityId}
          open={unlocking.open}
          userId={userId}
          identityId={unlocking.identity.userIdentityId}
          username={unlocking.identity.username}
          returnFocus={returnFocus}
          onClose={() => {
            setUnlocking({ ...unlocking, open: false });
          }}
          onDone={(message) => {
            setQueued(message);
            setUnlocking({ ...unlocking, open: false });
          }}
          onClosed={() => {
            setUnlocking(undefined);

            if (queued !== undefined) {
              setAnnouncement(queued);
              setQueued(undefined);
            }
          }}
        />
      )}
    </section>
  );
}
