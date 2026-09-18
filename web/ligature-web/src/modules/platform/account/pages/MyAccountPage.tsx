import { Page } from "@/shared/components/Page";
import { ChangePasswordForm } from "../components/ChangePasswordForm";
import { SessionActions } from "../components/SessionActions";

/**
 * My account (docs/requirements.md, "CRD-C4 and SES-C4 (self) — the My account
 * page"): the first page about the CALLER rather than someone they administer.
 * It needs no permission; the server decides whether each operation applies.
 *
 * Change password is offered to every caller for now (M10): only local
 * identities exist, and the server refuses any other kind with its uniform
 * message. To revisit when external identities arrive.
 */
export function MyAccountPage() {
  return (
    <Page title="My account">
      <section aria-labelledby="change-password-heading" className="flex flex-col gap-4">
        <h2 id="change-password-heading" className="text-lg font-semibold">
          Change password
        </h2>
        <ChangePasswordForm />
      </section>

      <section aria-labelledby="sessions-heading" className="flex flex-col gap-4">
        <h2 id="sessions-heading" className="text-lg font-semibold">
          Sessions
        </h2>
        <SessionActions />
      </section>
    </Page>
  );
}
