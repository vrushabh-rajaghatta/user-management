import { useEffect, useRef, type ReactNode } from "react";
import { useLocation } from "react-router";
import { PageHeader } from "./PageHeader";

interface PageProps {
  readonly title: string;
  readonly description?: ReactNode;
  readonly actions?: ReactNode;
  readonly children?: ReactNode;
}

/**
 * Every routed page. It sets the document title and, after a navigation, moves
 * focus to the page heading (docs/frontend-architecture.md §15), so keyboard and
 * screen-reader users arrive at the new page instead of wherever they were.
 *
 * Focus does not move on the first page load: nothing has changed yet, and
 * taking focus from the browser's own starting point would be disorienting.
 */
export function Page({ title, description, actions, children }: PageProps) {
  const heading = useRef<HTMLHeadingElement>(null);
  const { key } = useLocation();

  useEffect(() => {
    document.title = `${title} · Ligature`;
  }, [title]);

  useEffect(() => {
    // The router gives the first location of a session the key "default".
    if (key !== "default") {
      heading.current?.focus();
    }
  }, [key]);

  return (
    <div className="mx-auto flex w-full max-w-5xl flex-col gap-6 px-4 py-8 sm:px-6">
      <PageHeader title={title} description={description} actions={actions} headingRef={heading} />
      {children}
    </div>
  );
}
