# Ligature Web Client — Architecture Contract

**Status:** Approved. Part I of the web client design (v2), approved by the owner on 14 September 2026 with decisions O1–O9 closed. Referenced from `docs/architecture.md` §2.

**Applies to:** `web/ligature-web/`.

This document is the architecture contract for the web client. Its rules are meant to stay true as Ligature grows well past User Management. It deliberately contains no plan: what each story builds belongs in that story's pull request, not here.

Every rule is written so that a reviewer can check a change against it. **Where lint can enforce a rule, lint enforces it**, and each lint-enforced rule is proven by a fixture that breaks it on purpose (§16).

The backend architecture (`docs/architecture.md`) remains authoritative for everything the client consumes: the HTTP contract, the carrier and its transport (§17), and authorization. This contract never reinterprets those decisions; the client consumes them.

---

## 1. Governing principle

> **The frontend mirrors the backend's module boundaries. It does not mirror the backend's internal implementation.**

A frontend module matches a backend capability: Users, Roles, Permissions, and later Regulatory submissions. It owns that capability's screens, operations and vocabulary. It does not reproduce commands, handlers, aggregates, the pipeline or the database's shape. The client consumes an HTTP contract. It is not a second implementation of the domain.

The client is built from two kinds of code:

| Kind | Where | Meaning |
| --- | --- | --- |
| **Application infrastructure** | `app/`, `shared/`, `components/ui/` | Authentication state, the API client, routing composition, layout, errors, design primitives. No business meaning. |
| **Modules** | `modules/platform/*`, later `modules/regulatory/*` | One per capability. They carry all business meaning. |

Business modules may use platform modules. Platform modules may not use business modules. This is `docs/architecture.md` §10's direction, applied to the client.

---

## 2. Layers and dependencies

```text
web/ligature-web/src/
├── app/                composition root: router, providers. Imports everything, owns nothing.
├── shared/             application infrastructure (§3)
│   ├── api/            transport: client, ApiError, response parsing
│   ├── auth/           AuthSession, can(), useCan(), <Can>, RequireAuth
│   ├── feedback/       toast (§10)
│   ├── forms/          FormField, useUnsavedChangesGuard
│   ├── components/     application components (§4)
│   ├── layout/         AppShell, PublicShell, RouteError, NotFound
│   └── format/         dates, numbers
├── components/ui/      vendored shadcn primitives, never hand-edited
├── lib/                cn() and similar pure helpers
└── modules/
    └── platform/
        └── users/
            ├── api/            API operations — the only callers of shared/api
            ├── hooks/          query and mutation hooks
            ├── components/     feature components
            ├── pages/
            ├── permissions.ts  this module's permission codes (§9)
            ├── schemas/        zod: form input and API response shapes
            ├── routes.tsx
            └── index.ts        public surface
```

| From | May import | May not import |
| --- | --- | --- |
| `app/` | a module's `index.ts` and `routes.tsx`, `shared/` | module internals |
| `modules/x/y/*` | its own files, `shared/`, `components/ui`, `lib/`, another module's `index.ts` | another module's internals, `app/`, `shared/api/client` outside `api/` (§6) |
| a platform module | other platform modules' `index.ts` | any business module |
| `shared/` | `components/ui`, `lib/`, other `shared/` | any module, `app/` |
| `components/ui` | `lib/` | everything else |

**Proven, not just asserted.** Each boundary rule has a fixture that breaks it on purpose, and lint must fail on that fixture. A rule that has been quietly switched off then fails loudly instead of passing (§16).

---

## 3. What belongs in `shared/`

> **Admission test.** Something may enter `shared/` only if it has **no business or module meaning**, and it is either application infrastructure or already used by at least two modules.

If the code needs a module's vocabulary to explain what it does, it belongs to that module. Being used twice doesn't earn a place in `shared/` by itself: two modules both showing a user badge means the Users module should export `UserBadge`, not that `shared/` should hold it.

| Candidate | Verdict | Reason |
| --- | --- | --- |
| `shared/api/client` | Belongs | Transport infrastructure |
| `shared/auth/AuthSession` | Belongs | Infrastructure; it knows nothing about users as a domain |
| `shared/auth/Can` | Belongs | Checks a permission code without knowing what the code means |
| `shared/components/PageHeader` | Belongs | Layout with no business meaning |
| `shared/components/DateDisplay` | Belongs | Formatting with no business meaning |
| `shared/components/StatusBadge` | Belongs | Takes a tone and a label; knows no statuses |
| `shared/components/UserBadge` | Module | Means "a user" → `platform/users` |
| `shared/components/RoleSelector` | Module | Means "a role" → `platform/roles` |
| `shared/permissions/UserPermissions` | Module | User permission codes → `platform/users/permissions.ts` |
| `shared/types/UserStatus` | Module | Domain vocabulary |

Lint can't tell whether code has business meaning, so this rule is checked in review. What lint can enforce is the concrete part: `shared/` never imports a module (§2). That already rules out most code that fails the test.

---

## 4. Design-system layers

| Tier | Contents |
| --- | --- |
| **`components/ui`** — vendored primitives | Button, Input, Select, Dialog, Badge, Table, Tooltip, Tabs, DropdownMenu, Sheet, Skeleton, Sonner. Installed with the shadcn CLI and **never hand-edited**, so updates stay a re-install. |
| **`shared/components`** — application components | Page, PageHeader, DataTable, EmptyState, ErrorState, ConfirmAction, FormField, StatusBadge, DateDisplay. Each one sets Ligature's standard for a recurring pattern. |
| **`modules/*/components`** — feature components | CreateUserForm, UsersTable. These carry business meaning. |

Each tier is composed into the one below it.

> **Rule.** Once an application component exists for a pattern, features must use it and may not rebuild it from primitives. Features may use primitives directly for anything no application component covers, such as a single Button.

Application components are added when a second feature needs the pattern, with one exception: `Page`, `PageHeader`, `FormField`, `EmptyState`, `ErrorState` and `ConfirmAction` are built with the foundation, because the reference module needs them straight away. `DataTable` waits for the first list endpoint, since a table built without real data guesses its API.

**Theme:** colours, radius and typography come from tokens in `index.css` and nowhere else. Features never use raw colour values. This is also what makes the contrast requirement in §15 checkable.

---

## 5. Routing

Each module registers its routes explicitly in its own `routes.tsx`, one by one, each carrying its requirement ID. `app/router.tsx` only puts those groups into shells and adds the error and 404 routes. The page list stays readable per module, and the router file never grows into a list of every page.

```tsx
// app/router.tsx — composition only
createBrowserRouter([
  { element: <PublicShell />, errorElement: <RouteError />, children: [...authRoutes, ...accountRoutes] },
  { element: <RequireAuth />, errorElement: <RouteError />, children: [
    { element: <AppShell />, children: [...platformHomeRoutes, ...userRoutes] },
  ]},
  { path: "*", element: <NotFound /> },
]);

// modules/platform/users/routes.tsx — explicit, lazy, traceable
export const userRoutes: RouteObject[] = [
  { path: "/users/new", lazy: () => import("./pages/CreateUserPage") }, // USR-C1
];
```

- Pages load lazily and never through file-based routing.
- A route that depends on a permission (§9) wraps its element in `<Can>`. Showing the page is UX; the server still decides.
- A route whose path is part of a backend contract (§13) says so in a comment, and must not be renamed.

---

## 6. API layering

| Layer | Responsibility |
| --- | --- |
| **Component** | Renders and handles events. Never imports the API client or an API operation. |
| **Hook** | e.g. `useCreateUser()`: wraps TanStack Query and owns cache invalidation and query keys. |
| **API operation** | e.g. `modules/…/api/createUser.ts`: one function per endpoint. It parses the response against its schema (§16). |
| **API client** | `shared/api/client.ts`: fetch, JSON, status mapping, `ApiError`, and 401 reporting to `AuthSession`. |

Each layer calls only the one below it.

```ts
// modules/platform/users/api/createUser.ts
export const createUser = (request: CreateUserRequest) =>
  api.post("/api/users", request, createUserResponseSchema);
```

**Enforced by lint:**

- `shared/api/client` may only be imported from `modules/**/api/**` and `shared/auth/**`.
- A `modules/**/api/**` file may only be imported from that module's `hooks/`.
- `fetch` and `XMLHttpRequest` are banned everywhere except `shared/api/client.ts`.

Hooks are part of a module's public surface. API operations are not: another module that needs a user reads it through the Users module's hook, which keeps caching and invalidation in one place.

---

## 7. Errors

The backend returns every failure in the same shape, and those uniform responses are deliberate security controls. The client keeps them that way.

| Status | Client behaviour | User sees |
| --- | --- | --- |
| `2xx` | Parse the body against the operation's schema. A mismatch is a contract defect (§16). | — |
| `400` | `ApiError(400, message)` | The server's message, word for word, next to the action that failed |
| `401` | Report it to `AuthSession`, which moves to `unauthenticated` (§8) | Redirect to sign-in. No reason is given. |
| `403` | Handled as a permission refusal. The backend doesn't send 403 for authorization yet. | Server message |
| `5xx`, network | `ApiError(status)`. The session is not changed. | `ErrorState` with retry, or an inline error on a form |

> **Rule.** Logic branches on status codes, never on message text. A message is shown to the user and never interpreted.

---

## 8. Authentication state

The `HttpOnly` cookie can't be read by script, which is the reason for choosing it. The UI still has to decide what to render. That decision sits behind one abstraction, so no route or component knows how the answer was obtained.

```ts
// shared/auth/AuthSession.ts
type AuthState =
  | { status: "unknown" }                                        // not yet determined
  | { status: "authenticated"; principal: Principal | null }     // null until a source can describe the user
  | { status: "unauthenticated" };

interface AuthSessionSource {   // the only thing that changes when GET /me arrives
  resolve(): Promise<AuthState>; // decides the initial state
  signedIn(): void;              // sign-in succeeded
  signedOut(): void;             // sign-out completed, or a 401 arrived
}
```

| Event | Next state | Emitted by |
| --- | --- | --- |
| App starts | `unknown` → whatever `resolve()` returns | `AuthProvider` |
| Sign-in succeeds | `authenticated` | `platform/auth` hook |
| Any 401 (except on sign-in itself) | `unauthenticated`; the query cache is cleared | API client |
| Sign-out settles, whether or not the call succeeded | `unauthenticated`; the query cache is cleared | `platform/auth` hook |

`RequireAuth` renders nothing while the state is `unknown`, the app shell when `authenticated`, and a redirect with a return path when `unauthenticated`. Components read `useAuthSession()` and never find out which source is in use.

| | Source today | Source after `GET /me` |
| --- | --- | --- |
| Name | `SessionHintSource`: a flag in `sessionStorage` | `ServerSessionSource` |
| `principal` | always `null` | holds the identity and effective permissions |
| `resolve()` | answers immediately from the flag | asks the server; `unknown` lasts until it answers |

- **The hint is never authoritative.** Until a server-backed source exists, the client cannot reliably know the authenticated user's identity after a reload. The server's answer to each request — a 401 in particular — always wins over the hint.
- Lint bans `sessionStorage` and `localStorage` everywhere except the hint source's own file, so the hint can't spread into the rest of the app.
- The hint source's file header says it is **not a security control**. Setting the flag by hand gets you an empty shell and a redirect on the first request.
- Switching sources changes one line in `app/providers.tsx`. Routes, guards and components stay as they are.

---

## 9. Authorization in the UI

### Authorization is server-authoritative

> **Rule.**
> - The frontend never grants authorization.
> - `can()`, `useCan()` and `<Can>` control visibility and interaction only.
> - A permission check may include a scope type and scope ID.
> - The backend command stays authoritative even when the UI has shown an action as available.

A hidden button protects nothing, and a visible one permits nothing.

### Permission state

Authorization state and visibility are two different questions, and the architecture keeps them apart. `AuthSession` holds the authorization state of each permission and scope. `can()` answers a presentation question: should this capability be visible right now?

| Authorization state | Meaning | Visibility (`can()`) |
| --- | --- | --- |
| `unknown` | No source has provided effective permissions yet | visible |
| known → `allowed` | The effective permissions include this code in this scope | visible |
| known → `denied` | The effective permissions don't include it | hidden |

> **Optimistic visibility while authorization is unresolved.** An unknown authorization state lets a capability be shown for rendering purposes. It does not mean the user is authorized. Once effective permissions are known, denied capabilities are hidden. Backend authorization remains authoritative.

So `can(code) === true` must never be read as "the user has this permission". Code that needs to know whether authorization has been resolved asks `AuthSession` for the state; it doesn't infer it from visibility.

### Three ways to check, one source

`AuthSession` holds the effective permissions with their scopes, or `unknown`. It is the only thing that holds permission data, and it is consumed only through:

| Check | Kind | Use |
| --- | --- | --- |
| `can()` | Imperative | Event handlers and plain functions |
| `useCan()` | Reactive | Component logic; re-renders when the permissions change |
| `<Can>` | Declarative | A rendering boundary that shows or hides its children |

```tsx
<Can permission={UserPermissions.create}>
  <Button asChild><Link to="/users/new">Create user</Link></Button>
</Can>

// global scope: no scope argument
const visible = useCan(UserPermissions.create);

// scoped: scope type + scope id — exact TypeScript shape settled in implementation
const visibleInTenant = useCan(UserPermissions.create, { type: "Tenant", id: tenantId });
```

### Rules

- **No component inspects permissions itself.** `principal.permissions.includes(…)` is banned by lint outside `shared/auth`. Every check goes through `can()`, `useCan()` or `<Can>`, and all three read the same `AuthSession` state.
- **Permission codes are typed constants owned by their module**, e.g. `platform/users/permissions.ts` exports `UserPermissions.create = "user.create"`. A code must match the backend catalogue. `shared/auth` knows the type `PermissionCode`, never the values.
- **Scope is part of the check.** The backend model isn't "permission → yes/no". It's a permission, a scope type and a scope ID, because a role assignment carries a `ScopeType` (Global or a named type) and a `ScopeId`. So a check is `can(code, scope?)`. No scope means global. A scoped check supplies a scope type and scope ID. A permission granted in one scope says nothing about another. The TypeScript shape is decided during implementation; this rule is decided now.
- **Hide or disable:** hide navigation and entry points the user can't use. Disable an in-context action, with a tooltip saying why, only where hiding it would make the screen confusing.
- **A refusal still has to be handled.** Permissions can change after they were loaded, so every guarded action still shows the server's refusal (§7).

**Until a source provides effective permissions,** `principal` is `null` (§8) and every permission is `unknown`. Under optimistic visibility, guarded actions are shown, and a caller without the permission gets the server's refusal (§7). When a source provides effective permissions, denied capabilities disappear without any change to components.

**What the backend has to provide:** scoped checks in the UI need `GET /me` to return **effective permissions with their scopes**, not role names. Calculating them in the client from roles would copy the authorization rule into the frontend, which §1 rules out.

---

## 10. Feedback, notification, audit and logging

The backend keeps these as separate capabilities (`docs/architecture.md` §6–§8), and the client does too. They differ in owner, in lifetime, and in whether the client may create them.

| Concern | What it is | Owner | Lifetime | Client may create it? | Client surface |
| --- | --- | --- | --- | --- | --- |
| **Toast** | Short-lived feedback on an action the user just took | The client | Seconds | Yes | `shared/feedback/toast` ("User created.") |
| **Notification** | A message from the platform to a person, stored on the server | Notifications capability | Persistent | No | Email flows only: `/activate`, `/reset-password` (§13) |
| **Audit** | The business and security record of an action | Audit capability, written by the command pipeline | Permanent, immutable | **Never** | `modules/platform/audit`, read-only (future) |
| **Logging** | Diagnostics for developers and operators | Operations | Operational | Local console only | none |

### Rules

- **The client never produces an audit record, or anything that looks like one.** The only way an action is recorded is that its command runs: Create user → `POST /api/users` → the pipeline writes the audit record → an audit screen later reads it. There is no "report this action" call and no client code named for audit. This follows `docs/architecture.md` §6: no public audit writer exists, and none is added on the client either.
- **A toast is not a notification.** It's never persisted, never shown in a notification list, and never used to tell someone something they must not miss. Anything a person has to act on later belongs to the Notifications capability.
- **The word "notification" is reserved for the platform capability.** Toasts live under `feedback/`, so the two can't be confused in code.
- **Client logs never contain tokens, passwords, carriers, request bodies or personal data.** Shipping logs to a server is not decided.

### What exists, and what doesn't

| Concept | State | Frontend consequence |
| --- | --- | --- |
| Notification email flows (activation, password reset) | Specified and implemented | The token-bearing pages in §13 are the frontend half of these flows |
| Notification Center | Not specified, not implemented | Needs its own product and domain decision. No `notifications/` module, route, hook or folder is created ahead of it |

Records of email deliveries are not user-facing notifications and must never be shown as an inbox.

---

## 11. Server state and loading

| Situation | Treatment | Never |
| --- | --- | --- |
| First load of a region | Skeleton shaped like the content | A "Loading…" sentence in place of the page |
| Refetch in the background | Keep the data on screen; a small indicator in the region header | Swapping content back to a skeleton |
| Mutation in progress | The button that started it is disabled and labelled with the action in progress ("Creating…"); the form stays as it is | Replacing the page or form |
| Empty collection | `EmptyState` that says why it's empty and what to do; filtered and unfiltered wording differ | An empty table body |
| Error | `ErrorState` with a retry for queries; an inline message for mutations | A full-page error for a failed mutation |

- `QueryState` applies to regions backed by a query only. Mutations never go through it.
- Paginated lists use `placeholderData: keepPreviousData`.
- Every module keeps its query keys in one factory (`userKeys.list(params)`). Invalidation always goes through the factory, never a hand-typed array.
- Signing out or getting a 401 clears the whole query cache, so the next person on the same browser sees nothing of the previous one.

---

## 12. Forms

### Validation is an affordance

A zod form schema plays the same role as a repository pre-check. It saves the user a round trip, and the command stays the authority.

- Schemas check that required fields are present and have an obvious shape. They **never copy the password policy**, which belongs to the tenant and can change.
- A server `400` is shown on the form, even when the schema accepted the input.
- A schema never rejects input the server would accept.
- Fields are built with `FormField` (label, control, description, error, `aria-describedby`) and never assembled from scratch.

### Unsaved changes

This pattern is defined for the whole app and adopted by every form that edits data.

```ts
useUnsavedChangesGuard(form.formState.isDirty && !mutation.isSuccess);
```

- Leaving the page inside the app is caught with React Router's `useBlocker`. It opens `ConfirmAction`: "Discard changes?" with the buttons **Keep editing** and **Discard**.
- Closing or reloading the tab triggers `beforeunload`, and the browser shows its own prompt.
- The guard turns off after a successful submit and while the redirect that follows it is in progress.
- A form inside a dialog uses the same guard when the dialog closes.

---

## 13. Security-sensitive pages

Emailed links put a credential in the **URL fragment**. Browsers never send a fragment to a server, so the token stays out of access logs, proxy logs and `Referer` headers. Any page that receives such a link has to preserve that.

1. Read the token once, from `window.location.hash`. **Never** read it from query parameters.
2. Remove it from the address bar and browser history straight away with `history.replaceState`.
3. Keep it only in component state: not in storage, the query cache, a URL or a log.
4. Send it only in the body of its own `POST`.
5. A link without a token shows a message, not a form.
6. Show every refusal with the server's single message. When the refusal is a password-policy `400`, the token is still valid, so keep the form open.

> **Rule.** Don't build a generic token-handling utility. This logic stays in each feature that needs it, in plain sight, with its own tests. A shared helper would hide the security reasoning, and one change to it would weaken every page at once.

**A route's path is part of the backend contract when the backend builds links to it.** `/activate` and `/reset-password` are built by `NotificationTemplates`. Renaming either breaks every link already sent.

**A signed-in browser ends its session first.** Sign-in, activation and password reset are refused under an established caller (`docs/architecture.md` §11, §17). The client signs out before starting any of them; it never tries to work around the refusal.

---

## 14. Responsive behaviour

| Tier | Width | Commitment |
| --- | --- | --- |
| **Desktop** | ≥ 1024px (`lg`) | Primary. Every feature is complete and designed here first. |
| **Tablet** | 768–1023px (`md`) | Supported. Every feature works, with a denser or collapsed layout. |
| **Mobile** | < 768px | Usable. Reading, account flows and simple actions work. Heavy data editing may say "best on a larger screen" instead of pretending to work. |

> **Rule.** Each application component defines how it behaves at every tier as part of being built. Features inherit that behaviour and don't write their own breakpoints.

| Pattern | Desktop | Below `lg` |
| --- | --- | --- |
| DataTable | All columns | Columns have a priority, and the lowest drop first; the table scrolls horizontally inside its own container |
| Side / review panel | Docked beside the content | Becomes a Sheet |
| Filters | Inline bar | Collapse behind a "Filters" button into a Sheet |
| App navigation | Persistent sidebar | Opens from the header |
| Timeline / metadata | Two columns | Stacked |

The page body never scrolls sideways. Wide content scrolls inside its own container.

---

## 15. Accessibility baseline

> **Rule.** Every new UI component and screen must meet **WCAG 2.2 AA**. Accessibility is part of each component's contract, not a final QA pass.

The shadcn and Base UI primitives handle much of the mechanical work. Ligature remains responsible for labels, focus management, keyboard operation, error messages, semantic structure, contrast, dialogs, announcing status changes, and reduced motion.

| Requirement | Where it's guaranteed |
| --- | --- |
| Everything works by keyboard, in a logical order | Application components; tested on each flow |
| Keyboard focus is always visible | Theme tokens (`ring`); never `outline: none` without a replacement |
| Native, semantic controls: buttons are `<button>`, links are `<a>` | Lint (`jsx-a11y`) |
| Every form field has a label; errors are tied to their field | `FormField` |
| Dialogs trap focus and return it when closed; Esc closes them | Base UI primitives; tested on `ConfirmAction` |
| Status changes are announced: loading, errors, toasts | `aria-live` in `ErrorState`, form errors and the toaster |
| Contrast of at least 4.5:1 for text and 3:1 for UI in both themes | Theme tokens, checked when tokens change |
| Reduced motion is respected | A global `prefers-reduced-motion` rule |
| Route changes move focus to the page heading and set the document title | `Page` |

Accessibility is tested at the level of components, not page by page (§16): if the application components are accessible, features built from them mostly are too.

---

## 16. Testing strategy

| Layer | Tool | Covers |
| --- | --- | --- |
| Behaviour | Vitest + Testing Library | Anything that makes a decision: the API client, `AuthSession` changes, `can`, token pages, the unsaved-changes guard, forms |
| Network isolation | MSW | Every request a unit or component test makes is answered by a handler; an unhandled request fails the test instead of reaching the network |
| Boundaries | ESLint + fixtures that break the rules | §2, §6 and §8 import and global bans |
| Accessibility | axe + keyboard tests | Every application component; each flow's main path |
| Contract, at the boundary | zod response schemas | Each API operation checks the response shape it receives |
| Real host | A small number of tests against a running host | Only where the cookie and authentication transport matter; the rest of the suite never needs PostgreSQL or .NET |
| Contract, against the document | Schemas compared with `/openapi/v1.json` | **Deferred (O8)** — it reopens `docs/architecture.md` §18's non-decisions and needs CI |
| End to end | Playwright | Complete flows against a running host — a later story |

### Contract validation

Every API operation declares a response schema, and the client parses the response against it. If the backend returns `201 {userId, userIdentityId}` and the client expects `{id}`, the result is an `ApiContractError` that names the operation and the path of the mismatch. It fails loudly at the boundary rather than showing up later as `undefined` deep in a component. This needs no generated clients and reopens no backend decision.

Catching drift before anything runs means comparing those schemas with the host's OpenAPI document in a test. The backend's §18 currently lists document generation at build time, generated clients and document linting as explicit non-decisions, and there's no CI yet. Using the document this way reopens part of that, so it is deferred and not assumed here.

Test names are full sentences, as in the backend.
