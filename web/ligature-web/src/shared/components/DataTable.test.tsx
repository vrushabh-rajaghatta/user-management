import { render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { expectNoAccessibilityViolations } from "@/test/axe";
import { DataTable } from "./DataTable";

/**
 * The standard for a list (docs/frontend-architecture.md §4, §14). It knows no
 * domain: the caller names the table, its columns and each row's key.
 */
interface Thing {
  readonly id: string;
  readonly name: string;
}

const COLUMNS = [
  { header: "Name", cell: (row: Thing) => row.name },
  { header: "Id", cell: (row: Thing) => <code>{row.id}</code> },
];

describe("DataTable", () => {
  it("is a table named by its caption, with a header row and one row per item", () => {
    render(
      <DataTable
        caption="Things"
        columns={COLUMNS}
        rows={[{ id: "1", name: "First" }, { id: "2", name: "Second" }]}
        rowKey={(row) => row.id}
      />,
    );

    const table = screen.getByRole("table", { name: "Things" });

    expect(within(table).getAllByRole("columnheader").map((cell) => cell.textContent)).toEqual(["Name", "Id"]);
    expect(within(table).getAllByRole("row")).toHaveLength(3);
    expect(within(table).getByRole("cell", { name: "Second" })).toBeInTheDocument();
  });

  it("has no accessibility violations", async () => {
    const { container } = render(
      <DataTable caption="Things" columns={COLUMNS} rows={[{ id: "1", name: "First" }]} rowKey={(row) => row.id} />,
    );

    await expectNoAccessibilityViolations(container);
  });
});
