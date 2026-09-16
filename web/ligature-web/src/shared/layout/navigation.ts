import type { PermissionCode } from "@/shared/auth/permissions";

/**
 * The application's navigation, as DATA (docs/frontend-architecture.md §5).
 *
 * Modules export sections; app/ composes them; the shell renders them. The
 * shell therefore never learns what a section means — "Users" originates in the
 * module that owns that vocabulary, which is what keeps shared/ free of
 * business meaning (§3) while still rendering a navigation area.
 */

export interface NavigationItem {
  readonly label: string;
  readonly to: string;

  /**
   * Visibility only. Hiding an entry a caller cannot use is presentation, never
   * access control: the route behind it is not gated, and the server authorises
   * the command either way (§9).
   */
  readonly permission?: PermissionCode;
}

export interface NavigationSection {
  readonly label: string;
  readonly items: readonly NavigationItem[];
}
