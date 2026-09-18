import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { useState } from "react";
import { Link, type RouteObject } from "react-router";
import { describe, expect, it } from "vitest";
import { expectNoAccessibilityViolations } from "@/test/axe";
import { renderWithApp } from "@/test/renderWithApp";
import { useUnsavedChangesGuard } from "./useUnsavedChangesGuard";

/**
 * The unsaved-changes guard (docs/frontend-architecture.md §12; USR-C2 UI,
 * U2 and U7). It takes a BOOLEAN — never a form library's object — so every
 * form uses it the same way, however it computes dirtiness.
 */

function Form() {
  const [value, setValue] = useState("");
  const [closed, setClosed] = useState(false);
  const guard = useUnsavedChangesGuard(value !== "");

  return (
    <main>
      <label>
        Name
        <input
          value={value}
          onChange={(event) => {
            setValue(event.target.value);
          }}
        />
      </label>
      <Link to="/elsewhere">Leave</Link>
      <button
        type="button"
        onClick={() => {
          guard.confirm(() => {
            setClosed(true);
          });
        }}
      >
        Close
      </button>
      {closed ? <p>Closed</p> : null}
      {guard.prompt}
    </main>
  );
}

/**
 * A form whose link lives in something that closes when the link is followed,
 * as the primary sidebar does on a phone: by the time the person chooses Keep
 * editing, the element they were on no longer exists.
 */
function FormBesideSheet() {
  const [value, setValue] = useState("");
  const [sheetOpen, setSheetOpen] = useState(true);
  const guard = useUnsavedChangesGuard(value !== "");

  return (
    <>
      {sheetOpen ? (
        <nav aria-label="Sheet">
          <Link
            to="/elsewhere"
            onClick={() => {
              setSheetOpen(false);
            }}
          >
            Leave from the sheet
          </Link>
        </nav>
      ) : null}
      <main id="main" tabIndex={-1}>
        <label>
          Name
          <input
            value={value}
            onChange={(event) => {
              setValue(event.target.value);
            }}
          />
        </label>
        {guard.prompt}
      </main>
    </>
  );
}

function routes(): RouteObject[] {
  return [
    { path: "/form", element: <Form /> },
    { path: "/elsewhere", element: <h1>Elsewhere</h1> },
  ];
}

function beforeUnloadIsCancelled(): boolean {
  const event = new Event("beforeunload", { cancelable: true });
  window.dispatchEvent(event);

  return event.defaultPrevented;
}

describe("the unsaved-changes guard", () => {
  it("lets a clean form close and navigate without asking", async () => {
    const { user } = renderWithApp(routes(), { path: "/form" });

    await user.click(await screen.findByRole("button", { name: "Close" }));
    expect(screen.getByText("Closed")).toBeInTheDocument();

    await user.click(screen.getByRole("link", { name: "Leave" }));
    expect(await screen.findByRole("heading", { name: "Elsewhere" })).toBeInTheDocument();
  });

  it("asks before closing a dirty form; Keep editing keeps it, Discard closes it", async () => {
    const { user } = renderWithApp(routes(), { path: "/form" });

    await user.type(await screen.findByLabelText("Name"), "Ada");
    await user.click(screen.getByRole("button", { name: "Close" }));

    const prompt = await screen.findByRole("dialog", { name: "Discard changes?" });
    expect(screen.queryByText("Closed")).toBeNull();

    await user.click(within(prompt).getByRole("button", { name: "Keep editing" }));
    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });
    expect(screen.getByLabelText("Name")).toHaveValue("Ada");
    expect(screen.queryByText("Closed")).toBeNull();

    // Found in the browser (UI-9): focus returns to what the person was on
    // when the prompt opened, not to the document body.
    await waitFor(() => {
      expect(document.activeElement).toBe(screen.getByRole("button", { name: "Close" }));
    });

    await user.click(screen.getByRole("button", { name: "Close" }));
    await user.click(await screen.findByRole("button", { name: "Discard" }));

    expect(await screen.findByText("Closed")).toBeInTheDocument();
  });

  it("blocks in-app navigation from a dirty form until Discard", async () => {
    const { user, router } = renderWithApp(routes(), { path: "/form" });

    await user.type(await screen.findByLabelText("Name"), "Ada");
    await user.click(screen.getByRole("link", { name: "Leave" }));

    expect(await screen.findByRole("dialog", { name: "Discard changes?" })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe("/form");

    await user.click(screen.getByRole("button", { name: "Keep editing" }));
    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });
    expect(router.state.location.pathname).toBe("/form");
    await waitFor(() => {
      expect(document.activeElement).toBe(screen.getByRole("link", { name: "Leave" }));
    });

    await user.click(screen.getByRole("link", { name: "Leave" }));
    await user.click(await screen.findByRole("button", { name: "Discard" }));

    expect(await screen.findByRole("heading", { name: "Elsewhere" })).toBeInTheDocument();
  });

  /**
   * Found in the My account browser check (UI-10), at phone width: the link
   * followed was inside the sidebar sheet, which closed as it was followed, so
   * Keep editing had nowhere to return focus and left it on the document body.
   * With the element gone, focus goes back into the main content, where the
   * skip link sends it.
   */
  it("returns focus to the main content when the element the person was on has gone", async () => {
    const { user, router } = renderWithApp(
      [
        { path: "/form", element: <FormBesideSheet /> },
        { path: "/elsewhere", element: <h1>Elsewhere</h1> },
      ],
      { path: "/form" },
    );

    // Filled without ever holding focus, so nothing but the link could have
    // been where the person was — as in the browser, where focus was in the
    // sheet.
    fireEvent.change(await screen.findByLabelText("Name"), { target: { value: "Ada" } });

    // By keyboard, as the person this protects would: focus is ON the link
    // when it is followed, and the link then leaves the document.
    screen.getByRole("link", { name: "Leave from the sheet" }).focus();
    await user.keyboard("{Enter}");

    const prompt = await screen.findByRole("dialog", { name: "Discard changes?" });
    expect(screen.queryByRole("link", { name: "Leave from the sheet" })).toBeNull();

    await user.click(within(prompt).getByRole("button", { name: "Keep editing" }));
    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });

    expect(router.state.location.pathname).toBe("/form");
    expect(screen.getByLabelText("Name")).toHaveValue("Ada");
    // Into the main content, not onto the body. The dialog library, handed a
    // container that is not itself tabbable, moves on to the first tabbable
    // element inside it, which puts the person back in the form.
    await waitFor(() => {
      expect(document.activeElement).not.toBe(document.body);
      expect(screen.getByRole("main").contains(document.activeElement)).toBe(true);
    });
  });

  it("holds beforeunload only while dirty", async () => {
    const { user } = renderWithApp(routes(), { path: "/form" });
    const input = await screen.findByLabelText("Name");

    expect(beforeUnloadIsCancelled()).toBe(false);

    await user.type(input, "A");
    expect(beforeUnloadIsCancelled()).toBe(true);

    await user.clear(input);
    expect(beforeUnloadIsCancelled()).toBe(false);
  });

  it("has no accessibility violations with the prompt open", async () => {
    const { user } = renderWithApp(routes(), { path: "/form" });

    await user.type(await screen.findByLabelText("Name"), "Ada");
    await user.click(screen.getByRole("button", { name: "Close" }));
    await screen.findByRole("dialog", { name: "Discard changes?" });

    await expectNoAccessibilityViolations(document.body);
  });
});
