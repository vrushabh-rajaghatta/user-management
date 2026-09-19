import { useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { ApiError } from "@/shared/api/errors";
import { useCan } from "@/shared/auth/useCan";
import { DataTable, type DataTableColumn } from "@/shared/components/DataTable";
import { EmptyState } from "@/shared/components/EmptyState";
import { ErrorState } from "@/shared/components/ErrorState";
import { useUserSessions } from "../hooks/useUserSessions";
import { UserPermissions } from "../permissions";
import type { UserSession } from "../schemas/sessions";
import { formatInstant } from "./formatInstant";
import { RevokeSessionDialog } from "./RevokeSessionDialog";
import { revokeOffered } from "./sessionActions";

interface UserSessionsSectionProps {
  readonly userId: string;
}

type Revoking = { readonly session: UserSession; readonly open: boolean };

/**
 * The User detail page's Active sessions (SES-Q1). MOUNTED ONLY FOR
 * session.read HOLDERS by the page, so without that permission this component
 * — and its request — never exists (SS5).
 *
 * What is active, and which row is the caller's own, are the SERVER's facts:
 * this lists what it returned and marks `current` as This session. It never
 * compares instants, never counts down from idleExpiresAt, and never works
 * out its own session. Its loading, error and retry are its own.
 */
export function UserSessionsSection({ userId }: UserSessionsSectionProps) {
  const sessions = useUserSessions(userId);
  const holdsRevoke = useCan(UserPermissions.revokeSessions);

  const [revoking, setRevoking] = useState<Revoking | undefined>(undefined);
  const [announcement, setAnnouncement] = useState("");
  const [queued, setQueued] = useState<string | undefined>(undefined);
  const returnFocus = useRef<HTMLElement | null>(null);

  const columns: DataTableColumn<UserSession>[] = [
    {
      header: "Signed in",
      cell: (session) => (
        <span className="flex flex-wrap items-center gap-2">
          <span>{formatInstant(session.createdAt)}</span>
          {session.current ? <span className="text-sm font-medium">This session</span> : null}
        </span>
      ),
    },
    { header: "Last active", cell: (session) => formatInstant(session.lastActivityAt) },
    { header: "IP address", cell: (session) => session.ipAddress ?? "—" },
    {
      header: "Browser",
      cell: (session) => <span className="break-all">{session.userAgent ?? "—"}</span>,
    },
  ];

  // The action column exists only for someone who may revoke: an empty column
  // would announce an action the caller cannot take.
  if (holdsRevoke) {
    columns.push({
      header: "Action",
      cell: (session) =>
        revokeOffered({ session, holdsRevoke }) ? (
          <Button
            variant="outline"
            size="sm"
            aria-label={`Revoke session signed in ${formatInstant(session.createdAt)}`}
            onClick={(event) => {
              setAnnouncement("");
              returnFocus.current = event.currentTarget;
              setRevoking({ session, open: true });
            }}
          >
            Revoke
          </Button>
        ) : null,
    });
  }

  let body;

  if (sessions.data !== undefined) {
    body =
      sessions.data.sessions.length === 0 ? (
        <EmptyState title="No active sessions." />
      ) : (
        <DataTable
          caption="Active sessions"
          rows={sessions.data.sessions}
          rowKey={(session) => session.sessionId}
          columns={columns}
        />
      );
  } else if (sessions.isError) {
    body = (
      <ErrorState
        message={sessions.error instanceof ApiError ? sessions.error.message : "The sessions could not be loaded."}
        onRetry={() => {
          void sessions.refetch();
        }}
      />
    );
  } else {
    body = (
      <div role="group" aria-label="Loading active sessions" aria-busy="true">
        <Skeleton className="h-12 w-full" />
      </div>
    );
  }

  return (
    <section aria-labelledby="user-sessions-heading" className="flex flex-col gap-4">
      <h2 id="user-sessions-heading" className="text-lg font-semibold">
        Active sessions
      </h2>

      <p role="status" className="text-sm empty:hidden">
        {announcement}
      </p>

      {body}

      {revoking === undefined ? null : (
        <RevokeSessionDialog
          key={revoking.session.sessionId}
          open={revoking.open}
          userId={userId}
          sessionId={revoking.session.sessionId}
          returnFocus={returnFocus}
          onClose={() => {
            setRevoking({ ...revoking, open: false });
          }}
          onDone={(message) => {
            setQueued(message);
            setRevoking({ ...revoking, open: false });
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
    </section>
  );
}
