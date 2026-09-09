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
              SES-C1 SES-C2  USR-C1 USR-C4
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

## Provisioning entry point — RESOLVED

**State:** resolved. `src/Tools/Ligature.Provisioning` runs PRV-C1 and PRV-C3
against a migrated database; `AGENTS.md` §3 documents it. Seeds only, refuses
when migrations are pending, idempotent.

**What this also fixed:** `ProvisionAsync` previously had **no call sites
anywhere** — not in `src`, not in `tests`. `CatalogueDriftTests` compared the
static seed lists against a hand-provisioned database, which proved the lists
matched that database but never that this code could produce it. The
first-provision paths of PRV-C1 and PRV-C3 now execute under test against
throwaway databases.

**The token-delivery invariant.** PRV-C3's plaintext activation token exists
once and is never persisted, and its sentinel means a second administrator can
never be issued. So delivery is a **required** callback invoked after
`SaveChanges` and **before** `CommitAsync`: committed implies the token was
already durable. A failed delivery rolls the whole thing back and leaves the
tenant provisionable. Reverse that ordering and two tests fail.

**Now resolved:** the tool runs as `provisioning_role`, not as the same
superuser as everything else (`docs/architecture.md` §19). PE2's remaining
obligation — `permission` writable only by a migration role — is tracked in the
enforcement-layer entry below.

## Access token issuance — §17 escalation, RESOLVED

**State:** resolved and implemented. `docs/architecture.md` §17 freezes the
contract; `src/Host/Ligature.Host` issues and verifies the carrier and
`CallerEstablisher` owns session validity and caller establishment.

Kept here rather than deleted because the *reasoning* is the audit trail: the
frozen model gave three properties only — short-lived, carries the `SessionId`,
"session state is authoritative; the token is a carrier" — and settling the rest
was an owner decision under `AGENTS.md` §17, not a feature story's to make.

**Still open, deliberately:** browser token storage, cookie transport, any
standard token container, self-contained authorization claims, a token column on
`user_session`, and asymmetric signing. §17 lists these as explicit
non-decisions; reopening one is an architectural change, not an implementation
detail.


## Audit emission is not implemented

**State:** the audit tables and their tamper boundary now exist
(`docs/architecture.md` §19), but nothing writes to them: there is no emission
pipeline and no ActorSnapshot type. Every command that should emit audit events
carries an explicit TODO instead.

**There is deliberately no `IAuditWriter`.** An earlier version of this entry
and of `docs/architecture.md` §6 described one as the intended contract; the
frozen Audit model names a direct write API as an anti-feature, and the
architecture text was corrected rather than the model. Handlers declare events;
the pipeline writes them. Do not introduce a public writer abstraction.

The catalogue requires audit events to be written INSIDE the command's transaction — "if the business write committed, the audit write committed" (inv. 16). Handler-owned transactions (`docs/architecture.md` §11) already make that possible without restructuring.

**Also missing, and larger than it looks:** a full ActorSnapshot needs `Username`, `IdentityProvider`, `SubjectId` and `AuthorizingRole`, none of which `IExecutionContext` carries. `AuthorizationService` returns `bool` and discards which assignment authorised the action.

**Deferred to:** the Audit capability (`docs/architecture.md` §6).
**Where recorded:** TODOs in `CreateUserCommandHandler`, `ActivateAccountCommandHandler`, `SignInCommandHandler`, `SignOutCommandHandler`.

## Authorization failures are not distinguishable from validation failures

**State:** `AuthorizationBehavior` raises `BusinessRuleViolationException` when a
caller lacks the required permission — the same type a duplicate email raises.
`ProblemMiddleware` therefore maps both to **400**, and the host cannot separate
them without matching on the exception's message, which it must not do.

**What 400 does and does not achieve.** It does not hide the distinction: the
message reads "does not have permission", so a human or a client reading the
body can tell the two apart. What it removes is the ability to branch on the
distinction by STATUS — which pushes any client that needs to into exactly the
string-matching the host is forbidden from doing. Asserted in
`CreateUserEndpointTests.The_two_kinds_of_400_differ_only_in_their_message`.

**Why it was left alone.** 403 would be the conventional status, but reaching it
cleanly needs a fourth exception type, and "exactly three, no hierarchy" is a
frozen decision. Reopening the exception taxonomy for one status code is a
larger decision than the endpoint that exposed the problem, and expanding it
silently inside a feature story would be worse than leaving the limitation
visible.

**Deferred to:** an architecture decision round on error classification, most
naturally alongside Audit — which will want the same distinction for a different
reason, since an authorization failure is a security event and a mistyped email
is not.
**Where recorded:** `UserEndpoints` class doc.

## Notifications are not implemented

**State:** USR-C1 generates an activation token whose plaintext has no consumer. It is never persisted, returned or logged, so today the token simply cannot be delivered.

**The trap for whoever builds delivery:** a queued row carrying an activation link necessarily carries the plaintext token — the exact disclosure that storing only a hash exists to prevent (UT7). Retention and encryption of that queue need deciding, not assuming.

**Deferred to:** the Notifications capability, which owns delivery — User Management does not own email infrastructure (`docs/architecture.md` §8).
**Where recorded:** TODO in `CreateUserCommandHandler`.

## PRV-C2 — a provisioned tenant never receives new permissions

**Rule:** every release that adds permissions runs `SeedPermissionCatalog`, under the migration role (PE2).

**State:** not implemented. `PlatformProvisioner.ProvisionAsync` seeds the catalogue once and then no-ops forever, so adding a permission to `GetPermissionSeeds()` changes nothing for any existing tenant database — silently.

`CatalogueDriftTests` detects the divergence; nothing fixes it, and the only current remedy is hand-written SQL.

**No longer blocked, still not implemented.** Both blockers are now gone: the
provisioning entry point was resolved above, and role separation now exists
(`docs/architecture.md` §19). PE2's own premise — `permission` writable only by
a migration role — is not yet met (see the enforcement-layer entry below), but
that does not prevent PRV-C2 being written.

**Deferred to:** its own story. Deliberately kept out of AUD-S01, which
established the deployment's security boundary and nothing else.

## Enforcement layers G1, G4, PE2, PH3 and SP2 are not implemented

**State:** no `GRANT`/`REVOKE` statements and no triggers exist in any migration — verified. So none of these hold at the database level:

- **G1** no hard deletes (application role granted SELECT/INSERT/UPDATE only, with `user_session` the sole purge exception)
- **G4** BEFORE UPDATE triggers rejecting writes to immutable and write-once columns
- **PE2** `permission` readable but not writable by the application role
- **PH3** `password_history` insert-only
- **SP2** `security_policy` append-only

The domain enforces the equivalent rules in code, so behaviour is correct today; what is missing is the database-level backstop the frozen model specifies, which is what makes these structural rather than a matter of developer discipline.

**Partially lifted.** A database role model now exists (`docs/architecture.md`
§19), and `AddUserManagementPrivilegeModel` grants the ordinary tables to
`app_role` and `provisioning_role` in G1's shape — `SELECT`, `INSERT`, `UPDATE`,
and no `DELETE` for anyone. That closes G1's **grant** half only.

**Still outstanding:**

- **G1** — `user_session` is named as the eventual purge exception; no purge
  exists, so no `DELETE` is granted for it either. Revisit when one is built.
- **G4** — no BEFORE UPDATE triggers on immutable/write-once columns of the
  User Management tables. The audit tables have their equivalents; these do not.
- **PE2** — `permission` is granted `INSERT`/`UPDATE` to `provisioning_role`
  rather than being writable only by a migration role.
- **PH3** — `password_history` is not insert-only at the database.
- **SP2** — `security_policy` is not append-only at the database.

**Deferred to:** unscheduled. The blocker was the missing role model; that is
no longer the obstacle, so these are now schedulable work rather than blocked
work.

## CR1 — no unique constraint on credential.user_identity_id

**Rule:** one credential per identity, 1:0..1.

**State:** the index on `(user_identity_id, identity_type)` is NOT unique, so two credential rows for one identity are possible. The composite FK pins `identity_type` to the identity but does not constrain cardinality.

Nothing produces a second row today — CRD-C1 only ever inserts after consuming a single-use token — so this is a missing backstop rather than a live defect.

**Deferred to:** unscheduled.

## Integration-test fixtures can outlive a failed dispatch

**State:** the persistence integration tests assign their fixture handle outside the `try`, so when a dispatch throws, the `finally` cleanup never runs and rows are left in the shared database. Two such rows were found and removed during CRD-C1 validation.

Harmless to correctness — every test scopes by its own ids — but it accumulates.

**Intended fix:** establish the cleanup scope BEFORE any operation that can throw, rather than wrapping more code in another `try`/`finally`.
**Deferred to:** unscheduled.

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
