import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { listUsers } from "../api/listUsers";
import { userKeys } from "./userKeys";

/**
 * One page of the Users list. keepPreviousData, as every paginated list uses
 * (§11): moving between pages keeps the current rows on screen until the next
 * ones arrive, rather than falling back to a skeleton.
 */
export function useUsers(page: number) {
  return useQuery({
    queryKey: userKeys.list({ page }),
    queryFn: ({ signal }) => listUsers(page, signal),
    placeholderData: keepPreviousData,
  });
}
