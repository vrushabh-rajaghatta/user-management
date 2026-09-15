import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { App } from "./App";

describe("the application shell", () => {
  it("renders the placeholder home with a level-one heading", () => {
    render(<App />);

    expect(screen.getByRole("heading", { level: 1, name: "Ligature" })).toBeInTheDocument();
  });
});
