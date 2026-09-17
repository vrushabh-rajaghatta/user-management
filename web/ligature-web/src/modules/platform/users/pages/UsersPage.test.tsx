import { act, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { definePermission, type PermissionCode } from "@/shared/auth/permissions";
import { renderWithApp } from "@/test/renderWithApp";
import { TestSessionSource } from "@/test/sessions";
import { UsersPage } from "./UsersPage";

/**
 * USR-Q1's page, before its table (the Administration shell step). It is the
 * page header and the one action that already exists: New user, which is
 * USR-C1 and its own permission. The list itself arrives with the next step.
 */

const CREATE = definePermission("user.create");

/**
 * Renders with the session held UNRESOLVED, then settles it to a caller holding
 * exactly these permissions, before any assertion.
 *
 * Not a convenience. Until a session resolves every permission is unknown, and
 * unknown is shown (§9) — so an assertion that the action IS offered, made
 * before resolution, passes whatever permission gates it. A mutant gating New
 * user on user.read instead of user.create survived exactly that way.
 */
async function render(...codes: PermissionCode[]) {
  const source = new TestSessionSource();

  const result = renderWithApp([{ path: "/admin/users", element: <UsersPage /> }], { path: "/admin/users", source });

  await act(async () => {
    source.settle({ status: "authenticated", principal: { permissions: codes.map((code) => ({ code })) } });
    await Promise.resolve();
  });

  return result;
}

describe("the Users page", () => {
  it("is titled Users", async () => {
    await render();

    expect(await screen.findByRole("heading", { level: 1, name: "Users" })).toBeInTheDocument();
    expect(document.title).toBe("Users · Ligature");
  });

  it("offers New user, linking to the create user page, to a caller who holds user.create", async () => {
    await render(CREATE);

    expect(await screen.findByRole("link", { name: "New user" })).toHaveAttribute("href", "/admin/users/new");
  });

  it("does not offer New user to a caller who does not", async () => {
    await render();

    await screen.findByRole("heading", { level: 1, name: "Users" });

    expect(screen.queryByRole("link", { name: "New user" })).toBeNull();
  });

  /** No table yet: the page does not pretend to list anyone. */
  it("renders no table", async () => {
    await render(CREATE);

    await screen.findByRole("heading", { level: 1, name: "Users" });

    expect(screen.queryByRole("table")).toBeNull();
  });
});
