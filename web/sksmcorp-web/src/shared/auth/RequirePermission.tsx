import type { ReactNode } from "react";
import { Page } from "@/shared/components/Page";
import type { PermissionCode, PermissionScope } from "./permissions";
import { useCan } from "./useCan";

/**
 * Route authorization (docs/frontend-architecture.md §5, §9).
 *
 * NOT the same mechanism as <Can>, and deliberately a separate component.
 * <Can> decides whether a navigation entry is SHOWN; this decides whether a
 * page may be REACHED. Hiding an entry never protected anything and still does
 * not, and the server authorizes every operation whatever either of them did.
 *
 * A denial renders an EXPLICIT state. Not a blank page, which reads as
 * breakage; not the not-found page, which would lie about the route existing;
 * and not a redirect, which leaves the person wondering what happened.
 *
 * It follows §9's optimistic visibility: while the caller's permissions are
 * unknown the page renders, and the server refuses the operation if it must.
 */
interface RequirePermissionProps {
  readonly permission: PermissionCode;
  readonly scope?: PermissionScope;
  readonly children: ReactNode;
}

export function RequirePermission({ permission, scope, children }: RequirePermissionProps) {
  return useCan(permission, scope) ? children : <NotAvailable />;
}

/**
 * Plain on purpose. It tells the reader this page exists and is not theirs,
 * which is the honest thing to say inside an application where the set of
 * pages is not itself a secret.
 */
function NotAvailable() {
  return (
    <Page title="Not available">
      <p className="text-muted-foreground">
        You do not have access to this page. If you think you should, ask an administrator.
      </p>
    </Page>
  );
}
