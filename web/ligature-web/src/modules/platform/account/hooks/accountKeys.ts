/** Query keys for the caller's own account. */
export const accountKeys = {
  /** SES-Q2: the caller's own active sessions. */
  mySessions: ["account", "sessions"] as const,
};
