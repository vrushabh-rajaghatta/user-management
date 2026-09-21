import { QueryClientProvider } from "@tanstack/react-query";
import { act, render, screen } from "@testing-library/react";
import type { ReactNode } from "react";
import { describe, expect, it } from "vitest";
import { createQueryClient } from "@/app/queryClient";
import { TestSessionSource } from "@/test/sessions";
import type { AuthSessionSource } from "./AuthSession";
import { AuthProvider } from "./AuthProvider";
import { Can } from "./Can";
import { definePermission } from "./permissions";
import { useAuthSession } from "./useAuthSession";
import { useCan } from "./useCan";

/**
 * Visibility is a presentation question (docs/frontend-architecture.md §9).
 * These tests prove what is SHOWN; nothing here, or anywhere in the client,
 * grants anything. The server authorizes every protected operation.
 */

const create = definePermission("user.create");
const unlock = definePermission("user.unlock");
const deactivate = definePermission("user.deactivate");
const tenant = { type: "Tenant", id: "t1" } as const;
const otherTenant = { type: "Tenant", id: "t2" } as const;

function withSession(source: AuthSessionSource, children: ReactNode) {
  return render(
    <QueryClientProvider client={createQueryClient()}>
      <AuthProvider source={source}>{children}</AuthProvider>
    </QueryClientProvider>,
  );
}

const principal = {
  permissions: [{ code: create }, { code: unlock, scope: tenant }],
};

function Probe({ label, check }: { label: string; check: Parameters<ReturnType<typeof useAuthSession>["can"]> }) {
  const { can, permissionState } = useAuthSession();

  return (
    <p>
      {label}: {permissionState(...check)} {can(...check) ? "visible" : "hidden"}
    </p>
  );
}

describe("permission visibility", () => {
  it("shows a capability while authorization is unknown, without claiming it is allowed", async () => {
    withSession(
      new TestSessionSource({ status: "authenticated", principal: null }),
      <>
        <Probe label="create" check={[create]} />
        <Can permission={create}>
          <button type="button">Create user</button>
        </Can>
      </>,
    );

    expect(await screen.findByText("create: unknown visible")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Create user" })).toBeInTheDocument();
  });

  it("shows an allowed capability and hides a denied one", async () => {
    withSession(
      new TestSessionSource({ status: "authenticated", principal }),
      <>
        <Probe label="create" check={[create]} />
        <Probe label="deactivate" check={[deactivate]} />
        <Can permission={create}>
          <button type="button">Create user</button>
        </Can>
        <Can permission={deactivate}>
          <button type="button">Deactivate user</button>
        </Can>
      </>,
    );

    expect(await screen.findByText("create: allowed visible")).toBeInTheDocument();
    expect(screen.getByText("deactivate: denied hidden")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Create user" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Deactivate user" })).toBeNull();
  });

  it("matches scope exactly: a scoped grant says nothing about another scope, or about global", async () => {
    withSession(
      new TestSessionSource({ status: "authenticated", principal }),
      <>
        <Probe label="unlock in t1" check={[unlock, tenant]} />
        <Probe label="unlock in t2" check={[unlock, otherTenant]} />
        <Probe label="unlock globally" check={[unlock]} />
        <Probe label="create in t1" check={[create, tenant]} />
      </>,
    );

    expect(await screen.findByText("unlock in t1: allowed visible")).toBeInTheDocument();
    expect(screen.getByText("unlock in t2: denied hidden")).toBeInTheDocument();
    expect(screen.getByText("unlock globally: denied hidden")).toBeInTheDocument();

    // How a global grant relates to a scoped check is the server's answer
    // (B6), not a rule the client invents. Until then, matching is exact.
    expect(screen.getByText("create in t1: denied hidden")).toBeInTheDocument();
  });

  it("re-renders useCan when effective permissions become known", async () => {
    const source = new TestSessionSource();

    function Deactivate() {
      return useCan(deactivate) ? <button type="button">Deactivate user</button> : null;
    }

    withSession(source, <Deactivate />);

    expect(screen.getByRole("button", { name: "Deactivate user" })).toBeInTheDocument();

    await act(async () => {
      source.settle({ status: "authenticated", principal });
      await Promise.resolve();
    });

    expect(screen.queryByRole("button", { name: "Deactivate user" })).toBeNull();
  });
});
