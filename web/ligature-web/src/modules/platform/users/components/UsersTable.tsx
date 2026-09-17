import { ChevronLeftIcon, ChevronRightIcon, MoreHorizontalIcon } from "lucide-react";
import { useRef, useState, type RefObject } from "react";
import { Link, Navigate, useLocation, useSearchParams } from "react-router";
import { Button, buttonVariants } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Pagination, PaginationContent, PaginationItem } from "@/components/ui/pagination";
import { Skeleton } from "@/components/ui/skeleton";
import { ApiError } from "@/shared/api/errors";
import { useCan } from "@/shared/auth/useCan";
import { DataTable, type DataTableColumn } from "@/shared/components/DataTable";
import { EmptyState } from "@/shared/components/EmptyState";
import { ErrorState } from "@/shared/components/ErrorState";
import { useUsers } from "../hooks/useUsers";
import { UserPermissions } from "../permissions";
import type { UserRow } from "../schemas/users";
import { UserActionDialog, type UserAction } from "./UserActionDialog";

/** The largest page the server can accept: its page parameter is an int (USR-Q1). */
const MAX_PAGE = 2_147_483_647;

/**
 * The page named in the URL, or undefined when the URL names one the server
 * would refuse. Absent means page 1. Only a plain positive whole number counts:
 * "1.5", "2e1", "0", "-1", "" and anything past the server's int are not pages.
 */
function pageFrom(value: string | null): number | undefined {
  if (value === null) {
    return 1;
  }

  if (!/^[1-9]\d*$/.test(value)) {
    return undefined;
  }

  const page = Number(value);

  return page <= MAX_PAGE ? page : undefined;
}

/** Page 1 carries no parameter, so the plain /admin/users is page 1's one URL. */
function searchFor(page: number): string {
  return page === 1 ? "" : `?page=${String(page)}`;
}

function actionsLabel(user: UserRow): string {
  return user.email === null ? `Actions for ${user.displayName}` : `Actions for ${user.displayName} (${user.email})`;
}

interface Pending {
  readonly user: UserRow;
  readonly action: UserAction;

  /** False while the dialog is closing; it stays mounted until it has closed. */
  readonly open: boolean;
}

/**
 * USR-Q1's table (docs/requirements.md; docs/frontend-architecture.md §11).
 *
 * THE URL IS THE STATE. The page lives in the query string, so a page can be
 * bookmarked and Back returns to the one before. A page the server would refuse
 * is replaced with page 1 before anything is sent.
 *
 * The confirmation lives here rather than in a row, so it survives its row
 * leaving the screen. The status region is always mounted, and an announcement
 * is placed in it only once the dialog has FINISHED closing: until then the page
 * behind the modal is hidden from assistive technology, and a region that is
 * revealed and filled in the same moment can go unannounced.
 */
export function UsersTable() {
  const [searchParams] = useSearchParams();
  const { pathname } = useLocation();

  const requested = pageFrom(searchParams.get("page"));

  const canReset = useCan(UserPermissions.resetPassword);
  const canRevoke = useCan(UserPermissions.revokeSessions);

  const [pending, setPending] = useState<Pending | undefined>(undefined);
  const [announcement, setAnnouncement] = useState("");
  const [queued, setQueued] = useState<string | undefined>(undefined);
  const triggers = useRef(new Map<string, HTMLButtonElement>());
  const returnFocus = useRef<HTMLElement | null>(null);

  const users = useUsers(requested ?? 1);

  if (requested === undefined) {
    return <Navigate to={pathname} replace />;
  }

  function open(user: UserRow, action: UserAction) {
    setAnnouncement("");
    returnFocus.current = triggers.current.get(user.userId) ?? null;
    setPending({ user, action, open: true });
  }

  return (
    <div className="flex flex-col gap-4">
      <p role="status" className="text-sm empty:hidden">
        {announcement}
      </p>

      <UsersRegion
        query={users}
        pathname={pathname}
        canReset={canReset}
        canRevoke={canRevoke}
        triggers={triggers}
        onAction={open}
      />

      {pending === undefined ? null : (
        <UserActionDialog
          key={`${pending.user.userId}:${pending.action}`}
          open={pending.open}
          user={pending.user}
          action={pending.action}
          returnFocus={returnFocus}
          onClose={() => {
            setPending({ ...pending, open: false });
          }}
          onDone={(message) => {
            setQueued(message);
            setPending({ ...pending, open: false });
          }}
          onClosed={() => {
            setPending(undefined);

            if (queued !== undefined) {
              setAnnouncement(queued);
              setQueued(undefined);
            }
          }}
        />
      )}
    </div>
  );
}

interface UsersRegionProps {
  readonly query: ReturnType<typeof useUsers>;
  readonly pathname: string;
  readonly canReset: boolean;
  readonly canRevoke: boolean;
  readonly triggers: RefObject<Map<string, HTMLButtonElement>>;
  readonly onAction: (user: UserRow, action: UserAction) => void;
}

/**
 * The states (§11): skeleton on the first load; the current rows kept on screen
 * while the next page loads; the failure stated with a retry; and two different
 * empties — no users at all, or a page past the end, which is a valid answer
 * and not an error.
 */
function UsersRegion({ query, pathname, canReset, canRevoke, triggers, onAction }: UsersRegionProps) {
  if (query.data === undefined) {
    if (query.isError) {
      return (
        <ErrorState
          message={query.error instanceof ApiError ? query.error.message : "The users could not be loaded."}
          onRetry={() => {
            void query.refetch();
          }}
        />
      );
    }

    return (
      <div role="group" aria-label="Loading users" aria-busy="true" className="flex flex-col gap-2">
        {[0, 1, 2].map((row) => (
          <Skeleton key={row} className="h-12 w-full" />
        ))}
      </div>
    );
  }

  const { users, page, hasMore } = query.data;

  if (users.length === 0) {
    return page === 1 ? (
      <EmptyState title="No users" description="Nobody has been added yet." />
    ) : (
      <EmptyState
        title="This page is past the end"
        description="There are no users on this page."
        action={
          <Link to={pathname} className={buttonVariants({ variant: "outline" })}>
            Go to page 1
          </Link>
        }
      />
    );
  }

  const columns: DataTableColumn<UserRow>[] = [
    {
      header: "User",
      className: "whitespace-normal",
      cell: (user) => (
        <div className="flex flex-col">
          <span className="font-medium">{user.displayName}</span>
          <span className="text-sm text-muted-foreground">{user.email ?? "No email address"}</span>
        </div>
      ),
    },
  ];

  // Offered per permission; with neither, there is no Actions column at all.
  if (canReset || canRevoke) {
    columns.push({
      header: "Actions",
      className: "w-12 text-right",
      cell: (user) => (
        <DropdownMenu>
          <DropdownMenuTrigger
            ref={(element: HTMLButtonElement | null) => {
              if (element === null) {
                triggers.current.delete(user.userId);
              } else {
                triggers.current.set(user.userId, element);
              }
            }}
            render={<Button variant="ghost" size="icon" aria-label={actionsLabel(user)} />}
          >
            <MoreHorizontalIcon aria-hidden="true" />
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            {canReset ? (
              <DropdownMenuItem
                onClick={() => {
                  onAction(user, "reset-password");
                }}
              >
                Reset password
              </DropdownMenuItem>
            ) : null}
            {canRevoke ? (
              <DropdownMenuItem
                onClick={() => {
                  onAction(user, "sign-out-everywhere");
                }}
              >
                Sign out everywhere
              </DropdownMenuItem>
            ) : null}
          </DropdownMenuContent>
        </DropdownMenu>
      ),
    });
  }

  return (
    <>
      <DataTable caption="Users" rows={users} rowKey={(user) => user.userId} columns={columns} />

      <Pagination aria-label="Users pages">
        <PaginationContent className="w-full justify-between gap-2">
          <PaginationItem>
            {page > 1 ? (
              <Link to={{ pathname, search: searchFor(page - 1) }} className={buttonVariants({ variant: "outline" })}>
                <ChevronLeftIcon aria-hidden="true" />
                Previous
              </Link>
            ) : null}
          </PaginationItem>
          <PaginationItem className="text-sm text-muted-foreground">Page {page}</PaginationItem>
          <PaginationItem>
            {hasMore ? (
              <Link to={{ pathname, search: searchFor(page + 1) }} className={buttonVariants({ variant: "outline" })}>
                Next
                <ChevronRightIcon aria-hidden="true" />
              </Link>
            ) : null}
          </PaginationItem>
        </PaginationContent>
      </Pagination>
    </>
  );
}
