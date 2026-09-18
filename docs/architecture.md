# Ligature Architecture

**Status:** Architectural contract

Ligature is the current codebase/project name. **RegOS** may be used when referring to the broader Regulatory Operating System product concept — in discussion only, never in code. Every project, assembly and namespace is `Ligature.*`.

This document defines the architectural direction and boundaries for the Ligature codebase. Build, test, migration and validation procedure lives in `AGENTS.md` §3 and is not repeated here.

---

# 1. Current Scope

All work to date is in the **Platform** module, specifically **User Management** (its ownership is defined in §5).

Little else described in this document exists yet — no Notifications or Logging implementation, no business domain, no tenancy. Where a later section describes one of those, it is describing target state.

The one exception is **Audit's schema and tamper boundary**, which is implemented: the five audit tables, the database role model and the privilege, ownership and trigger protections around them (§19). Audit *emission* — the pipeline that writes records — is not built, so nothing yet populates the trail.

---

# 2. Current Structure vs Target Structure

What exists today:

```text
src/
├── Platform/
│   ├── Ligature.Platform.Domain        aggregates, value objects, enums    (Users/, Provenance/)
│   ├── Ligature.Platform.Application   commands, handlers, behaviors, abstractions
│   └── Ligature.Platform.Persistence   EF Core, repositories, services, migrations, provisioning
└── Shared/
    └── Ligature.SharedKernel           Entity, AggregateRoot, ValueObject, StronglyTypedId,
                                        ICommand/IQuery/ICommandHandler/ICommandBehavior, exceptions

tests/Platform/                         one test project per layer, mirroring src/Platform/
```

Identity and access live in the `Users/` folders of each layer. That is the User Management module.

## Target Mapping

**A conceptual module does not automatically require its own .NET project.**

| Capability          | Initial project boundary                         |
| ------------------- | ------------------------------------------------ |
| User Management     | Platform projects (`Users/` folders)             |
| Audit               | Platform projects                                |
| Notifications       | Platform projects                                |
| Logging             | Platform projects                                |
| Document Management | Platform projects unless independently justified |
| Workflow            | Platform projects unless independently justified |
| Regulatory          | Separate `Ligature.Regulatory.*` project tree    |
| Clinical            | Separate `Ligature.Clinical.*` project tree      |

Platform capabilities are folders inside the `Ligature.Platform.*` layer projects: add `Ligature.Platform.Domain/Audit/`, not `Ligature.Audit`.

A folder becomes its own project only for a demonstrated reason — independent schema, independent deployment, dependency isolation, or significant size. Surface the reason before doing it. Do not create empty projects to satisfy a theoretical architecture.

New business domains follow the same layering:

```text
src/
└── Regulatory/
    ├── Ligature.Regulatory.Domain
    ├── Ligature.Regulatory.Application
    └── Ligature.Regulatory.Persistence
```

## The web client

The browser client lives at `web/ligature-web/`. It consumes the host's HTTP contract and is **not** a second implementation of any module. Its structure, layering and rules are governed by **`docs/frontend-architecture.md`**, which mirrors this section's module boundaries on the client: platform modules first, business modules later, with the same dependency direction (§10).

---

# 3. Modular Monolith

Ligature is a **modular monolith initially**: one deployable application composed of well-defined modules.

```text
Ligature Application
│
├── Platform
│   ├── User Management
│   ├── Audit
│   ├── Notifications
│   ├── Logging
│   └── other platform capabilities
│
├── Regulatory
├── Clinical
└── future business modules
```

Do not introduce microservices unless explicitly requested. The objective is strong module boundaries without premature distributed-system complexity. A module may be extracted into a service in the future if there is a demonstrated need.

---

# 4. Packaging and the Host Application

The current class-library/DLL boundaries are the three Platform layer projects — `Ligature.Platform.Domain`, `Ligature.Platform.Application` and `Ligature.Platform.Persistence` — with `Ligature.SharedKernel` beneath them. Conceptual capabilities (User Management today; Audit, Notifications and Logging later) live *within* those platform libraries according to the project structure in §2. **A capability does not get its own DLL**: the DLLs are the layers, not the capabilities.

A **host application** will reference those DLLs and compose them into the client-facing, deployable application. It is the composition root: it registers the libraries, supplies configuration such as the connection string, establishes the calling user, and exposes the application to clients. The existing libraries already expose composition entry points used at that boundary (for example `AddPlatformApplication()` and `AddPlatformPersistence(...)`); these are implementation details of the current code, not themselves an architectural contract.

```text
Ligature Host Application
│
├── Ligature.Platform.Domain.dll
├── Ligature.Platform.Application.dll
├── Ligature.Platform.Persistence.dll
│
├── Ligature.Regulatory.Domain.dll        (future)
├── Ligature.Regulatory.Application.dll   (future)
└── Ligature.Regulatory.Persistence.dll   (future)
```

**The host application is `src/Host/Ligature.Host`.** It exposes activation, sign-in and sign-out over HTTP and implements §17. It is deliberately thin: HTTP binding, carrier issuance and verification, and composition. Everything that decides anything sits behind `ICommandDispatcher`.

It is still true that the class libraries are built, tested and exercised **without** it — `AGENTS.md` §3 describes how — and that remains the rule. Do not grow the host as a side effect of another story, and do not move logic into it because HTTP made that convenient.

## Packaging boundaries are not deployment boundaries

The DLL/project boundaries provide modularity, reuse and dependency control. They do **not** mean each capability is a separate process or service.

> **A DLL/project boundary is a packaging and dependency boundary, not automatically a deployment or service boundary.**

The initial deployment is a single host application — one process — loading every module's DLLs. This is the modular monolith of §3, expressed in packaging terms.

## Rules

- **Platform libraries must not depend on the host application.** Dependencies point from the host to the libraries, never back. Anything a library needs from its host — configuration, the connection string, the current caller — arrives through a registration entry point or an abstraction the library itself defines. The current code already works this way; the specific entry points are implementation details, not a frozen host API.
- **Future business modules follow the same pattern**: `Ligature.Regulatory.Domain.dll`, `Ligature.Regulatory.Application.dll`, `Ligature.Regulatory.Persistence.dll` — and likewise Clinical — referenced by the same host.
- **Extraction into separate services or processes remains possible** — that is what the boundaries preserve — but it is **not the current architecture or goal**. See §13.

---

# 5. User Management

Owns identity and access: users, actors, roles, permissions, user-role assignments, sessions, identity rules, security policy, and authorization.

Does **not** own: audit persistence, technical logging, email infrastructure, general notifications, or any business-domain logic.

Other modules ask it *who is the current user?* and *is this user authorized to perform this action?* without depending on how any of it is stored.

---

# 6. Audit

Audit is a separate platform capability. It records business- and security-relevant events that form an authoritative history: user created, role assigned, permission changed, submission status changed, document approved, workflow completed.

Audit is not application logging. Audit records are business/compliance records, not diagnostic messages.

Handlers do not write audit records. A handler **declares** the events its catalogue entry names — event type and version, primary entity, entity references, before/after or payload, reason — and returns them. The command pipeline resolves the event catalogue, attaches the actor snapshot, correlation and timestamps, validates the declaration, and writes `audit.audit_record` and `audit.audit_entity_ref`.

There is deliberately **no public `IAuditWriter` contract, and none is to be introduced.** A direct write API is a named anti-feature: anything a handler can call, a handler can omit, and a caller holding a writer can invent its own snapshot. The writer is internal to the pipeline assembly, is not registered in the container, and handler assemblies do not reference it. The pipeline cannot omit itself.

**Do not introduce an event bus solely for audit decoupling.** Reconsider an event-based mechanism when there is a demonstrated need, such as multiple independent consumers.

**Implemented.** The pipeline emits: `TransactionScopeBehavior` opens the command's transaction and `AuditEmissionBehavior` writes inside it, before the commit. `PlatformProvisioner` emits `TenantProvisioned` as the tenant's first record — the one emission that is not a command. USR-C1 is wired end to end and lands three records under one operation. The remaining commands, the events of Slice B, and audit queries are separate stories. See §11 for the pattern and §19 for the schema and its tamper boundary.

---

# 7. Logging

Logging is separate from Audit. It is for technical and operational diagnostics: request started/completed, query failed, external call failed, timings, unexpected exceptions.

Application logs must never be used as a replacement for regulatory audit records.

*Target state — not yet implemented.*

---

# 8. Notifications

Notifications is a separate platform capability owning delivery mechanisms — email, in-app, future channels.

Business modules express intent ("notify the reviewer that a submission requires review"); Notifications decides delivery. User Management may use it for invitations, password resets and account notifications, but does not own email infrastructure.

*Target state — not yet implemented.*

> **Amended (development mail sink). A second transport, for development only.**
>
> Delivery goes through `INotificationTransport`, whose only production implementation is `GmailTransport`. A developer without a Google Workspace service account had no way to receive an activation or reset link: with mail unconfigured every notification ages to `Abandoned`, and the link — held only in memory by the command that issued it — is gone.
>
> `DevelopmentMailSink` is a second implementation that writes each rendered message to a file instead of sending it. Rendering, the eligibility gate and the sender are unchanged; only the final hand-off differs.
>
> **It cannot reach production, by two independent locks:**
>
> 1. **It exists only in Debug builds.** The type and its registration are compiled under `#if DEBUG`. The production image is published in Release, so its assemblies do not contain the sink at all. The development container runs `dotnet watch run`, a Debug build.
> 2. **It needs an explicit setting**, `LIGATURE_MAIL_DEV_SINK_DIRECTORY`. `ASPNETCORE_ENVIRONMENT` is deliberately not the gate, for the reason §18 gives: it is ambient and settable from outside the deployment.
>
> **Misconfiguration refuses start-up**, naming the setting and never a value: the sink combined with any Gmail setting (two transports), the sink without `LIGATURE_PUBLIC_BASE_URL` (the link is built from it), and **the sink setting in a Release build** — so a production deployment that somehow carried it stops rather than silently ignoring it.
>
> **What it writes.** One file per message — recipient, subject and the full body, activation link included — created owner-read/write only, in a directory that must already exist. The row is recorded as `Sent` with `transport_message_id = dev-sink:<file name>`, so the record states where the message went. One Information log line names the file, never its contents.
>
> **Accepted exposure.** Live links sit in plain files on the developer's machine, as they already sit in the Gmail sending account's Sent folder (`GmailTransport`). Both are bounded by the token lifetime, and the sink directory (`.secrets/mail`) is git-ignored. This is acceptable for development and is the reason the sink cannot exist in a Release build.

---

# 9. Database Ownership

**Target state:** database-per-tenant. Each tenant has its own database containing every module's tables. Tenant management is intended to live *outside* the tenant application/platform runtime; this document does not yet name or specify that component.

**Current state:** one database, one connection string, no tenant resolution, no tenant management. None of the target state above is implemented. Do not assume tenant plumbing exists; changing tenant/database isolation is an escalation item.

Regardless of tenancy, each module owns the database objects belonging to its domain. A module must not read or write another module's tables; use the owning module's application contract.

**One recorded exception: the notification eligibility gate.** `NotificationGate`
reads `user_token`, `user_identity` and `app_user` directly. This is deliberate,
not drift. The frozen Notification specification defines the eligibility
predicate (§5.2) across exactly those three records, and its privilege matrix
(§8.1) grants the gate the reads it needs — "as User Management grants; the gate
is a read". It is evaluated immediately before transport, never cached, and
writes nothing: a token can be invalidated by a concurrent issuance between the
notification row being written and the send being attempted, and noticing that
is the entire purpose of the read.

Routing it through a User Management application contract would mean a
background sender taking a request-scoped dependency, which is the arrangement
IMPL-N02 exists to avoid. Recorded here so a future reviewer does not "fix" an
intentional decision.

---

# 10. Dependency Direction

Business modules may consume platform capabilities. Platform modules must not depend on business modules without an explicitly approved architectural decision.

```text
Regulatory → User Management | Audit | Notifications      acceptable
User Management | Audit | Notifications → Regulatory      not acceptable
```

Circular dependencies are never acceptable. Check direction before adding a project reference.

Within a module, the layer direction is `Persistence → Application → Domain → SharedKernel`. Domain references nothing but SharedKernel.

---

# 11. Established Patterns — Use These

These are decisions already made in code. Follow them; do not introduce a parallel mechanism. Where a file is named, read it before writing code in that area.

### Command pipeline

`ICommandDispatcher` resolves the handler and the registered `ICommandBehavior<,>` instances and runs them through `CommandPipeline`, which executes behaviors in **registration order**:

```text
AuthenticationBehavior → HumanActorBehavior → AuthorizationBehavior
    → TransactionScopeBehavior → AuditEmissionBehavior → handler
```

The last two are the audit pipeline, and their position is the design: a command refused by authorisation never opens a transaction, and everything a permitted command records is written before that transaction commits.

**Authorization is the pipeline's responsibility, not the handler's.** A command declares its requirement through `IAuthorizableCommand` / `IHumanActorOnlyCommand`; the handler assumes it has already been enforced. Do not call handlers directly and do not re-check authorization inside a handler.

`IAuthorizationService` returns an `AuthorizationResult`, not a `bool`: it reports **which** assignment permitted the act, and `AuthorizationBehavior` records that on the execution context. The selection — earliest `EffectiveFrom`, then assignment id — decides only what is *reported*; several assignments may legitimately authorise one act, and the decision is unchanged. Reconstructing the authority later would re-run the predicate against tables that have since changed, which answers a question about the past with today's configuration.

### The execution context has two lifetimes

`IExecutionContext` carries the caller's **identity** and the **authority** under which the current command was permitted. They are not the same lifetime, and conflating them is a defect:

```text
Identity    scope lifetime      established once, atomically, immutable
Authority   command lifetime    established per command, released afterwards
```

**A DI scope is not one command** — a request or a test may dispatch several. Authority that outlived its command would be read by the next one, and an audit record naming the wrong authorising assignment is worse than one naming none. `AuthorizationBehavior` therefore holds it for the duration of the command and releases it afterwards, including when the handler throws.

Absent authority is a real answer, not a half-built context: sign-in, self-service and token-bearer commands are authenticated and authorised by no role, and a refused command has an identity and no authority by definition.

There are two write seams, `IExecutionContextInitializer` (identity) and `IAuthorityInitializer` (authority), kept separate so the component that records authority cannot rewrite who the caller is.

**Bearer-authenticated commands establish their identity later, and that is the rule, not an exception. Frozen (E2a, widened in E2b).** For a bearer-authenticated command, actor identity may be established inside the handler after successful bearer authentication or validation, using exactly the identity that authentication resolved. Once established it is atomic and immutable for the remainder of the command execution, exactly as it is on the normal path. This does not loosen the rule above: it is a second establishment path, anticipated by AUD-D28, for commands that carry their credential in their payload. Bearer, not token — an activation token and a password at sign-in are the same shape, and the rule was named too narrowly the first time. Which identity the credential belongs to is not knowable before the step that validates it has run, so establishing earlier would mean either resolving the identity twice or trusting something the bearer supplied.

The authenticated identity is the sole authority for who is acting. `IBearerActorEstablisher` therefore takes a `UserIdentityId` rather than resolving one, and it must be the exact value authentication returned: the row the token consumption updated, or the identity whose password verified. No second token lookup, no walk from the user back to an identity, no fallback to `ICallerEstablisher`. The chain is: token, conditional consume, that identity, the establisher, the execution context, and the refs on the records. Any other route lets the actor drift from the bearer who was actually authenticated, and a drifted actor is indistinguishable in the trail from a correct one.

**A bearer-authenticated identity-establishing command may execute only when no caller is already established in the execution context.** Such commands declare `IBearerAuthenticatedCommand`, which extends `IAnonymousCommand`; today they are SES-C1 SignIn, CRD-C1 ActivateAccount and CRD-C3 ResetPassword. `AuthenticationBehavior` refuses one with `AuthenticationFailedException` when a caller is established, before anything is verified, consumed or declared, and it tests the marker rather than naming any command.

Why: the identity cannot be rebound, and a record's origin follows the scope's caller. Under an established caller, such a command could reach another account's credential and then be unable to act as its owner, and its failure records — `SignInFailed` and `TokenRejected`, permitted only an Anonymous origin (EO5) — could not be written. Before this rule, a caller holding any live session got a different answer for a correct password for another account (401) than for a wrong one (500), and the guesses left no records.

Where, and why there: the refusal decides whether the command may *start*, so it sits in the first behaviour, outside the transaction. An execution strategy retries the transaction and the handler inside it in the same scope, where the first attempt has already established the bearer; a check inside that work would refuse its own retry. `BearerActorEstablisher`'s tolerance of an established caller exists only for that replay.

What it does not change: a plain `IAnonymousCommand` that establishes nobody — CRD-C2 RequestPasswordReset — still runs with or without a caller. The client consequence is that a signed-in browser ends its session before signing in, activating or resetting, as any account including its own.

The establisher adds no eligibility rules. Whether a deactivated user may still activate an account is a User Management question, recorded as an open requirement, and answering it inside an audit story would turn an audit change into an authentication-policy change.

**Not decided:** the context assumes one command at a time within a scope. Its guards are check-then-set, which is correct for the sequential pipeline but is not a synchronisation primitive; a scope driven concurrently by several commands is outside the model, not something the guards make safe. Supporting that is an explicit decision, not an implementation detail to be added by whoever first needs it.

### Queries

**Decided in B6-A.** `IQuery<TResult>` and `IQueryHandler<TQuery, TResult>` stay as they are in SharedKernel: bare, and already identical in shape to `ICommand<TResult>`. Query-specific semantics arrive only with a demonstrated need.

Queries are **dispatched**:

```text
HTTP endpoint → IQueryDispatcher → IQueryHandler<TQuery, TResult> → read
```

`IQueryDispatcher` is the seam between the Host and the application, exactly as `ICommandDispatcher` is. It is **not** justified by a future need for behaviours; it exists because an endpoint resolving a query handler directly would itself be a new architectural pattern (§12), and the application boundary stays consistent with the one commands established.

> **A dispatcher is not a pipeline.** There is deliberately no query pipeline and no query behaviour. Nothing is copied onto queries by symmetry with commands.

| | Queries today | Why |
| --- | --- | --- |
| Behaviours | none | None is justified yet. Each would need its own demonstrated need |
| Registration | explicit, one line per handler, commented with its requirement ID | As commands. Nothing scans, so an unregistered handler fails loudly instead of being found by magic |
| Authorization | **no generic contract yet** | The first read is caller establishment, not a permission-gated resource read. **A query that requires authorization introduces the appropriate abstraction as part of the story that requires it** — this is not a ruling that queries cannot be authorized |
| Transaction | none | Queries do not inherit the command transaction boundary. A read needing internal consistency obtains it through its own read operation, not by borrowing a boundary built for writes |
| Audit | **not audited by default** | The audit boundary (§6) covers state-changing operations. **A requirement to audit a particular read must be introduced explicitly by the story that requires it** — this is a bounded decision about the default, not a ruling that read auditing is forbidden |

The caller is established by middleware **before** any pipeline runs, so a query inherits an established caller without needing query-side machinery to produce one.

**A new cross-cutting concern for queries still needs its own decision**, exactly as the first query did. Do not add a query behaviour, pipeline or authorization contract inside another story.

> **Amended. The first permission-gated query has arrived, and it brings the authorization contract §11 reserved for it.**
>
> The `Authorization — no generic contract yet` row above is superseded. That row was correct while the only read was `/me`, which is self-scoped and refuses nobody; the first read that must refuse a caller introduces the abstraction, as the row required.
>
> ### Query authorization declaration
>
> Every registered query handler must have an explicit authorization classification.
>
> A query that requires no authorization declares `NotRequired`. A permission-gated query declares the required permission **on the query type itself**, as a static abstract member of its classification interface — the single source of truth, consumed by both registration and enforcement.
>
> **The classification is two explicit states, never one nullable value.** `NotRequired` and `Required(<permission>)` are both positive declarations. A single nullable member — `static abstract string? RequiredPermission`, where `null` is taken to mean "no authorization required" — is forbidden, and an absent, null or blank permission is not a declaration of anything and must be rejected by the verifier.
>
> The reason is the same one the whole contract rests on: it would make the most consequential state in the system the one you get by **not typing anything**. "This query is deliberately open" and "somebody left this blank" would become indistinguishable, which is precisely the implicitness being removed.
>
> Query registration must require an authorization classification and must not provide an unclassified registration path.
>
> The application verifies all registered query handlers at startup. Any handler registered outside the approved registration mechanism, or whose query does not provide a valid authorization classification, **prevents application startup**.
>
> **Open-generic query-handler registrations are not supported** by the query registration model and must cause startup verification to fail. An open generic `IQueryHandler<,>` handles queries that cannot be named at registration, so there is no concrete query whose declaration could be verified; permitting one would be an escape hatch around the invariant rather than an exception to it.
>
> The declaration is **descriptive only**; it does not authorize a request. A permission-gated handler must enforce its query's declared permission against the established caller before accessing protected data.
>
> `IAuthorizationService` remains unchanged. Query authorization consumes `IsAllowed`; `Authority` is **not** used unless a later read explicitly requires audit authority capture — it exists to record the assignment a command acted under, and a read that is not audited has no use for it.
>
> **No query authorization pipeline or dispatcher behaviour is introduced by this story.** The dispatcher resolves a handler and invokes it; that remains the whole of it.
>
> ### Why omission, not just refusal, is the thing being designed against
>
> Registration was made explicit because *"an unregistered handler fails loudly instead of being found by magic"*. That reasoning does not transfer to authorization. An unregistered handler fails **loudly** on first dispatch; a handler that forgets its authorization check fails **silently — it serves the data**. Explicitness is safe where forgetting is loud and dangerous where forgetting is quiet, which is why the declaration is compulsory at the point of registration and verified again at start-up.
>
> The two layers catch different failures:
>
> | | Catches | When |
> | --- | --- | --- |
> | Classification required to register | the ordinary omission | compile time |
> | Start-up verification of registered handlers | a deliberate bypass of the registration helper | start-up; the application refuses to run |
>
> ### Pattern note — static abstract interface members
>
> **Static abstract interface members are used here for the first time in this codebase, deliberately: they are the only mechanism that keeps the declaration on the query type, enforced at compile time, without reflection or scanning.**
>
> Registration has the query **type**, not an instance, so an instance member cannot be read there; a static member or attribute read by reflection would reintroduce exactly the scanning this section bans, and the compiler could not force one to exist. The alternatives were weighed and rejected on those grounds.
>
> This note exists so the decision is not later "simplified" into an attribute-plus-reflection mechanism, or into a second registry maintained beside the handler registrations — which would replace *"someone forgot the authorization check"* with *"someone forgot to update the authorization registry"*, and gain nothing.
>
> Permission codes remain plain strings, as `IAuthorizableCommand.RequiredPermission` and `AuthorizationRequest.PermissionCode` already are. A strongly typed permission code is a separate and larger change, and the command side is where it would have to start.

> **Decided. The user-list read (USR-Q2) is not audited.**
>
> The `Audit — not audited by default` row required a read that is audited to say so in its own story. The first permission-gated read, listing a tenant's users under `user.read`, was examined against that row and **does not** introduce read auditing. The default is confirmed for it deliberately, not inherited by omission.
>
> This is the Audit design's own rule, not an exception to it. Audit design specification §12.2, *Reads are not events*: routine tenant inspection queries write nothing (AUD-17). The control on reading is the permission, and the visibility of who holds it; an export is the exception because it leaves the system. A user-list read leaves nothing behind that the trail would need to explain.
>
> Consequently:
>
> - The read declares `Required("user.read")` and its handler enforces it through `IsAllowed`. `Authority` remains unused.
> - No audit event type is added for it, and no catalogue change accompanies it.
> - **`AuditInspected` does not apply.** It records a platform operator reading the *audit trail* (behaviour 19): its primary entity type is `AuditTrail`, and AR27 makes the database refuse it for any actor that is not a `PlatformOperator`. It is not a general read-audit event and must not be reused as one.
> - A refused read writes nothing either. That is not a decision taken here: the refusal event, `AuthorisationDenied`, has no writable form for commands or queries alike (`docs/requirements.md`, *AuthorisationDenied and CommandRejected have no writable form*), and a read will not be the place it is first solved.
>
> This decides one read. A later read that must be audited — an export, or anything the Audit design names — still introduces that requirement in its own story, as the row says.

### Handler registration

Handlers are registered explicitly, one by one, in `Ligature.Platform.Application/DependencyInjection.cs`, with the requirement ID as a comment. Do not introduce assembly scanning. "Which commands are wired in" must be answerable by reading that method.

### Transaction boundary

`IUnitOfWork` owns the transaction. The handler wraps its whole operation in `IUnitOfWork.ExecuteInTransactionAsync(...)`; the unit of work saves, translates known constraint violations, and commits. One business operation, one transaction. Repositories never save or commit.

`TransactionScopeBehavior` now opens that transaction first, through the same unit of work. The handler's own `ExecuteInTransactionAsync` call is unchanged and still correct: the unit of work already enlists in a transaction someone else owns, running the work and flushing while leaving the commit to the owner. Handlers were not modified for this and must not be.

### Audit emission

Handlers **declare**; the pipeline **writes**. A handler injects `IAuditEvents`, whose entire surface is `Emit(code, version)`, and describes the event with the fluent builder — primary entity, references, before/after or payload, reason. It never sees a record, a snapshot or a table.

`AuditEmissionBehavior` runs inside the transaction `TransactionScopeBehavior` opened. When the handler returns, it takes what was declared, attaches the actor snapshot and the command's operation id and timestamps, resolves each event against the deployed catalogue, validates it, and writes. The rules it enforces are the invariants, named in the message when one fails.

Three things follow, and each is load-bearing:

- **`IAuditRecordWriter` is internal and unregistered outside the pipeline.** Handlers live in the same assembly, so the compiler cannot enforce the separation; `AuditWriterIsolationTests` does. There is no public write API and none is to be introduced (§6).
- **Every emission failure is `InvalidOperationException`, never a domain error.** It is a defect in the code that declared the event, so it propagates, the transaction rolls back, and the host answers 500. A user-facing message would invite a retry that cannot succeed, and a record that fails validation is not evidence.

  **Frozen (E1).** A dedicated `AuditEmissionDefect` was considered and rejected: the platform's exception vocabulary is three types, and widening it for one capability is a larger decision than this story. Every emission failure therefore opens its message with `Audit emission defect`, which is what identifies it in a log when the response carries no detail. Keep that phrase; a new emission failure that does not use it is a defect in the defect.
- **Which command may emit which codes is a static registry**, `AuditDeclarations`, verified against the deployed catalogue at start-up in `Program.cs`. A release whose handlers declare an event the database has not seeded refuses to start, rather than failing on the first request that reaches it.

**Some records must outlive the command. Frozen (E2b).** A record of a FAILURE cannot ride the transaction that failed: it would be rolled back exactly when it was needed. Those events are marked `Autonomous` in the catalogue, and the pipeline writes them on a connection and transaction of their own, after the command's transaction has committed or rolled back. `AuditCommandScopeBehavior` sits outside the transaction behaviour and does this; it also owns the `OperationId` and the command clock, which belong to the command rather than to either transaction and must outlive the transactional one.

```text
audit command scope
  ├── transaction
  │     ├── transactional emission
  │     └── handler
  └── autonomous emission (its own transaction)
```

Routing is the catalogue's. Each declaration is looked up and sent to the writer its entry names, so a handler cannot choose, and a code the catalogue does not know stays on the transactional path where its defect is raised rather than falling between two writers.

**What happens when an autonomous write fails. Frozen (E2b).** If the command succeeded, the request fails: the business change stays committed, because it was committed before the write was attempted and undoing it is not on offer, but nobody is told an action was recorded when it was not. If the command was already failing, its own exception wins unchanged — a 400 must not become a 500 because the trail was unavailable — and the audit failure is attached to it and logged by the host. Nothing is ever swallowed.

**The V1 autonomous emission crash window.** Between the command's commit and the autonomous commit the process can die, and that record is lost. This is accepted by the frozen failure semantics rather than overlooked: closing it means putting the record inside the command's transaction, which is the one thing these events cannot do. It is a known consistency boundary, not a reliability gap to be fixed by a retry someone adds later without reopening the decision.

**An event may be attributed to System, and to nothing else. Frozen (E2b).** `AsSystem()` on a declaration replaces the command's actor with the System actor for that one event. There is deliberately no `As(userId)`: a handler that could name any actor could write a record blaming somebody. It exists because one command can produce events with different actors — a failing sign-in that crosses the lockout threshold records the attempt, which nobody authenticated, and the lock, which the system imposed by policy. The catalogue's permitted origins are the second line of defence.

**A path the catalogue declares as PII is exempt from value-shape secret detection. Frozen (E2b).** Such a path is known personal data, declared in advance and governed by the anonymisation model; the scan exists to catch secrets nobody declared. Without this, whether a refused sign-in could be recorded would depend on the length and character composition of whatever was typed into the username box. Name-based detection stays mandatory everywhere.

**The catalogue decides the write path, and the validator enforces it. Frozen (E2a).** Every emission path states which write path it can honour, and `AuditRecordAssembler` refuses a declaration whose catalogue entry says the other one. Behaviour 7 passes `Transactional`; the autonomous writer will pass `Autonomous` and reuse the same rule rather than escape it. This is a comparison, not a ban on one value, so neither path can quietly write the other's events. It matters most in the direction that is currently possible: an autonomous event describes a FAILURE, and writing it on the command's transaction would roll it back precisely when it was needed.

**`CausationId` is null within a command. Frozen (E1).** `OperationId` already groups the records of one command, and causation names the record that *caused* another. Declaring it between USR-C1's three events would assert a dependency the handler does not have, and would make the trail claim something about causality that nobody established. Causation is for a cause that is genuinely a different act; use it there, and nowhere else, and do not backfill it into a command's own events.

Emission is not free of consequences elsewhere. An audit record names its actor by foreign key to `app_user` (AR10) and the assignment that authorised it by foreign key to `user_role` (AR12), and no role may delete audit rows. **Once a user has acted under an audited command, that user and that assignment can no longer be deleted** — by anyone. Integration suites that used to seed and discard a caller now seed a permanent one; see `PermanentTestCaller`.

### Pre-checks and database constraints

**The application pre-check is an affordance; the database constraint is the authority.**

Repositories mirror named database constraints (AU3, UI7, …) with raw SQL that uses the same normalisation the constraint does (database-side `lower()`, not C# `ToLowerInvariant()`), so the pre-check and the constraint agree. `PostgresExceptionTranslator` maps a violation of a *known, named* constraint to the same exception type the pre-check throws, so a caller cannot tell which one rejected them. Two concurrent creates both pass the pre-check; only one passes the INSERT.

The translator is deliberately narrow: only constraints a command can actually reach are mapped; everything else is rethrown untouched. When a new command makes a constraint reachable, add it to the map — do not add a generic fallback.

Never present a pre-check as the integrity guarantee, and never remove a database constraint because an application check exists.

### Serialising commands on one user

**Commands whose correctness depends on a user's lifecycle status take that user's row lock before reading it.** `IUserRepository.FindForUpdateAsync` locks the `app_user` row (`SELECT … FOR UPDATE`) and then loads it, inside the unit of work's transaction. USR-C4, USR-C5 and AUT-C1 GrantRole all do this (docs/requirements.md, "USR-C4 / USR-C5", D6).

Read Committed alone cannot stop a grant that read `Active` from committing after a deactivation. No constraint spans "this user is inactive" and "this assignment is live", so the ordering has to come from the lock. Whichever command locks first decides the order; the other waits, then re-reads. Do not solve a race like this with retries or after-the-fact cleanup.

Two rules make the lock mean something:

- **Lock first.** EF returns an entity it already tracks without re-reading it, so a user loaded earlier in the same scope would come back as read, not as it is under the lock.
- **Read the clock after the lock.** A row committed while the command waited was written later than a clock read taken before the wait.

A new command that must not interleave with deactivation takes the same lock. Isolation stays at Read Committed.

### Provenance

`CreatedAt`/`CreatedBy` are domain properties: the handler reads them from `IClock` and `IExecutionContext` and passes them to the aggregate factory. `UpdatedAt`/`UpdatedBy` (G4) are **EF shadow properties** stamped by `ProvenanceStampingInterceptor` on every `SaveChanges`. Handlers and entities must never set them by hand. Append-only tables declare no shadow properties and are left alone by construction.

### Domain model

- Every identifier is a strongly-typed ID deriving from the non-generic `StronglyTypedId` base (e.g. `UserId`), with a `StronglyTypedIdValueConverter<TId>` in Persistence.
- Entities derive from `Entity<TId>` / `AggregateRoot<TId>`; value objects from `ValueObject`.
- **Aggregate shape:** construction is controlled by private constructors — a private full-state constructor, plus a private parameterless constructor only where EF needs one to materialise the entity (EF binds the full constructor by parameter name for some entities, so the parameterless one is not universal). Static factory methods (`User.CreateHuman`) enforce invariants; setters are private. No public constructors.
- **Exception vocabulary** — three types in SharedKernel, each deriving directly from `Exception`; there is no inheritance hierarchy among them:
  - `DomainException` — thrown by Domain: aggregate invariants and value-object validation.
  - `BusinessRuleViolationException` — thrown by Application: handler pre-checks, `AuthorizationBehavior`, `HumanActorBehavior`; and by `PostgresExceptionTranslator` for known constraint violations, so the pre-check and the database produce the same type.
  - `AuthenticationFailedException` — thrown by `AuthenticationBehavior`.

  Do not invent parallel exception types or introduce a base-class hierarchy.

### Persistence

- One `LigatureDbContext`; one `IEntityTypeConfiguration<T>` per aggregate/entity in `Persistence/Configurations`.
- Seed data (system roles, permissions, initial security policy, bootstrap administrator) is defined once in `SecurityBaseline` / `PlatformProvisioner` and asserted against the live database by `CatalogueDriftTests`. Change the seed in one place and the drift test tells you if the database disagrees.
- `Ligature.Platform.Persistence` exposes internals to its test project via `InternalsVisibleTo`.

### Tests

- Test names are sentences: `An_administrator_creates_a_user_an_identity_and_a_token`.
- Tests verify behaviour and business rules, not implementation details.
- Persistence tests are integration tests against real PostgreSQL and inspect rows with Npgsql directly rather than through the DbContext.

### Documentation

Non-obvious decisions carry an XML doc explaining **why**, and where a plausible alternative exists, why it was rejected. Cite the governing requirement ID.

---

# 12. Avoid Premature Abstraction

Do not introduce abstractions, frameworks, event buses, generic infrastructure, or additional projects because they might be useful in the future. An abstraction should solve a demonstrated problem.

> Simple implementation + strong boundaries. Not maximum abstraction.

The dispatcher/behavior pipeline and the other patterns in §11 are already part of the architecture. Reusing them is not premature abstraction; bypassing them to "keep it simple" is a new pattern and needs discussion.

---

# 13. Future Extraction

A module may eventually become an independent service. Do not design for that today. Maintain clean boundaries and contracts so extraction remains possible if it becomes necessary.

---

# 14. Architectural Decision Rule

When a new feature is introduced, first determine:

1. Which module owns the responsibility?
2. Does the capability already exist?
3. Which existing pattern should be used?
4. What dependencies are required?
5. Does the change cross a module boundary?
6. Does it require a new architectural decision?

If the answer conflicts with this document, stop and surface the conflict. The coding agent may recommend an architectural change but must not silently introduce one.

---

# 15. Requirement Catalogue

Implementation work is traceable to `docs/requirements.md`. See `AGENTS.md` §16 for the traceability rules and the interim status of the catalogue.

---

# 16. Architectural Principle

Ligature is:

> **One application composed of multiple well-defined modules, with strong boundaries and minimal coupling.**

Prefer: clear ownership, explicit dependencies, simple implementations, reuse of established patterns, database-enforced invariants, traceable requirements, owner-approved architectural decisions.

Avoid: premature microservices, unnecessary abstractions, cross-module database access, duplicate platform capabilities, hidden architectural decisions inside feature work.

---

# 17. Access Token and Caller Establishment

**Status:** Architectural decision, **implemented** by `src/Host/Ligature.Host` and `CallerEstablisher`. Resolves the `AGENTS.md` §17 escalation recorded in `docs/requirements.md` under *"Access token issuance is unspecified"*.

**Amended** by the cookie transport decision (web client design, 2026-09-14). See *Amendment: cookie transport* at the end of this section. The statements it changes are marked *Amended* where they stand, not rewritten.

The enforcement tolerance and the activity-staleness threshold are both 60 seconds, and both live on `CallerEstablisher` as named constants.

## The constraint that settles most of this

Decision **D7** in the frozen specification already rules out the largest option:

> *"Server-side sessions, or self-contained tokens alone? **Server-side sessions.** The requirement that the server terminates inactive sessions cannot be met honestly by a self-contained token, and browser-side enforcement is not a control."*

And the functional walkthrough states the per-request obligation:

> *"Every subsequent request verifies that the session has not been revoked, has not passed its absolute expiry, has not been idle beyond the configured timeout, and that both the user and the identity remain active. That final condition is what allows a deactivation to take effect immediately, rather than waiting for a token to lapse."*

**Every authenticated request therefore consults the database.** A token carrying its own claims about identity, permissions or validity cannot satisfy that, because a deactivation would not take effect until the token lapsed.

## What the token is

The access token is a **short-lived signed carrier for the server-side SessionId. It has no independent expiry; its validity is determined by the authoritative server-side session validity predicate.**

```text
<key-id>.<base64url(SessionId)>.<base64url(HMAC-SHA256)>
```

The signature covers the canonical unsigned portion — the key identifier and payload, joined as they appear:

```text
<key-id>.<base64url(SessionId)>
```

The format is deliberately minimal rather than a standard container. A container designed to carry claims will eventually be given some, and they will appear to work. This format has nowhere to put one.

### What it contains

Exactly two things: the **key identifier** and the **SessionId**.

It does **not** carry `UserId`, `IdentityId`, roles, permissions, an issued-at time, a token expiry, or a session expiry. The session row is the single source of truth for lifetime, and a second copy of that fact would eventually disagree with it — the same reasoning that rejects a `LogoutAt` column and a `PendingActivation` status.

## Signing and keys

**HMAC-SHA256.** The Host both issues and verifies, and no external party verifies, so symmetric signing is sufficient and adds no dependency.

The signing key is **configuration**, and:

- there is **no default, no hard-coded development key, and no automatically generated key**. Missing or invalid key configuration is a startup failure. A generated-per-restart key would silently sign every user out on deploy; a default key would be a vulnerability no test would catch
- key material is **at least 32 bytes**, validated at startup
- the Host holds one **current signing key** and a set of **accepted verification keys**

The first token component is the **key identifier**, not a format version. It names which configured key verifies this carrier; the initial identifier is `v1`. Keeping these distinct means a future format change and a key rotation cannot be confused for one another.

Rotation costs nothing structurally: because sessions are server-side, retiring a key invalidates outstanding carriers but destroys no session state.

## Transport

```text
Authorization: Bearer <carrier>
```

> **Amended (cookie transport).** This was the only transport. It is still supported, but it is no longer the only one: browsers present the same carrier in the `__Host-ligature` cookie, and a non-blank `Authorization` header takes precedence with no fallback. See *Amendment: cookie transport*.

## Ownership boundary

| Owner | Responsibility |
| --- | --- |
| **Host / infrastructure** | HTTP authentication, ~~bearer extraction~~ credential extraction — bearer header or carrier cookie (*amended*), cross-site refusal (*amended*), signature verification, carrier issuance |
| **User Management** | Session lookup and validity, user and identity validity, establishing the caller |

The Host must not decide independently whether a session is active or whether a user has been deactivated. It extracts a `SessionId` and asks the platform.

### Issuance

SES-C1 returns `Succeeded` and a `SessionId`. The Host mints the carrier from that. `SignInCommandHandler` knows nothing of tokens, signing keys or HTTP headers, which keeps sign-in usable outside HTTP.

> **Amended (cookie transport).** The minted carrier is no longer returned in a response body. Successful sign-in answers `204 No Content` and delivers the carrier only in the `Set-Cookie` header. A caller that is not a browser takes it from there and may present it as `Authorization: Bearer`. The handler is unchanged: delivery is still entirely the Host's concern.

## Per-request caller establishment

One platform operation, given a `SessionId`:

```text
load session
  → not revoked
  → within absolute expiry
  → within idle window + enforcement tolerance
  → user active
  → identity active
  → establish IExecutionContext
  → record activity if required
```

Validity is evaluated **before** activity is recorded. Otherwise an already-idle session would resurrect itself simply by making one more request.

## Session activity

The `LastActivityAt` update is **authenticated-request infrastructure**. It is not a command, and no `RecordActivityCommand` exists — a command per API call would be absurd, and activity represents the authenticated *request*, not the successful completion of business work. A failed command is still legitimate activity.

It is:

- **throttled** — written only when the stored value is sufficiently stale
- **monotonic** — `UserSession.RecordActivity` already rejects backwards movement in the domain, and persistence must additionally prevent a concurrent stale write from moving the value backwards
- **independently persisted** — caller establishment and the activity write are one semantic operation but must not be assumed to share a database transaction

### Enforcement tolerance: 60 seconds

Closes open decision **A6**.

This is an **enforcement and write-recognition tolerance**, not an extension of the configured timeout. With a 30-minute `SessionIdleTimeout`, the configured timeout remains 30 minutes; a request arriving shortly after may still be accepted because the stored activity value is throttled.

US7 sets the direction: the effective idle timeout may exceed the configured value by the documented tolerance, but **must never fall short**. A genuinely active user is never signed out early.

## Failure behaviour

Every invalid state produces **one indistinguishable outcome** — no caller established:

malformed carrier · invalid signature · unknown key identifier · unknown session · revoked session · expired session · idle session · inactive user · inactive identity.

No distinction reaches the caller. Session identifiers are supplied by callers, so any observable difference between these states would let one be probed for the others.

**An absent `Authorization` header is not an authentication failure.** The middleware simply establishes no caller, and the command pipeline decides:

```text
anonymous command   + no caller        → reaches the handler
authenticated command + no caller      → AuthenticationBehavior rejects
authenticated command + invalid carrier → no caller → AuthenticationBehavior rejects
```

> **Amended (cookie transport).** Read "absent `Authorization` header" as "absent credential": a request that presents neither a bearer header nor the carrier cookie establishes no caller. An invalid cookie is treated exactly like an invalid header, and that includes a cookie presented twice or under a differently cased name. How the credential is chosen is set out in *Amendment: cookie transport*.

Caller establishment **returns a result; it does not throw**. It is middleware, not a command, and an exception escaping to a client is how internal detail leaks.

## Explicit non-decisions

This section deliberately does **not** decide, and code must not assume:

- ~~**browser token storage** — a UI security decision, made when a UI exists~~ — **decided** by the owner when the web client was designed: not application-managed. See *Amendment: cookie transport*.
- ~~**cookie transport** — not required by anything today~~ — **decided** by the owner when the web client was designed: an approved browser transport. See *Amendment: cookie transport*.
- **any standard token container**, or the terminology that comes with one
- **self-contained authorization claims** of any kind
- **a token or verifier column on `user_session`** — the frozen entity is unchanged
- **a query dispatcher or query pipeline** — see §11
- **asymmetric signing** — revisit only when something outside the Host must verify

Reopening any of these is an architectural change, not an implementation detail. The two struck through above were reopened that way — by owner decision, not inside a feature story — and the decision is recorded below. The others remain non-decisions.

## Amendment: cookie transport

**Status:** Owner decision, taken with the web client design (v2, decisions O1–O3, approved 2026-09-14). **Implemented** by `src/Host/Ligature.Host` on `feature/host-cookie-transport`: `CarrierCookie`, credential selection in `CallerMiddleware`, `CrossSiteMiddleware`, and the sign-in and sign-out endpoints.

Nothing earlier in this section has been deleted. Every statement this amendment changes stays where it was, marked *Amended*, so the record shows what was decided first and what changed.

### Why

The web client is a browser application. A carrier held where page script can read it can be stolen by any script that runs on the page, so one injection becomes session theft. An `HttpOnly` cookie is not readable by script.

The cookie is a second transport for the **same carrier**. Its format, the signing and keys, the session row as the only authority on validity, and the single indistinguishable failure outcome are all unchanged.

### Two transports, one carrier

```text
Authorization: Bearer <carrier>        non-browser callers — unchanged
Cookie: __Host-ligature=<carrier>      browsers
```

- **Bearer remains supported.** Nothing that worked with the header stops working.
- **Cookie transport is an approved browser transport.** `CarrierCookie` is the one place that names the cookie and sets its attributes: `HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/`, and no `Domain`. It has no `Expires` and no `Max-Age`, because the session row stays the only record of lifetime.
- **Browser token storage is not application-managed.** The browser holds the cookie. The web client never reads, stores or forwards the carrier, and no client-side token store exists.

### Choosing the credential

The source is chosen by **presence**, before either one is interpreted:

```text
a non-blank Authorization header   → that header alone; never falls back to the cookie
otherwise, the carrier cookie      → exact name, presented exactly once
otherwise                          → no credential
```

A forged bearer header next to a valid cookie authenticates nothing. Every invalid state of either transport is the one outcome described in *Failure behaviour*.

### Issuance and removal

| Operation | Carrier cookie |
| --- | --- |
| `POST /api/auth/sign-in`, success | `204 No Content`, no body; the carrier is set **only** in `Set-Cookie` |
| sign-in refused (`400`, `401`, `403`) | not set |
| `POST /api/auth/sign-out` | the session is revoked, **then** the cookie is cleared — whichever transport presented the carrier |
| sign-out refused (`401`) | nothing cleared |
| `POST /api/account/sign-out-everywhere` | cleared when `keepCurrentSession` is false, omitted or null; left alone when true |
| `POST /api/users/{userId}/sign-out-everywhere`, `POST /api/sessions/{sessionId}/revoke` | never cleared — an administrator's action is not a sign-out of the browser making the request, even when it ends that administrator's own session |

**A caller that is not a browser gets the carrier from `Set-Cookie`** on the sign-in response. It may then present it as `Authorization: Bearer`. No response body carries it any more.

Revoke, then clear, is the contract. `ProblemMiddleware` clears the response on every mapped failure, which would also remove a deletion written too early. That is a second line of enforcement, not the reason for the order.

### Cross-site requests

A browser attaches the cookie to requests the page did not make. The Host therefore refuses cross-site state-changing requests **before the caller is established**: `CrossSiteMiddleware` runs ahead of `CallerMiddleware`, so a refused request never reaches the session store.

```text
GET, HEAD, OPTIONS                              → allowed
a non-blank Sec-Fetch-Site                      → decides alone; only "same-origin" is allowed
otherwise, no non-blank Origin                  → allowed
otherwise, exactly one http(s) origin whose
host[:port] equals Request.Host                 → allowed
anything else                                   → 403
```

- **A blank `Sec-Fetch-Site` is treated as absent** and falls through to the `Origin` rule. A non-blank value is authoritative: `same-site`, `cross-site` and `none` are refused, and weaker evidence never overrides it.
- **A safe method never changes state.** The rule allows `GET`, `HEAD` and `OPTIONS` unconditionally, and that is sound only while none of them has a side effect. An operation that changes state must not be exposed on a safe method, and that includes any future query.
- `SameSite=Strict` is not enough on its own. It is scoped to the site, so a request from a sibling subdomain still carries the cookie; and sign-in, activation and reset carry no cookie for it to withhold.
- A refusal is `403` with the same `{"error": …}` body shape as every other failure. It is logged with bounded values, and the `Cookie` and `Authorization` headers are never logged.

### Signing in while signed in

Sign-in, activation and password reset do not run under an established caller (§11), whichever transport established it and whether the target identity is the same or a different one. They are refused with the same `401`, and no cookie is set or cleared.

A browser that is signed in must therefore **end its current session first**. Tabs share the cookie, so signing out and back in rotates the session for all of them. A cookie that names a revoked or unknown session establishes no caller, so it never locks a browser out of signing in. That needs no special handling at sign-in.

---

# 18. API Documentation

**Status:** Architectural decision, **implemented** by `src/Host/Ligature.Host`. No requirement ID: this is infrastructure, not a catalogue entry (`AGENTS.md` §16).

The host publishes an **OpenAPI document** and a **Scalar reference UI**, and publishes neither unless an operator asks for it.

## What generates the document

`Microsoft.AspNetCore.OpenApi`, the framework's own generator, which emits **OpenAPI 3.1.1**.

Swashbuckle was not chosen. It is no longer what the `webapi` template installs, it is community-maintained rather than shipped with the framework, and adopting it would mean carrying a second description pipeline alongside the one already in the box. The endpoints are minimal APIs, which the built-in generator describes natively.

## What renders it

`Scalar.AspNetCore`, mapped at `/scalar`.

Microsoft's documentation presents Swagger UI and Scalar as equal options and recommends neither. Scalar was chosen because it is one call with no options object, and because its stated purpose — readable API reference — is what this surface is for. Swagger UI's framing in the same documentation is ad-hoc endpoint testing, which is not the need here.

**This is a presentation choice, not a contract.** Replacing Scalar with Swagger UI or ReDoc would change nothing else in this section.

## The gate is explicit configuration, not the environment name

`LIGATURE_API_DOCUMENTATION` must be `true`. **Absent means off**, and a value that is neither `true` nor `false` **stops the process** rather than being read as off.

Microsoft's sample and the .NET templates gate the equivalent routes on `IHostEnvironment.IsDevelopment()`. **Ligature does not**, for the same reason §4's host registers no developer exception page in any environment: `ASPNETCORE_ENVIRONMENT` is ambient, inherited, and settable from outside the deployment, so a gate that reads it publishes on somebody else's mistake. Enumerating the surface of an authentication host is worth an affirmative act.

The safe state is therefore the one reached by doing nothing, and the failure mode of a typo is a host that will not start rather than a host that quietly published.

## What the document may say

The descriptions are metadata, and they inherit §17's rule: **they must not enumerate the causes of a rejection.** Sign-in returns one 401 for unknown user, wrong password, locked and inactive alike, and activation returns one 400 for every bad token. A document that listed those separately would be the oracle the handlers exist to deny, so the summaries describe the single outcome.

## Explicit non-decisions

This section deliberately does **not** decide, and code must not assume:

- **authentication on the documentation routes** — when enabled, `/openapi/v1.json` and `/scalar` are anonymous; the decision made here is whether they exist at all, not who may read them
- **build-time document generation** (`Microsoft.Extensions.ApiDescription.Server`) — nothing consumes a checked-in document yet
- **document linting in the build** — no CI pipeline exists to run it (`AGENTS.md` §3)
- **generated clients** from the document
- **API versioning**, or any meaning for the document name `v1` beyond the generator's default

---

# 19. Audit Schema Ownership and the Database Role Model

**Status:** Architectural decision, **implemented** by `docker/roles.sql`,
`src/Platform/Ligature.Platform.Persistence/Audit/` and
`src/Tools/Ligature.AuditSchema`. Implements Audit Command/Query Catalog
AUD-S01. No requirement ID: this is infrastructure, not a catalogue entry
(`AGENTS.md` §16).

Until this decision, every component connected to PostgreSQL as one superuser.
That is why it matters: a superuser bypasses privileges, ownership rules and —
via `session_replication_role` — triggers. Any audit protection built on top of
a superuser connection is decoration.

## The property being established

> **The application runtime and the ordinary deployment roles cannot rewrite or
> delete a committed audit record, and the role that owns the audit schema
> cannot be reached through the application's role graph.**

Stated deliberately narrowly. It is **not** "nobody can ever destroy audit
objects": a cluster superuser can always perform privileged DDL, and
PostgreSQL treats superusers as unrestricted by design. Pretending otherwise
would be a false control. What the boundary removes is every route available to
the roles the running system actually uses.

## The five roles

| Role | Used by | Holds |
| --- | --- | --- |
| `app_role` | The host application | `SELECT`, `INSERT` on the trail. No `UPDATE`, no `DELETE` |
| `migration_role` | EF migrations | Owns the ordinary schema. **Nothing at all** on `audit_record` or `audit_entity_ref` |
| `provisioning_role` | `Ligature.Provisioning` | Seeds retention v1 and **reads** the catalogue, the trail and the deployment ledger so it can verify a tenant before handing it over. Appends `TenantProvisioned` (`INSERT` only, script `004`); cannot write the catalogue |
| `audit_owner` | Nobody | Owns the `audit` schema and every object in it. `NOLOGIN`, no members, no password |
| `audit_anonymiser` | The future erasure worker | Column-level `UPDATE` on the AR20 set only. `NOLOGIN` until that worker exists |

`audit_owner` is the permanent owner of the Audit schema and its objects.
**The migration role must never become their owner.**

## Why the Audit schema is not an EF migration

Whoever runs `CREATE TABLE` owns the table, and an owner's authority is
implicit: it can disable triggers and drop what it owns regardless of any
`GRANT`, and it can grant itself back anything revoked. EF migrations run as
`migration_role`, so EF-created audit tables would be owned — and therefore
destroyable — by the migration credential.

`Ligature.AuditSchema` applies the Audit DDL instead, under a privileged
connection that issues `SET ROLE audit_owner` **before** creating anything, so
objects are born owned by a role nothing can authenticate as. Ownership is
never transferred: a transfer implies an interval during which something else
owned the trail, and the property above would be false for that interval.

The accepted cost is that these tables leave `__EFMigrationsHistory`. A
checksummed ledger, `audit.audit_schema_version`, replaces it, and an already
applied script whose content has changed fails the deployment rather than being
silently re-applied.

## Why a dedicated schema, not a naming prefix

`DROP TABLE` permits the table owner, **the schema owner**, or a superuser.
Since PostgreSQL 15 the `public` schema is owned by the database owner — so
with the audit tables in `public`, `migration_role` could drop tables it did
not own. This is not theoretical: probing during AUD-S01 destroyed two audit
tables that way. Object ownership alone was never sufficient.

A dedicated `audit` schema owned by `audit_owner` closes it. Treat that schema
as a genuine module boundary and reference its objects explicitly —
`audit.audit_record` — rather than compensating with `search_path`, which would
make the boundary invisible at the call site.

## Three independent mechanisms

1. **Privilege** — `app_role` holds `SELECT` and `INSERT`; no reachable role
   holds `UPDATE` or `DELETE`.
2. **Ownership** — the schema and its objects belong to `audit_owner`, which
   has no login and no members, so the powers no `GRANT` can remove have no
   reachable holder.
3. **Trigger** — every protection trigger is `ENABLE ALWAYS`, so it fires even
   under `session_replication_role = replica`. Without this a superuser reaches
   every row with one `SET`; with it, defeating the trail requires an explicit
   DDL statement.

Each is asserted by an adversarial test in `AuditTamperBoundaryTests`, which
attempts the attack and requires refusal. Configuration assertions were
deliberately not accepted as evidence.

## Two verifications, deliberately redundant

They answer different questions, so neither trusts the other.

**`Ligature.AuditSchema` verifies construction** — *did it build what it
claims?* After applying its scripts it checks ownership of the schema and every
object, that `audit_owner` cannot log in and has no members, that every
protection trigger is `ENABLE ALWAYS`, that the expected indexes and
constraints exist, and the full privilege matrix — including that the
anonymiser's `UPDATE` covers exactly the AR20 columns. Any failing fact stops
the deployment; all failing facts are reported together.

**Provisioning verifies handover** — *is this tenant safe to hand over?*
`AUD-C4` checks, before it writes anything, that every audit script is in the
ledger and that the application role cannot update or delete any audit table;
and after its own writes (`AUD-S11`) that the **deployed** catalogue matches the
release seed, that Anonymous
origin is declared for exactly the two types EO5 permits, that retention v1
exists, and that the trail is empty **and its sequence unconsumed** — a
rolled-back write consumes a value, and the first record must be Sequence 1.
Any failure refuses the tenant and rolls the whole provisioning back.

The handover set is small and tied to the acceptance criteria; the adversarial
suite is not duplicated into it. What the redundancy buys is that a tenant is
never handed over on the strength of an earlier step having exited 0.

## The catalogue is deployed, not migrated and not provisioned

`AuditEventCatalogue` is the single code-side source of the 49 V1 event types
and their origins, in the pattern of `GetPermissionSeeds()`; the Entity
Workbook's Event Catalogue sheet is the normative origin. `AuditCatalogueSeeder`
writes it with raw SQL — no `DbSet`, no entity, and deliberately the same
mechanism `AuditRecordWriter` uses to write into a schema it does not own.
`AuditCatalogueDriftTests` holds a deployed database to the seed. The retention
floor, `AuditReleaseBaseline.MinimumRetentionMonths`, is a placeholder pending
`AUD-O11` and is marked as one.

**The catalogue belongs to the DEPLOYMENT phase, not to provisioning.** It is
applied by `Ligature.AuditSchema` as `audit_owner`, immediately after the
schema scripts and before the host starts:

```text
db → roles → migrator → audit-schema (schema + catalogue) → host → bootstrap
```

It sits there because the compiled handlers depend on it. `AuditDeclarations`
is verified against the deployed catalogue at start-up and a release that
disagrees refuses to run (§11), which makes the catalogue a **precondition of
the host** rather than tenant data. Written by a bootstrap step that runs after
the host, it could never be present when the host needed it, and a fresh
installation could not start at all.

Two consequences follow, and both are deliberate:

- **It reconciles rather than inserts.** It runs on every deployment, so a
  catalogue holding the previous release's rows is the normal case. New
  entries are inserted, changed definitions updated, and entries the release no
  longer declares are marked **inactive**. Nothing is ever deleted: a committed
  `audit_record` references its event type, so a catalogue that could drop rows
  could orphan the trail.
- **No role that can log in may write it.** `provisioning_role` holds `SELECT`
  and no `INSERT` on the catalogue tables (script `004`). Release data is
  written by the release.

**Retention v1 stays with `AUD-C4`,** and that split is a constraint rather
than a preference: `RT5` (script `003`) makes `audit_retention_policy.created_by`
a foreign key to `public.app_user`, and the System actor it names is created by
`PRV-C1`. It cannot exist before the host.

## What the application does with a schema it does not own

`AuditRecordWriter` holds no `DbContext` state of its own. It takes the
`NpgsqlConnection` and `NpgsqlTransaction` off `DbContext.Database.CurrentTransaction`
— the transaction `TransactionScopeBehavior` opened — and inserts with raw SQL,
so the audit rows and the business rows commit together or not at all
(invariant 16). It refuses to run outside a transaction rather than opening one
of its own.

The catalogue is read once per process, not per command: `IAuditEventCatalogue`
is a lazy singleton over the deployed rows. A release that disagrees with those
rows never starts (§11), so the snapshot cannot drift beneath a running host.

Provisioning writes `TenantProvisioned` through the same writer, on its own
transaction, against the **deployed** catalogue it reads back — the same rows
the host verified its declarations against at start-up. It used to validate
against the in-memory release seed, because it was in the middle of writing the
catalogue and a database read would not have found the uncommitted rows. Now
that the catalogue is deployed beforehand, provisioning consumes it like every
other reader, and there is no second mechanism that could describe a different
release. That record is Sequence 1, and `AuditHandoverVerification` checks it
is before handing the tenant over.

## Rules

- **Never grant `audit_owner` membership to anything.** It is the whole of
  mechanism 2.
- **An actor recorded in the trail cannot be deleted.** AR10 and AR12 point at
  `app_user` and `user_role`, and nothing may delete an audit row. Code and
  tests must treat users as deactivated, never removed.
- **Never create audit objects from an EF migration**, and never `ALTER … OWNER`
  an audit object to a role that can log in.
- Any security or integrity `CHECK` whose correctness depends on the presence
  or absence of a value **must account for SQL NULL semantics**: a `CHECK`
  passes when its expression evaluates to NULL. Five constraints written
  straight from the workbook admitted rows they existed to refuse until they
  were rewritten with `IS NOT DISTINCT FROM`. Correct the implementation to
  match the frozen invariant; do not weaken the invariant to accommodate
  three-valued logic.
- Extensions are database infrastructure and are installed by the foundation
  step, not by migrations. The alternative — `GRANT CREATE ON DATABASE` to
  `migration_role` — also permits creating schemas.
- **Audit schema scripts are immutable once deployed.** The ledger records each
  script's checksum and a changed script fails the deployment. A correction
  ships as a new numbered script — `003` added the RT5 foreign key that `001`
  had omitted, and the provisioning read grants, exactly this way.

## Explicit non-decisions

This section deliberately does **not** decide, and code must not assume:

- **secret management** for role passwords beyond "configuration, no defaults"
- **CI/CD**, **PRV-C2**, **tenancy**, **backup/restore** or **rollback policy**
- **forward-only database evolution** — a deployment policy that still needs
  stating; EF migrations currently retain working `Down()` methods, so the
  repository presently implies reversibility that no policy has confirmed
