import { RolePermissions } from "@/modules/platform/roles";
import { useCan } from "@/shared/auth/useCan";
import { UserPermissions } from "../permissions";
import type { UserRow } from "../schemas/users";
import type { UserAction } from "./UserActionDialog";

/**
 * THE action matrix (docs/requirements.md, "USR-C4 / USR-C5 — the Users-table
 * UI", as amended; and "USR-Q1 GetUser v2 and the User detail page", G5). The
 * Users table and the User detail page both ask this, so they cannot disagree
 * about what a user is offered — one source for the rules frozen across those
 * stories.
 */

/** What the caller holds, as far as the actions are concerned. */
export interface UserActionPermissions {
  readonly resend: boolean;
  readonly reset: boolean;
  readonly revoke: boolean;

  /** user.deactivate and user.reactivate (USR-C4, USR-C5). */
  readonly deactivate: boolean;
  readonly reactivate: boolean;

  /** role.read (AUT-Q2), owned by the roles module (RA8): whether the caller may see a user's role assignments. */
  readonly manageRoles: boolean;

  /** user.update (USR-C2): whether the caller may edit a user's names. */
  readonly editProfile: boolean;

  /** user.update (USR-C3): whether the caller may change a user's email address. */
  readonly changeEmail: boolean;
}

/** A row action: a confirmed command, or Manage roles, Edit profile or Change email, which open their own dialogs. */
export type RowAction = UserAction | "manage-roles" | "edit-profile" | "change-email";

export const ACTION_LABEL: Record<RowAction, string> = {
  "resend-activation": "Resend activation link",
  "reset-password": "Reset password",
  "sign-out-everywhere": "Sign out everywhere",
  deactivate: "Deactivate",
  reactivate: "Reactivate",
  "manage-roles": "Manage roles",
  "edit-profile": "Edit profile",
  "change-email": "Change email",
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
 * An inactive row offers only Reactivate, Manage roles and Edit profile (the
 * USR-C2 UI's amendment, U4): Resend and Reset
 * would be refused, and Sign out everywhere would change nothing, since
 * deactivation already revoked every session. Whether a row is the caller is
 * not known here and not guessed (U4): Deactivate follows the matrix on every
 * active row, and the server refuses the caller's own.
 */
export function getUserActions({
  user,
  can,
}: {
  readonly user: Pick<UserRow, "status" | "activationPending">;
  readonly can: UserActionPermissions;
}): RowAction[] {
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

  // Every row, active or inactive (USR-C2 G7; the USR-C2 UI amends the
  // USR-C4/C5 matrix, U4): a departed person's name may still need correcting.
  if (can.editProfile) {
    actions.push("edit-profile");
  }

  // Every row, active or inactive (USR-C3, CE8): an inactive user's address is
  // what USR-C5's email refusal is remedied by.
  if (can.changeEmail) {
    actions.push("change-email");
  }

  // Last, apart from the everyday actions: it ends the person's access.
  if (user.status === "Active" && can.deactivate) {
    actions.push("deactivate");
  }

  return actions;
}

/** The caller's action permissions, from the session: the one place they are read. */
export function useUserActionPermissions(): UserActionPermissions {
  return {
    resend: useCan(UserPermissions.create),
    reset: useCan(UserPermissions.resetPassword),
    revoke: useCan(UserPermissions.revokeSessions),
    deactivate: useCan(UserPermissions.deactivate),
    reactivate: useCan(UserPermissions.reactivate),
    manageRoles: useCan(RolePermissions.read),
    editProfile: useCan(UserPermissions.update),
    changeEmail: useCan(UserPermissions.update),
  };
}

/** Whether the caller holds any action permission at all. */
export function holdsAnyAction(can: UserActionPermissions): boolean {
  return Object.values(can).some(Boolean);
}
