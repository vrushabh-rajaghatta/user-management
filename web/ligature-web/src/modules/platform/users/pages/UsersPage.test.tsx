import { screen } from "@testing-library/react";
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

const holding = (...codes: PermissionCode[]) =>
  new TestSessionSource({ status: "authenticated", principal: { permissions: codes.map((code) => ({ code })) } });

function render(source: TestSessionSource) {
  return renderWithApp([{ path: "/admin/users", element: <UsersPage /> }], { path: "/admin/users", source });
}

describe("the Users page", () => {
  it("is titled Users", async () => {
    render(holding());

    expect(await screen.findByRole("heading", { level: 1, name: "Users" })).toBeInTheDocument();
    expect(document.title).toBe("Users · Ligature");
  });

  it("offers New user, linking to the create user page, to a caller who holds user.create", async () => {
    render(holding(CREATE));

    expect(await screen.findByRole("link", { name: "New user" })).toHaveAttribute("href", "/admin/users/new");
  });

  it("does not offer New user to a caller who does not", async () => {
    render(holding());

    await screen.findByRole("heading", { level: 1, name: "Users" });

    expect(screen.queryByRole("link", { name: "New user" })).toBeNull();
  });

  /** No table yet: the page does not pretend to list anyone. */
  it("renders no table", async () => {
    render(holding(CREATE));

    await screen.findByRole("heading", { level: 1, name: "Users" });

    expect(screen.queryByRole("table")).toBeNull();
  });
});
