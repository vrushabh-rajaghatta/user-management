/**
 * This module's public surface (docs/frontend-architecture.md §6): hooks and
 * components, never API operations. The account module ends a session through
 * useEndSession here, which keeps one copy of that sequence.
 */
export { SignOutButton } from "./components/SignOutButton";
export { SessionNotEndedError, useEndSession } from "./hooks/useEndSession";
export { SIGN_IN_PATH } from "./paths";
