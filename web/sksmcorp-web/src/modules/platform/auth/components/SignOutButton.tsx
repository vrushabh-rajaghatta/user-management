import { Button } from "@/components/ui/button";
import { useSignOut } from "../hooks/useSignOut";
import { SIGN_IN_PATH } from "../paths";

/**
 * Composed into the application shell by app/, because the shell is shared
 * infrastructure and may not import a module (docs/frontend-architecture.md
 * §2). The shell offers the slot; this module fills it.
 */
export function SignOutButton() {
  const signOut = useSignOut(SIGN_IN_PATH);

  return (
    <Button
      variant="outline"
      onClick={() => {
        void signOut();
      }}
    >
      Sign out
    </Button>
  );
}
