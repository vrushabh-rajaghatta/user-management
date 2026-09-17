import { Link } from "react-router";
import { buttonVariants } from "@/components/ui/button";
import { Can } from "@/shared/auth/Can";
import { Page } from "@/shared/components/Page";
import { UserPermissions } from "../permissions";

/**
 * USR-Q1's page. Until its table is built it is the header and the one action
 * that already exists: New user (USR-C1), offered to holders of user.create.
 * It lists no one rather than pretending to.
 *
 * <Can> decides whether the action is SHOWN. The create user route guards
 * itself, and the server authorises POST /api/users either way (§9).
 */
export function UsersPage() {
  return (
    <Page
      title="Users"
      actions={
        <Can permission={UserPermissions.create}>
          <Link to="/admin/users/new" className={buttonVariants()}>
            New user
          </Link>
        </Can>
      }
    />
  );
}
