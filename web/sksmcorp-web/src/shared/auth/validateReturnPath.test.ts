import { describe, expect, it } from "vitest";
import { validateReturnPath } from "./validateReturnPath";

/**
 * The return path is attacker-influenced: it arrives in router state on the way
 * to sign-in, and following it blindly is an open redirect
 * (docs/frontend-architecture.md §13).
 *
 * React Router's refusal to navigate externally is NOT what these tests
 * exercise. This function is the contract, and it is tested on its own.
 */

const SAFE = "/";

describe("validateReturnPath", () => {
  it.each([
    ["a plain path", "/users/new"],
    ["a path with a query", "/users?step=2&sort=name"],
    ["the root itself", "/"],
    ["a path whose segment merely looks like a host", "/start/evil.example"],
  ])("accepts %s and returns it unchanged", (_, path) => {
    expect(validateReturnPath(path)).toBe(path);
  });

  it("returns the normalised path the URL parser agreed to, not the raw input", () => {
    expect(validateReturnPath("/users/../roles")).toBe("/roles");
  });

  it.each([
    ["undefined", undefined],
    ["null", null],
    ["a number", 42],
    ["an object, as forged router state could supply", { toString: () => "/users" }],
    ["an empty string", ""],
  ])("refuses %s, because a return path must be a non-empty string", (_, value) => {
    expect(validateReturnPath(value)).toBe(SAFE);
  });

  it.each([
    ["a protocol-relative URL", "//evil.example/users"],
    ["a backslash-relative URL, which the parser reads as //", "/\\evil.example/users"],
    ["an absolute http URL", "https://evil.example/users"],
    ["a scheme with no slash at all", "javascript:alert(1)"],
    ["a path with no leading slash", "users/new"],
    ["a backslash anywhere in the path", "/users\\new"],
    ["a tab, which the parser strips before resolving", "/users\tnew"],
    ["a newline, likewise", "/users\nnew"],
    ["a carriage return, likewise", "/users\rnew"],
    ["a delete character", "/usersnew"],
    ["a fragment, which is where emailed credentials travel", "/users#token=secret"],
    ["a bare fragment", "#token=secret"],
  ])("refuses %s and falls back to the safe path", (_, value) => {
    expect(validateReturnPath(value)).toBe(SAFE);
  });

  /**
   * It runs on the path to sign-in. Throwing would turn a hostile value into a
   * broken sign-in page rather than a redirect to the safe path.
   */
  it.each([
    ["a lone percent, which the URL parser rejects", "/%"],
    ["an unpaired surrogate", "/\uD800"],
    ["a very long path", `/${"a".repeat(20_000)}`],
  ])("never throws on %s", (_, value) => {
    expect(() => validateReturnPath(value)).not.toThrow();
  });

  /**
   * The invariant, asserted of the RESULT rather than inferred from the rules
   * above: even if an input reached the construction carrying a fragment, the
   * value is built from the pathname and search, so it cannot contain one.
   */
  it.each([
    "/users/new",
    "/users?step=2",
    "/users#token=secret",
    "//evil.example",
    "https://evil.example/users#token=secret",
    "",
  ])("returns a path with an optional query and never a fragment, for %s", (value) => {
    const result = validateReturnPath(value);

    expect(result.startsWith("/")).toBe(true);
    expect(result).not.toContain("#");
  });
});
