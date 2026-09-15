import { Button } from "@/components/ui/button";

interface ErrorStateProps {
  readonly title?: string;

  /** Safe to show: an ApiError's message, or fixed text. Never a raw response. */
  readonly message: string;

  readonly onRetry?: () => void;
}

/**
 * A region that could not be shown (docs/frontend-architecture.md §11). It is
 * announced as an alert, and offers a retry when the caller can retry.
 */
export function ErrorState({ title = "Something went wrong", message, onRetry }: ErrorStateProps) {
  return (
    <div role="alert" className="flex flex-col items-start gap-3 rounded-lg border p-6">
      <p className="font-medium">{title}</p>
      <p className="text-muted-foreground">{message}</p>
      {onRetry === undefined ? null : (
        <Button variant="outline" onClick={onRetry}>
          Try again
        </Button>
      )}
    </div>
  );
}
