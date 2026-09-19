import { Skeleton } from "@/components/ui/skeleton";
import { ApiError } from "@/shared/api/errors";
import { DataTable, type DataTableColumn } from "@/shared/components/DataTable";
import { EmptyState } from "@/shared/components/EmptyState";
import { ErrorState } from "@/shared/components/ErrorState";
import { formatInstant } from "@/shared/format/formatInstant";
import { useMySessions } from "../hooks/useMySessions";
import type { MySession } from "../schemas/mySessions";

const COLUMNS: DataTableColumn<MySession>[] = [
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
  { header: "Browser", cell: (session) => <span className="break-all">{session.userAgent ?? "—"}</span> },
];

/**
 * SES-Q2 on My account (docs/requirements.md, "SES-Q2 GetMySessions on the My
 * account page", MY5): where the caller is signed in. Every signed-in caller
 * sees it; no permission gates it.
 *
 * READ-ONLY. There is no per-row action: the catalogue has no self command to
 * end one other session (MY4), and the section's own two buttons are the
 * mechanism. Which row is the caller's own is the server's `current`, only
 * ever rendered; `idleExpiresAt` is never counted down from. Its loading,
 * error and retry are its own, and its failure leaves those buttons working.
 */
export function MySessionsList() {
  const sessions = useMySessions();

  if (sessions.data !== undefined) {
    return sessions.data.sessions.length === 0 ? (
      <EmptyState title="No active sessions." />
    ) : (
      <DataTable
        caption="Your sessions"
        rows={sessions.data.sessions}
        rowKey={(session) => session.sessionId}
        columns={COLUMNS}
      />
    );
  }

  if (sessions.isError) {
    return (
      <ErrorState
        message={sessions.error instanceof ApiError ? sessions.error.message : "Your sessions could not be loaded."}
        onRetry={() => {
          void sessions.refetch();
        }}
      />
    );
  }

  return (
    <div role="group" aria-label="Loading your sessions" aria-busy="true">
      <Skeleton className="h-12 w-full" />
    </div>
  );
}
