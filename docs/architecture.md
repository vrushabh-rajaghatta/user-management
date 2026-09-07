# Ligature Architecture

**Status:** Architectural contract

Ligature is the current codebase/project name. **RegOS** may be used when referring to the broader Regulatory Operating System product concept — in discussion only, never in code. Every project, assembly and namespace is `Ligature.*`.

This document defines the architectural direction and boundaries for the Ligature codebase. Build, test, migration and validation procedure lives in `AGENTS.md` §3 and is not repeated here.

---

# 1. Current Scope

All work to date is in the **Platform** module, specifically **User Management** (its ownership is defined in §5).

Nothing else described in this document exists yet — no Audit, Notifications or Logging implementation, no business domain, no host application, no tenancy. Where a later section describes one of those, it is describing target state.

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

**The host application does not exist yet, and current work does not need it.** The class libraries are built, tested and exercised without it — `AGENTS.md` §3 describes how. Do not create the host as a side effect of another story.

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

When Audit is implemented, modules will write audit information through an `IAuditWriter` contract; that contract does not exist yet. **Do not introduce an event bus solely for audit decoupling.** Reconsider an event-based mechanism when there is a demonstrated need, such as multiple independent consumers.

*Target state — not yet implemented.*

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

---

# 9. Database Ownership

**Target state:** database-per-tenant. Each tenant has its own database containing every module's tables. Tenant management is intended to live *outside* the tenant application/platform runtime; this document does not yet name or specify that component.

**Current state:** one database, one connection string, no tenant resolution, no tenant management. None of the target state above is implemented. Do not assume tenant plumbing exists; changing tenant/database isolation is an escalation item.

Regardless of tenancy, each module owns the database objects belonging to its domain. A module must not read or write another module's tables; use the owning module's application contract.

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
AuthenticationBehavior → HumanActorBehavior → AuthorizationBehavior → handler
```

**Authorization is the pipeline's responsibility, not the handler's.** A command declares its requirement through `IAuthorizableCommand` / `IHumanActorOnlyCommand`; the handler assumes it has already been enforced. Do not call handlers directly and do not re-check authorization inside a handler.

### Queries

`IQuery` / `IQueryHandler` exist in SharedKernel, but no query dispatcher, pipeline or handler has been built and no query pattern is established. Do not invent a query dispatcher or query pipeline inside another story; the first query needs its own approved story and architectural decision.

### Handler registration

Handlers are registered explicitly, one by one, in `Ligature.Platform.Application/DependencyInjection.cs`, with the requirement ID as a comment. Do not introduce assembly scanning. "Which commands are wired in" must be answerable by reading that method.

### Transaction boundary

`IUnitOfWork` owns the transaction. The handler wraps its whole operation in `IUnitOfWork.ExecuteInTransactionAsync(...)`; the unit of work saves, translates known constraint violations, and commits. One business operation, one transaction. Repositories never save or commit.

### Pre-checks and database constraints

**The application pre-check is an affordance; the database constraint is the authority.**

Repositories mirror named database constraints (AU3, UI7, …) with raw SQL that uses the same normalisation the constraint does (database-side `lower()`, not C# `ToLowerInvariant()`), so the pre-check and the constraint agree. `PostgresExceptionTranslator` maps a violation of a *known, named* constraint to the same exception type the pre-check throws, so a caller cannot tell which one rejected them. Two concurrent creates both pass the pre-check; only one passes the INSERT.

The translator is deliberately narrow: only constraints a command can actually reach are mapped; everything else is rethrown untouched. When a new command makes a constraint reachable, add it to the map — do not add a generic fallback.

Never present a pre-check as the integrity guarantee, and never remove a database constraint because an application check exists.

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
