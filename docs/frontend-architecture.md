# SKSMCorp Web Client — Architecture Contract

**Status:** Approved. Part I of the web client design (v2), approved by the owner on 14 September 2026 with decisions O1–O9 closed. Referenced from `docs/architecture.md` §2.

**Applies to:** `web/sksmcorp-web/`.

This document is the architecture contract for the web client. Its rules are meant to stay true as SKSMCorp grows well past User Management. It deliberately contains no plan: what each story builds belongs in that story's pull request, not here.

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
web/sksmcorp-web/src/
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
├── hooks/              vendored shadcn hooks (use-mobile), never hand-edited
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
| `components/ui` | `lib/`, `hooks/` | everything else |
| `hooks/` | `lib/`, other `hooks/` | everything else |

`hooks/` exists because the shadcn CLI installs a component's hooks there (`sidebar` brings `use-mobile`). It is a vendored layer like `components/ui`: only primitives use it, and application code reaches its behaviour through the primitive, not the hook.

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
| **`shared/components`** — application components | Page, PageHeader, DataTable, EmptyState, ErrorState, ConfirmAction, FormField, StatusBadge, DateDisplay. Each one sets SKSMCorp's standard for a recurring pattern. |
| **`modules/*/components`** — feature components | CreateUserForm, UsersTable. These carry business meaning. |

Each tier is composed into the one below it.

> **Rule.** Once an application component exists for a pattern, features must use it and may not rebuild it from primitives. Features may use primitives directly for anything no application component covers, such as a single Button.

Application components are added when a second feature needs the pattern. The exception is the foundation's own: an application component the foundation itself renders (`Page`, `PageHeader` and `ErrorState`, used by its shells and error routes) is built with the foundation.

> **Amended (W2).** Every other application component is **built with its first user**, never ahead of it: `FormField` with the first form, `ConfirmAction` with the first confirmation, `EmptyState` with the first collection, `StatusBadge` and `DateDisplay` with the first data that needs them, and `DataTable` with the first list endpoint. A component built before a real flow uses it guesses its API. Accessibility work is never a reason to build one early: tokens are tested as tokens (§15), and components are tested when they exist.

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

> **Amended (W3).** The authentication flows' paths, and where each comes from:
>
> | Path | Origin |
> | --- | --- |
> | `/sign-in` | Chosen by the client |
> | `/forgot-password` | Chosen by the client |
> | `/activate` | **Backend contract** — built by `NotificationTemplates` |
> | `/reset-password` | **Backend contract** — built by `NotificationTemplates` |
>
> The last two must not be renamed: every link already emailed points at them.
>
> The flows mirror the backend's own split. Sign-in and sign-out are `modules/platform/auth`; activation, forgot-password and reset-password are `modules/platform/account`. Where one needs the other's operation — activation and reset end an existing session first, and forgot-password does not (§13) — it goes through that module's **hook**, which is its public surface, never its API operation (§6).

> **Amended (W4). Navigation is composed the way routes are, and it is DATA.**
>
> A module exports the sections it contributes; `app/` composes them; the shell renders them. `shared/layout` may not import a module (§2), and a section label like "Users" carries business meaning that §3 keeps out of `shared/`, so the label travels as data from the module that owns the vocabulary — the shell renders a section without ever learning what one means.
>
> ```ts
> // shared/layout/navigation.ts — no business meaning, only shape
> interface NavigationItem { label: string; to: string; permission?: PermissionCode }
> interface NavigationSection { label: string; items: readonly NavigationItem[] }
>
> // modules/platform/users/navigation.ts — the vocabulary lives here
> export const usersNavigation: readonly NavigationSection[] = [
>   { label: "Users", items: [{ label: "Create user", to: "/users/new", permission: UserPermissions.create }] },
> ];
> ```
>
> Data rather than a `ReactNode` slot, deliberately: markup would let each module style its own navigation, and the hierarchy would stop being inspectable. A section whose entries are all hidden renders nothing, rather than a heading over an empty list.
>
> **An entry's `permission` decides visibility and nothing else.** Hiding an entry a caller cannot use is presentation (§9); the route behind it is **not** gated, because `<Can>` hides its children and a hidden route would render a blank page — an authorization outcome the client is not entitled to invent. What a denied route should render is a real decision, and it belongs with `GET /me`, when a server-backed permission source exists. The server authorizes the command either way.

> **Amended (B6). That decision has arrived, and protected routes are now gated.**
>
> W4 left `/users/new` ungated for one reason: with no server-backed permission source, every permission was permanently `unknown`, so a gate would have been unreachable and untestable. `GET /me` removes that reason.
>
> A route that requires a permission now guards it, and **denial renders an explicit denied state** — never a blank page, never a redirect that makes the route look as though it does not exist. A hidden route is not an authorization outcome; a stated refusal is.
>
> This does not merge the two concerns. An entry's `permission` still decides only whether it is **shown**; the route's guard decides whether it may be **reached**; and the server still authorizes the operation whatever either of them did.

> **Amended (Administration shell). Navigation has two levels: the shell lists areas, and an area lists its pages.**
>
> The primary sidebar lists **areas** in labelled groups; an area with pages of its own renders a **secondary navigation** beside them. Both levels are read from ONE data object, which the module owning the area exports:
>
> ```ts
> // shared/layout/navigation.ts — shape only
> interface NavigationItem  { label: string; to: string; permission?: PermissionCode }
> interface NavigationArea  { label: string; to: string; title: string; description?: string; items: readonly NavigationItem[] }
> interface NavigationGroup { label: string; areas: readonly NavigationArea[] }
>
> // modules/platform/administration — the vocabulary
> export const administrationArea: NavigationArea = {
>   label: "Administration", to: "/admin", title: "Administration", items: [...usersNavigation],
> };
> ```
>
> - **An area has no permission of its own.** It is visible when at least one of its items is visible, and both levels filter the SAME items with the same `can()`. The primary navigation therefore cannot offer an area whose secondary navigation is empty, and the two cannot disagree about what is available.
> - **Only real destinations are listed.** An item enters an area's `items` when its page and its read capability exist. There are no placeholder entries for planned pages or modules.
> - **URLs are the state.** An area is a nested layout route (`/admin`) whose index redirects to its first page (`/admin/users`), and each page is a child path. An entry is active when the current path is its `to` or beneath it, so Administration is active on every `/admin/*` path and Users on `/admin/users/new`. Every page is a deep link; back and forward work because nothing about navigation lives outside the URL.
> - **`AreaLayout` is generic.** It lives in `shared/layout`, renders the area it is given beside an `<Outlet />`, and knows no area's name, pages or permissions. The owning module supplies all three.
>
> Both levels are built from shadcn's `sidebar` primitives (`Sidebar`, `SidebarMenu`, `SidebarMenuButton` rendering a `NavLink`), and each is a `<nav>` landmark with its own label.
>
> **`/users/new` moved to `/admin/users/new`.** It was a client-chosen path, never a backend contract, and nothing linked to it; no redirect is kept.

> **Amended (My account). The caller's own controls live in the shell's footer, not in the navigation.**
>
> The footer carries **My account** beside **Sign out** for every signed-in caller. `app/` fills the footer slot with each module's control, as it already did with Sign out: `MyAccountLink` from `platform/account`, `SignOutButton` from `platform/auth`. The shell still imports no module.
>
> - **`/account` is a signed-in, client-chosen path.** It is composed into the application shell behind `RequireAuth`. The account module's public routes (`/activate`, `/forgot-password`, `/reset-password`) stay in the public shell. No email links to `/account`, so it is not a backend contract path.
> - **The sidebar stays permission-driven areas only.** A page every caller may reach is not an area, and adding one would give the primary navigation an entry no permission governs.
> - **Every part of the shell is in a landmark** (§15): the brand in the banner, the areas in the *Main* navigation, the caller's name and own controls in an *Account* navigation, and the page in `main`.

---

## 6. API layering

| Layer | Responsibility |
| --- | --- |
| **Component** | Renders and handles events. Never imports the API client or an API operation. |
| **Hook** | e.g. `useCreateUser()`: wraps TanStack Query and owns cache invalidation and query keys. |
| **API operation** | e.g. `modules/…/api/createUser.ts`: one function per endpoint. It parses the response against its schema (§16). |
| **API client** | `shared/api/client.ts`: the HTTP operation boundary described below. It reports 401s; it never changes authentication state. |

Each layer calls only the one below it.

```ts
// modules/platform/users/api/createUser.ts
export const createUser = (request: CreateUserRequest) =>
  api.post("/api/users", { body: request, response: createUserResponseSchema });
```

### The HTTP operation boundary

> **Rule.** Feature code communicates with the backend **only** through `shared/api`.

The boundary describes an HTTP operation, not today's endpoint inventory. Every operation goes through one method-agnostic call, and the method helpers are thin conveniences over it:

```ts
api.request(method, path, { body?, response?, unauthorized?, signal? })
api.get(path, options)    api.post(path, options)    api.put(path, options)
api.patch(path, options)  api.delete(path, options)
```

**The boundary owns:**

- **same-origin, relative paths** — an absolute URL is refused, so a credential can never be sent anywhere but the application's own origin;
- **credential transport** — the carrier cookie travels with same-origin credentials; the client never sets an `Authorization` header;
- **JSON** serialisation of request bodies and parsing of responses; a `GET` or `DELETE` with a body is refused;
- **response contract validation** (below);
- **normalised errors** — `ApiError` and `ApiContractError` (§7);
- **unauthorized reporting** (below);
- **abort handling** — an aborted request rethrows the platform's `AbortError` untouched; it is never converted into an `ApiError` and never reported.

**It does not own authentication state.** `shared/api` never imports `shared/auth` and never changes a session. Authentication state listens to the boundary, not the other way round:

```text
feature → shared/api → HTTP
shared/auth → subscribes to shared/api's unauthorized report
```

#### The response contract

Status and body are separate questions, and the contract states both. `204` is not a special case.

| The operation declares | The response must | A contract violation (`ApiContractError`) |
| --- | --- | --- |
| a `response` schema | contain a JSON body that matches the schema | a `204`; a missing body; a body that is not JSON; a body that does not match |
| no `response` schema | contain no body the client consumes | any response body |

So a `204` is valid only for an operation that declares no response schema, and a response schema on a `204` is a violation. A contract violation names the method, the path and the failing field, and fails loudly at the boundary.

#### Unauthorized reporting

`onUnauthorized(handler)` holds **exactly one** registration; registering a second is a defect and throws. `AuthProvider` registers it (§8). Each operation chooses how its `401` is treated:

| Mode | On a `401` |
| --- | --- |
| `"report"` (the default) | 1. the result is `ApiError(401)`; 2. the registered handler is invoked **once** for that response; 3. `AuthProvider` transitions to `unauthenticated`; 4. `AuthProvider` clears the query cache; 5. the error continues to the caller |
| `"return"` | 1. the result is `ApiError(401)`; 2. the handler is **not** invoked; 3. authentication state is not changed; 4. the query cache is not cleared |

`"return"` exists for operations where a `401` is an expected answer rather than evidence that an established session has ended — sign-in, activation, password reset and sign-out (§13). A failed sign-in says nothing about any other session.

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
| `2xx` | Validate against the response contract (§6). A violation is `ApiContractError`, a contract defect (§16). | — |
| `400` | `ApiError(400, message)` | The server's message, word for word, next to the action that failed |
| `401` | `ApiError(401)`, reported or returned as the operation declares (§6) | For a reported 401: a redirect to sign-in. No reason is given. |
| `403` | `ApiError(403, message)`. The session is not changed. | Server message |
| `5xx`, network | `ApiError(status)`, or an `ApiError` of kind network. The session is not changed. | `ErrorState` with retry, or an inline error on a form |
| aborted | The platform's `AbortError`, untouched. Never reported, never shown. | — |

> **Amended (W2).** The row for `403` used to read "handled as a permission refusal; the backend doesn't send 403 for authorization yet". The host's only `403` today is its **cross-site refusal** (`docs/architecture.md` §17), which a same-origin client sees only when a deployment is misconfigured — a proxy rewriting `Host`, for example. It is therefore an ordinary error, not a permission state. A missing permission is still refused with `400`.

> **Rule.** Logic branches on status codes, never on message text. A message is shown to the user and never interpreted.

> **Amended (W3).** One named exception, on one page. **The sign-in page shows fixed text — "Invalid username or password." — for a `401`, and never the server's own `401` string.** `"Authentication is required."` is the pipeline's wording, addressed to an API caller rather than to a person at a sign-in form, and the backend returns it deliberately for every failure alike: unknown user, wrong password, locked, inactive, and a request that already presents a live session.
>
> This branches on **status**, not on message text, so the rule above stands unchanged. Every other status on that page follows the table above, and a `400` is still shown word for word.

---

## 8. Authentication state

`AuthSession` answers one question: **what does the client currently believe about authentication?** It is not a source of authorization (§9).

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
| A reported 401 (§6) | `unauthenticated`; the query cache is cleared | `AuthProvider`, on the API client's unauthorized report |
| Sign-out settles, whether or not the call succeeded | `unauthenticated`; the query cache is cleared | `platform/auth` hook |

`RequireAuth` renders nothing while the state is `unknown`, the app shell when `authenticated`, and a redirect with a return path when `unauthenticated`. Components read `useAuthSession()` and never find out which source is in use.

| | Source today | Source after `GET /me` |
| --- | --- | --- |
| Name | `SessionHintSource`: a flag in `sessionStorage` | `ServerSessionSource` |
| `principal` | always `null` | holds the identity and effective permissions |
| `resolve()` | answers immediately from the flag | asks the server; `unknown` lasts until it answers |

- **The hint is never authoritative.** Until a server-backed source exists, the client cannot reliably know the authenticated user's identity after a reload. The server's answer to each request — a 401 in particular — always wins over the hint.
- **`sessionStorage` remains the session hint source. It is not an identity-discovery mechanism and cannot establish whether the browser's carrier cookie represents a live session. W3 identity-establishing flows must end any existing session before proceeding. B6 remains the eventual authoritative mechanism for resolving this ambiguity.**
- **This mitigates the new-tab ambiguity; it does not resolve it.** `sessionStorage` belongs to one tab, so a tab opened on its own starts without a hint while the shared cookie may still be live. Ending the session first (§13) keeps identity-establishing flows correct in that state. Nothing else can tell the two apart until B6 provides a server-backed source.
- Lint bans `sessionStorage` and `localStorage` everywhere except the hint source's own file, so the hint can't spread into the rest of the app.
- The hint source's file header says it is **not a security control**. Setting the flag by hand gets you an empty shell and a redirect on the first request.
- Switching sources changes one line in the composition root (`app/`). Routes, guards and components stay as they are.

> **Amended (B6). `GET /me` is the source, and `AuthState` has four states.**
>
> The `sessionStorage` hint is **gone**. The bullets above describing it — that it is never authoritative, and that it mitigates but does not resolve the new-tab ambiguity — are superseded: the server answers the question directly, and web storage is now banned everywhere with no exception.
>
> | State | Meaning | `RequireAuth` renders |
> | --- | --- | --- |
> | `unknown` | resolution has not completed | nothing |
> | `authenticated` | the server established a caller | the application |
> | `unauthenticated` | the server answered `401` | a redirect to sign-in |
> | `error` | resolution **failed**, so the caller could not be determined | an error with a retry |
>
> **A failed resolution is an authentication-resolution error, not an unauthenticated state.** A `5xx` or a network failure means *we do not know* whether the session is valid, and treating that as signed-out would end a live session because a server had a bad moment. Only a `401` means there is no caller.
>
> The retry re-asks `/me`. It does **not** clear authentication state or the query cache, because nothing has been established about the session — a retry is not a sign-out.
>
> **`/me` is called on application and authentication-boundary resolution, and on explicit retry. Never on a timer.** It is an authenticated request and therefore counts as session activity, so polling it would keep an idle session alive indefinitely and quietly defeat the idle timeout. It must not be used as a heartbeat or liveness probe.
>
> **The event table's `Sign-in succeeds | authenticated | platform/auth hook` row is superseded.** A successful sign-in is an authentication boundary, so it *resolves* the session rather than declaring it:
>
> | Event | Next state | Emitted by |
> | --- | --- | --- |
> | Sign-in succeeds | `unauthenticated` → whatever `/me` then returns | `platform/auth` hook, through `AuthSession` |
>
> Accepted credentials establish that the password was right. They do not establish **who the caller is**, and the difference is not academic: a session entered on the strength of the `204` alone has a `null` principal, so every permission is `unknown`, so §9's optimistic visibility — which applies only *while resolution is in flight* — becomes permanent for the life of that session.
>
> The intermediate state is therefore **not** `authenticated` with a `null` principal: that claims an established caller before anything has established one. The sign-in surface stays on screen while `/me` resolves, so the authenticated application is never rendered without an answer, and a sign-in whose caller cannot be resolved is an authentication-resolution failure — stated as one, and never reported as a rejected sign-in.
>
> **The pre-flight sign-out of §13 is unchanged.** `/me` does not replace it. The browser cannot inspect the `HttpOnly` cookie, so an earlier `401` from `/me` is not grounds to conclude that no session exists now, and identity-establishing commands are still refused while a caller is established.

---

## 9. Authorization in the UI

### Authorization is server-authoritative

> **Rule.**
> - The frontend never grants authorization.
> - `can()`, `useCan()` and `<Can>` control visibility and interaction only.
> - A permission check may include a scope type and scope ID.
> - The backend command stays authoritative even when the UI has shown an action as available.

A hidden button protects nothing, and a visible one permits nothing.

> **A hidden UI element is not authorization. Every protected operation remains server-authorized.**

Authentication and authorization are separate. `AuthSession` holds what the client believes about authentication; when a source supplies effective permissions, `AuthSession` carries them **as presentation input only** — it never becomes the authoritative source of permissions. `can()`, `useCan()` and `<Can>` are presentation mechanisms. They are never a security boundary, and no code may treat their answer as permission to do anything.

### Permission state

Authorization state and visibility are two different questions, and the architecture keeps them apart. `AuthSession` holds the server-reported authorization state of each permission and scope, as presentation input. `can()` answers a presentation question: should this capability be visible right now?

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
| `can()` | Imperative | Event handlers and plain functions; obtained from `useAuthSession()`, never from global mutable state |
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

> **Amended (B6). That source now exists.** `GET /me` returns the caller's **effective permissions with their scopes** — each entry carrying a `code`, a `scopeType`, and a `scopeId` that is a UUID or null — rather than role names, which the client would otherwise have to interpret into permissions and so reimplement the authorization rule (§1).
>
> Optimistic visibility therefore applies only **while resolution is in flight**, not permanently. Once `/me` has answered, a denied capability is known to be denied and is hidden.
>
> **Route authorization and navigation visibility remain distinct**, and the distinction is now load-bearing rather than theoretical:
>
> | | Decides | Mechanism |
> | --- | --- | --- |
> | Navigation visibility | whether an entry is **shown** | `<Can>` on the entry |
> | Route authorization | whether a page may be **reached** | the route's own guard |
>
> Hiding an entry has never protected anything, and it still does not. The server authorizes every operation regardless of either.

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

> **Amended (B6). The first real read exists, so query keys begin here.**
>
> `GET /me` is a query like any other, with its own key factory in `shared/auth`. Authentication resolution consumes that query rather than reaching past the query layer to the transport — the cache is how the answer is held, and the server remains the authority for what the answer is.
>
> Its lifecycle is the one exception worth stating: it is resolved at the application and authentication boundary and on explicit retry, and **never on an interval**. A stale cached `/me` is not proof of authentication after a fresh resolution.
>
> There is no circularity in caching it. `/me` does not authenticate anyone — the server does that from the session cookie — it only reports who the caller is, and signing out clears the cache without needing to ask.

---

## 12. Forms

### Validation is an affordance

A zod form schema plays the same role as a repository pre-check. It saves the user a round trip, and the command stays the authority.

- Schemas check that required fields are present and have an obvious shape. They **never copy the password policy**, which belongs to the tenant and can change.
- A server `400` is shown on the form, even when the schema accepted the input.
- A schema never rejects input the server would accept.
- Fields are built with `FormField` (label, control, description, error, `aria-describedby`) and never assembled from scratch.

> **Amended (W3).** **No form library.** Forms hold their input in React state and parse it with zod on submit. The `useUnsavedChangesGuard` example below is written in React Hook Form's vocabulary and describes the pattern for the first form that *edits* data; the library arrives with that form, not ahead of it.
>
> A **confirmation field** — "confirm your new password" — is a client affordance against mistyping a value the user cannot read back. It is not part of the request body, which carries only the new password, so it does not breach the rule that a schema never rejects input the server would accept.
>
> `FormField` is an accessibility presentation primitive and **knows nothing of zod, of any form library, or of API errors**. It receives an already-computed error string and does not care where it came from. Lint enforces this.

> **Status (USR-C2 UI, 2026-09-18).** React Hook Form was introduced with USR-C2 as the first data-editing form (the Edit profile dialog), consistent with W3's deferred form-library decision above, together with `@hookform/resolvers` for Zod. W3 is kept as written: it records the decision as it stood. `useUnsavedChangesGuard(dirty: boolean)` now exists in `shared/forms` and is independent of any form library. The existing forms stay on `useState` until they are migrated: Create user computes its own dirty flag and uses the guard; the Grant role form has no guard yet.

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

> **Amended (W3).** Clause 1, precisely. The fragment must contain **exactly one `token` parameter whose value is non-empty and not only whitespace**. Missing, duplicated, blank or unparseable all produce clause 5's no-token state: `#token=a&token=b` is **refused**, rather than quietly taking the first, which is what `URLSearchParams.get` would do. Be explicit about credential-bearing URL syntax instead of inheriting a library default.
>
> The value is never trimmed or otherwise altered before it is sent. It is an opaque credential, and the backend percent-escapes it when building the link. No other fragment parameter is read, and none affects this decision.
>
> Clause 2 is observable: the fragment is removed on mount, preserving path and query, and **a test asserts that `window.location.hash` is empty afterwards**.

> **Rule.** Don't build a generic token-handling utility. This logic stays in each feature that needs it, in plain sight, with its own tests. A shared helper would hide the security reasoning, and one change to it would weaken every page at once.

**A route's path is part of the backend contract when the backend builds links to it.** `/activate` and `/reset-password` are built by `NotificationTemplates`. Renaming either breaks every link already sent.

**A signed-in browser ends its session first.** Sign-in, activation and password reset are refused under an established caller (`docs/architecture.md` §11, §17). The client ends any existing session before starting any of them; it never tries to work around the refusal.

> **Amended (W2).** How the session is ended, exactly. Because sign-out is itself a state-changing operation, its response is examined rather than ignored. It happens **when the form is submitted**, never when the page loads, so merely opening an emailed link while signed in changes nothing.

| `POST /api/auth/sign-out` with `unauthorized: "return"` | Then |
| --- | --- |
| `204` — a session existed and has been revoked | `signedOut()` (hint and query cache cleared), then submit the identity-establishing request |
| `401` — no live session; the hint was stale or absent | `signedOut()`, then submit |
| `400`, `403`, `5xx`, a network failure, or a contract violation | **Stop.** Do not submit the identity-establishing request. Show the error inline, and leave authentication state unchanged, because whether a revocation happened cannot be known. The user may retry. |
| aborted | Stop, silently |

The identity-establishing request itself also uses `unauthorized: "return"`: its `401` is an answer about the credentials presented, not about any session. This is implemented with the flows in W3.

> **Amended (W3).** Which flows, and what the local state does *not* prove.
>
> Sign-in, activation and password reset end any existing session first. **Forgot-password does not**: it establishes no identity and is refused by nothing. Sign-out is the operation itself.
>
> **The client does not treat local `signedOut()` as proof that the server revoked anything.** It is a statement about what the client believes. If the identity-establishing request that follows still meets a live server session, the backend's `401` remains authoritative. That is a property worth keeping rather than a case to work around.

> **Amended (W3). The return path is validated by `shared/auth/validateReturnPath`, and by nothing else.** `RequireAuth` captures the path and query and passes them on without judging them. **React Router's refusal to perform external navigation is not this contract**: it is a library behaviour that may change, it resolves some malformed inputs rather than rejecting them, and it is not a security boundary.
>
> A value is accepted only when every one of these holds; otherwise the result is `"/"`, and the function never throws:
>
> 1. it is a non-empty string;
> 2. it begins with exactly one `/`, and the next character is neither `/` nor `\`;
> 3. it contains no backslash anywhere — the URL parser reads one as `/`;
> 4. it contains no control character (below `U+0020`, or `U+007F`) — the parser strips tabs and newlines before resolving;
> 5. it contains no `#`;
> 6. it still resolves to this origin.
>
> **The returned value is always a path with an optional query, and never contains a fragment.** It is built as `${url.pathname}${url.search}` — the normalised form the parser agreed to, never the raw input — so it cannot carry a fragment even if one reached the construction, and a test asserts this of the result directly, independently of the rules above. The fragment is where emailed credentials travel, and a return path is never allowed to become somewhere one could be parked.

> **Amended (W3). A successful password reset does not revoke existing sessions.** CRD-C3's frozen contract writes the credential, the token and the password history and clears the lockout; it does not touch `user_session` and emits no `SessionRevoked`. Change-password (CRD-C4) *does* end the account's other sessions, by A5 — the two differ, and the difference is deliberate in the specification rather than an implementation oversight.
>
> **The client must therefore not represent a successful reset as revoking other sessions.** The success wording is "Your password has been changed. Sign in with your new password." and says nothing further. A future change to revoke sessions on reset requires backend change control and is outside this contract.

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

> **Amended (Administration shell). Navigation uses two breakpoints, taken from the shadcn `sidebar` rather than imposed on it.**
>
> | Width | Primary sidebar | An area's secondary navigation |
> | --- | --- | --- |
> | < `md` (< 768px) | A sheet, opened from the header | A horizontal row above the page |
> | `md` – < `lg` | Persistent | A horizontal row above the page |
> | ≥ `lg` | Persistent | A persistent column beside the page |
>
> The primary breakpoint is `md`, not `lg`, because the vendored `use-mobile` hook switches there, and vendored files are never hand-edited. The table above replaces the "App navigation" row's `lg` for navigation only.
>
> **The header carries a `SidebarTrigger` at every width.** Below `md` it opens the sheet; above, it collapses and restores the sidebar. `SidebarProvider` also binds **Ctrl/⌘+B** to the same toggle, so the visible trigger is the way back for anyone who collapsed the sidebar by keyboard. The shortcut uses a modifier and is therefore not a single-character shortcut under WCAG 2.1.4. Navigating from the sheet closes it.
>
> **`sidebar_state` is accepted as a UI-state cookie.** `SidebarProvider` writes `sidebar_state=true|false; path=/` when the sidebar is toggled. Recorded because this client is otherwise deliberate about cookies (`docs/architecture.md` §17):
>
> - it holds only whether the sidebar is expanded — no authentication or authorization state;
> - it is not `HttpOnly`, because shadcn's component writes it from script;
> - its path is `/`, so it is sent with every same-origin request, API requests included;
> - the backend does not read it;
> - **nothing reads it.** The provider writes it and never reads it back — the upstream design has a server render read it, and this client has none — so a collapsed sidebar is expanded again after a reload. Observed in the running client, not inferred.
>
> It is not redesigned, and the vendored component is not edited to remove it.

---

## 15. Accessibility baseline

> **Rule.** Every new UI component and screen must meet **WCAG 2.2 AA**. Accessibility is part of each component's contract, not a final QA pass.

The shadcn and Base UI primitives handle much of the mechanical work. SKSMCorp remains responsible for labels, focus management, keyboard operation, error messages, semantic structure, contrast, dialogs, announcing status changes, and reduced motion.

| Requirement | Where it's guaranteed |
| --- | --- |
| Everything works by keyboard, in a logical order | Application components; tested on each flow |
| Keyboard focus is always visible | Theme tokens (`ring`); never `outline: none` without a replacement |
| Native, semantic controls: buttons are `<button>`, links are `<a>` | Lint (`jsx-a11y`) |
| Every form field has a label; errors are tied to their field | `FormField` |
| Dialogs trap focus and return it when closed; Esc closes them | Base UI primitives; tested on `ConfirmAction` |
| Status changes are announced: loading, errors, toasts | `aria-live` in `ErrorState`, form errors and the toaster |
| Contrast of at least 4.5:1 for text and 3:1 for UI in both themes | Theme tokens, proved by the token contrast test (below) |
| Reduced motion is respected | A global `prefers-reduced-motion` rule |
| Route changes move focus to the page heading and set the document title | `Page` |

Accessibility is tested at the level of components, not page by page (§16): if the application components are accessible, features built from them mostly are too.

> **Amended (W2).** Two separate accessibility contracts, and neither stands in for the other:
>
> - **Rendered semantics — axe.** `axe-core` runs over rendered components and shells. jsdom cannot lay out a page, so axe's colour-contrast rule is off there, and a passing axe run makes no claim about contrast.
> - **Theme tokens — the token contrast test.** A deterministic invariant: token pair → contrast ratio → WCAG threshold, in both themes, for every surface a token is used on. A required token that is missing fails the test. It proves the tokens; it does not prove every rendered combination.
>
> The keyboard focus indicator is an application-level solid outline in `var(--ring)`, because a vendored primitive's translucent ring cannot meet 3:1. `--border` is **decorative** — dividers and outlines of containers that are identified by other means — and is exempt from the 3:1 non-text requirement; anything that is the only visible boundary of a control uses `--input`, which is not exempt.

> **Amended (My account). Shells are audited as a whole page.** axe applies its *region* rule, *all content is contained by landmarks*, only when it audits the page. Every earlier shell test audited the render container, so the rule never ran. Audited as a page, the brand, the caller's display name and the footer controls were outside every landmark. That was true before My account added a link there. The shell now places each in one (§5, *Amended (My account)*). `app/accessibility.test.tsx` audits `document.body` for callers whose permissions are unknown, empty, or include `user.read`, with the caller's name shown.

---

## 16. Testing strategy

| Layer | Tool | Covers |
| --- | --- | --- |
| Behaviour | Vitest + Testing Library | Anything that makes a decision: the API client, `AuthSession` changes, `can`, token pages, the unsaved-changes guard, forms |
| Network isolation | MSW | Every request a unit or component test makes is answered by a handler; an unhandled request fails the test instead of reaching the network |
| Boundaries | ESLint + fixtures that break the rules | §2, §6 and §8 import and global bans |
| Accessibility, rendered | `axe-core` + keyboard tests | Every application component and shell; each flow's main path. Colour contrast is not evaluated in jsdom. |
| Accessibility, theme tokens | The token contrast test | Every required token pair in both themes against WCAG 2.2 AA thresholds (§15) |
| Contract, at the boundary | zod response schemas | Each API operation checks the response shape it receives |
| Real host | A small number of tests against a running host | Only where the cookie and authentication transport matter; the rest of the suite never needs PostgreSQL or .NET |
| Contract, against the document | Schemas compared with `/openapi/v1.json` | **Deferred (O8)** — it reopens `docs/architecture.md` §18's non-decisions and needs CI |
| End to end | Playwright | Complete flows against a running host — a later story |

> **Amended (W3).** The real-host tests are **`npm run test:host`**: a separate Vitest project and configuration, and never part of `npm test`, which stays independent of PostgreSQL, .NET, certificates and browsers.
>
> The command owns its whole environment — a throwaway database built with the production tooling, the host, and the HTTPS development server — and drops the database in teardown, including after a failure. It reuses nothing that happens to be running, because a host already running points at a database this suite must not touch. **A missing prerequisite fails naming which one and how to provide it; nothing skips silently.**
>
> Two techniques, and neither substitutes for the other:
>
> | Technique | Proves |
> | --- | --- |
> | **Chromium**, driven by Playwright (pinned, Chromium only) | What a real browser originates: how it stores and transmits a `Secure`, `__Host-` prefixed cookie, and what it actually sends for Fetch Metadata. Requests are issued **by the page**; `page.request` is not used for any proof, because it shares the cookie jar but its handling of `Secure`, `SameSite`, `__Host-` and Fetch Metadata is unstated, and an unstated behaviour cannot be evidence. |
> | **`node:http`** | The exact `Host` / `Origin` / `Sec-Fetch-Site` matrix. Node's `fetch` silently drops `Host`. |
>
> A browser cannot demonstrate the cross-site refusal with a JSON `POST`: the CORS preflight stops it before the middleware sees it, so a cross-origin browser proof uses a simple request. Each matrix row asserts **"not the cross-site refusal"**, never "succeeded" — those requests may still fail on their own merits with `400` or `401`, and a proof that accepted any non-`403` would pass for the wrong reason.
>
> These are **transport tests**. End-to-end UI testing remains a later story.

### Contract validation

Every API operation that returns a body declares a response schema, and the client parses the response against it (§6). If the backend returns `201 {userId, userIdentityId}` and the client expects `{id}`, the result is an `ApiContractError` that names the operation and the path of the mismatch. It fails loudly at the boundary rather than showing up later as `undefined` deep in a component. This needs no generated clients and reopens no backend decision.

Catching drift before anything runs means comparing those schemas with the host's OpenAPI document in a test. The backend's §18 currently lists document generation at build time, generated clients and document linting as explicit non-decisions, and there's no CI yet. Using the document this way reopens part of that, so it is deferred and not assumed here.

Test names are full sentences, as in the backend.
