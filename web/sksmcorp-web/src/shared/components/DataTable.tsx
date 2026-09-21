import type { ReactNode } from "react";
import { Table, TableBody, TableCaption, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";

export interface DataTableColumn<T> {
  readonly header: string;
  readonly cell: (row: T) => ReactNode;

  /** Applied to both the header and the cells of this column. */
  readonly className?: string;
}

interface DataTableProps<T> {
  /** Names the table for assistive technology. Not shown: the page heading already is. */
  readonly caption: string;
  readonly columns: readonly DataTableColumn<T>[];
  readonly rows: readonly T[];

  /** A stable identity for each row, never its position. */
  readonly rowKey: (row: T) => string;
}

/**
 * The standard for a list (docs/frontend-architecture.md §4, §14), built with its
 * first user, the Users table. It knows no domain: the caller names the table,
 * its columns and each row's key.
 *
 * The vendored Table scrolls sideways inside its own container, so a wide table
 * never makes the page body scroll. States — loading, empty, error — are the
 * caller's: a table with no rows is not an empty state (§11).
 */
export function DataTable<T>({ caption, columns, rows, rowKey }: DataTableProps<T>) {
  return (
    <Table>
      <TableCaption className="sr-only">{caption}</TableCaption>
      <TableHeader>
        <TableRow>
          {columns.map((column) => (
            <TableHead key={column.header} className={column.className}>
              {column.header}
            </TableHead>
          ))}
        </TableRow>
      </TableHeader>
      <TableBody>
        {rows.map((row) => (
          <TableRow key={rowKey(row)}>
            {columns.map((column) => (
              <TableCell key={column.header} className={column.className}>
                {column.cell(row)}
              </TableCell>
            ))}
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}
