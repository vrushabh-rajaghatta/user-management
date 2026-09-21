import { api } from "@/shared/api/client";
import { mySessionsSchema } from "../schemas/mySessions";

/** SES-Q2: GET /api/account/sessions — the caller's own, no permission. */
export const listMySessions = (signal?: AbortSignal) =>
  api.get("/api/account/sessions", { response: mySessionsSchema, signal });
