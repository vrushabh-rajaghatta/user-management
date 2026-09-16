// Control: a presentation primitive that is handed an error string and does not
// care where it came from.
export const describedBy = (ids: readonly string[]) => (ids.length === 0 ? undefined : ids.join(" "));
