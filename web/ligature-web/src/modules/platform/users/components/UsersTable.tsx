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
import { ManageRolesDialog } from "./ManageRolesDialog";
import { UserActionDialog, type UserAction } from "./UserActionDialog";

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

interface Allowed {
  readonly resend: boolean;
  readonly reset: boolean;
  readonly revoke: boolean;

  /** user.deactivate and user.reactivate (USR-C4, USR-C5). */
  readonly deactivate: boolean;
  readonly reactivate: boolean;

  /** role.read (AUT-Q2): whether the caller may see a user's role assignments. */
  readonly manageRoles: boolean;
}

/** A row action: a confirmed command, or Manage roles, which opens its own dialog. */
type RowAction = UserAction | "manage-roles";

const LABEL: Record<RowAction, string> = {
  "resend-activation": "Resend activation link",
  "reset-password": "Reset password",
  "sign-out-everywhere": "Sign out everywhere",
  deactivate: "Deactivate",
  reactivate: "Reactivate",
  "manage-roles": "Manage roles",
};

/**
 * What a row offers: the client's whole rule, and nothing broader (USR-Q2
 * amendments 1 and 2; USR-C4/C5 UI, the action matrix).
 *
 * AN AFFORDANCE, NOT AUTHORIZATION. It is derived from the row's status and
 * activationPending and the caller's permissions; the API decides what is
 * accepted, and a row may be stale. activationPending decides between Resend
 * and Reset and is never read as anything more.
 *
 * An inactive row offers only Reactivate and Manage roles: Resend and Reset
 * would be refused, and Sign out everywhere would change nothing, since
 * deactivation already revoked every session. Whether a row is the caller is
 * not known here and not guessed (U4): Deactivate follows the matrix on every
 * active row, and the server refuses the caller's own.
 */
function actionsFor(user: UserRow, can: Allowed): RowAction[] {
  const actions: RowAction[] = [];

  if (user.status === "Inactive") {
    if (can.reactivate) {
      actions.push("reactivate");
    }
  } else {
    if (user.activationPending && can.resend) {
      actions.push("resend-activation");
    }

    if (!user.activationPending && can.reset) {
      actions.push("reset-password");
    }

    if (can.revoke) {
      actions.push("sign-out-everywhere");
    }
  }

  // Offered on every row to a role.read holder: which roles a user holds is
  // not a question the row can answer without asking.
  if (can.manageRoles) {
    actions.push("manage-roles");
  }

  // Last, apart from the everyday actions: it ends the person's access.
  if (user.status === "Active" && can.deactivate) {
    actions.push("deactivate");
  }

  return actions;
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

  const can: Allowed = {
    resend: useCan(UserPermissions.create),
    reset: useCan(UserPermissions.resetPassword),
    revoke: useCan(UserPermissions.revokeSessions),
    deactivate: useCan(UserPermissions.deactivate),
    reactivate: useCan(UserPermissions.reactivate),
    manageRoles: useCan(UserPermissions.readRoles),
  };

  const [pending, setPending] = useState<Pending | undefined>(undefined);
  const [managing, setManaging] = useState<{ user: UserRow; open: boolean } | undefined>(undefined);
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
  readonly can: Allowed;
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
            <span className="font-medium">{user.displayName}</span>
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
  if (can.resend || can.reset || can.revoke || can.deactivate || can.reactivate || can.manageRoles) {
    columns.push({
      header: "Actions",
      className: "w-12 text-right",
      cell: (user) => {
        const actions = actionsFor(user, can);

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
                  {LABEL[action]}
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
