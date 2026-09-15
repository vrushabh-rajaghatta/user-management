// Control: shared/auth is where effective permissions are read.
export const holds = (principal: { permissions: readonly string[] }, code: string) => principal.permissions.includes(code);
