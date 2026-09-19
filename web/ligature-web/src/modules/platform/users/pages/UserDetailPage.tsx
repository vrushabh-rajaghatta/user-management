import { useQueryClient } from "@tanstack/react-query";
import { useRef, useState } from "react";
import { useParams } from "react-router";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Skeleton } from "@/components/ui/skeleton";
import { ApiError } from "@/shared/api/errors";
import { ErrorState } from "@/shared/components/ErrorState";
import { useCan } from "@/shared/auth/useCan";
import { Page } from "@/shared/components/Page";
import { EditProfileDialog } from "../components/EditProfileDialog";
import { ManageRolesDialog } from "../components/ManageRolesDialog";
import { UserActionDialog, type UserAction } from "../components/UserActionDialog";
import { UserIdentitiesSection } from "../components/UserIdentitiesSection";
import { UserRolesSection } from "../components/UserRolesSection";
import { UserSessionsSection } from "../components/UserSessionsSection";
import { ACTION_LABEL, getUserActions, type RowAction, useUserActionPermissions } from "../components/userActions";
import { userKeys } from "../hooks/userKeys";
import { useUserProfile } from "../hooks/useUserProfile";
import { UserPermissions } from "../permissions";
import type { UserProfile } from "../schemas/userProfile";
import type { UserRow } from "../schemas/users";

/**
 * The User detail page (docs/requirements.md, "USR-Q1 GetUser v2 and the User
 * detail page"): /admin/users/:userId, reached from the display name in the
 * Users table.
 *
 * COMPOSED FROM INDEPENDENTLY AUTHORISED READS (G2). The route needs user.read
 * (its guard renders the denied state); the page reads GetUser and nothing
 * else — no list read is a prerequisite. The Roles section needs role.read and
 * is not mounted without it, so its AUT-Q2 request is never made. Two
 * administrators can legitimately see different versions of this page.
 *
 * THE SERVER IS THE SOURCE OF TRUTH (G5). The actions are the table's, from
 * the one shared matrix (getUserActions), through the existing dialogs. After
 * any of them succeeds this page re-reads GetUser and invalidates the list;
 * nothing is patched locally.
 */
export function UserDetailPage() {
  const { userId = "" } = useParams();
  const profile = useUserProfile(userId);

  if (profile.data !== undefined) {
    return <UserDetail key={userId} detail={profile.data} />;
  }

  return (
    <Page title="User">
      {profile.isError ? (
        // A refusal, including "The user does not exist." for an unknown user
        // or the System actor, is stated word for word: a not-found that says
        // so, never a redirect.
        <ErrorState
          message={profile.error instanceof ApiError ? profile.error.message : "The user could not be loaded."}
          onRetry={() => {
            void profile.refetch();
          }}
        />
      ) : (
        <div role="group" aria-label="Loading user" aria-busy="true" className="flex flex-col gap-3">
          <Skeleton className="h-6 w-64" />
          <Skeleton className="h-4 w-48" />
        </div>
      )}
    </Page>
  );
}

/** The dialog a chosen action opens; it stays mounted until it has finished closing. */
type Open = { readonly action: RowAction; readonly open: boolean };

function UserDetail({ detail }: { readonly detail: UserProfile }) {
  const client = useQueryClient();
  const can = useUserActionPermissions();

  // IDN-Q1's section needs identity.read, and is not mounted without it — so
  // its request is never made (I5).
  const canReadIdentities = useCan(UserPermissions.readIdentities);
  const canReadSessions = useCan(UserPermissions.readSessions);
  const actions = getUserActions({ user: detail, can });

  const [opened, setOpened] = useState<Open | undefined>(undefined);
  const [announcement, setAnnouncement] = useState("");
  const [queued, setQueued] = useState<string | undefined>(undefined);
  const trigger = useRef<HTMLButtonElement | null>(null);
  const returnFocus = useRef<HTMLElement | null>(null);

  // The dialogs take a list row; this is the same user, as GetUser reads it.
  const user: UserRow = {
    userId: detail.userId,
    displayName: detail.displayName,
    email: detail.email,
    activationPending: detail.activationPending,
    status: detail.status,
  };

  /** G5: the server says what changed. GetUser again, and the list is stale. */
  function reread() {
    void client.invalidateQueries({ queryKey: userKeys.profile(detail.userId) });
    void client.invalidateQueries({ queryKey: userKeys.lists });
  }

  function open(action: RowAction, from: HTMLElement | null) {
    setAnnouncement("");
    returnFocus.current = from;
    setOpened({ action, open: true });
  }

  function close() {
    if (opened !== undefined) {
      setOpened({ ...opened, open: false });
    }
  }

  function closed() {
    setOpened(undefined);

    if (queued !== undefined) {
      setAnnouncement(queued);
      setQueued(undefined);
    }
  }

  const markers = (
    <span className="flex flex-wrap gap-2">
      {/* In words, never colour alone, as the table marks them. */}
      {detail.status === "Inactive" ? (
        <span className="rounded-sm border px-1.5 py-0.5 text-xs text-muted-foreground">Inactive</span>
      ) : null}
      {detail.activationPending ? (
        <span className="rounded-sm border px-1.5 py-0.5 text-xs text-muted-foreground">Pending activation</span>
      ) : null}
    </span>
  );

  return (
    <Page
      title={detail.displayName}
      description={
        <span className="flex flex-col gap-1">
          <span>{`${detail.firstName} ${detail.lastName}`}</span>
          <span>{detail.email ?? "No email address"}</span>
          {markers}
        </span>
      }
      actions={
        actions.length === 0 ? null : (
          <DropdownMenu>
            <DropdownMenuTrigger ref={trigger} render={<Button variant="outline" />}>
              Actions
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
              {actions.map((action) => (
                <DropdownMenuItem
                  key={action}
                  onClick={() => {
                    open(action, trigger.current);
                  }}
                >
                  {ACTION_LABEL[action]}
                </DropdownMenuItem>
              ))}
            </DropdownMenuContent>
          </DropdownMenu>
        )
      }
    >
      <p role="status" className="text-sm empty:hidden">
        {announcement}
      </p>

      {can.manageRoles ? (
        <UserRolesSection
          userId={detail.userId}
          onManage={() => {
            open("manage-roles", document.activeElement instanceof HTMLElement ? document.activeElement : null);
          }}
        />
      ) : null}

      {canReadIdentities ? <UserIdentitiesSection userId={detail.userId} userStatus={detail.status} /> : null}

      {canReadSessions ? <UserSessionsSection userId={detail.userId} /> : null}

      {opened === undefined ? null : opened.action === "manage-roles" ? (
        <ManageRolesDialog
          open={opened.open}
          user={user}
          returnFocus={returnFocus}
          onClose={close}
          onClosed={closed}
          onChanged={reread}
        />
      ) : opened.action === "edit-profile" ? (
        // Its own save already re-reads GetUser and the list (USR-C2 UI).
        <EditProfileDialog
          open={opened.open}
          user={user}
          returnFocus={returnFocus}
          onClose={close}
          onSaved={(message) => {
            setQueued(message);
          }}
          onClosed={closed}
        />
      ) : (
        <UserActionDialog
          key={opened.action}
          open={opened.open}
          user={user}
          action={opened.action satisfies UserAction}
          returnFocus={returnFocus}
          onClose={close}
          onDone={(message) => {
            reread();
            setQueued(message);
            close();
          }}
          onClosed={closed}
        />
      )}
    </Page>
  );
}
