# Ligature Requirement Catalogue

**Status:** In progress — being back-filled from committed code

This document is the authoritative index of Ligature requirements.

Every requirement/story must have a stable identifier, used consistently across story discussions, implementation branches, commit messages, tests, pull requests, and architecture decisions where applicable.

## Interim status

The catalogue does not yet define the requirements that existing code implements. Until it does:

- **IDs that already appear in committed code are provisionally valid.** Agents must not block on their absence here.
- **Do not invent new IDs.** A story that needs an ID neither in this file nor in the code goes back to the owner.
- Back-fill entries as they are touched; a story that modifies code citing an undefined ID should add that entry.

IDs currently referenced in code and not yet defined here:

```text
Commands:     AUT-C1 AUT-C2 AUT-C5 AUT-C7  CRD-C1 CRD-C2  IDN-C3  PRV-C1 PRV-C3
              SES-C1  USR-C1 USR-C4
Rules:        AU3 AU8 AU11  UI5 UI7 UI8  UT4 UT5 UT7  UR3 UR5 UR7 UR8 UR9 UR10 UR12
              RP2 RP6  SP1 SP2 SP3 SP4  G1 G4
```

## Identifier Format

Two families are in use.

**Command / capability IDs** — `<AREA>-C<n>`, e.g. `USR-C1`, `PRV-C3`. Areas observed so far: `PRV` (provisioning), `USR` (user), `CRD` (credential / token lifecycle), `AUT` (role assignment and authorization), `IDN` (identity), `SES` (session / authentication). Define an area here before using a new one.

`CRD` and `SES` are deliberately distinct: `CRD` owns credential and token lifecycle, `SES` owns sessions and authentication.

**Rule / constraint IDs** — `<TABLE><n>`, e.g. `AU3`, `UI7`. The prefix names the table the rule governs:

| Prefix | Table              |
| ------ | ------------------ |
| `AU`   | user               |
| `UI`   | user_identity      |
| `UT`   | user_token         |
| `UR`   | user_role          |
| `RP`   | role_permission    |
| `SP`   | security_policy    |
| `G`    | global / all rows  |

Rule IDs are what repositories, constraints and `PostgresExceptionTranslator` cite; command IDs are what handlers, branches and PRs cite.

## Rules

1. Do not invent requirement IDs.
2. Do not reuse an existing ID for a different requirement.
3. Do not silently change the meaning of an existing requirement.
4. If a requirement changes materially, record the change explicitly.
5. Keep requirements focused on behaviour and acceptance criteria.
6. Architectural implementation details belong in `docs/architecture.md`, unless they are themselves explicit requirements.

---

# Requirements

<!--
Populate this section with the approved requirement catalogue.

Suggested structure:

## PRV-C1 — <Requirement title>

**Status:** Approved

### Requirement

<Requirement description>

### Acceptance Criteria

- ...
- ...
- ...

### Notes

<Important constraints or decisions>
-->

---

# Known Gaps and Deliberate Deferrals

Things the code knowingly does not do yet. An agent that encounters one of these should **not** "fix" it inside an unrelated story and should **not** report it as a defect — cite this section instead. Remove an entry when the deferral is closed.

## UT4 — no partial unique index on open user tokens

**Rule:** at most one open token per `(UserIdentityId, TokenType)`.
**State:** the schema has no partial unique index for this, so `UserTokenRepository` has no `Exists` pre-check for it and nothing to mirror.
**Deferred to:** CRD-C2, alongside UT5 (invalidate prior tokens on issue), so the invariant and the command that depends on it land together.
**Where recorded:** `UserTokenRepository` class doc.

## PostgresExceptionTranslator — role-overlap and live-grant constraints not mapped

**State:** the role-overlap exclusion constraints (raise `23P01`) and RP2's live-grant index are not in the translator's map because no command can reach them yet.
**Deferred to:** AUT-C1 and AUT-C7, where their wording can be written against a real trigger.
**Where recorded:** `PostgresExceptionTranslator.KnownViolations` doc.

## No provisioning entry point

**State:** schema is applied by `dotnet ef database update`, but the seed data (`PlatformProvisioner.ProvisionAsync`: system roles, permissions, initial security policy, bootstrap administrator) has no CLI, host or test that runs it. A fresh database is provisioned out of band.
**Consequence:** persistence tests need a database that was provisioned by hand; `CatalogueDriftTests` fails with an explicit message on an unprovisioned one.
**Deferred to:** the first host application.

## Persistence tests skip silently when PostgreSQL is unreachable

**State:** the connection helpers return `null` and test bodies return early, so xUnit reports the tests as passed. See `AGENTS.md` §3 for how to validate a run.
**Intended fix:** make the skip explicit (throw or `Assert.Skip`) so a run without a database cannot report green. Not yet scheduled.

## Access token issuance is unspecified — §17 escalation

**State:** SES-C1 creates the authoritative `user_session` row and returns its
`SessionId`. It does **not** issue an access token, because the frozen model
does not say what one is.

The source material gives three properties only — short-lived, carries the
`SessionId`, and "session state is authoritative; the token is a carrier"
(catalogue SES-C1 step 8; specification section 11). It does not settle
JWT versus opaque token, signing algorithm, key management, claims, issuer or
audience, token lifetime, transport, validation mechanism, or which component
owns issuance.

Its only consumer — pipeline behaviour 1, "resolves the caller from the access
token" — does not exist either, and neither does a host application to issue a
token to.

**Consequence:** choosing a token scheme would change the security and
authorization architecture, which `AGENTS.md` section 17 reserves to the owner.
Do not pick one inside a feature story.

**Deferred to:** its own design decision, most naturally alongside the first
host application.
**Where recorded:** `SignInCommandHandler` class doc.

## Database-per-tenant not implemented

**State:** one database, one connection string, no tenant resolution. See `docs/architecture.md` §9.
**Deferred to:** unscheduled; changing tenant isolation is an escalation item.

## Build works on macOS only by accident

**State:** the project file is `Ligature.Sharedkernel.csproj` (lower-case `k`) while `Ligature.Platform.Domain.csproj` and `Ligature.Platform.Application.csproj` reference `Ligature.SharedKernel.csproj`. Case-insensitive filesystems resolve it; Linux will not.
**Intended fix:** rename the file to match the references. Trivial, but touches the solution file; do it as its own commit.

---

# Traceability

```text
Requirement ID → Story → Implementation plan → Branch → Commit(s) → Pull Request → Owner approval → Merge
```
