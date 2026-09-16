import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { ApiError } from "@/shared/api/errors";
import { Page } from "@/shared/components/Page";
import { FormField } from "@/shared/forms/FormField";
import { useCreateUser } from "../hooks/useCreateUser";
import { createUserSchema } from "../schemas/createUser";

/**
 * USR-C1 (docs/frontend-architecture.md §7, §9, §12).
 *
 * Two things this page must not do, both because the backend deliberately
 * withholds the information:
 *
 * It does not explain a 400. The host answers 400 for a missing 'user.create'
 * permission AND for invalid input, from the same exception type, and says so
 * plainly in its own documentation. Rendering either reading would tell the
 * administrator something the client cannot know.
 *
 * It does not claim the activation link was sent. USR-C1 declares the
 * notification, but delivery depends on how the deployment is configured and is
 * attempted after this response returns. The account exists; that is the whole
 * of what the response establishes.
 */

const SUCCESS = "The account has been created. They must activate it before they can sign in.";

const UNKNOWN = "The request could not be completed.";

const EMPTY = { firstName: "", lastName: "", displayName: "", email: "", initialUsername: "" };

/** What was created, kept so the page can confirm it after the form is cleared. */
interface Created {
  readonly displayName: string;
  readonly username: string;
  readonly email: string;
}

function messageFor(failure: unknown): string {
  return failure instanceof ApiError ? failure.message : UNKNOWN;
}

export function CreateUserPage() {
  const [values, setValues] = useState(EMPTY);
  const [error, setError] = useState<string | undefined>(undefined);
  const [created, setCreated] = useState<Created | undefined>(undefined);

  const create = useCreateUser();

  function set(field: keyof typeof EMPTY, value: string) {
    setValues((current) => ({ ...current, [field]: value }));
  }

  function submit() {
    setError(undefined);

    const parsed = createUserSchema.safeParse(values);

    if (!parsed.success) {
      setError(parsed.error.issues[0]?.message ?? UNKNOWN);

      return;
    }

    const request = parsed.data;

    create.mutate(request, {
      onSuccess: () => {
        // The entered details, not the response: the response carries only
        // identifiers, which mean nothing here and address no endpoint.
        setCreated({
          displayName: request.displayName,
          username: request.initialUsername,
          email: request.email,
        });

        setValues(EMPTY);
      },
      onError: (failure: unknown) => {
        setCreated(undefined);
        setError(messageFor(failure));
      },
    });
  }

  return (
    <Page title="Create user">
      {created === undefined ? null : (
        <div role="status" className="flex flex-col gap-1 rounded-lg border p-4">
          <p>{SUCCESS}</p>
          <p className="text-sm text-muted-foreground">
            {created.displayName} · {created.username} · {created.email}
          </p>
        </div>
      )}

      <form
        onSubmit={(event) => {
          event.preventDefault();
          submit();
        }}
        noValidate
        className="flex max-w-sm flex-col gap-4"
      >
        <FormField id="first-name" label="First name" required>
          {(control) => (
            <Input
              {...control}
              autoComplete="given-name"
              value={values.firstName}
              onChange={(event) => {
                set("firstName", event.target.value);
              }}
            />
          )}
        </FormField>

        <FormField id="last-name" label="Last name" required>
          {(control) => (
            <Input
              {...control}
              autoComplete="family-name"
              value={values.lastName}
              onChange={(event) => {
                set("lastName", event.target.value);
              }}
            />
          )}
        </FormField>

        <FormField id="display-name" label="Display name" description="How this person is shown across Ligature." required>
          {(control) => (
            <Input
              {...control}
              value={values.displayName}
              onChange={(event) => {
                set("displayName", event.target.value);
              }}
            />
          )}
        </FormField>

        <FormField id="email" label="Email address" required>
          {(control) => (
            <Input
              {...control}
              type="email"
              autoComplete="email"
              value={values.email}
              onChange={(event) => {
                set("email", event.target.value);
              }}
            />
          )}
        </FormField>

        <FormField id="username" label="Username" description="What they will sign in with." required>
          {(control) => (
            <Input
              {...control}
              autoComplete="off"
              value={values.initialUsername}
              onChange={(event) => {
                set("initialUsername", event.target.value);
              }}
            />
          )}
        </FormField>

        {error === undefined ? null : (
          <p role="alert" className="text-sm text-destructive">
            {error}
          </p>
        )}

        <Button type="submit" disabled={create.isPending}>
          {create.isPending ? "Creating..." : "Create user"}
        </Button>
      </form>
    </Page>
  );
}
