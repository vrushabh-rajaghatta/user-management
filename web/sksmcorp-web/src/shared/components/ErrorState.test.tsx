import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { ErrorState } from "./ErrorState";

describe("ErrorState", () => {
  it("announces the error", () => {
    render(<ErrorState message="The server could not be reached." />);

    expect(screen.getByRole("alert")).toHaveTextContent("The server could not be reached.");
  });

  it("offers a retry that calls back when one is given", async () => {
    const retry = vi.fn();

    render(<ErrorState message="The server could not be reached." onRetry={retry} />);

    await userEvent.setup().click(screen.getByRole("button", { name: "Try again" }));

    expect(retry).toHaveBeenCalledTimes(1);
  });

  it("offers no retry when none is given", () => {
    render(<ErrorState message="The server could not be reached." />);

    expect(screen.queryByRole("button")).toBeNull();
  });
});
