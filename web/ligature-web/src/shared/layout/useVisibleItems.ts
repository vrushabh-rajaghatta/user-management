import { useCallback } from "react";
import { useAuthSession } from "@/shared/auth/useAuthSession";
import type { NavigationItem } from "./navigation";

/**
 * THE visibility rule for navigation, used by both levels (§5). The shell
 * decides whether an area is offered by asking this for the area's items, and
 * the area's own navigation lists exactly what this returns — one function, so
 * the two cannot disagree.
 *
 * `can` answers "should this be visible", never "is this permitted" (§9).
 */
export function useVisibleItems() {
  const { can } = useAuthSession();

  return useCallback(
    (items: readonly NavigationItem[]) =>
      items.filter((item) => item.permission === undefined || can(item.permission)),
    [can],
  );
}
