import { screen } from "@testing-library/react";
import { Link, type RouteObject } from "react-router";
import { describe, expect, it } from "vitest";
import { renderWithApp } from "@/test/renderWithApp";
import { Page } from "./Page";
import { PageHeader } from "./PageHeader";

const routes: RouteObject[] = [
  {
    path: "/",
    element: (
      <Page title="First">
        <Link to="/second">Go to the second page</Link>
      </Page>
    ),
  },
  { path: "/second", element: <Page title="Second">second content</Page> },
];

describe("Page", () => {
  it("sets the document title", async () => {
    renderWithApp(routes);

    await screen.findByRole("heading", { name: "First" });

    expect(document.title).toBe("First · SKSMCorp");
  });

  it("does not move focus on the first page load", async () => {
    renderWithApp(routes);

    await screen.findByRole("heading", { name: "First" });

    expect(document.activeElement).toBe(document.body);
  });

  it("moves focus to the page heading and updates the title after navigation", async () => {
    const { user } = renderWithApp(routes);

    await user.click(await screen.findByRole("link", { name: "Go to the second page" }));

    const heading = await screen.findByRole("heading", { level: 1, name: "Second" });

    expect(document.activeElement).toBe(heading);
    expect(document.title).toBe("Second · SKSMCorp");
  });
});

describe("PageHeader", () => {
  it("renders the heading, its description and its actions", async () => {
    renderWithApp([
      {
        path: "/",
        element: (
          <PageHeader
            title="Users"
            description="People who can sign in."
            actions={<button type="button">Create user</button>}
          />
        ),
      },
    ]);

    expect(await screen.findByRole("heading", { level: 1, name: "Users" })).toBeInTheDocument();
    expect(screen.getByText("People who can sign in.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Create user" })).toBeInTheDocument();
  });
});
