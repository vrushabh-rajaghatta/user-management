const format = new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" });

/**
 * An instant as the application shows it, in the browser's locale and time
 * zone, so the same moment reads the same on every page — the User detail
 * page and My account among them.
 *
 * FORMATTING ONLY. No session, role or other business meaning belongs here:
 * it is shared so that two modules need not depend on each other for it.
 */
export const formatInstant = (instant: string) => format.format(new Date(instant));
