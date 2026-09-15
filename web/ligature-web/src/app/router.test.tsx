import { screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { renderWithApp } from "@/test/renderWithApp";
import { appRoutes, composeRoutes } from "./router";

function Boom(): never {
  throw new Error("secret detail at Ligature.Host");
}

describe("the application routes", () => {
  it("load the placeholder home page lazily at /", async () => {
    renderWithApp(appRoutes, { path: "/" });

    expect(await screen.findByRole("heading", { level: 1, name: "Home" })).toBeInTheDocument();
    expect(screen.getByRole("main")).toHaveTextContent("Nothing can be done here yet.");
  });

  it("show the not-found page for an unknown path, inside the shell", async () => {
    renderWithApp(appRoutes, { path: "/no/such/page" });

    expect(await screen.findByRole("heading", { level: 1, name: "Page not found" })).toBeInTheDocument();
    expect(screen.getByRole("main")).toBeInTheDocument();
  });

  it("show the route error inside the shell when a page throws, without its detail", async () => {
    vi.spyOn(console, "error").mockImplementation(() => undefined);

    renderWithApp(composeRoutes([{ path: "/", element: <Boom /> }]), { path: "/" });

    expect(await screen.findByRole("heading", { level: 1, name: "Something went wrong" })).toBeInTheDocument();
    expect(screen.getByRole("main")).toBeInTheDocument();
    expect(screen.queryByText(/secret detail/)).toBeNull();
  });
});
