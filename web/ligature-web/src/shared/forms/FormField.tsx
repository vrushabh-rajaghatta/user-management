import type { ReactNode } from "react";
import { Label } from "@/components/ui/label";

/**
 * A field's accessible structure, and nothing else
 * (docs/frontend-architecture.md §12, §15).
 *
 * It is handed an error string and does not care where it came from — a schema,
 * the server, or the component itself. That is why nothing here mentions zod, a
 * form library or an ApiError, and why lint forbids importing them.
 */

export interface FormFieldControl {
  readonly id: string;
  readonly "aria-describedby": string | undefined;
  readonly "aria-invalid": true | undefined;
  readonly required: true | undefined;
}

interface FormFieldProps {
  readonly id: string;
  readonly label: string;
  readonly description?: string;

  /** Already computed by the caller. Present means the field is invalid. */
  readonly error?: string;

  readonly required?: boolean;

  /** Receives the attributes the control must carry to stay accessible. */
  readonly children: (control: FormFieldControl) => ReactNode;
}

export function FormField({ id, label, description, error, required = false, children }: FormFieldProps) {
  const descriptionId = `${id}-description`;
  const errorId = `${id}-error`;

  // The description comes first, so a screen reader hears what the field is for
  // before what is wrong with it.
  const describedBy = [
    description === undefined ? undefined : descriptionId,
    error === undefined ? undefined : errorId,
  ].filter((value): value is string => value !== undefined);

  return (
    <div className="flex flex-col gap-2">
      <Label htmlFor={id}>{label}</Label>
      {description === undefined ? null : (
        <p id={descriptionId} className="text-sm text-muted-foreground">
          {description}
        </p>
      )}
      {children({
        id,
        "aria-describedby": describedBy.length === 0 ? undefined : describedBy.join(" "),
        "aria-invalid": error === undefined ? undefined : true,
        required: required ? true : undefined,
      })}
      {error === undefined ? null : (
        <p id={errorId} role="alert" className="text-sm text-destructive">
          {error}
        </p>
      )}
    </div>
  );
}
