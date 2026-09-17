import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useRef, useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { expectNoAccessibilityViolations } from "@/test/axe";
import { ConfirmAction } from "./ConfirmAction";

/**
 * A confirmation before a consequential action (docs/frontend-architecture.md
 * §4, §15). Dialogs trap focus, close on Escape, and return focus — here to the
 * element the caller names, which is how a dialog opened from a menu gets back
 * to the button that opened the menu.
 */
function Harness({ busy = false, onConfirm = vi.fn() }: { busy?: boolean; onConfirm?: () => void }) {
  const [open, setOpen] = useState(false);
  const opener = useRef<HTMLButtonElement>(null);

  return (
    <>
      <button ref={opener} type="button" onClick={() => setOpen(true)}>
        Open
      </button>
      <ConfirmAction
        open={open}
        onOpenChange={setOpen}
        title="Do the thing"
        description="It cannot be undone."
        confirmLabel="Do it"
        busyLabel="Doing…"
        busy={busy}
        onConfirm={onConfirm}
        returnFocus={opener}
      >
        <p>Extra content</p>
      </ConfirmAction>
    </>
  );
}

describe("ConfirmAction", () => {
  it("is a dialog named by its title, with its description and content", async () => {
    const user = userEvent.setup();
    render(<Harness />);

    await user.click(screen.getByRole("button", { name: "Open" }));

    const dialog = await screen.findByRole("dialog", { name: "Do the thing" });

    expect(dialog).toHaveTextContent("It cannot be undone.");
    expect(dialog).toHaveTextContent("Extra content");
  });

  it("calls onConfirm when confirmed", async () => {
    const user = userEvent.setup();
    const onConfirm = vi.fn();
    render(<Harness onConfirm={onConfirm} />);

    await user.click(screen.getByRole("button", { name: "Open" }));
    await user.click(await screen.findByRole("button", { name: "Do it" }));

    expect(onConfirm).toHaveBeenCalledTimes(1);
  });

  it("disables confirming, and names what is in progress, while busy", async () => {
    const user = userEvent.setup();
    const onConfirm = vi.fn();
    render(<Harness busy onConfirm={onConfirm} />);

    await user.click(screen.getByRole("button", { name: "Open" }));

    const busy = await screen.findByRole("button", { name: "Doing…" });

    expect(busy).toBeDisabled();

    await user.click(busy);

    expect(onConfirm).not.toHaveBeenCalled();
  });

  it("closes on Cancel and on Escape, returning focus to the named element", async () => {
    const user = userEvent.setup();
    render(<Harness />);

    await user.click(screen.getByRole("button", { name: "Open" }));
    await user.click(await screen.findByRole("button", { name: "Cancel" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });
    await waitFor(() => {
      expect(document.activeElement).toBe(screen.getByRole("button", { name: "Open" }));
    });

    await user.click(screen.getByRole("button", { name: "Open" }));
    await screen.findByRole("dialog");
    await user.keyboard("{Escape}");

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });
  });

  it("has no accessibility violations when open", async () => {
    const user = userEvent.setup();
    render(<Harness />);

    await user.click(screen.getByRole("button", { name: "Open" }));
    await screen.findByRole("dialog");

    await expectNoAccessibilityViolations(document.body);
  });
});
