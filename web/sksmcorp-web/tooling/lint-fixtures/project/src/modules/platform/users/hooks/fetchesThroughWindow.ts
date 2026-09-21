// Violation: fetch reached through window.
export const load = () => window.fetch("/api/users");
