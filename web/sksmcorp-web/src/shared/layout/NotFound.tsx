import { Link } from "react-router";
import { Page } from "@/shared/components/Page";

export function NotFound() {
  return (
    <Page title="Page not found">
      <p className="text-muted-foreground">There is no page at this address.</p>
      <p>
        <Link to="/" className="underline underline-offset-4">
          Go to the home page
        </Link>
      </p>
    </Page>
  );
}
