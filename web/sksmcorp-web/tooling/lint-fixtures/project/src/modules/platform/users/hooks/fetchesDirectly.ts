// Violation: fetch outside the API client.
export const load = () => fetch("/api/users");
