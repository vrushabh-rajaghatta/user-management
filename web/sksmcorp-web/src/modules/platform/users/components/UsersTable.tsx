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
import { DataTable, type DataTableColumn } from "@/shared/components/DataTable";
import { EmptyState } from "@/shared/components/EmptyState";
import { ErrorState } from "@/shared/components/ErrorState";
import { useUsers } from "../hooks/useUsers";
import type { UserRow } from "../schemas/users";
import { ChangeEmailDialog } from "./ChangeEmailDialog";
import { EditProfileDialog } from "./EditProfileDialog";
import { ManageRolesDialog } from "./ManageRolesDialog";
import { UserActionDialog, type UserAction } from "./UserActionDialog";
import {
  ACTION_LABEL,
  getUserActions,
  holdsAnyAction,
  type RowAction,
  type UserActionPermissions,
  useUserActionPermissions,
} from "./userActions";

/** The largest page the server can accept: its page parameter is an int (USR-Q2). */
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
 * USR-Q2's table (docs/requirements.md; docs/frontend-architecture.md §11).
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

  const can = useUserActionPermissions();

  const [pending, setPending] = useState<Pending | undefined>(undefined);
  const [managing, setManaging] = useState<{ user: UserRow; open: boolean } | undefined>(undefined);
  const [editing, setEditing] = useState<{ user: UserRow; open: boolean } | undefined>(undefined);
  const [changing, setChanging] = useState<{ user: UserRow; open: boolean } | undefined>(undefined);
  const [announcement, setAnnouncement] = useState("");
  const [queued, setQueued] = useState<string | undefined>(undefined);
  const triggers = useRef(new Map<string, HTMLButtonElement>());
  const returnFocus = useRef<HTMLElement | null>(null);

  const users = useUsers(requested ?? 1);

  if (requested === undefined) {
    return <Navigate to={pathname} replace />;
  }

  function open(user: UserRow, action: RowAction) {
    setAnnouncement("");
    returnFocus.current = triggers.current.get(user.userId) ?? null;

    if (action === "manage-roles") {
      setManaging({ user, open: true });
    } else if (action === "edit-profile") {
      setEditing({ user, open: true });
    } else if (action === "change-email") {
      setChanging({ user, open: true });
    } else {
      setPending({ user, action, open: true });
    }
  }

  return (
    <div className="flex flex-col gap-4">
      <p role="status" className="text-sm empty:hidden">
        {announcement}
      </p>

      <UsersRegion
        query={users}
        pathname={pathname}
        can={can}
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

      {editing === undefined ? null : (
        <EditProfileDialog
          key={editing.user.userId}
          open={editing.open}
          user={editing.user}
          returnFocus={returnFocus}
          onClose={() => {
            setEditing({ ...editing, open: false });
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

      {changing === undefined ? null : (
        <ChangeEmailDialog
          key={changing.user.userId}
          open={changing.open}
          user={changing.user}
          returnFocus={returnFocus}
          onClose={() => {
            setChanging({ ...changing, open: false });
          }}
          onSaved={(message) => {
            setQueued(message);
          }}
          onClosed={() => {
            setChanging(undefined);

            if (queued !== undefined) {
              setAnnouncement(queued);
              setQueued(undefined);
            }
          }}
        />
      )}

      {managing === undefined ? null : (
        <ManageRolesDialog
          key={managing.user.userId}
          open={managing.open}
          user={managing.user}
          returnFocus={returnFocus}
          onClose={() => {
            setManaging({ ...managing, open: false });
          }}
          onClosed={() => {
            setManaging(undefined);
          }}
        />
      )}
    </div>
  );
}

interface UsersRegionProps {
  readonly query: ReturnType<typeof useUsers>;
  readonly pathname: string;
  readonly can: UserActionPermissions;
  readonly triggers: RefObject<Map<string, HTMLButtonElement>>;
  readonly onAction: (user: UserRow, action: RowAction) => void;
}

/**
 * The states (§11): skeleton on the first load; the current rows kept on screen
 * while the next page loads; the failure stated with a retry; and two different
 * empties — no users at all, or a page past the end, which is a valid answer
 * and not an error.
 */
function UsersRegion({ query, pathname, can, triggers, onAction }: UsersRegionProps) {
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
          <span className="flex flex-wrap items-center gap-2">
            {/* The User detail page (USR-Q1 GetUser v2, G4), relative to this route. */}
            <Link to={user.userId} className="font-medium underline-offset-4 hover:underline">
              {user.displayName}
            </Link>
            {/* U2: inactive rows only, in words — never colour alone. */}
            {user.status === "Inactive" ? (
              <span className="rounded-sm border px-1.5 py-0.5 text-xs text-muted-foreground">Inactive</span>
            ) : null}
          </span>
          <span className="text-sm text-muted-foreground">{user.email ?? "No email address"}</span>
        </div>
      ),
    },
  ];

  // Offered per permission and, for Resend and Reset, per row: hidden, never
  // disabled. With no permission at all there is no Actions column, and a row
  // with nothing to offer has no button rather than an empty menu.
  if (holdsAnyAction(can)) {
    columns.push({
      header: "Actions",
      className: "w-12 text-right",
      cell: (user) => {
        const actions = getUserActions({ user, can });

        if (actions.length === 0) {
          return null;
        }

        return (
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
              {actions.map((action) => (
                <DropdownMenuItem
                  key={action}
                  onClick={() => {
                    onAction(user, action);
                  }}
                >
                  {ACTION_LABEL[action]}
                </DropdownMenuItem>
              ))}
            </DropdownMenuContent>
          </DropdownMenu>
        );
      },
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
