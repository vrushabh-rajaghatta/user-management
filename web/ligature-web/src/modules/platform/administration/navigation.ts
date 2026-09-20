import { rolesNavigation } from "@/modules/platform/roles";
import { usersNavigation } from "@/modules/platform/users";
import type { NavigationArea } from "@/shared/layout/navigation";

/**
 * The Administration area (docs/frontend-architecture.md §5): the platform layer
 * the business modules stand on, not a business module itself.
 *
 * It owns the vocabulary and nothing else. It has no permission of its own —
 * the shell offers it when at least one item is visible — and it lists only
 * real destinations. Audit trail and Notifications join when their read
 * capabilities exist.
 */
export const administrationArea: NavigationArea = {
  label: "Administration",
  to: "/admin",
  title: "Administration",
  description: "Users, authority and evidence. Shared by every module, owned by none.",
  items: [...usersNavigation, ...rolesNavigation],
};
