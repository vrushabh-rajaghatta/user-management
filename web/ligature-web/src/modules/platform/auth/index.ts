/**
 * This module's public surface (docs/frontend-architecture.md §6): hooks and
 * components, never API operations. The account module ends a session through
 * useEndSession here, which keeps one copy of that sequence, and signs out
 * everywhere through useSignOutEverywhere, because session lifecycle is this
 * module's.
 */
export { SignOutButton } from "./components/SignOutButton";
export { SessionNotEndedError, useEndSession } from "./hooks/useEndSession";
export { useSignOutEverywhere } from "./hooks/useSignOutEverywhere";
export { SIGN_IN_PATH } from "./paths";
