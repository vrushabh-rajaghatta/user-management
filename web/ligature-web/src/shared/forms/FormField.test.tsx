import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { expectNoAccessibilityViolations } from "@/test/axe";
import { FormField } from "./FormField";

/**
 * FormField owns a field's accessible structure and nothing else
 * (docs/frontend-architecture.md §12, §15). It is handed an error string and
 * does not care where it came from — a schema, the server, or the component
 * itself — which is why nothing here mentions zod, a form library or an
 * ApiError.
 */

function renderField(props: { description?: string; error?: string; required?: boolean } = {}) {
  return render(
    <FormField id="username" label="Username" {...props}>
      {(control) => <input type="text" {...control} />}
    </FormField>,
  );
}

describe("FormField", () => {
  it("associates the label with the control, so the control can be found by its label", () => {
    renderField();

    expect(screen.getByLabelText("Username")).toHaveAttribute("id", "username");
  });

  it("describes the control with its description", () => {
    renderField({ description: "Your work username." });

    expect(screen.getByLabelText("Username")).toHaveAccessibleDescription("Your work username.");
  });

  it("describes the control with its error", () => {
    renderField({ error: "A username is required." });

    expect(screen.getByLabelText("Username")).toHaveAccessibleDescription("A username is required.");
  });

  it("describes the control with the description and the error together, description first", () => {
    renderField({ description: "Your work username.", error: "A username is required." });

    expect(screen.getByLabelText("Username")).toHaveAccessibleDescription("Your work username. A username is required.");
  });

  it("describes the control with nothing when there is neither", () => {
    renderField();

    expect(screen.getByLabelText("Username")).not.toHaveAttribute("aria-describedby");
  });

  it("marks the control invalid when there is an error", () => {
    renderField({ error: "A username is required." });

    expect(screen.getByLabelText("Username")).toHaveAttribute("aria-invalid", "true");
  });

  it("does not mark the control invalid when there is no error", () => {
    renderField();

    expect(screen.getByLabelText("Username")).not.toHaveAttribute("aria-invalid");
  });

  it("announces the error, so it reaches a screen reader when it appears", () => {
    renderField({ error: "A username is required." });

    expect(screen.getByRole("alert")).toHaveTextContent("A username is required.");
  });

  /**
   * Politely, for a description that changes with the field's state — Create
   * user's "Username available." (IDN-Q3, UN8). A static description never
   * changes, so this announces nothing for it.
   */
  it("announces a change of description politely", () => {
    renderField({ description: "Username available." });

    expect(screen.getByText("Username available.")).toHaveAttribute("aria-live", "polite");
  });

  it("marks the control required when the field is required", () => {
    renderField({ required: true });

    expect(screen.getByLabelText(/Username/)).toBeRequired();
  });

  it("has no accessibility violations, with and without an error", async () => {
    const { container, rerender } = renderField({ description: "Your work username." });

    await expectNoAccessibilityViolations(container);

    rerender(
      <FormField id="username" label="Username" description="Your work username." error="A username is required.">
        {(control) => <input type="text" {...control} />}
      </FormField>,
    );

    await expectNoAccessibilityViolations(container);
  });
});
