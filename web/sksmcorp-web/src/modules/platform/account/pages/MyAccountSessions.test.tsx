import { screen, waitFor, within } from "@testing-library/react";
import { delay, http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { appRoutes } from "@/app/router";
import { expectNoAccessibilityViolations } from "@/test/axe";
import { server } from "@/test/msw/server";
import { renderWithApp } from "@/test/renderWithApp";
import { TestSessionSource } from "@/test/sessions";

/**
 * SES-Q2 on the My account page (docs/requirements.md, "SES-Q2 GetMySessions
 * on the My account page", MS-7 to MS-10).
 *
 * The list belongs to every signed-in caller: no permission gates it. It is
 * read-only — no per-row action — and it is read again after the two things
 * on this page that end other sessions succeed. Which row is the caller's own
 * is the SERVER's `current`, only ever rendered.
 */

const at = (path: string) => new URL(path, window.location.origin).href;

const SESSIONS = at("/api/account/sessions");
const CHANGE = at("/api/account/change-password");
const EVERYWHERE = at("/api/account/sign-out-everywhere");

interface Session {
  sessionId: string;
  createdAt: string;
  lastActivityAt: string;
  expiresAt: string;
  idleExpiresAt: string;
  ipAddress: string | null;
  userAgent: string | null;
  current: boolean;
}

const HERE: Session = {
  sessionId: "f6000000-0000-4000-8000-0000000e0001",
  createdAt: "2026-09-19T09:15:00Z",
  lastActivityAt: "2026-09-19T11:52:00Z",
  expiresAt: "2026-09-19T17:15:00Z",
  idleExpiresAt: "2026-09-19T12:07:00Z",
  ipAddress: "192.168.65.1",
  userAgent: "Mozilla/5.0 (Macintosh) This browser",
  current: true,
};

const ELSEWHERE: Session = {
  sessionId: "f6000000-0000-4000-8000-0000000e0002",
  createdAt: "2026-09-19T08:05:00Z",
  lastActivityAt: "2026-09-19T10:40:00Z",
  expiresAt: "2026-09-19T16:05:00Z",
  idleExpiresAt: "2026-09-19T10:55:00Z",
  ipAddress: null,
  userAgent: null,
  current: false,
};

interface Backend {
  sessions: Session[];
  readonly reads: () => number;
}

function backend(
  options: {
    read?: () => Response | Promise<Response>;
    change?: () => Response;
    everywhere?: () => Response;
  } = {},
): Backend {
  let reads = 0;
  const state: Backend = { sessions: [HERE, ELSEWHERE], reads: () => reads };

  server.use(
    http.get(SESSIONS, () => {
      reads += 1;
      return options.read !== undefined ? options.read() : HttpResponse.json({ sessions: state.sessions });
    }),
    http.post(CHANGE, () => {
      if (options.change !== undefined) {
        return options.change();
      }
      state.sessions = state.sessions.filter((x) => x.current);
      return new HttpResponse(null, { status: 204 });
    }),
    http.post(EVERYWHERE, () => {
      if (options.everywhere !== undefined) {
        return options.everywhere();
      }
      state.sessions = state.sessions.filter((x) => x.current);
      return new HttpResponse(null, { status: 204 });
    }),
  );

  return state;
}

/** Authenticated with NO permissions: the list is every caller's (MY1). */
function render() {
  const source = new TestSessionSource({ status: "authenticated", principal: { permissions: [] } });

  return renderWithApp(appRoutes, { path: "/account", source });
}

const region = () => screen.getByRole("region", { name: "Sessions" });

async function table() {
  await screen.findByRole("heading", { level: 1, name: "My account" });
  return within(region()).findByRole("table");
}

async function rowOf(text: string) {
  const cell = await within(await table()).findByText(text);
  const row = cell.closest("tr");

  if (row === null) {
    throw new Error(`No row shows ${text}.`);
  }

  return row;
}

type User = ReturnType<typeof renderWithApp>["user"];

async function signOutOthers(user: User) {
  await user.click(within(region()).getByRole("button", { name: "Sign out other sessions" }));
  const dialog = await screen.findByRole("dialog", { name: "Sign out other sessions?" });
  await user.click(within(dialog).getByRole("button", { name: "Sign out other sessions" }));
}

// ---------------------------------------------------------------- MS-7

describe("the list of my sessions", () => {
  it("lists every session with Signed in, Last active, IP address and Browser, to a caller holding no permission", async () => {
    const state = backend();
    render();

    const list = await table();

    for (const header of ["Signed in", "Last active", "IP address", "Browser"]) {
      expect(within(list).getByRole("columnheader", { name: header })).toBeInTheDocument();
    }

    const rows = within(list).getAllByRole("row").slice(1);
    expect(rows).toHaveLength(2);
    expect(rows[0]).toHaveTextContent("192.168.65.1");
    expect(rows[0]).toHaveTextContent("Mozilla/5.0 (Macintosh) This browser");
    expect(within(rows[1] as HTMLElement).getAllByText("—")).toHaveLength(2);

    expect(state.reads()).toBe(1);
  });

  it("marks the server's current session as This session, and only that one", async () => {
    backend();
    render();

    const here = await rowOf("192.168.65.1");

    expect(within(here).getByText("This session")).toBeInTheDocument();
    expect(within(region()).getAllByText("This session")).toHaveLength(1);
  });

  it("offers no action on any row: the two section buttons are the only ones", async () => {
    backend();
    render();
    await table();

    expect(
      within(region())
        .getAllByRole("button")
        .map((button) => button.textContent),
    ).toStrictEqual(["Sign out other sessions", "Sign out everywhere"]);
  });

  it("sits above the two buttons", async () => {
    backend();
    render();

    const list = await table();
    const button = within(region()).getByRole("button", { name: "Sign out other sessions" });

    expect(list.compareDocumentPosition(button) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });
});

// ---------------------------------------------------------------- MS-8

describe("when the list cannot be read", () => {
  it("says so with Try again, and reads again when asked", async () => {
    let calls = 0;
    backend({
      read: () => {
        calls += 1;
        return calls === 1
          ? HttpResponse.json({ error: "The request could not be completed." }, { status: 400 })
          : HttpResponse.json({ sessions: [HERE] });
      },
    });
    const { user } = render();
    await screen.findByRole("heading", { level: 1, name: "My account" });

    expect(await within(region()).findByText("The request could not be completed.")).toBeInTheDocument();

    await user.click(within(region()).getByRole("button", { name: "Try again" }));
    expect(await within(region()).findByText("192.168.65.1")).toBeInTheDocument();
  });

  it("leaves both buttons working", async () => {
    backend({ read: () => HttpResponse.json({ error: "The request could not be completed." }, { status: 400 }) });
    const { user } = render();
    await screen.findByRole("heading", { level: 1, name: "My account" });
    await within(region()).findByText("The request could not be completed.");

    await signOutOthers(user);

    await waitFor(() => {
      expect(screen.getAllByRole("status").some((x) => x.textContent.includes("Your other sessions have been signed out."))).toBe(
        true,
      );
    });
  });
});

// ---------------------------------------------------------------- MS-9

describe("reading the list again", () => {
  it("reads it again after Sign out other sessions, leaving this session alone in the list", async () => {
    const state = backend();
    const { user } = render();
    await rowOf("192.168.65.1");
    const before = state.reads();

    await signOutOthers(user);

    await waitFor(() => {
      expect(state.reads()).toBeGreaterThan(before);
    });
    await waitFor(() => {
      expect(within(region()).getAllByRole("row").slice(1)).toHaveLength(1);
    });
    expect(within(region()).getByText("This session")).toBeInTheDocument();
  });

  it("reads it again after a password change", async () => {
    const state = backend();
    const { user } = render();
    await rowOf("192.168.65.1");
    const before = state.reads();

    const form = screen.getByRole("region", { name: "Change password" });
    await user.type(within(form).getByLabelText("Current password"), "old secret");
    await user.type(within(form).getByLabelText("New password"), "new secret");
    await user.type(within(form).getByLabelText("Confirm new password"), "new secret");
    await user.click(within(form).getByRole("button", { name: "Change password" }));

    await waitFor(() => {
      expect(state.reads()).toBeGreaterThan(before);
    });
    await waitFor(() => {
      expect(within(region()).getAllByRole("row").slice(1)).toHaveLength(1);
    });
  });

  it("does not read it again when Sign out other sessions is refused", async () => {
    const state = backend({
      everywhere: () => HttpResponse.json({ error: "The request could not be completed." }, { status: 400 }),
    });
    const { user } = render();
    await rowOf("192.168.65.1");
    const before = state.reads();

    await signOutOthers(user);

    // A refusal is shown in the confirmation, which stays open (M9).
    const dialog = screen.getByRole("dialog", { name: "Sign out other sessions?" });
    await within(dialog).findByText("The request could not be completed.");
    await delay(50);

    expect(state.reads()).toBe(before);
  });
});

// ---------------------------------------------------------------- MS-10

describe("accessibility", () => {
  it("has no violations with the list loaded", async () => {
    backend();
    render();
    await rowOf("192.168.65.1");

    await expectNoAccessibilityViolations(document.body);
  });

  it("has no violations while the list loads, and when its read failed", async () => {
    backend({
      read: async () => {
        await delay(100);
        return HttpResponse.json({ error: "The request could not be completed." }, { status: 400 });
      },
    });
    render();
    await screen.findByRole("heading", { level: 1, name: "My account" });

    await within(region()).findByRole("group", { name: "Loading your sessions" });
    await expectNoAccessibilityViolations(document.body);

    await within(region()).findByText("The request could not be completed.");
    await expectNoAccessibilityViolations(document.body);
  });
});
