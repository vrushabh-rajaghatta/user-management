import { api } from "@/shared/api/client";

/**
 * SES-C2 over HTTP. The session named by the carrier this request presents is
 * revoked, and the cookie is cleared by the host. There is no body either way:
 * reporting whether this call was the one that revoked it would distinguish
 * "already signed out" from "not yours".
 *
 * "return", because its 401 means there was no live session — an ordinary
 * answer here, not evidence about some other session.
 */
export const signOut = () => api.post("/api/auth/sign-out", { unauthorized: "return" });
