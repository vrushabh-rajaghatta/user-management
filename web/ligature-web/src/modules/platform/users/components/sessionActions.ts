import type { UserSession } from "../schemas/sessions";

// RED-TEST STUB: never offers Revoke.
// eslint-disable-next-line @typescript-eslint/no-unused-vars -- red-test stub
export function revokeOffered(_input: { readonly session: UserSession; readonly holdsRevoke: boolean }): boolean {
  return false;
}
