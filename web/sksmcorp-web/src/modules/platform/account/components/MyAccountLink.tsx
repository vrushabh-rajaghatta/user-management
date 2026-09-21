import { SidebarMenu } from "@/components/ui/sidebar";
import { NavigationEntry } from "@/shared/layout/NavigationEntry";
import { MY_ACCOUNT_PATH } from "../paths";

/**
 * The footer's My account entry (My account, M2). Composed into the shell's
 * footer by app/, beside Sign out, as that button is: the shell may not import
 * a module (docs/frontend-architecture.md §2).
 *
 * Not in the primary navigation. That lists areas shown by permission; this
 * page belongs to every signed-in caller. It is still an ordinary navigation
 * entry — current on /account, and closing the phone sheet when followed.
 */
export function MyAccountLink() {
  return (
    <SidebarMenu>
      <NavigationEntry label="My account" to={MY_ACCOUNT_PATH} />
    </SidebarMenu>
  );
}
