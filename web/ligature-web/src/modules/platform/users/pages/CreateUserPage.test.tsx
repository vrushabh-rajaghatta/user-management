import { act, screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import type { RouteObject } from "react-router";
import { RequireAuth } from "@/shared/auth/RequireAuth";
import { expectNoAccessibilityViolations } from "@/test/axe";
import { renderWithApp } from "@/test/renderWithApp";
import { server } from "@/test/msw/server";
import { TestSessionSource } from "@/test/sessions";
import { CreateUserPage } from "./CreateUserPage";

/**
 * USR-C1 (docs/frontend-architecture.md §7, §9, §12).
 *
 * The endpoint answers 400 for a missing permission AND for invalid input, with
 * the same exception type behind both — so this page must never turn a 400 into
 * a claim about which one happened.
 *
 * It also must not claim the activation email was sent. The command declares the
 * notification; delivery depends on how the deployment is configured and is
 * attempted after the response is returned, so the client cannot know.
 */

const at = (path: string) => new URL(path, window.location.origin).href;

const USERS = at("/api/users");

const CREATED = { userId: "9f1d0e52-0000-4000-8000-000000000001", userIdentityId: "9f1d0e52-0000-4000-8000-000000000002" };

const SUCCESS = "The account has been created. They must activate it before they can sign in.";

const routes: RouteObject[] = [
  {
    element: <RequireAuth signInPath="/sign-in" />,
    children: [{ path: "/users/new", element: <CreateUserPage /> }],
  },
  { path: "/sign-in", element: <h1>Sign in</h1> },
];

function render() {
  return renderWithApp(routes, {
    path: "/users/new",
    source: new TestSessionSource({ status: "authenticated", principal: null }),
  });
}

async function fill(user: ReturnType<typeof renderWithApp>["user"], email = "ada@example.test") {
  await user.type(await screen.findByLabelText("First name"), "Ada");
  await user.type(screen.getByLabelText("Last name"), "Lovelace");
  await user.type(screen.getByLabelText("Display name"), "Ada Lovelace");
  await user.type(screen.getByLabelText("Email address"), email);
  await user.type(screen.getByLabelText("Username"), "ada.lovelace");
  await user.click(screen.getByRole("button", { name: "Create user" }));
}

describe("creating a user", () => {
  it("sends the five fields the host names, and nothing else", async () => {
    let body: unknown;

    server.use(
      http.post(USERS, async ({ request }) => {
        body = await request.json();
        return HttpResponse.json(CREATED, { status: 201 });
      }),
    );

    const { user } = render();

    await fill(user);

    expect(body).toEqual({
      firstName: "Ada",
      lastName: "Lovelace",
      displayName: "Ada Lovelace",
      email: "ada@example.test",
      initialUsername: "ada.lovelace",
    });
  });

  /**
   * The client never copies the server's rules (§12). The address below is one a
   * stricter client-side pattern would refuse; the server decides.
   */
  it("submits an address a stricter rule would have rejected, because the policy is the server's", async () => {
    let requests = 0;

    server.use(
      http.post(USERS, () => {
        requests += 1;
        return HttpResponse.json(CREATED, { status: 201 });
      }),
    );

    const { user } = render();

    await fill(user, "ada+tagged@sub.domain.example");

    expect(await screen.findByText(SUCCESS)).toBeInTheDocument();
    expect(requests).toBe(1);
  });

  it("does not submit until every field has been given", async () => {
    let requests = 0;

    server.use(
      http.post(USERS, () => {
        requests += 1;
        return HttpResponse.json(CREATED, { status: 201 });
      }),
    );

    const { user } = render();

    await user.type(await screen.findByLabelText("First name"), "Ada");
    await user.click(screen.getByRole("button", { name: "Create user" }));

    expect(await screen.findByRole("alert")).toBeInTheDocument();
    expect(requests).toBe(0);
  });
});

describe("after the account is created", () => {
  function created() {
    server.use(http.post(USERS, () => HttpResponse.json(CREATED, { status: 201 })));
  }

  it("says exactly what the backend guarantees", async () => {
    created();

    const { user } = render();

    await fill(user);

    expect(await screen.findByText(SUCCESS)).toBeInTheDocument();
  });

  /**
   * The command emits the activation notification, but the response establishes
   * account creation and nothing about delivery: mail may be unconfigured, and
   * the attempt happens after this response. Saying "we have emailed them" would
   * be telling the administrator something the client cannot know.
   */
  it("never claims an activation link was sent", async () => {
    created();

    const { user } = render();

    await fill(user);

    const page = (await screen.findByText(SUCCESS)).closest("main, div");

    expect(page?.textContent ?? "").not.toMatch(/emailed|e-mail has|link has been sent|we have sent/i);
  });

  it("does not show the identifiers the host returned", async () => {
    created();

    const { user } = render();

    await fill(user);

    expect(await screen.findByText(SUCCESS)).toBeInTheDocument();
    expect(screen.queryByText(/9f1d0e52/)).toBeNull();
  });

  it("clears the form, so the next account does not inherit this one's details", async () => {
    created();

    const { user } = render();

    await fill(user);

    expect(await screen.findByText(SUCCESS)).toBeInTheDocument();
    expect(screen.getByLabelText("First name")).toHaveValue("");
    expect(screen.getByLabelText("Email address")).toHaveValue("");
  });

  it("stays on the page, with no timed redirect", async () => {
    created();

    const { user, router } = render();

    await fill(user);

    expect(await screen.findByText(SUCCESS)).toBeInTheDocument();
    expect(router.state.location.pathname).toBe("/users/new");
  });

  it("announces the outcome, so it is not silent to a screen reader", async () => {
    created();

    const { user } = render();

    await fill(user);

    expect(await screen.findByRole("status")).toHaveTextContent(SUCCESS);
  });
});

describe("when the host refuses", () => {
  it("shows its message word for word", async () => {
    server.use(http.post(USERS, () => HttpResponse.json({ error: "That email address is already in use." }, { status: 400 })));

    const { user } = render();

    await fill(user);

    expect(await screen.findByRole("alert")).toHaveTextContent("That email address is already in use.");
  });

  /**
   * The host returns the SAME 400 for a missing permission and for invalid
   * input, deliberately. Rendering either reading would tell the administrator
   * something this client cannot know.
   */
  it("never explains a 400 as a permission problem", async () => {
    server.use(http.post(USERS, () => HttpResponse.json({ error: "A first name, last name, display name, email address and initial username are required." }, { status: 400 })));

    const { user } = render();

    await fill(user);

    const alert = await screen.findByRole("alert");

    expect(alert.textContent).not.toMatch(/permission|not allowed|authoris|authoriz|access denied/i);
  });

  it("keeps what was typed, so it can be corrected", async () => {
    server.use(http.post(USERS, () => HttpResponse.json({ error: "That username is already in use." }, { status: 400 })));

    const { user } = render();

    await fill(user);

    expect(await screen.findByRole("alert")).toBeInTheDocument();
    expect(screen.getByLabelText("First name")).toHaveValue("Ada");
    expect(screen.getByLabelText("Username")).toHaveValue("ada.lovelace");
  });

  /**
   * The opposite of the W3 flows. There a 401 answered a question about the
   * credentials presented; here it is evidence that an established session has
   * ended, so it is REPORTED and the session ends.
   */
  it("ends the session when the host answers 401", async () => {
    server.use(http.post(USERS, () => HttpResponse.json({ error: "Authentication is required." }, { status: 401 })));

    const { user } = render();

    await fill(user);

    expect(await screen.findByRole("heading", { name: "Sign in" })).toBeInTheDocument();
  });
});

describe("the create user page", () => {
  it("has no accessibility violations, before and after creating", async () => {
    server.use(http.post(USERS, () => HttpResponse.json(CREATED, { status: 201 })));

    const { container, user } = render();

    expect(await screen.findByLabelText("First name")).toBeInTheDocument();
    await expectNoAccessibilityViolations(container);

    await fill(user);

    expect(await screen.findByText(SUCCESS)).toBeInTheDocument();
    await expectNoAccessibilityViolations(container);
  });
});

/**
 * UI-7 (USR-C2 UI, option (a)): the unsaved-changes guard on the create page.
 * The page keeps its useState form; its dirty flag is "any editable field
 * differs from its initial empty value", over EVERY editable field — not
 * only the ones the server requires.
 */
describe("unsaved changes on the create page", () => {
  const unloadCancelled = () => {
    const event = new Event("beforeunload", { cancelable: true });
    window.dispatchEvent(event);
    return event.defaultPrevented;
  };

  it.each(["First name", "Last name", "Display name", "Email address", "Username"])(
    "holds beforeunload once %s alone has been typed into, and releases it when emptied again",
    async (label) => {
      const { user } = render();
      const field = await screen.findByLabelText(label);

      expect(unloadCancelled()).toBe(false);

      await user.type(field, "x");
      expect(unloadCancelled()).toBe(true);

      await user.clear(field);
      expect(unloadCancelled()).toBe(false);
    },
  );

  it("asks before navigating away from a started form", async () => {
    const { user, router } = render();

    await user.type(await screen.findByLabelText("Email address"), "ada@example.test");
    await act(async () => {
      await router.navigate("/sign-in");
    });

    expect(await screen.findByRole("dialog", { name: "Discard changes?" })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe("/users/new");

    await user.click(screen.getByRole("button", { name: "Discard" }));
    expect(await screen.findByRole("heading", { name: "Sign in" })).toBeInTheDocument();
  });

  it("releases the guard after a successful create", async () => {
    server.use(http.post(USERS, () => HttpResponse.json(CREATED, { status: 201 })));
    const { user } = render();

    await fill(user);
    expect(await screen.findByText(SUCCESS)).toBeInTheDocument();

    expect(unloadCancelled()).toBe(false);
  });
});
