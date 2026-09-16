/**
 * The only thing that decides whether a return path may be followed
 * (docs/frontend-architecture.md §13).
 *
 * The value arrives in router state on the way to sign-in, so a crafted link
 * can set it to anything. Following it unchecked is an open redirect: a
 * convincing sign-in page that sends the visitor onward to somewhere else.
 *
 * React Router refuses to navigate to some of these, and that is NOT this
 * contract. It is a library behaviour that may change, it resolves some
 * malformed inputs rather than rejecting them, and it is not a security
 * boundary. This function is the boundary, and it is tested on its own.
 *
 * It never throws. It runs on the path to sign-in, and a hostile value must
 * produce a redirect to the safe path rather than a broken page.
 */

/** Where a value that cannot be trusted goes instead. */
const SAFE = "/";

/**
 * C0 controls and DEL, tested by code point rather than by a regular
 * expression, which would itself have to contain the control characters.
 *
 * The URL parser strips tab, newline and carriage return before resolving, so a
 * value carrying one is not the path it appears to be.
 */
function hasControlCharacter(value: string): boolean {
  for (const character of value) {
    const code = character.codePointAt(0);

    if (code !== undefined && (code < 0x20 || code === 0x7f)) {
      return true;
    }
  }

  return false;
}

export function validateReturnPath(value: unknown): string {
  if (typeof value !== "string" || value.length === 0) {
    return SAFE;
  }

  // Exactly one leading slash. "//evil.example" is protocol-relative, and the
  // URL parser reads a backslash as a slash, so the backslash form is too.
  if (!value.startsWith("/") || value[1] === "/" || value[1] === "\\") {
    return SAFE;
  }

  if (value.includes("\\") || hasControlCharacter(value)) {
    return SAFE;
  }

  // The fragment is where emailed credentials travel (§13). A return path is
  // never allowed to become somewhere one could be parked.
  if (value.includes("#")) {
    return SAFE;
  }

  let url: URL;

  try {
    url = new URL(value, window.location.origin);
  } catch {
    return SAFE;
  }

  if (url.origin !== window.location.origin) {
    return SAFE;
  }

  // Built from the parsed parts rather than returned as given: the result is
  // the normalised form the parser agreed to, and it cannot carry a fragment
  // even if one had reached this line.
  return `${url.pathname}${url.search}`;
}
