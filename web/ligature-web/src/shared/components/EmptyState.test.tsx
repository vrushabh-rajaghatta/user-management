import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { EmptyState } from "./EmptyState";

/**
 * An empty collection (docs/frontend-architecture.md §11): why it is empty and
 * what to do. It is a normal result, so it is never announced as an alert.
 */
describe("EmptyState", () => {
  it("shows its title, description and action", () => {
    render(<EmptyState title="Nothing here" description="Because of a reason." action={<button type="button">Do it</button>} />);

    expect(screen.getByText("Nothing here")).toBeInTheDocument();
    expect(screen.getByText("Because of a reason.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Do it" })).toBeInTheDocument();
  });

  it("is not an alert", () => {
    render(<EmptyState title="Nothing here" />);

    expect(screen.queryByRole("alert")).toBeNull();
  });
});
