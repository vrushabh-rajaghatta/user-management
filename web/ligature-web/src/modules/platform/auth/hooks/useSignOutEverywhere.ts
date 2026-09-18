import { useMutation } from "@tanstack/react-query";
import { useNavigate } from "react-router";
import { useAuthSession } from "@/shared/auth/useAuthSession";
import { signOutEverywhere } from "../api/signOutEverywhere";
import { SIGN_IN_PATH } from "../paths";

/**
 * SES-C4, self form, for the My account page (docs/requirements.md, "CRD-C4
 * and SES-C4 (self) — the My account page", M3, M7, M9). Session lifecycle is
 * this module's, so the account module reaches it through this hook and never
 * through the API operation.
 *
 * `{ keepCurrentSession: true }` ends the OTHER sessions; the caller stays.
 * `{ keepCurrentSession: false }` ends this one too: on 204 the client signs
 * out locally (the query cache is cleared) and goes to sign-in.
 *
 * A FAILURE IS NOT A SIGN-OUT (M9). This is deliberately unlike the Sign out
 * button, which leaves whether or not its call succeeded because the visitor
 * asked to leave. Here a failure means nobody knows what was revoked, so
 * authentication state is left exactly as it was and the caller decides what
 * to do next. A 401 is the exception, and it is not handled here: the API
 * boundary reports it, and the application signs the caller out.
 *
 * Local sign-out comes BEFORE the navigation. Signing out unmounts the signed-in
 * page, and with it any unsaved-changes guard, so a password form left
 * half-typed does not ask "Discard changes?" about a session that has ended.
 */
export function useSignOutEverywhere() {
  const { signedOut } = useAuthSession();
  const navigate = useNavigate();

  return useMutation({
    mutationFn: signOutEverywhere,
    onSuccess: async (_, { keepCurrentSession }) => {
      if (keepCurrentSession) {
        return;
      }

      signedOut();

      await navigate(SIGN_IN_PATH, { replace: true });
    },
  });
}
