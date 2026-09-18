import type { ReactNode } from "react";

interface EmptyStateProps {
  readonly title: string;
  readonly description?: ReactNode;
  readonly action?: ReactNode;
}

/**
 * An empty collection (docs/frontend-architecture.md §11): why it is empty and
 * what to do next. An empty result is a normal answer, so it is never
 * announced as an alert; ErrorState is for a region that could not be shown.
 */
export function EmptyState({ title, description, action }: EmptyStateProps) {
  return (
    <div className="flex flex-col items-start gap-3 rounded-lg border border-dashed p-6">
      <p className="font-medium">{title}</p>
      {description === undefined ? null : <div className="text-muted-foreground">{description}</div>}
      {action === undefined ? null : <div>{action}</div>}
    </div>
  );
}
