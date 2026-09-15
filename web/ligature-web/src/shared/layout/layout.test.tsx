import { screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { renderWithApp } from "@/test/renderWithApp";
import { AppShell } from "./AppShell";
import { NotFound } from "./NotFound";
import { PublicShell } from "./PublicShell";
import { RouteError } from "./RouteError";

function Boom(): never {
  throw new Error("secret detail at Ligature.Host");
}

describe("AppShell", () => {
  it("lets keyboard users skip straight to the main content", async () => {
    const { user } = renderWithApp([
      { element: <AppShell />, children: [{ path: "/", element: <p>main content</p> }] },
    ]);

    await screen.findByText("main content");

    await user.tab();
    const skip = screen.getByRole("link", { name: "Skip to content" });
    expect(document.activeElement).toBe(skip);

    await user.keyboard("{Enter}");
    expect(document.activeElement).toBe(screen.getByRole("main"));
  });
});

describe("PublicShell", () => {
  it("renders its route inside the main landmark", async () => {
    renderWithApp([{ element: <PublicShell />, children: [{ path: "/", element: <p>public content</p> }] }]);

    expect(await screen.findByRole("main")).toHaveTextContent("public content");
  });
});

describe("RouteError", () => {
  it("shows fixed text and nothing of the error that was thrown", async () => {
    vi.spyOn(console, "error").mockImplementation(() => undefined);

    renderWithApp([
      {
        element: <PublicShell />,
        children: [{ errorElement: <RouteError />, children: [{ path: "/", element: <Boom /> }] }],
      },
    ]);

    expect(await screen.findByRole("heading", { level: 1, name: "Something went wrong" })).toBeInTheDocument();
    expect(screen.queryByText(/secret detail/)).toBeNull();
    expect(screen.getByRole("link", { name: "Go to the home page" })).toHaveAttribute("href", "/");
  });
});

describe("NotFound", () => {
  it("says the page does not exist and sets the title", async () => {
    renderWithApp([{ path: "*", element: <NotFound /> }], { path: "/nowhere" });

    expect(await screen.findByRole("heading", { level: 1, name: "Page not found" })).toBeInTheDocument();
    expect(document.title).toBe("Page not found · Ligature");
  });
});
