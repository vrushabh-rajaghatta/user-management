import { screen, waitFor } from "@testing-library/react";
import { delay, http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import type { RouteObject } from "react-router";
import { definePermission } from "@/shared/auth/permissions";
import { RequireAuth } from "@/shared/auth/RequireAuth";
import { expectNoAccessibilityViolations } from "@/test/axe";
import { renderWithApp } from "@/test/renderWithApp";
import { server } from "@/test/msw/server";
import { TestSessionSource } from "@/test/sessions";
import { CreateUserPage } from "./CreateUserPage";

/**
 * IDN-Q3 on Create user (docs/requirements.md, "IDN-Q3 CheckUsernameAvailable
 * on Create user", UA-5 to UA-11).
 *
 * Checked when focus leaves Username, only for an identity.read holder — and
 * without it NO request is ever made. A taken answer blocks Create for THAT
 * value only: any edit clears it, and an answer that arrives after the field
 * changed is discarded. A failed check blocks nothing. USR-C1 stays the
 * authority at submit.
 */

const at = (path: string) => new URL(path, window.location.origin).href;

const USERS = at("/api/users");
const AVAILABILITY = at("/api/identities/username-availability");

const CREATED = { userId: "9f1d0e52-0000-4000-8000-000000000001", userIdentityId: "9f1d0e52-0000-4000-8000-000000000002" };

const IN_USE = "This username is already in use.";
const AVAILABLE = "Username available.";

const IDENTITY_READ = definePermission("identity.read");
const USER_CREATE = definePermission("user.create");

const routes: RouteObject[] = [
  {
    element: <RequireAuth signInPath="/sign-in" />,
    children: [{ path: "/users/new", element: <CreateUserPage /> }],
  },
  { path: "/sign-in", element: <h1>Sign in</h1> },
];

interface Backend {
  readonly checks: unknown[];
  readonly creates: () => number;
}

function backend(options: { check?: (username: string) => Response | Promise<Response>; create?: () => Response } = {}) {
  const checks: unknown[] = [];
  let creates = 0;

  server.use(
    http.post(AVAILABILITY, async ({ request }) => {
      const body = (await request.json()) as { username: string };
      checks.push(body);
      return options.check !== undefined
        ? options.check(body.username)
        : HttpResponse.json({ available: body.username !== "v.r" });
    }),
    http.post(USERS, () => {
      creates += 1;
      return options.create !== undefined ? options.create() : HttpResponse.json(CREATED, { status: 201 });
    }),
  );

  return { checks, creates: () => creates } satisfies Backend;
}

function render(codes = [USER_CREATE, IDENTITY_READ]) {
  return renderWithApp(routes, {
    path: "/users/new",
    source: new TestSessionSource({
      status: "authenticated",
      principal: { permissions: codes.map((code) => ({ code })) },
    }),
  });
}

type User = ReturnType<typeof renderWithApp>["user"];

async function fillOthers(user: User) {
  await user.type(await screen.findByLabelText("First name"), "Ada");
  await user.type(screen.getByLabelText("Last name"), "Lovelace");
  await user.type(screen.getByLabelText("Display name"), "Ada Lovelace");
  await user.type(screen.getByLabelText("Email address"), "ada@example.test");
}

/** Types a username and leaves the field, which is when the check runs. */
async function enterUsername(user: User, value: string) {
  const field = await screen.findByLabelText("Username");
  await user.clear(field);
  await user.type(field, value);
  await user.tab();
}

async function create(user: User) {
  await user.click(screen.getByRole("button", { name: "Create user" }));
}

// ---------------------------------------------------------------- UA-5

describe("checking a username", () => {
  it("sends exactly the trimmed username, once, when the field is left", async () => {
    const state = backend();
    const { user } = render();

    await enterUsername(user, "  ada.lovelace  ");

    await waitFor(() => {
      expect(state.checks).toStrictEqual([{ username: "ada.lovelace" }]);
    });
  });

  it("checks nothing for a blank field", async () => {
    const state = backend();
    const { user } = render();

    await enterUsername(user, "   ");
    await delay(50);

    expect(state.checks).toHaveLength(0);
  });
});

// ---------------------------------------------------------------- UA-6

describe("a username that is taken", () => {
  it("says so on the field, and Create sends nothing", async () => {
    const state = backend();
    const { user } = render();
    await fillOthers(user);

    await enterUsername(user, "v.r");
    expect(await screen.findByText(IN_USE)).toBeInTheDocument();

    await create(user);
    await delay(50);

    expect(state.creates()).toBe(0);
  });

  it("stops blocking as soon as the field is edited, and the next Create goes to the server", async () => {
    const state = backend({ check: () => HttpResponse.json({ available: false }) });
    const { user } = render();
    await fillOthers(user);

    await enterUsername(user, "v.r");
    await screen.findByText(IN_USE);

    await user.type(screen.getByLabelText("Username"), "2");

    expect(screen.queryByText(IN_USE)).toBeNull();

    await create(user);

    await waitFor(() => {
      expect(state.creates()).toBe(1);
    });
  });
});

// ---------------------------------------------------------------- UA-7

describe("a username that is available", () => {
  it("says so, and Create is sent", async () => {
    const state = backend();
    const { user } = render();
    await fillOthers(user);

    await enterUsername(user, "ada.lovelace");
    expect(await screen.findByText(AVAILABLE)).toBeInTheDocument();

    await create(user);

    await waitFor(() => {
      expect(state.creates()).toBe(1);
    });
  });

  it("still shows the server's refusal at submit, word for word", async () => {
    backend({
      create: () => HttpResponse.json({ error: "A user identity with this username already exists." }, { status: 400 }),
    });
    const { user } = render();
    await fillOthers(user);

    await enterUsername(user, "ada.lovelace");
    await screen.findByText(AVAILABLE);
    await create(user);

    expect(await screen.findByText("A user identity with this username already exists.")).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------- UA-8

describe("a check that fails", () => {
  it.each([
    ["a 5xx", () => HttpResponse.json({ error: "The request could not be completed." }, { status: 500 })],
    ["a network failure", () => HttpResponse.error()],
    ["a contract violation", () => HttpResponse.json({ taken: true })],
  ])("shows nothing and blocks nothing after %s", async (_, check) => {
    const state = backend({ check });
    const { user } = render();
    await fillOthers(user);

    await enterUsername(user, "ada.lovelace");
    await delay(100);

    expect(screen.queryByText(IN_USE)).toBeNull();
    expect(screen.queryByText(AVAILABLE)).toBeNull();

    await create(user);

    await waitFor(() => {
      expect(state.creates()).toBe(1);
    });
  });
});

// ---------------------------------------------------------------- UA-9

describe("a result that arrives late", () => {
  it("is discarded when the field changed while it was in flight", async () => {
    const state = backend({
      check: async () => {
        await delay(200);
        return HttpResponse.json({ available: false });
      },
    });
    const { user } = render();
    await fillOthers(user);

    await enterUsername(user, "v.r");
    await user.type(screen.getByLabelText("Username"), "2");
    await delay(300);

    expect(screen.queryByText(IN_USE)).toBeNull();

    await create(user);

    await waitFor(() => {
      expect(state.creates()).toBe(1);
    });
  });
});

// ---------------------------------------------------------------- UA-10

describe("without identity.read", () => {
  it("never asks about a username — on blur or on submit — and Create works as before", async () => {
    const state = backend();
    const { user } = render([USER_CREATE]);
    await fillOthers(user);

    await enterUsername(user, "v.r");
    await create(user);

    await waitFor(() => {
      expect(state.creates()).toBe(1);
    });
    expect(state.checks).toHaveLength(0);
    expect(screen.queryByText(IN_USE)).toBeNull();
  });
});

// ---------------------------------------------------------------- UA-11

describe("accessibility", () => {
  // The page is rendered without the application shell, so the audit is of
  // what it renders, as CreateUserPage.test.tsx's own audit is.
  it("has no violations with the in-use message, and with the available message", async () => {
    backend();
    const { container, user } = render();

    await enterUsername(user, "v.r");
    await screen.findByText(IN_USE);
    await expectNoAccessibilityViolations(container);

    await enterUsername(user, "ada.lovelace");
    await screen.findByText(AVAILABLE);
    await expectNoAccessibilityViolations(container);
  });

  it("ties the message to the field", async () => {
    backend();
    const { user } = render();

    await enterUsername(user, "v.r");
    const message = await screen.findByText(IN_USE);

    expect(screen.getByLabelText("Username").getAttribute("aria-describedby")).toContain(message.id);
  });
});
