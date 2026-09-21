// Control: shared/auth owns the return path, and validating it is its job.
export const destination = (state: { returnTo?: string }) => state.returnTo ?? "/";
