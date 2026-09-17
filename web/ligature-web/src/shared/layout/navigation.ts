import type { PermissionCode } from "@/shared/auth/permissions";

/**
 * The application's navigation, as DATA (docs/frontend-architecture.md §5).
 *
 * Two levels, one source. The shell lists AREAS in labelled groups; an area
 * lists its pages in its own navigation beside them. Both levels read the SAME
 * area object and filter its items with the same visibility rule, which is what
 * keeps them from disagreeing about what is available.
 *
 * Modules export areas; app/ composes them; the shell renders them. The shell
 * therefore never learns what an area means — "Administration" originates in
 * the module that owns that vocabulary, which keeps shared/ free of business
 * meaning (§3).
 */

export interface NavigationItem {
  readonly label: string;
  readonly to: string;

  /**
   * Visibility only. Hiding an entry a caller cannot use is presentation, never
   * access control: the route behind it guards itself, and the server
   * authorises the operation either way (§9).
   */
  readonly permission?: PermissionCode;
}

/**
 * A navigable area with pages of its own. It has NO permission: it is visible
 * when at least one of its items is.
 */
export interface NavigationArea {
  /** Its entry in the shell. */
  readonly label: string;

  /** The area's root. The entry is current anywhere beneath it. */
  readonly to: string;

  /** Names the area's own navigation, and labels its landmark. */
  readonly title: string;

  readonly description?: string;

  /** Real destinations only: an item is listed once its page exists. */
  readonly items: readonly NavigationItem[];
}

export interface NavigationGroup {
  readonly label: string;
  readonly areas: readonly NavigationArea[];
}
