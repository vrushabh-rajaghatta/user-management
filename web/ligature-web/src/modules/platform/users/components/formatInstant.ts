const format = new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" });

/**
 * An instant as the users module shows it, in the browser's locale and time
 * zone. Shared by Manage roles and the User detail page's Roles section, so
 * the same assignment reads the same in both.
 */
export const formatInstant = (instant: string) => format.format(new Date(instant));
