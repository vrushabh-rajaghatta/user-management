# Ligature — Coding Agent Instructions

## 1. Purpose

Ligature is being developed as a modular regulatory application.

This file defines how coding agents must work in this repository.

The architecture contract is maintained in `docs/architecture.md`.
The requirement catalogue is maintained in `docs/requirements.md`.

Read the relevant sections of both before implementing a story.

**Current scope:** all work to date is in the Platform module, and within it
User Management (ownership defined in `docs/architecture.md` §5). No business
domain (Regulatory, Clinical) has been started. Do not assume infrastructure
beyond what §3 describes exists.

---

## 2. Owner-Driven Development

The coding agent is an implementation partner, not the owner of product or architectural decisions.

For every story or significant change, follow this sequence:

```text
Analyse → Explain → Clarify → Plan → Owner approval
→ Create branch → Implement → Test / Validate → Commit → Pull Request
→ Owner approval → Merge
```

Do not skip these stages.

### Important

Do not start coding immediately after receiving a story.

First understand the requirement and determine whether sufficient information exists to implement it correctly.

If an important requirement is ambiguous or missing, ask the owner.

Do not invent requirements simply to avoid asking questions.

### Trivial changes are exempt

The full sequence applies to stories and to any change that touches behaviour,
data, schema, security, or architecture. It does **not** apply to genuinely
trivial changes **the owner has asked for**: a typo in a comment or document, a
documentation-only correction, a formatting fix, a rename that no other code
observes. Make those directly, describe what you did, and keep them out of story
branches unless they are part of the story.

The exemption is about ceremony, not scope. It does **not** authorise
opportunistic clean-up of known unrelated issues while doing something else.
In particular, the `Behavious` → `Behaviors` and `Createuser` → `CreateUser`
folder names are known items: address them only when the owner requests it or
they are explicitly within an approved story's scope.

The `Ligature.Sharedkernel.csproj` casing was the third such item and is now
**resolved**: the file is `Ligature.SharedKernel.csproj`, matching both its
directory and the two `ProjectReference` paths that always spelled it that way.
It was fixed because it blocked the Docker build — macOS hides the mismatch, a
case-sensitive Linux filesystem does not — not as opportunistic clean-up.

If in doubt whether a change is trivial, it is not.

---

## 3. Working in this Repository

.NET 10 (`net10.0`, SDK 10.0.400), nullable and implicit usings enabled,
EF Core 10 + Npgsql on PostgreSQL, xUnit. The solution file is `Ligature.slnx`
— the XML solution format, not `.sln`. The host application is
`src/Host/Ligature.Host`; everything else is class libraries and test projects.

```bash
dotnet build Ligature.slnx
dotnet test  Ligature.slnx
```

The Platform layers have corresponding test projects; SharedKernel currently has
no separate test project (a source project is not required to have one):

| Project | Kind |
| --- | --- |
| `tests/Platform/Ligature.Platform.Domain.Tests` | unit |
| `tests/Platform/Ligature.Platform.Application.Tests` | unit |
| `tests/Platform/Ligature.Platform.Persistence.Tests` | integration against PostgreSQL (except `UserTokenServiceTests`, which is pure) |
| `tests/Host/Ligature.Host.Tests` | HTTP end-to-end against PostgreSQL (except `AccessCarrierTests` and `SigningKeyRingTests`, which are pure) |
| `tests/Tools/Ligature.Provisioning.Tests` | CLI end-to-end against throwaway PostgreSQL databases (except `ProvisioningOptionsTests`, which is pure) |

### Database connection

Persistence tests read `LIGATURE_CONNECTION` and fall back to:

```text
Host=localhost;Port=5432;Database=ligature;Username=postgres;Password=postgres
```

**The EF design-time factory reads `LIGATURE_CONNECTION` and has NO fallback.**
It used to hard-code the string above, which meant `dotnet ef database update`
silently applied migrations to whichever database the default named, no matter
what the environment said. An unset variable now stops the command and names
the fix. Set it before any `dotnet ef` invocation, including
`migrations add`.

### Database roles

There is no longer one connection for everything. Each component authenticates
as the role its job needs, because the Audit tamper boundary is a property of
the application **not** being a superuser — a superuser bypasses privileges,
ownership and triggers alike (`docs/architecture.md` §19).

| Role | Used by |
| --- | --- |
| `app_role` | The host application |
| `migration_role` | EF migrations |
| `provisioning_role` | `Ligature.Provisioning` |
| `audit_owner` | Nobody — owns the `audit` schema, `NOLOGIN`, no members |
| `audit_anonymiser` | The future erasure worker; `NOLOGIN` today |

`docker/roles.sql` creates the first three; `Ligature.AuditSchema` creates the
last two and refuses, naming them, if the first three are missing. `./up.sh`
generates the passwords into `.env`. **There are no defaults and no committed
development passwords**, for the reason `docs/architecture.md` §17 gives about
the signing key.

**The test suites do not need any of this.** Every fixture that migrates
establishes the roles itself, exactly as an installation runs the roles step
before the migrator, so `dotnet test` works against a clean cluster.

**Audited suites leave permanent rows behind.** USR-C1 is audited, and audit
rows can be deleted by nobody, so the integration and host suites append records
to the shared database on every run and their callers — seeded once, with fixed
identifiers — can never be removed. That is the trail behaving correctly, not
leakage to clean up; a database that must be pristine has to be recreated.

### Running the host

The host reads its configuration from the environment and **has no defaults**.
It refuses to start without a connection string, and refuses to start without a
signing key — see `docs/architecture.md` §17 for why a generated or default key
is not an option.

It also refuses to start when the audit event catalogue in the database does not
contain, and keep active, every event its handlers declare. The message names
each mismatch. The fix is to deploy the release the database was seeded for, or
to seed the database for this release — never to remove the declaration.

```bash
export LIGATURE_CONNECTION="Host=localhost;Port=5432;Database=ligature;Username=postgres;Password=postgres"
export LIGATURE_SIGNING_KEY_CURRENT=v1
export LIGATURE_SIGNING_KEY_V1=$(openssl rand -base64 32)
dotnet run --project src/Host/Ligature.Host
```

`LIGATURE_SIGNING_KEY_<ID>` configures the key named `<id>`; the accepted set is
whatever is configured, and `LIGATURE_SIGNING_KEY_CURRENT` names the one that
signs. There is no `appsettings.json` carrying secrets, deliberately.

The host suite supplies its own configuration, so none of this is needed to run
`dotnet test`.

### Provisioning a database

Schema first, then seed data. They are separate steps on purpose: separately
auditable, and eventually separately privileged (PE2).

Schema now comes in two steps, because the Audit tables cannot be an EF
migration: whoever runs `CREATE TABLE` owns the table, and an owner can drop
what it owns and disable its triggers regardless of any `GRANT`
(`docs/architecture.md` §19). `Ligature.AuditSchema` applies them under a
privileged connection that becomes `audit_owner` first. It runs **after** the
migrator, because the Audit foreign keys reference `app_user`, `role` and
`user_role`.

```bash
export LIGATURE_CONNECTION="Host=localhost;Port=5432;Database=ligature;Username=migration_role;Password=..."
dotnet ef database update --project src/Platform/Ligature.Platform.Persistence

export LIGATURE_PRIVILEGED_CONNECTION="Host=localhost;Port=5432;Database=ligature;Username=postgres;Password=postgres"
dotnet run --project src/Tools/Ligature.AuditSchema

export LIGATURE_CONNECTION="Host=localhost;Port=5432;Database=ligature;Username=provisioning_role;Password=..."
dotnet run --project src/Tools/Ligature.Provisioning -- \
    --first-name Ada --last-name Lovelace --display-name "Ada Lovelace" \
    --email ada@example.test --username ada.lovelace \
    --activation-token-out ./bootstrap.token
```

The tool seeds only. It refuses to run when migrations are pending, and it
refuses to run when the Audit schema is not deployed — each with the command
that fixes it. It is idempotent: a second run reports that provisioning is
already complete, changes nothing, and does not touch the token file.

Provisioning also runs `AUD-C4`: it verifies the Audit boundary it is about to
seed against (and refuses the tenant, rolling everything back, if the
application role could write the trail), then seeds the 49-row event catalogue
and retention policy v1. It emits no audit record — the trail is left empty
with its sequence unconsumed, so the tenant's first record can be Sequence 1.

**A database provisioned before `AUD-C4` existed never receives the
catalogue.** PRV-C1 runs once per database, guarded by the System actor, and
does not seed retroactively — by decision, because doing so would be `AUD-C3`'s
job without `AUD-C3`'s audit event. Recreate such a database and provision it
again; `AuditCatalogueDriftTests` says exactly this when it finds one.

`--activation-token-out` receives the bootstrap administrator's one-time
activation token, written owner-only. **The token is never printed**, and it
cannot be recovered if the file is lost — only its hash is stored, and the
sentinel prevents a second administrator being issued. Deliver it, then delete
the file.

Provisioning is NOT part of the host application, and must not become part of
it (`docs/architecture.md` §4).

### Running in Docker

A clean clone to a running system, in one command:

```bash
./up.sh
```

That starts PostgreSQL, creates the database roles, applies migrations,
deploys the Audit schema and starts the host, in that order, each step waiting
for the previous one rather than sleeping. The host is on
`http://localhost:8080`, the API reference on `/scalar`, and the container
database is published on **55432** so it cannot collide with a PostgreSQL
running natively on 5432 — which is the one the test suites use.

`up.sh` exists for one reason: it generates secrets into `.env` on first run —
the signing key and the three database role passwords. `docs/architecture.md` §17 forbids a default key, a committed development
key and a key regenerated per restart, so the key cannot live in `compose.yaml`
and the host cannot mint one. Once `.env` has a key, plain `docker compose up`
works identically.

Seeding is a separate command, as it is outside Docker:

```bash
./bootstrap.sh --first-name Ada --last-name Lovelace \
    --display-name "Ada Lovelace" --email ada@example.test \
    --username ada.lovelace
```

The activation token is written to `.secrets/bootstrap.token`, which is
gitignored. Re-running reports that provisioning is already complete and leaves
any existing token alone.

`docker/Dockerfile` builds **four** images from one source tree: `host`,
`migrator`, `audit-schema` and `provisioner`. They are separate because §4 and this section keep
schema and seed data out of the host — a host image that migrated on startup
would collapse a distinction the architecture depends on. The migrator runs an
EF migration bundle, so no runtime image carries the SDK.

**`dotnet test` does not use any of this.** The suites run against the developer's
own PostgreSQL as described below, not against the container.

### Persistence tests and PostgreSQL availability

**Expected behaviour:** persistence/integration test infrastructure must make
database unavailability visible — either fail clearly when PostgreSQL is
required but unreachable, or explicitly skip the test with a visible reason. It
must never silently convert database unavailability into a passing test.

**Current gap (known; recorded in `docs/requirements.md` Known Gaps):** the
persistence test classes whose connection helpers catch
`NpgsqlException`/`SocketException` currently return `null` and let the test
body return early, so **xUnit reports them as passed having asserted nothing.**
Until that is fixed, the command exiting 0 is not evidence, and the procedure
below is the workaround.

Validate like this, in order:

1. **Confirm PostgreSQL is reachable first**, at the host/port in the
   connection string. On macOS: `nc -z localhost 5432` (exit 0 = reachable).
   If this fails, stop: the persistence suite cannot validate anything.
2. **Confirm the schema and seed data are present.** Schema comes from
   `dotnet ef database update` (below); seed data comes from
   `src/Tools/Ligature.Provisioning` (see "Provisioning a database"). A
   reachable but unprovisioned database fails `CatalogueDriftTests` with an
   explicit message; that is the one loud signal the suite gives.
3. Run `dotnet test tests/Platform/Ligature.Platform.Persistence.Tests`.
4. In the report, say which of the above held. "Persistence tests passed" is
   only a true statement if step 1 succeeded. Otherwise write
   "persistence tests not validated — PostgreSQL unreachable."

### EF Core migrations

`LigatureDbContextFactory` (in `Ligature.Platform.Persistence/Database`) is the
`IDesignTimeDbContextFactory`, so `dotnet ef` works with `--project` alone.
There is no `--startup-project`; do not go looking for one.

```bash
dotnet ef migrations add <Name> --project src/Platform/Ligature.Platform.Persistence
dotnet ef database update        --project src/Platform/Ligature.Platform.Persistence
```

Do not introduce a separate design-time configuration mechanism.

### Repository hygiene

There is no CI pipeline, no `global.json`, no `.editorconfig` and no
`Directory.Build.props`. A clean build emits `CS8618`/`CS8620` warnings on
EF-materialised aggregates and nullable ID converters; do not add new
categories of warning.

---

## 4. Story Analysis

Before implementation, identify:

- The business intent.
- Expected behaviour.
- Business rules.
- Affected module(s).
- Existing code and patterns that are relevant.
- Dependencies on other modules.
- Domain changes.
- Application changes.
- Persistence/database changes.
- API/presentation changes, where applicable.
- Testing requirements.
- Migration requirements.
- Architectural implications.

Also identify what the story explicitly does **not** require when that prevents scope creep.

If the story conflicts with `docs/architecture.md`, surface the conflict before coding.

---

## 5. Explain the Story

Before proposing implementation, provide the owner with a concise summary of:

- What the story means.
- What will change.
- What will not change.
- Important business rules.
- Relevant architectural considerations.
- Assumptions, if any.

The owner should be able to confirm that the agent has understood the story correctly.

---

## 6. Clarification

Ask questions whenever the information required for a correct implementation is missing.

When multiple reasonable interpretations exist:

1. State the ambiguity.
2. Present the relevant alternatives.
3. Explain the consequences.
4. Recommend an option when appropriate.
5. Wait for the owner's decision.

Do not silently choose an interpretation that materially affects behaviour, architecture, data, security, or compliance.

---

## 7. Implementation Plan

Before coding, provide a concrete implementation plan.

The plan should identify, where applicable:

1. Projects/modules affected.
2. Files expected to change.
3. Domain changes.
4. Application/handler changes.
5. Persistence changes.
6. Database migrations.
7. API/presentation changes.
8. Tests to add or modify.
9. Validation/build/test steps.

The plan must follow the architecture and established repository patterns.

---

## 8. Owner Approval Before Coding

Do not begin implementation until the owner explicitly approves the proposed plan.

If the owner changes the requirements, update the analysis and plan before implementation.

If implementation reveals that the approved plan is materially incorrect or insufficient, stop and explain the issue before making a significant architectural change.

The agent may recommend architectural changes, but must not silently make them.

---

## 9. Git Branches

Every new story must be implemented on a dedicated branch.

Naming:

```text
feature/<story-id>-<short-description>
fix/<story-id>-<short-description>
```

Work that has no requirement ID drops the ID segment — see §12.

**This is a new convention.** Existing branches (`PRV-C1`, `usr-c1`,
`command-dispatch`, `authorization-service`, …) predate it and are not a
description of what to do. Do not rename them.

Do not mix unrelated work into a story branch.

Do not create a new branch for every tiny follow-up commit on the same story.

---

## 10. Implementation

After approval:

- Follow the approved plan.
- Follow `docs/architecture.md`, including its **Established Patterns** section.
- Keep the change focused on the story.
- Do not perform unrelated refactoring.
- Do not introduce new architectural patterns without discussion.
- Do not duplicate functionality already provided by another module.
- Preserve module boundaries.
- Add or update tests as part of the implementation.

**Check deliberate deferrals before "fixing" something.** When you encounter
what looks like a missing constraint, index, validation rule or capability,
first check the *Known Gaps and Deliberate Deferrals* section of
`docs/requirements.md`. Some omissions are intentional and scheduled — the
missing `user_token` unique index (UT4) is deferred to CRD-C2 — and implementing
one inside an unrelated story is a defect, not a favour. If it is listed, leave
it and cite the entry; if it is not listed and looks wrong, raise it with the
owner rather than fixing it silently. This is a safeguard, not a prerequisite:
current User Management work does not wait on the catalogue.

Prefer the simplest implementation that satisfies the requirement and preserves the architecture.

---

## 11. Validation

Before declaring a story complete:

- Build the solution.
- Run the unit test projects.
- Run the persistence test project **and validate it as §3 describes**.
- Verify migrations when applicable.
- Review the final Git diff.
- Check for accidental or unrelated changes.

A green build alone is not sufficient evidence of correctness. Report what was
actually verified, and name anything that was not.

---

## 12. Commits

Use focused, meaningful commits.

Commit messages explain the actual change and lead with the story/requirement ID:

```text
<story-id>: <description>
```

For example:

```text
PRV-C2: enforce non-overlapping user role assignments
```

Avoid: `fix`, `changes`, `stuff`, `updates`, `wip`.

Do not create meaningless commits solely to record progress.

**This is a new convention.** Existing history uses plain imperative subjects
without an ID prefix.

### Work with no requirement ID

Not every change implements a requirement. Documentation, governance, and
architectural or infrastructure work that implements a decision in
`docs/architecture.md` rather than a catalogue entry has no story ID, and one
must not be invented for it (§16).

Such commits lead with a meaningful category prefix instead:

```text
docs: <description>
Host: <description>
```

The rule is that a subject leads with a meaningful identifier — a story ID
where one exists, a category where none does. This is not a licence for `fix`,
`changes` or `wip`.

Branches for such work drop the ID segment: `feature/host-application`.

Cite the driving decision in the body and in the PR — for example,
"Implements `docs/architecture.md` §17" — so the work stays traceable to
something frozen, which is what the ID would otherwise have provided.

**Do not add AI co-author attribution** (`Co-Authored-By` trailers or similar)
to commits in this repository. The owner reviews and approves every merge; the
Git author is the accountable party.

---

## 13. Pull Requests

Create a pull request for each story.

The PR description should explain:

- What was implemented.
- Why it was implemented this way.
- Important design decisions.
- Database/migration changes.
- Tests performed.
- Validation results — including whether the persistence suite ran against PostgreSQL.
- Known limitations.
- Follow-up work, if any.

Reference the applicable requirement/story IDs.

---

## 14. Owner Review and Merge

The owner must explicitly approve the PR before merging.

The coding agent must not merge its own work without explicit owner authorization.

When review feedback is received:

1. Understand the feedback.
2. Ask for clarification if necessary.
3. Make the requested changes.
4. Re-run relevant validation.
5. Update the PR.

Do not dismiss review feedback silently.

---

## 15. Architecture

Architecture rules — module boundaries, the module-to-project mapping,
dependency direction, database ownership, and the established coding
patterns — live in **`docs/architecture.md`** and are not restated here.
**On architectural questions, `docs/architecture.md` is authoritative.** On
operational workflow — how to analyse, plan, branch, validate, commit and
review — this file is authoritative.

The short version: Ligature is a modular monolith. A conceptual module does not
automatically get its own .NET project. Follow the patterns already in the
code before introducing new ones.

---

## 16. Requirement Traceability

Requirements are identified by stable IDs (see `docs/requirements.md` for the
families in use).

When working on a requirement:

- Reference its ID in the branch name where practical.
- Reference it in commit messages.
- Reference it in the PR.
- Cite it in the XML doc of code that implements it, as existing code does.

**The catalogue is being back-filled.** IDs that already appear in committed
code are provisionally valid even though `docs/requirements.md` does not yet
define them. Do not block on their absence, and do not invent new IDs: if a
story needs an ID that is neither in the catalogue nor in the code, ask the
owner.

---

## 17. Architectural Escalation

Stop and ask the owner before making a change that:

- Creates a new architectural dependency.
- Changes module ownership.
- Introduces a new cross-cutting infrastructure pattern.
- Introduces an event bus.
- Introduces microservices.
- Changes tenant/database isolation.
- Changes security or authorization architecture.
- Changes established persistence patterns.
- Requires substantial restructuring of existing modules.

The agent should recommend solutions, not make these decisions unilaterally.

---

## 18. General Principle

Prefer:

> Simple implementation + strong boundaries + explicit decisions.

Avoid:

> Premature abstraction + unnecessary infrastructure + architectural decisions hidden inside feature work.

When uncertain, make the uncertainty visible to the owner.
