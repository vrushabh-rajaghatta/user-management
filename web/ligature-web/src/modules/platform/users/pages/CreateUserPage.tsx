import { useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { ApiError } from "@/shared/api/errors";
import { useCan } from "@/shared/auth/useCan";
import { Page } from "@/shared/components/Page";
import { FormField } from "@/shared/forms/FormField";
import { useCreateUser } from "../hooks/useCreateUser";
import { useUsernameAvailability } from "../hooks/useUsernameAvailability";
import { UserPermissions } from "../permissions";
import { createUserSchema } from "../schemas/createUser";
import { useUnsavedChangesGuard } from "@/shared/forms/useUnsavedChangesGuard";

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

const USERNAME_HELP = "What they will sign in with.";
const USERNAME_IN_USE = "This username is already in use.";
const USERNAME_AVAILABLE = "Username available.";

/**
 * IDN-Q3's answer, and the one value it is about (UN6). "checking" blocks
 * nothing; "taken" blocks Create only while the field still holds that value.
 */
type Availability = { readonly username: string; readonly state: "checking" | "taken" | "available" };

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

  // IDN-Q3 (docs/requirements.md, "IDN-Q3 CheckUsernameAvailable on Create
  // user"). Only for identity.read: without it no availability request is
  // ever made, and USR-C1's refusal at submit still applies.
  const canCheck = useCan(UserPermissions.readIdentities);
  const check = useUsernameAvailability();
  const [availability, setAvailability] = useState<Availability | undefined>(undefined);

  // What the field holds NOW, trimmed as it will be submitted — read when an
  // answer arrives, so an answer about an earlier value is discarded.
  const currentUsername = useRef("");

  // USR-C2 UI, option (a): this form keeps useState, and its dirty flag is
  // computed by hand — "any editable field differs from its initial empty
  // value", over EVERY field, not only those the server requires. A
  // successful create clears the form, and with it the guard.
  const dirty = (Object.keys(EMPTY) as (keyof typeof EMPTY)[]).some((field) => values[field] !== EMPTY[field]);
  const guard = useUnsavedChangesGuard(dirty);

  function set(field: keyof typeof EMPTY, value: string) {
    setValues((current) => ({ ...current, [field]: value }));

    // Any edit makes the previous answer about something else (UN6).
    if (field === "initialUsername") {
      currentUsername.current = value.trim();
      setAvailability(undefined);
    }
  }

  /** On blur: ask about exactly the value that would be submitted. */
  function checkUsername() {
    const username = values.initialUsername.trim();

    if (!canCheck || username === "" || availability?.username === username) {
      return;
    }

    setAvailability({ username, state: "checking" });

    check.mutate(username, {
      onSuccess: (result) => {
        if (currentUsername.current !== username) {
          return;
        }

        setAvailability({ username, state: result.available ? "available" : "taken" });
      },
      onError: () => {
        // UN7: a failed check blocks nothing and says nothing.
        setAvailability((current) => (current?.username === username ? undefined : current));
      },
    });
  }

  function submit() {
    setError(undefined);

    const parsed = createUserSchema.safeParse(values);

    if (!parsed.success) {
      setError(parsed.error.issues[0]?.message ?? UNKNOWN);

      return;
    }

    const request = parsed.data;

    // UN6: the server has said this exact value is taken, and under D2 that
    // cannot change. Any other value — or no answer — goes to the server.
    if (availability?.state === "taken" && availability.username === request.initialUsername) {
      return;
    }

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
        currentUsername.current = "";
        setAvailability(undefined);
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

        <FormField
          id="username"
          label="Username"
          description={availability?.state === "available" ? USERNAME_AVAILABLE : USERNAME_HELP}
          error={availability?.state === "taken" ? USERNAME_IN_USE : undefined}
          required
        >
          {(control) => (
            <Input
              {...control}
              autoComplete="off"
              value={values.initialUsername}
              onChange={(event) => {
                set("initialUsername", event.target.value);
              }}
              onBlur={checkUsername}
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
      {guard.prompt}
    </Page>
  );
}
