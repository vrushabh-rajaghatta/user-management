import type { ReactNode, Ref } from "react";

interface PageHeaderProps {
  readonly title: string;
  readonly description?: ReactNode;
  readonly actions?: ReactNode;

  /** Used by Page to move focus to the heading after navigation. */
  readonly headingRef?: Ref<HTMLHeadingElement>;
}

/**
 * The page's single level-one heading, an optional description, and a place for
 * page actions. The heading can receive focus programmatically, so a route
 * change can move keyboard and screen-reader users to the new page.
 */
export function PageHeader({ title, description, actions, headingRef }: PageHeaderProps) {
  return (
    <header className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
      <div className="flex flex-col gap-1">
        <h1 ref={headingRef} tabIndex={-1} className="text-2xl font-semibold tracking-tight">
          {title}
        </h1>
        {description === undefined ? null : <div className="text-muted-foreground">{description}</div>}
      </div>
      {actions === undefined ? null : <div className="flex shrink-0 flex-wrap gap-2">{actions}</div>}
    </header>
  );
}
