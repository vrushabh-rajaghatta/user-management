import { useQuery } from "@tanstack/react-query";
import { listMySessions } from "../api/mySessions";
import { accountKeys } from "./accountKeys";

export function useMySessions() {
  return useQuery({
    queryKey: accountKeys.mySessions,
    queryFn: ({ signal }) => listMySessions(signal),
  });
}
