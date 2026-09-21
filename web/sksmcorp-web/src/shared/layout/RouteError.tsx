import { Link } from "react-router";
import { Page } from "@/shared/components/Page";

/**
 * What a route shows when it throws. Fixed text only: nothing of the error is
 * rendered, for the same reason the host returns no detail — an error's message
 * or stack is not written for the person looking at the page, and can carry
 * internal detail.
 */
export function RouteError() {
  return (
    <Page title="Something went wrong">
      <p className="text-muted-foreground">This page could not be shown.</p>
      <p>
        <Link to="/" className="underline underline-offset-4">
          Go to the home page
        </Link>
      </p>
    </Page>
  );
}
