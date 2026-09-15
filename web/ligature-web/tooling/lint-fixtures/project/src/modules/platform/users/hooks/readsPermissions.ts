// Violation: a module inspects permissions itself instead of using can(), useCan() or <Can>.
export const mayCreate = (principal: { permissions: readonly string[] }) => principal.permissions.includes("user.create");
