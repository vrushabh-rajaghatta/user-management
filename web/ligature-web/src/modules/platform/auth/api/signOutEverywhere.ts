import { api } from "@/shared/api/client";

/**
 * SES-C4, self form: POST /api/account/sign-out-everywhere. Ends every active
 * session of the caller's account — including this one unless
 * keepCurrentSession is true, in which case the host keeps the cookie too.
 * 204 with no body, including when nothing was active.
 *
 * No reason is sent (My account, M8): the account holder does not justify a
 * routine security action, and the server records its default explanation.
 *
 * "report", the default: a 401 here is evidence that this session has ended,
 * which is exactly what the application's handling of a 401 is for (M9).
 */
export const signOutEverywhere = ({ keepCurrentSession }: { keepCurrentSession: boolean }) =>
  api.post("/api/account/sign-out-everywhere", { body: { keepCurrentSession } });
