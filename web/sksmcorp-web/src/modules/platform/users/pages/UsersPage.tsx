import { Link } from "react-router";
import { buttonVariants } from "@/components/ui/button";
import { Can } from "@/shared/auth/Can";
import { Page } from "@/shared/components/Page";
import { UsersTable } from "../components/UsersTable";
import { UserPermissions } from "../permissions";

/**
 * USR-Q2's page: the Users table, and New user (USR-C1) for holders of
 * user.create.
 *
 * <Can> decides what is SHOWN. Every route guards itself and the server
 * authorises every operation either way (§9).
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
    >
      <UsersTable />
    </Page>
  );
}
