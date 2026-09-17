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

Three families are in use.

**Command / capability IDs** — `<AREA>-C<n>`, e.g. `USR-C1`, `PRV-C3`. Areas observed so far: `PRV` (provisioning), `USR` (user), `CRD` (credential / token lifecycle), `AUT` (role assignment and authorization), `IDN` (identity), `SES` (session / authentication). Define an area here before using a new one.

`CRD` and `SES` are deliberately distinct: `CRD` owns credential and token lifecycle, `SES` owns sessions and authentication.

**Query IDs** — `<AREA>-Q<n>`, e.g. `USR-Q1`. The same areas as commands; `Q` means query and `C` means command, so a read is not forced into the command catalogue. Numbered independently of the area's commands: `USR-Q1` and `USR-C1` are unrelated.

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

## PRV-C2 — Catalogue synchronisation

**Status:** Approved and implemented. Contract frozen 2026-09-16 by owner decision; implementation completed 2026-09-17.

### Requirement

The permission catalogue, the role catalogue and the role-permission grants a release defines must reach **every existing database**, not only databases provisioned after the change.

Today they do not. `PlatformProvisioner.ProvisionAsync` returns as soon as the System actor exists — before it reaches any permission, role or grant seeding — so the whole catalogue sits behind a first-provision-only gate. A permission added to `GetPermissionSeeds()` changes nothing for any existing tenant, silently. `CatalogueDriftTests` detects the divergence; nothing repairs it, and the only remedy is hand-written SQL.

PRV-C2 resolves this by **removing catalogue evolution from the first-provision lifecycle**, not by making `ProvisionAsync` progressively more complicated. `ProvisionAsync` remains first-provision logic and does not become the synchronisation mechanism.

### The governing principle

> **Catalogue synchronisation is monotonic with respect to authorization.** It may introduce new catalogue entries and new grants, and it may reconcile metadata that carries no authorization semantics. It must never silently remove, reactivate, or weaken existing authorization state.

**Monotonic does not mean "always succeeds".** Synchronisation refuses rather than resolving anything it is not entitled to change.

> **Authorization expansion is permitted only where explicitly represented by an additive catalogue entry; authorization reduction is never performed by synchronisation.**

Stated that way rather than as "never expands", which would be false: inserting a grant the catalogue introduces *does* expand what holders of that role can do, and that is the point of the requirement. What is forbidden is expansion that no additive catalogue entry represents — re-granting a revoked grant, reactivating a deactivated permission — and reduction of any kind.

```text
Seed:      permission A, permission B
Database:  permission A, permission C

NOT  ->  A, B
BUT  ->  REFUSED: C exists in the database but not in the catalogue
```

```text
Seed:      RequiresHumanActor = true
Database:  RequiresHumanActor = false

REFUSED, not repaired.
```

```text
Seed:      grant X -> role Y
Database:  grant X -> role Y, revoked

REFUSED, not re-granted.
```

### Direction is the whole design

The two directions are never symmetric, and no rule may be written that treats them as one.

| Direction | Meaning | Outcome |
| --- | --- | --- |
| **Seed → Database** — in the catalogue, absent from the database | This release introduces it | **Additive reconciliation** |
| **Database → Seed** — in the database, absent from the catalogue | The database holds authorization state the catalogue does not account for | **Drift, and a refusal** |

Reading the second as "the catalogue is authoritative, so remove it" is the single mistake this requirement exists to prevent. The catalogue is authoritative about what a release *introduces*; it is not authoritative about what may be *taken away*.

### Permitted mutations

Exhaustive. Anything not listed here is forbidden.

| # | Mutation |
| --- | --- |
| M1 | INSERT a permission |
| M2 | INSERT a role |
| M3 | INSERT a role-permission grant |
| M4 | UPDATE permission metadata — `Name`, `Description` |

### Forbidden — each is a refusal, never a repair

| # | Condition | Why |
| --- | --- | --- |
| F1 | DELETE anything | History and audit reference these rows |
| F2 | A permission or role exists in the database but not in the catalogue | Far more often a bad merge or a rename than a deliberate retirement. Auto-deactivating would remove authorization from every holder at deploy time |
| F3 | Deactivate an existing permission or role | As F2 |
| F4 | Reactivate an existing permission or role | Deactivation is an operational act, possibly an incident response. A deploy must not undo it |
| F5 | Revoke an existing grant | An authorization contraction for every holder of that role |
| F6 | Re-create a revoked grant | Revoked means a human decided. Re-granting is an authorization expansion a deploy is not entitled to make |
| F7 | Modify an immutable or security-semantic field — `Code`, `Resource`, `Action`, `RequiresHumanActor`, `IsSystemRole` | Identity and security semantics. Flipping `RequiresHumanActor` false→true makes every agent holding that permission non-compliant under RP6; true→false silently weakens a human-only control |
| F8 | Modify an existing `role_permission` row in any way | A grant is either newly introduced or it already represents authorization state. Its revocation history is authoritative and is never edited — there is no such thing as reconciling a grant |

> **Amended during implementation. Role metadata is not reconciled, and M5 is withdrawn.**
>
> `Name` and `Description` are non-security metadata for an ordinary role. Every role the catalogue seeds is a **system role**, and `Role.UpdateMetadata` refuses one outright — *"System roles cannot be modified."* M5 could therefore never legitimately execute: role metadata drift threw a domain exception out of the middle of a run instead of reconciling or refusing.
>
> Role metadata drift is classified as `SecuritySemanticDrift` and **causes refusal**. No domain mutator may be introduced to let synchronisation bypass this invariant, and no seventh reason code is added for it.
>
> `Permission.UpdateMetadata` carries no such guard, which is why M4 stands and M5 does not. The asymmetry is the domain's, not this requirement's.
>
> The rule that survives is worth stating plainly, because it is what four mutations instead of five buys:
>
> **Synchronisation mutates only state for which the domain already provides the mutation.** No cleverness, and no C2-specific bypass.

**F7 is already enforced by the domain and must stay that way.** `Permission.Code`, `Resource`, `Action` and `RequiresHumanActor` are get-only, as are `Role.IsSystemRole`. The only mutators are `UpdateMetadata` (name and description) and `Deactivate`/`Reactivate`. Synchronisation therefore *cannot* change authorization semantics without someone first adding a domain mutator — a visible, reviewable act. No mutator may be added for the convenience of this process.

### Scope

`permission`, `role` and `role_permission`. All three, because a release that adds `user.read` to `access-reviewer` is a **grant** change, and because `CatalogueDriftTests` already asserts all three. Permissions alone would not close the gap this requirement exists to close.

### Execution

| | |
| --- | --- |
| **Authority** | `migration_role`. PE2 requires `permission` to be writable only by a migration role; running this as `provisioning_role` would entrench the opposite |
| **Placement** | An explicit one-shot step in the deployment chain, beside `migrator` and `audit-schema`, so it runs on every deployment rather than when someone remembers |
| **Not** | Inside `ProvisionAsync`, which owns the System actor, `TenantProvisioned` and first-tenant semantics |
| **Atomicity** | See below. There is no partial synchronisation |
| **Order** | Permissions, then roles, then grants — foreign-key order |
| **Identity** | A permission and a role are matched by `Code`; a grant by the pair `(role code, permission code)` |

**`Code` is the identity key, not a compared field.** It appears in F7 because it is immutable and must stay so; it is absent from A6 because a row is *matched* by `Code`, so two rows with different codes are two different rows rather than one row that drifted. A permission whose code changed in the catalogue therefore surfaces as a pair of findings — a new code absent from the database, and an old code absent from the catalogue — and the second of those is a refusal (`PermissionMissingFromSeed`). That is the intended behaviour: renaming a code is exactly the accident F2 exists to catch.

**An unprovisioned database is a no-op, not a refusal — and it emits no audit event.** A *structurally ready* one: the preconditions come first, deliberately. A database missing its migrations or its audit schema is not deployable, and reporting a clean no-op for it would hide a real deployment problem behind a success. Ordering the bootstrap check first would make A11 read as an absolute, at the cost of that. The provisioner sits behind the `bootstrap` Compose profile, so `./up.sh` reaches a database with schema and no System actor. With no catalogue there is nothing to reconcile and first provision will create it, so the step reports that and exits 0. Refusing would break the documented path from a clean clone to a running system.

**No synchronisation occurred, so none is recorded.** There is also no System actor to attribute an event to in precisely this state, and inventing a synthetic actor to satisfy an audit rule would put a fiction in the trail to describe something that did not happen. The tool correctly determined there was nothing it could synchronise; that is a bootstrap no-op, not a synchronisation with an empty result.

**A run that changes nothing is a success, not a no-op to be skipped.** Idempotence is required: a second run against an unchanged database performs no mutation and still reports success.

### The transaction boundary

> Catalogue mutations are atomic. A refused synchronisation applies none of its catalogue mutations. The synchronisation audit event is retained independently of the rolled-back catalogue transaction.

Stated separately, and in those words, because an implementation that puts the audit write inside the catalogue transaction destroys the evidence of every refusal — the one outcome the evidence exists for.

### Audit

`PermissionCatalogUpdated` is **the audit event for the synchronisation operation**, not an event for an individual mutation. One per synchronisation — including runs that change nothing and runs that refuse.

The code is the release-controlled identity already established for PRV-C2, ahead of its emitter. Its definition is corrected to match this contract (see the Notes); the code itself is not renamed, because a code is a stable identifier and a description is where the semantics live. Its description is `Catalogue synchronised`.

Its absence against a **provisioned** database means the step did not run, which is itself informative. Against an unprovisioned one it means the bootstrap no-op above, where no synchronisation occurred and there is no System actor to attribute an event to.

It is written through the **autonomous** path, outside the command pipeline, as `TenantProvisioned` already is.

**It must not merely say that a sync ran.** A reviewer or investigator has to be able to distinguish these without reading the database:

```text
PermissionCatalogUpdated
Outcome = Succeeded
```

```text
PermissionCatalogUpdated
Outcome  = Refused
Reasons  = PermissionMissingFromSeed
```

The database remains the source of truth for what the catalogue now contains, so the event carries counts and reason codes — never the changed rows:

| Field | Content |
| --- | --- |
| Release identifier | The synchronisation tool's assembly informational version — deterministic, and requires nothing of the operator |
| Actor | The System actor, as `TenantProvisioned` uses — which exists by definition wherever this event does, since an unprovisioned database emits none |
| Outcome | `Succeeded` or `Refused` |
| Counts | permissions inserted; permission metadata reconciled; roles inserted; grants inserted |
| Refusal reasons | On refusal, the distinct reason codes found and a count per code |

**Refusal reason codes.** A closed set, one per detectable condition, so the set stays bounded however large the drift is:

| Code | Condition | Rule |
| --- | --- | --- |
| `PermissionMissingFromSeed` | A permission in the database, absent from the catalogue | F2 |
| `RoleMissingFromSeed` | A role in the database, absent from the catalogue | F2 |
| `GrantMissingFromSeed` | An active grant in the database, absent from the catalogue | F5 |
| `InactiveCatalogueEntry` | A permission or role inactive in the database and listed in the catalogue | F4 |
| `RevokedGrantInSeed` | A grant revoked in the database and listed in the catalogue | F6 |
| `SecuritySemanticDrift` | `Code`, `Resource`, `Action`, `RequiresHumanActor` or `IsSystemRole` differs, or a role's `Name` or `Description` differs — the domain forbids modifying a system role | F7 |

**The human-readable detail — which permission, which role, which field — goes to the operator's output and the exit code, not into the audit trail.** The reason codes say what KIND of refusal it was, with a count each; the subjects stay with the operator. Nothing here puts catalogue contents into audit.

> **Amended during implementation. Neither a cryptographic digest nor a database identifier appears in this payload.**
>
> Behaviour 14's secret scan rejects a hex digest of 40 characters or more, and rejects a high-entropy string that is not a GUID; a match is a defect under §15.1, so the record is not written at all. A SHA-256 refusal digest is therefore rejected on **every** refused run, and `current_database()` is rejected whenever a deployment's database name happens to look high-entropy.
>
> C2 does not place either value in the audit payload. **Refusal identity is represented by reason codes and counts, and by the operator output.** The database identifier is redundant in any case: the record is written to the database whose synchronisation it describes.
>
> **Behaviour 14 is not modified, and no encoding is chosen to evade it.** Designing around a frozen secret-detection control to preserve a convenience fingerprint would trade a security property for forensic tidiness. If Audit later defines a sanctioned non-secret digest representation, it arrives through Audit's own change control, not through this requirement.

**A refused run must still record its event**, per the transaction boundary above. `AuditCommandScopeBehavior` wraps the transaction for exactly this reason — what it writes must outlive the transaction's fate.

### The privilege expansion is a C2 security decision

Not an implementation detail, and recorded here so it is deliberate rather than accidental.

`migration_role` today holds **nothing at all on the trail** — an invariant asserted by `AuditConstructionVerification`. Emitting `PermissionCatalogUpdated` from a step that runs as `migration_role` is impossible without changing that. A new deployment script `005` therefore grants, and the verification matrix is amended to match:

| Privilege | `audit.audit_record` | `audit.audit_entity_ref` |
| --- | --- | --- |
| `SELECT` | denied | denied |
| `INSERT` | **granted** | **granted** |
| `UPDATE` | denied | denied |
| `DELETE` | denied | denied |

> `migration_role` is permitted to append audit evidence for release-controlled migration operations. It has no authority to read, modify, or delete audit records.

The migration principal can therefore create immutable audit evidence and can never afterwards alter or remove it — AR19 holds unchanged. This is the same shape as script `004`, which granted `provisioning_role` the same two privileges when provisioning began emitting through the audit pipeline: the privilege model, not the path, was what was out of date.

**The considered alternative was running the step as `provisioning_role`**, which already holds these grants. It was rejected: it would collapse the authority boundary deliberately established around catalogue writes, and `004` removed that role's catalogue privileges for the same reason. That `migration_role` also holds `CREATE` on schema `public` is a real consideration, weighed and accepted.

### Acceptance criteria

- **A1** A permission in the catalogue and absent from the database is inserted.
- **A2** A role in the catalogue and absent from the database is inserted.
- **A3** A grant in the catalogue and absent from the database is inserted.
- **A4** A permission whose `Name` or `Description` differs from the catalogue is updated to match. A ROLE whose `Name` or `Description` differs causes refusal, because the domain forbids modifying a system role.
- **A5** A permission or role in the database and absent from the catalogue causes refusal, naming it.
- **A6** A difference in `Resource`, `Action`, `RequiresHumanActor` or `IsSystemRole`, or in a role's `Name` or `Description`, causes refusal as `SecuritySemanticDrift`, naming the subject.
- **A7** A permission or role that is inactive in the database and present in the catalogue causes refusal — it is neither reactivated nor ignored.
- **A8** A grant present in the catalogue and revoked in the database causes refusal — it is not re-granted.
- **A9** A refused run commits no mutations to `permission`, `role` or `role_permission`. Its audit record is the deliberate exception and is expected to be present.
- **A10** A second run against an unchanged database mutates nothing and succeeds.
- **A11** **Bootstrap no-op.** When run against a database whose application schema and audit schema satisfy the synchroniser's required preconditions, but which has no System actor, the synchroniser performs no catalogue or audit writes and exits successfully (`0`). The CLI evaluates required deployment preconditions **before** the bootstrap check; a failed precondition is an operational failure (`3`), even if the database has no System actor.
- **A12** Every synchronisation run against a **provisioned** database emits exactly one `PermissionCatalogUpdated` event, including successful no-change runs and refused runs. An unprovisioned database with no System actor is a successful bootstrap no-op and emits no synchronisation event.
- **A13** The event's counts equal the mutations actually committed.
- **A14** A refused run's event carries `Outcome = Refused` and at least one refusal reason code, and the codes are the ones the findings map to.
- **A15** An existing `role_permission` row is never updated, whatever the catalogue says.
- **A16** `migration_role` can INSERT into `audit.audit_record` and `audit.audit_entity_ref`, and can still not SELECT, UPDATE or DELETE either.
- **A17** The process exits non-zero on refusal, so a deployment stops.
- **A18** `CatalogueDriftTests` passes after a successful synchronisation of a purely additive divergence.

### Notes

**This is the first entry in this section.** The catalogue is being back-filled; PRV-C2 is defined here because its contract was frozen before implementation rather than after.

**PE2's premise is not yet met.** `permission` is not yet writable only by a migration role — that enforcement is tracked in the enforcement-layer entry under Known Gaps. It does not block PRV-C2: writing this step to run as `migration_role` costs nothing now and does not depend on the enforcement work landing first.

**No new audit event type is introduced.** The deployed catalogue already defines one for this requirement — `PermissionCatalogUpdated`, commented `// PRV-C2` — established ahead of its emitter. Its definition was written for a narrower, permissions-only conception of PRV-C2 and is **corrected in place** to match this contract:

| Field | Was | Is |
| --- | --- | --- |
| Name | `Permission catalog updated` | `Catalogue synchronised` |
| Write path | `Transactional` | `Autonomous` |
| `Permission` / `Added` | `Required: true` | optional |
| `Permission` / `Changed` | `Required: true` | optional |

The write path is the correction that matters: a `Transactional` record would be destroyed by the rollback of the very refusal it exists to evidence. The required references could never have been satisfied by a run that changed nothing or a run that refused, both of which this contract requires an event for. No `Role` or `RolePermission` reference is added — breadth belongs in the payload's counts, and per-row references would turn one event about an operation into a row-by-row change log.

> **An event-type definition may be corrected in place only where it is established that no deployed audit record can reference the affected `(code, version)`.** `PermissionCatalogUpdated` satisfies this condition because no emitter has existed in any release. **This is not a general rule for modifying deployed event definitions.**

`audit_record` carries a foreign key to `(event_type, event_version)`, so editing a definition at the same version silently restates the meaning of every record already pointing at it.

**`AuditEventCatalogue.Version` is not bumped, and must not be.** It is a whole-catalogue revision identifier, not a per-event one: `EventTypeSeed.Version` returns that same constant, so raising it would insert 49 new event-type rows at version 2, deactivate all 49 version-1 rows, and write every future audit record against `(code, 2)`. Propagation does not depend on it either — `AuditCatalogueSeeder` upserts every seed unconditionally on each `audit-schema` run, so the corrected definition reaches existing databases on the next deployment. The constant's documented consumers are `TenantProvisioned`'s payload and AUD-C3's future comparison.

## USR-Q1 — User List Query

**Requirement ID:** `USR-Q1`, assigned by the owner 2026-09-17 — the first ID in the query family (*Identifier Format*).

**Status:** Approved and frozen — the row projection, the endpoint and identifier semantics, pagination, sorting, filtering, and the implementation contract. All decided 2026-09-17 by owner decision. Not yet implemented.

### Requirement

A caller holding `user.read` can list the tenant's human users. Each row exposes the minimum a person needs to recognise a user and safely start an operation on them — nothing more.

The projection was built up from an empty row, not cut down from the `app_user` and `user_identity` models. A field is in the row because a supported operation needs it, never because a column exists. The authorization and audit posture are already decided in `docs/architecture.md` §11: the read declares `Required("user.read")`, its handler enforces it, and it is not audited.

### The row

| Field | Role | Why it is present |
| --- | --- | --- |
| `UserId` | **identity and routing** | Every operation a row can start is addressed by it: `POST /api/users/{userId}/password-reset` and `POST /api/users/{userId}/sign-out-everywhere` |
| `DisplayName` | recognition | A list of identifiers is not usable by a person |
| `Email` | disambiguation | `DisplayName` is not unique. Once the list is the entry point for password reset and sign-out-everywhere, two users with the same display name must be distinguishable before acting on either |

> **`UserId` is the only identifier. `DisplayName` is presentation data used to recognise a person. `Email` is presentation and disambiguation data used to distinguish users with similar display names. Neither `DisplayName` nor `Email` is a stable identity, and clients must not use either as a user key, route identifier, cache key, equality identity, or stable user reference.**

That prohibition is grounded in the model, not in caution. `Email` is unique only among *active* humans and may be reassigned — which is precisely why sign-in refuses it as a lookup key (`IUserIdentityRepository.FindLocalByUsernameAsync`). `DisplayName` carries no uniqueness at all.

### Scope: human users only

The list contains users whose actor type is `Human`. **The System user is excluded by the read itself**, and `ActorType` is not exposed to explain its absence.

The System actor is an infrastructure actor, not an administrable account, and no operation a row can start applies to it. Exposing `ActorType` would create a client-side distinction between human and system users in a list whose only purpose is administering human users.

### Excluded fields

Each of these stays out of the row until a named story brings the evidence below for it:

| Field | Why it is excluded |
| --- | --- |
| `Username` | A sign-in handle, and not required to identify a user for any row operation. `Email` already serves disambiguation |
| `FirstName`, `LastName` | `DisplayName` serves recognition |
| `Status` | See *Lifecycle status* below |
| Pending activation | Derived from the absence of a credential (inv. 15); no row operation acts on it |
| Locked | Derived from `credential.LockedUntil` against the current time; unlock is addressed by identity, not user, and is not a row operation here |
| `UserIdentityId` | A user may hold more than one identity, so it has no single value on a user row; only unlock needs it |
| `IdentityProvider`, `IdentityType` | Per identity, not per user; constant (`Application`, `Local`) in every current row |
| `ActorType` | The scope rule above makes it unnecessary |
| `CreatedAt`, `CreatedBy` | No row operation needs them; `CreatedBy` exposes a relationship between people |
| Roles | Authority held is not what this read is for |
| Session and sign-in activity | Behavioural data; no row operation needs it |
| Credential state, lockout counters, deactivation details | Security internals |

### Endpoint

**`GET /api/users`** is the user-list read. It is a **collection endpoint**: it returns user-list rows, and it requires `user.read`.

It sits beside the existing user routes — `POST /api/users` creates a user, and `/api/users/{userId}/…` addresses one — and shares their prefix, not their authorization. Holding `user.read` permits seeing the list and nothing else; every operation a row can start is a separate command that enforces its own permission, whatever the list returned.

What this does **not** decide:

- **A detail route.** `GET /api/users/{userId}` is a plausible future shape, but it is not implied by the collection taking `/api/users`, and it is not frozen here.
- **`Location` on create.** `POST /api/users` omits it deliberately, because no canonical retrieval URI exists. The collection is not one. A future detail-read story may introduce it explicitly; this one does not change it.

### Identifier semantics

> **The `UserId` returned by each user-list row is the `app_user.id` of that user and is the identifier accepted by existing user-scoped endpoints under `/api/users/{userId}/...`.**

This was traced, not assumed. Both existing user-scoped operations take the row's identifier as it is:

| Row → route | Command | Permission | How the target is resolved |
| --- | --- | --- | --- |
| `UserId` → `POST /api/users/{userId}/password-reset` | `AdminResetPasswordCommand` (CRD-C5) | `user.resetpassword` | By `UserId`; the handler requires exactly one active local identity and refuses rather than choose between identities |
| `UserId` → `POST /api/users/{userId}/sign-out-everywhere` | `RevokeUserSessionsCommand` (SES-C4, administrator form) | `session.revoke` | By `UserId`; every active session of every identity the user holds |

It is the same identifier `POST /api/users` already returns as `UserId`.

### Unlock is not reachable from a row

`POST /api/identities/{identityId}/unlock` (CRD-C6) is **out of scope** for the user list, and a row's `UserId` is **not** a way to reach it.

Unlock is identity-scoped by decision, not by accident. `UnlockAccountCommand` is keyed by `UserIdentityId` because lockout lives on one identity's credential, and it records that resolving through the user *"would have to choose between identities, and choosing is how a command reaches one it was never asked about."* `IdentityEndpoints` keeps the route off `/api/users` so that it cannot suggest otherwise.

`IUserIdentityRepository.FindLocalByUserIdAsync` exists, and CRD-C5 applies an *exactly one* rule to it — as its own eligibility rule. Carrying that rule over to unlock would be the identity selection CRD-C6 refused. Any path from a row to unlock is therefore a new identity-selection contract, however it is built: a `UserIdentityId` in the row, a lookup endpoint, or a client-side join.

**`UserIdentityId` is not added to the row because unlock needs it.** That would design the read around a command whose contract says it is identity-scoped. Making unlock reachable from a user list needs its own decision.

### Correlating a row with the caller

Whether a row is the signed-in user is **out of scope**. `GET /api/me` identifies the caller by `UserIdentityId`, not `UserId`, so a client cannot match a row to itself — and neither the list nor `/me` changes to allow it.

Nothing depends on the match. The server owns the rules it would inform: an administrator signing out their own user everywhere ends their own current session too (SES-C4, D4). A client does not reconstruct identity rules the server already enforces.

### Pagination

**Offset pagination.** The caller asks for a numbered page; the server returns that page and whether another follows.

| | Contract |
| --- | --- |
| **Request** | The caller may supply a page number, counted from 1, and may supply a page size. An omitted page number means page 1. |
| **Page size** | Chosen by the caller, up to a server-enforced maximum. When the caller supplies none, the server applies a default no larger than the maximum. **The server never executes an unbounded user-list query based on caller input.** |
| **Invalid values** | A page number or page size that is not a positive integer, or a page size above the maximum, is **refused as an invalid request** — never silently corrected. `0`, a negative value and an oversized value are not turned into something the caller did not ask for. |
| **Response** | The result identifies the page returned and states whether another page exists (`hasMore`). **The total number of users is not returned.** |
| **Past the end** | A page beyond the last is an empty page with no further page — not an error. |
| **Ordering** | `DisplayName` ascending, then `UserId` ascending as the deterministic tie-breaker. `DisplayName` is compared as defined under *Sorting*. |
| **Consistency** | Each page is a single read operation. No separate count query is required, and none is made. |

**Why offset.** It matches the paged table the web client already anticipates (`docs/frontend-architecture.md` §4, §11) and keeps request parameters free of personal data. A keyset cursor would have to carry the last row's sort values — here a display name — into query strings and access logs, against the discipline that keeps credentials out of URLs (`docs/frontend-architecture.md` §13). No paging precedent exists in the backend; the Audit design's keyset rule (IMPL-12) rests on the trail being append-only and ordered by `Sequence`, which the user table is not.

**The model is not justified by tenant size.** No upper bound on users per tenant is documented, and none is assumed here. The protection is the server-enforced maximum and the deterministic order. A demonstrated scale requirement would reopen the model on its evidence.

**Why no total.** Paging forward and back needs only `hasMore`. A total would disclose the tenant's population to every holder of `user.read`, and would add a count to every request. It may be added later if a client demonstrates a need, under the same evidence as any other field.

**Why this order.** The list is read by a person, and `DisplayName` is the field a row is recognised by, so a stable, human-scannable order makes page navigation predictable. `DisplayName` is not unique, so `UserId` makes the order total; without a total order, pages overlap or drop rows even when nothing changes.

> **The list does not promise a snapshot across separate requests.** A user created between two page requests can cause rows to shift between pages.

That is a property of the contract, not a defect to be engineered away. Snapshots, timestamps, cursor tokens or similar machinery are **not** introduced to prevent it.

What this does **not** decide:

- **The numeric maximum and default page size.** Established when the query is designed, on evidence.
- **Comparison semantics for `DisplayName`.** Decided afterwards by the sorting gate; see *Sorting*.
- **An index.** None supports this order today. Whether one is warranted is established from the query plan at implementation, not added in advance.
- **User-selectable sorting.** The order above is the pagination foundation, not the sorting model; see *Sorting*.
- **The response shape and parameter names.** `page`, `pageSize` and `hasMore` name the concepts; their representation belongs to the response envelope decision.
- **The status code of an invalid-request refusal.**

### Sorting

**No user-selectable sorting in this version.** The list is always returned in its default order. There is no client-controlled sort field and no client-controlled sort direction, and `Email` is not sortable.

This does not make the list unsorted. Pagination requires a deterministic order and has one; what is withheld is a *client-controlled* sorting contract, because no operational need for one has been identified. Adding sorting later is additive — a new, optional request parameter — while removing a sorting capability once clients depend on it is not.

#### The default order, defined exactly

> **`DisplayName` ascending, compared using PostgreSQL's ICU `"unicode"` collation, followed by `UserId` ascending as the deterministic tie-breaker.**

The collation is stated because leaving it to the database default does not define an order. The declared default is the same in the deployed database and the test database — `libc`, `en_US.utf8` — and the two still order the same values differently:

| Database | C library | Default order of the same values |
| --- | --- | --- |
| Deployment (`postgres:18-alpine`) | musl | `10 · 9 · Bob · Delacroix · OBrien · Zoë · _x · adam · alice · de la Cruz · o'Brien · Ängel · Émile` |
| Test suite (Debian) | glibc | `10 · 9 · adam · alice · Ängel · Bob · Delacroix · de la Cruz · Émile · o'Brien · OBrien · _x · Zoë` |

musl does not apply the locale's collation and compares bytes; glibc applies it. A test could pass against one while the other returns a different order. Under `COLLATE "unicode"` both return `_x · 10 · 9 · adam · alice · Ängel · Bob · de la Cruz · Delacroix · Émile · o'Brien · OBrien · Zoë`.

Two limits on what this claims:

- **`"unicode"` is a comparison mechanism, not a language policy.** It is ICU's root collation: a stable ordering across environments, not a locale-specific definition of alphabetical order for any particular language.
- **ICU ordering is not immutable.** An ICU version change can change it, and an index built on it would then need rebuilding. That is not solved here; it is recorded so the ordering is not assumed permanent.

`UserId` needs no collation: it is a `uuid`, compared by value, identically in every environment.

#### Future sort fields

> **A user-selectable sort field must correspond to a field exposed by the user-list row projection.**

The row is `UserId`, `DisplayName` and `Email`, so those are the only fields a future sorting decision may choose from. A field outside the row — `Username`, `CreatedAt`, lifecycle status, identity information — cannot become a sort key merely because the database has it: ordering by a hidden field discloses it through the order of the rows.

This bounds the choice; it does not make every row field sortable. Each would still need its own decision, including its direction semantics, null handling and comparison.

What this does **not** decide:

- **Descending order**, and the tie-breaker direction it would need.
- **Sorting by `Email`.**
- **Any index** supporting the default order, including one on `DisplayName` under `"unicode"`.
- **`DisplayName` validation or normalisation.** The API currently accepts a blank or whitespace-padded display name, which sorts first. That is a data-quality question with its own evidence, not a sorting one.
- **The wider difference between the deployment and test database images.** Only its effect on this ordering is resolved here.
- Wire parameter names, the response shape, numeric page limits and the invalid-request status, as before.

### Filtering

> **No filtering or search in this version.** `GET /api/users` returns the complete set of human users, subject to the frozen pagination rules and default ordering. No request parameter may narrow the result set.

The decision rests on the absence of a workflow, not on the absence of a filter:

- **No administrator workflow requires finding a user by name or email.** Every user-scoped administrator command is addressed by an identifier — `UserId` for password reset and sign-out-everywhere, `UserIdentityId` for unlock — and none accepts a searchable value. Before this list, the only way an administrator obtained a `UserId` was by creating the user.
- **The list is the first mechanism that makes those operations reachable.** No client page yet starts a user-scoped administrator action, so no existing interface needs to locate a user.
- **The only lookup by a personal value is not a precedent.** `IUserIdentityRepository.FindPasswordResetCandidatesAsync` serves the anonymous, self-service password reset request: an exact match, anti-enumeration by design, carried in a `POST` body. It locates one's own account, not somebody else's.
- **Filtering is additive later.** A new optional request parameter does not break a client of this contract.

`docs/frontend-architecture.md` defines how a filtered list is *presented* — distinct empty-state wording (§11), and a filters bar that collapses into a sheet (§14). Those are presentation conventions for a list that has filters; they do not establish that this one needs any.

#### Future filters

> **A future user-list filter may only operate on fields present in the row projection. This constraint does not make any row field filterable by itself.**

A filter on a field outside the row discloses it more directly than sorting would: `status=Inactive` names exactly who is inactive through which rows come back, although status is not a column.

#### Known consequence for a future filter

**Request query strings reach the host's logs.** The host configures no logging, so ASP.NET Core's default request logging writes the full request URL, query string included, at Information level. Observed against the running host:

```text
Request starting HTTP/1.1 GET http://localhost:8080/api/probe?email=probe.person%40example.com
```

A name or email filter carried in a `GET` query string would therefore write personal data to the host's logs on every use. This does not affect this version — the pagination parameters carry no personal data — and it is **not** a prohibition on future filtering. A future filter's own decision must address how its values travel; changing request handling or logging is a legitimate answer.

The logging behaviour itself is a platform finding, independent of this read, and is not changed here.

### Implementation contract

The decisions the conceptual contract left open, settled from implementation evidence. Nothing here introduces a new exception type, authorization abstraction, query pipeline or generic pagination type.

| | Contract |
| --- | --- |
| **Endpoint** | `GET /api/users` |
| **Authorization** | An authenticated caller holding `user.read` |
| **Parameters** | `page` — optional, default `1`. `pageSize` — optional, default `25`, maximum `100` |
| **Unknown parameters** | Ignored. `/api/users`, `/api/users?sortBy=email` and `/api/users?whatever=abc` return the same rows in the same order |
| **Recognised parameters** | Validated: an invalid value is refused as below, never ignored |
| **Ordering** | `DisplayName` ascending under `COLLATE "unicode"`, then `UserId` ascending |
| **Filtering** | None; human users only |
| **Pagination** | Offset; `hasMore`; no total |
| **Audit** | None |
| **Index** | None in this version |

#### Request parameters

**Unknown parameters are ignored; recognised parameters are validated.** No strict mechanism rejecting unknown query parameters is introduced for this endpoint.

A recognised parameter is refused when it is:

- **malformed** — not representable as the query's accepted integer parameter type, or supplied more than once (`page=abc`, `page=2147483648`, `page=1&page=2`); refused where the request is bound, since there is no value to hand on. A value that is mathematically an integer but does not fit the accepted type is malformed, not out of range;
- **out of range** — `page` below 1, `pageSize` below 1 or above 100; refused by the query itself, so the bound holds for every caller of the query and not only for HTTP.

Both are an invalid request. Parameters are bound as text and parsed explicitly rather than by framework binding, because framework binding refuses a malformed integer with an empty-bodied `400` that the host's error mapping never sees — a second error shape for the same kind of failure.

**Offset beyond representation.** If `(page − 1) × pageSize` cannot be represented by the query implementation's offset type, the result is an empty page with `hasMore: false`, and no query is executed.

#### Response

```json
{
  "users": [
    { "userId": "…", "displayName": "…", "email": "…" }
  ],
  "page": 1,
  "pageSize": 25,
  "hasMore": false
}
```

A response type specific to this query. `pageSize` is the size applied, so a caller that omitted it sees the default. Property names follow the host's JSON conventions (camel case).

**`email` may be `null`.** The column is nullable and no database constraint requires a human user to have an address, although every current creation path supplies one. A row with no address is represented as `"email": null`; the query does not pretend the database guarantees what it does not.

#### Refusals

The existing exception model, unchanged:

| Condition | Status | Mechanism |
| --- | --- | --- |
| No established caller | `401` | `AuthenticationFailedException`, with the host's fixed message |
| Caller lacks `user.read` | `400` | `BusinessRuleViolationException` — as a command's authorization refusal is today. Distinguishing the two remains the known gap *Authorization failures are not distinguishable from validation failures* |
| Invalid recognised parameter | `400` | the host's `{ "error": … }` body |

A query has no pipeline, so the handler establishes these itself, **in this order**:

1. **Authenticate** — no established caller is refused.
2. **Authorize `user.read`** — a caller without it is refused.
3. **Validate** `page` and `pageSize` ranges.
4. **Read.**

Authorization precedes parameter validation deliberately: a caller not entitled to the list receives the authorization refusal whatever parameters it sent, and learns nothing about which values are valid. Authorization consumes `IsAllowed` only.

The order governs the *range* of a value. A *malformed* parameter is refused where the request is bound, before the handler runs and therefore before authentication: there is no value to hand on. That refusal discloses only the request's syntax, which this contract publishes, and it matches the existing endpoints, which refuse a missing required field at binding for any caller.

#### Page-size values

`25` by default and `100` at most. **These are judgements supported by evidence, not values the evidence establishes.** Measured on the deployment image (`postgres:18-alpine`), reading `pageSize + 1` rows to determine `hasMore`, without an index:

| Human users | First page | Deep page (offset 89,900) |
| --- | --- | --- |
| 1,000 | 0.6 ms (top-N heapsort, 52 kB) | — |
| 100,000 | 11 ms (parallel scan, top-N heapsort) | 96 ms (sort spills 12 MB to disk) |

A row is three short strings, so a full page is small. `25` is a screen of a data table; `100` keeps the first-page read in single-digit to low-double-digit milliseconds at a hundred thousand users.

#### Query

One statement per page, with no count:

```sql
SELECT id, display_name, email
FROM app_user
WHERE actor_type = 'Human'
ORDER BY display_name COLLATE "unicode", id
OFFSET (page − 1) × pageSize
LIMIT pageSize + 1
```

The extra row determines `hasMore` and is not returned.

#### No index in this version

With the index `(display_name COLLATE "unicode", id) WHERE actor_type = 'Human'`, the same 100,000-user measurements were 0.07 ms for the first page and 22 ms for the deep page — the index scan still walks every skipped row. The unindexed plan is already fast at plausible tenant sizes, and the index is not free: under an explicit ICU collation it must be rebuilt when ICU's version changes. It is not added in advance; tenant-scale evidence reopens it.

### Lifecycle status

`Status` is **not** in the row.

The `user.read` permission is described as *"View user accounts and their current lifecycle status."* That description states the authority `user.read` grants; it is **not** the response schema of every read that uses it:

> `user.read` authorizes viewing user accounts and their lifecycle information where a particular read exposes such information. This user-list projection does not expose lifecycle status.

Exposing `app_user.status` today would be misleading rather than informative. Nothing can deactivate a user, so the value would read `Active` in every row, while the lifecycle states that are meaningful — pending activation and locked — are derived from other tables and would be absent from it. A later read that exposes lifecycle information decides what "status" means there, and whether `user.read` is sufficient for it.

No permission catalogue change and no audit catalogue change accompany this.

### Adding a field later

A field joins the row only with the same evidence that admitted these three:

1. **Concrete use** — which supported operation or interface needs it.
2. **Data exposure** — what information it discloses.
3. **Necessity** — why the use cannot be met without it.
4. **Removal cost** — the compatibility consequence of withdrawing it later.
5. **Stable identity** — whether a client could mistake it for an identifier.

*"The column already exists"* is not evidence. A field is cheap to add and expensive to remove once a client depends on it, which is why the default is absence.

### Acceptance Criteria

- **P1** A user-list row contains exactly `UserId`, `DisplayName`, and `Email`; no additional fields are exposed by the projection.
- **P2** The System user never appears in the list.
- **P3** No excluded field appears in a row, under any name.
- **P4** Identifier usage: `UserId` is the only identifier exposed by this projection. `DisplayName` and `Email` must not be used as identifiers by clients. Server-side tests verify that only `UserId` is designated as an identifier; client tests, where applicable, verify that `DisplayName` and `Email` are not used as keys, routes, cache identities, or matching identities.

  P4 is an API consumer contract as well as a server one. The server cannot prove a future client's behaviour, so its verification is split deliberately; the absence of a backend test for the client half is by design, not an omission.
- **P5** `GET /api/users` requires `user.read`, and returns user-list rows as a collection.
- **P6** A row's `UserId` is accepted, unchanged, as `{userId}` by `POST /api/users/{userId}/password-reset` and `POST /api/users/{userId}/sign-out-everywhere`.

  P6 is limited to the routes **accepting the identifier** — that it binds and addresses the user the row describes. It does not assert that either command succeeds: each keeps its own authorization and eligibility rules, and a refusal by those rules is not a P6 failure.
- **P7** A request returns at most one page, and the server never returns more rows than the server-enforced maximum. This is a bound on what is returned, not permission to cap: a request for more than the maximum is refused (P8), never accepted and truncated.
- **P8** A page number or page size that is not a positive integer, or a page size above the maximum, is refused as an invalid request and is never silently corrected.
- **P9** Rows are ordered by `DisplayName` ascending under ICU `"unicode"` collation, then `UserId` ascending, and consecutive pages over unchanged data neither repeat nor omit a row.
- **P10** The result states whether another page exists and does not contain a total user count.
- **P11** A page beyond the last is empty and states that no further page exists.
- **P12** The list offers no client-controlled sort field or sort direction: no request parameter changes the order, and every request returns rows in the default order. How an unrecognised parameter is treated is not decided here.
- **P13** The default order is the same in every environment the platform runs in, including where the database's default collation differs.
- **P14** No request parameter narrows the result set: every request pages over the complete set of human users.

### Notes

**Not decided here:** a detail route, `Location` on create, a path from a row to unlock, and correlating a row with the caller.

---

# Known Gaps and Deliberate Deferrals

Things the code knowingly does not do yet. An agent that encounters one of these should **not** "fix" it inside an unrelated story and should **not** report it as a defect — cite this section instead. Remove an entry when the deferral is closed.

## UT4 — no partial unique index on open user tokens — RESOLVED

**Rule:** at most one open token per `(UserIdentityId, TokenType)`.

**Resolved** by `AddOpenTokenUniqueIndex`, landing with CRD-C2 as planned:

```sql
CREATE UNIQUE INDEX "ux_user_token_open_per_type"
ON "user_token" ("user_identity_id", "token_type")
WHERE "used_at" IS NULL AND "invalidated_at" IS NULL;
```

**Note what the predicate does not say: expiry.** A partial index predicate
must be immutable and `now()` is not, so an expired-but-unused token still
occupies the slot. That is not a limitation worked around — it is what makes
UT4 and UT5 one mechanism. UT5 requires issuance to invalidate all prior unused
tokens of that type *including already-expired ones*, and this index is what
makes forgetting that clause impossible: a reissue after expiry collides here
rather than silently producing two open tokens.

No data-cleanup step; a probe found 142 tokens, 137 open, and no violations in
the development database. The index build is the authority elsewhere.

**Fixture consequence.** Two test classes seeded several open Activation tokens
for one identity and now seed an extra identity instead —
`ActivateAccountIntegrationTests` and `UserManagementImmutabilityTests`.

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

**Amended (2026-09-15):** browser token storage and cookie transport are no
longer open. The owner decided both with the web client design: cookie transport
is an approved browser transport, and browser token storage is not
application-managed. Both are recorded in `docs/architecture.md` §17,
*Amendment: cookie transport*. The other four remain open.


## Audit emission covers every implemented command

**State:** the pipeline emits. `TransactionScopeBehavior` opens the command's
transaction and `AuditEmissionBehavior` writes inside it; `AuditRecordAssembler`
resolves each declared event against the deployed catalogue and validates it;
`AuditRecordWriter` inserts on the ambient transaction. `docs/architecture.md`
§11 records the pattern.

**What is wired:** USR-C1 (`UserCreated`, `IdentityCreated`, `TokenIssued`),
SES-C2 (`SignedOut`), CRD-C1 (`TokenConsumed`, `PasswordSet`,
`AccountActivated`) and provisioning's own `TenantProvisioned`, which is the
tenant's Sequence 1.

SES-C1 (`SignInSucceeded`, `AccountLocked`, `SignInFailed`) and CRD-C1's
rejection (`TokenRejected`) joined in E2b, with them the autonomous write path
and the System attribution override. All four commands now record both their
outcomes.

**What is not:** the three autonomous events owned by the Audit context —
`AuthorisationDenied`, `CommandRejected` and `AuditInspected`. The first two
cannot be written at all as catalogued (see below); the third belongs with the
query work, since reading the trail is what it records. So a command refused by
authorisation still leaves no trace, including an attempt to sign out of
somebody else's session.

**There is deliberately no `IAuditWriter`.** An earlier version of this entry
and of `docs/architecture.md` §6 described one as the intended contract; the
frozen Audit model names a direct write API as an anti-feature, and the
architecture text was corrected rather than the model. Handlers declare events;
the pipeline writes them. Do not introduce a public writer abstraction.

**AUD-O1 is closed:** `AuthorizingAssignment` carries `AssignmentId`, so
point-in-time reconstruction is a direct reference to what authorised an act
rather than a re-evaluation of what would authorise it now.

**Two decisions were frozen at E1 review** and are not open to being
re-taken as implementation details:

- **`CausationId` stays null within a command.** The shared `OperationId` is
  what groups a command's records; causation would assert an event dependency
  the handler does not have. Recorded in `docs/architecture.md` §11.
- **No `AuditEmissionDefect` type.** Emission defects are
  `InvalidOperationException`, identified by a message opening with
  `Audit emission defect`. The plan called for a dedicated type; the platform's
  exception vocabulary is frozen at three, and widening it for one capability
  is a larger decision than this story had.

**Still missing:**

- **The other commands.** `ActivateAccountCommandHandler`,
  `SignInCommandHandler` and `SignOutCommandHandler` declare nothing, so they
  write nothing. A command that declares nothing is silent by design, not by
  failure, which is why nothing detects this for you.
- **A second establishment path for token-bearer commands** (CRD-C1, CRD-C3),
  which authenticate by possessing a token rather than a session and today
  establish no context at all. The contract can express the case (AUD-D28: no
  authorising role, `IdentityProvider` 'Application'); the path is not built.
- **Payload schema validation (IMPL-09).** `payload_schema_ref` is carried on
  the catalogue row and nothing validates against it.
- **Audit queries (AUD-Q1, AUD-Q3).** Reading the trail needs the query pattern
  that `docs/architecture.md` §11 says must not be invented inside another
  story.
- **The hard duration bound T** (behaviour 6 amended, AUD-18), excluded from
  Slice A and parked as AUD-O17.

**A consequence to know before writing tests.** An audit record references its
actor (AR10) and the assignment that authorised it (AR12) by foreign key, and no
role may delete an audit row. A user who has acted under an audited command can
never be deleted. `PermanentTestCaller` and the host suite's fixed callers exist
for that reason; a shared development database also accumulates audit records
permanently as those suites run.

**Deferred to:** the Audit capability (`docs/architecture.md` §6).

## AuthorisationDenied and CommandRejected have no writable form — catalogue correction

**State:** both are active event types whose `primary_entity_type` is null,
which the catalogue permits. `audit.audit_record.entity_type` is `NOT NULL`.
There is therefore no record either event could produce: the table requires an
entity type and the catalogue supplies none.

**A contradiction between two frozen artefacts, not a bug in either.** Nothing
declares these events yet, so nothing fails today. E2b found it while planning
the autonomous writer and deliberately did not resolve it: inventing a
synthetic entity type to satisfy the column would put a fiction in every such
record, and amending the catalogue casually inside an emission story is exactly
what release control exists to prevent.

**How it must be taken:** as a catalogue correction in a new release script,
under the existing rules — the deployed catalogue is immutable once shipped, so
the fix ships as a new version rather than an edit. Whoever takes it decides
what these events name as their entity, or whether the column's constraint is
the thing that is wrong. Until then, the pipeline's refusal is honest: a
declaration of either would fail the primary-entity rule rather than write
something untrue.

## Behaviour 19 assumes a query pipeline that §11 does not have

**Rule (Audit design specification §12.3, behaviour 19):** when the caller's
`ActorType` is `PlatformOperator`, *the query pipeline* wraps every `audit.read`
and `auditpolicy.read` query, and emits `AuditInspected` through the autonomous
writer before returning results. Failure is fail-closed: a read the tenant
cannot later see is not returned. AR27 enforces the operator restriction as a
database `CHECK`, and the deployed catalogue carries the event, commented
`Query pipeline (behaviour 19)`.

**State:** there is no query pipeline, deliberately. `docs/architecture.md` §11
says a dispatcher is not a pipeline, forbids query behaviours, and requires any
new cross-cutting query concern to be decided in its own story. The Audit
design places behaviour 19 exactly where §11 says nothing may go.

**A contradiction between two frozen artefacts, not a bug in either.** Nothing
triggers it today: the `PlatformOperator` actor type does not exist
(`AuditRecordAssembler` records it as awaiting AM-01) and no audit query
(AUD-Q1–Q12) is implemented. It was found while deciding that the user-list
read is not audited, which it does not affect — behaviour 19 covers the audit
trail's own reads, and AR27 refuses `AuditInspected` for any tenant user.

**How it must be taken:** by the first story that lets a platform operator
read the audit trail, as an explicit decision about *how* operator inspection
is recorded — whether §11 admits a query behaviour for it, or the obligation is
met some other way. It must not be settled by quietly adding a behaviour to the
dispatcher, and it must not be resolved by relaxing AR27.

**Deferred to:** the audit query stories (AUD-Q1–Q12) together with operator
access (AM-01, OPR-C1).

## AUD-S12 — the canonical form is not what the database stores

**State:** `CanonicalJson` produces the RFC 8785 form of a record's content
(IMPL-03), and that is what the writer sends. The `before`, `after` and
`payload` columns are `jsonb`, which stores a normalised representation of its
own: reading a record back returns PostgreSQL's key order and spacing, not the
canonical bytes that were written. Both forms are deterministic; they are not
the same form.

**Not a defect, and nothing in E2a depends on it.** Canonicalisation earns its
place on the input side, where it makes the same semantic value produce the
same bytes regardless of how a handler happened to build the object. The
observation was made while asserting a record's content in a test, which had to
compare parsed fields rather than text for exactly this reason.

**What it constrains:** any future integrity or hash mechanism must state which
representation it hashes — the canonical form computed before the write, or
what `jsonb` returns on read — and compute it the same way every time. A
verifier that canonicalises on the way in and re-reads on the way out would
compare two different normal forms and report tampering that did not happen.
That decision belongs to AUD-S12's reconciliation work, not to an emission
story.

## Token consumption checked too little — RESOLVED

**Was:** `UserTokenRepository.TryConsumeAsync` decided whether a token could be
used from the token row alone — id, secret hash, unused, uninvalidated,
unexpired. Two things it never checked:

- **Subject status.** A token belonging to a deactivated identity, or to an
  identity whose user had been deactivated, still activated the account.
- **Token type.** The token plaintext is an id and a secret and carries no
  type, so the activation endpoint would consume a password-reset token that
  matched on both.

**Why the second one mattered more than it looked.** `ActivateAccount` inserts
a credential unconditionally — fresh id, no lookup for an existing row — and
`CredentialRepository` resolves with `FirstOrDefaultAsync`. So a reset token
presented to the activation endpoint produced a **second credential row**, and
from then on authentication ran against whichever of two password hashes the
query happened to return. No error, no symptom, nothing in the trail. Latent
only because no path issues a reset token yet; CRD-C2 would have made it live.

**Resolved.** Both predicates now sit inside the single conditional UPDATE, and
`TryConsumeAsync` requires the expected `TokenType` rather than defaulting it —
so CRD-C2 inherits the contract instead of rediscovering the defect. The
subject predicate is `SignInCommandHandler`'s and `NotificationGate`'s
verbatim: one question, one spelling.

**The property that decided where the checks go.** Inside the UPDATE, a token
refused for a wrong type or an inactive subject is **not consumed**. A
caller-side check could only run once the token had already been burned, which
would permanently disable an account over a condition — a deactivation later
reversed — that was temporary. `TokenConsumptionContractTests` asserts both
halves of every refusal, the return value and the untouched `used_at`.

### UT6 is interpreted, not contradicted

The frozen workbook's UT6 reads:

> consumption is a single conditional UPDATE (used IS NULL AND invalidated IS
> NULL AND not expired); zero rows affected = rejection

It names three predicates. The previous implementation was a **faithful reading
of that text** — this was an incomplete rule, not a defective implementation of
a complete one. The two added predicates are recorded here as an interpretation
of UT6 rather than a departure from it, in the same register as the UR12 note
in `AuthorizationServiceTests`. **The frozen workbook is not edited.**

**Deliberately still out:** consumption does not check
`identity_type = 'Local'`, and `ActivateAccount`'s unconditional
credential-creation behaviour is unchanged — CR1's index now makes the second
row impossible rather than the handler declining to attempt it.

**Rejection reasons stay generic.** `TokenRejected` continues to record
`NotUsable` for every post-lookup refusal. Distinguishing them would mean a
second read of token state, free to drift from the predicate that actually
governs, and it would risk becoming a pre-check — reintroducing the
"SELECT, decide, UPDATE" race UT6 exists to forbid. Richer rejection telemetry,
if it is ever wanted, should be designed deliberately rather than smuggled in
here.

## Bearer-authenticated commands ran under an established caller — RESOLVED

**Was:** sign-in (SES-C1), activation (CRD-C1) and password reset (CRD-C3) are
anonymous commands that establish the caller from their own credential. The
pipeline let them run in a scope that already had a caller — which every
request carrying a live session does — and two frozen rules then collided: the
established identity cannot be rebound (E2a), and a record's origin follows the
scope's caller, while `SignInFailed` and `TokenRejected` permit only an
Anonymous origin (EO5).

Under a live session for A:

- **B's correct password** verified, then `Unlock()` and any rehash of B's
  credential committed, and only afterwards did `BearerActorEstablisher` decline
  to act as B — **401**, with the counter reset and nothing recorded.
- **B's wrong password** (and an unknown username, and a locked account)
  counted a failed attempt against B, committed it, and then failed on the
  audit emission defect — **500**, with the attempt unrecorded.
- **Together, a password oracle:** any holder of a session could test other
  accounts' passwords by the difference, and leave no `SignInFailed` behind.
- A's own mistyped password was a 500. Activation and reset kept their 400, but
  lost their `TokenRejected` record.

Reachable with a bearer header; the cookie transport would have made it routine.

**Resolved.** A bearer-authenticated identity-establishing command may execute
only when no caller is already established in the execution context. Such
commands declare `IBearerAuthenticatedCommand`, and `AuthenticationBehavior`
refuses them with `AuthenticationFailedException` before they start — outside
the transaction, so an execution-strategy retry of a command that did start is
unaffected. Nothing is verified, consumed, mutated or declared, so a correct and
a wrong password now produce the same answer. Plain `IAnonymousCommand`
(CRD-C2) is unchanged. The sign-in handler also establishes the actor before its
rehash and unlock, so it holds the invariant on its own. Recorded in
`docs/architecture.md` §11.

**Client consequence.** A signed-in browser ends its session before signing in,
activating or resetting — as any account, its own included.

**Where proved:** `BearerCommandUnderEstablishedCallerTests` (no mutation of the
credential or token, and a forced same-scope replay still succeeds),
`SignInCredentialMutationTests`, `AnonymousCommandTests`,
`BearerAuthenticatedCommandMarkerTests`, and the Host oracle test in
`AuthenticationEndToEndTests`.

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

## AUD-O11 — the retention baseline is a placeholder

**Rule:** every tenant inherits a minimum audit retention, in months, owned by
the release (RT3, AUD-12).
**State:** `AuditReleaseBaseline.MinimumRetentionMonths` is **120**, and that
number is not a decision. `AUD-O11` is parked with Regulatory; the value was
chosen so a development tenant reports a plausible policy, and the constant's
XML doc says so. Code and tests reference it by name, never by value, so
closing `AUD-O11` changes one line.
**Deferred to:** Regulatory's answer. Do not "correct" the number inside another
story.
**Where recorded:** `AuditReleaseBaseline` class doc.

## CRD-C2 is not first-tenant-ready: rate limiting is missing

**This is a blocking dependency, not an ordinary gap.** CRD-C2
(`RequestPasswordReset`) is implemented, tested and merged. It must not be
exposed to a real tenant until pipeline behaviour 11 exists.

**Why it blocks.** The command catalogue states CRD-C2 is *"Rate limited per
address and per IP"* — a **precondition** of the command, not a nicety. And
Notification's D-NOTIF-03 accepts the residual timing difference between the
issuing and non-issuing branches explicitly **because** behaviour 11
compensates for it:

> *"The residual timing difference between issuing and non-issuing paths is
> accepted and recorded; behaviour 11 (rate limiting) is the compensating
> control."*

Shipping CRD-C2 without behaviour 11 therefore does not make CRD-C2
unimplemented — it makes it **implemented but not deployable**, with an
accepted risk whose compensation is absent.

**What is and is not covered today.** The uniform response
(`PasswordResetRequestEndpointTests`) closes the *content* channel: unknown
address, external identity and known local identity return byte-identical
responses, and only the third writes anything. It cannot close the *timing*
channel, and nothing else does. Account lockout
(`FailedAttemptCount`/`LockedUntil`) guards password attempts against one
credential; it does nothing about repeated reset requests across many
addresses, or about an unauthenticated caller enumerating at speed.

**Scope when it is built.** Behaviour 11 applies to anonymous commands
generally — sign-in needs it too — so it is cross-cutting infrastructure rather
than part of CRD-C2. Note also that the host reads the client address from
`context.Connection.RemoteIpAddress` with no forwarded-headers handling, so
per-IP limiting behind a proxy would bucket every caller together until that is
addressed.

**Where recorded:** the class doc of `RequestPasswordResetCommandHandler`, and
a note on the endpoint in `AccountEndpoints`, so the dependency is visible to
whoever reads the code rather than only to whoever reads this file.

CRD-C3 (`ResetPassword`) completes the reset flow behind the same gate. It is
token-gated, so it is not itself an enumeration surface, but each accepted
token buys up to `PasswordHistoryDepth` adaptive-cost verifications.

CRD-C5 (`AdminResetPassword`) is **not** behind this gate: behaviour 11 covers
anonymous commands, and CRD-C5 requires an authenticated caller holding
`user.resetpassword`. Its link is still completed by CRD-C3 on the
`/reset-password` page, which no client serves yet, so it shares that
dependency and no other.

**Deferred to:** its own story, before the first tenant.

## CRD-C4 does not limit current-password attempts

**This is a known security gap, recorded by owner ruling.**

**Rule (CRD-C4, frozen):** *"Must not be usable to probe the current password —
constant-time comparison, generic error."*

**What is covered.** Verification is fixed-time inside the hasher. A wrong
current password, a locked credential and a session the command cannot act for
all return one generic message, and a locked credential still pays a real
derivation, so neither the message nor the timing distinguishes them. The
current password is verified before anything about the new one is judged.

**What is not.** Nothing limits how many times a holder of a valid session —
including a stolen one — may try. Behaviour 11 (rate limiting, not yet
implemented) is scoped to anonymous commands, and CRD-C4 deliberately does NOT
count a wrong current password toward `FailedAttemptCount`: doing so would add a
second producer of `AccountLocked`, decide threshold and reset semantics sign-in
alone owns today, and let a session holder lock the account — a security and
product behaviour CRD-C4 does not specify.

**Deferred to:** an explicit security decision, with catalogue review, on
whether authenticated password-change failures must count toward lockout or be
rate limited.
**Where recorded:** the class doc of `ChangePasswordCommandHandler`.

## A5 closed — CRD-C4 revokes other sessions, which amends two catalogues

**Decision (owner ruling, closes UM open decision A5 as option (b)).** A
password change revokes every other session of the identity whose credential
changed that would still pass the per-request session check. The session
making the request survives; sessions of the user's other identities are
untouched. Each revocation is its own `SessionRevoked` — reason
`PasswordChanged`, revoked by the user, caused by the `PasswordChanged` record
— and `PasswordChanged.otherSessionsRevoked` says whether any were.

**The inconsistency this exposed.** The frozen documents disagree once A5 is
resolved this way:

- the Audit Event Catalogue lists `SessionRevoked`'s producers as SES-C3,
  SES-C4, USR-C4, IDN-C3 and OPR-C2 — not CRD-C4;
- the UM command catalogue lists only `PasswordChanged` in CRD-C4's audit-events
  column, and AUD-11 makes that column the verbatim source of event codes;
- yet `PasswordChanged`'s own payload field `otherSessionsRevoked` presumes A5
  revokes sessions.

**What the code does.** The code catalogue carries no producer list, and
`SessionRevoked` already permits an Authenticated origin, so no seed changes.
`AuditDeclarations` lists `SessionRevoked` for `ChangePasswordCommand`, with a
comment naming it an amendment.

**Change control outstanding:** add CRD-C4 to `SessionRevoked`'s producers in
the Audit Event Catalogue; add `SessionRevoked (n)` to CRD-C4's audit-events
column and record A5 as closed in the UM command catalogue. Neither workbook is
edited by the implementing story.

## Session revocation — reason semantics and SES-C4 command split (change control)

Recorded by owner ruling while implementing SES-C3 and SES-C4. **No workbook is
edited**; each item below is outstanding change control against the frozen
documents.

**D2 — the two reason fields (R1, locked).** `user_session.RevocationReason` is
a controlled revocation code describing why the session was terminated. Audit
`audit_record.Reason` is the human explanation supplied by the command. For
`SessionRevoked`, the controlled code is persisted in the session Before/After
representation; the audit Reason remains the explanatory text. `SessionRevoked`
remains BeforeAfter-shaped and gains no payload. SES-C3 and administrator SES-C4
use `AdminRevoked`; self SES-C4 uses `SignOutEverywhere`. Self SES-C4 accepts an
optional explanation; when omitted, the command supplies `Signed out of all
sessions by the account holder` as the audit Reason.

**The vocabulary is not a database invariant.** `RevocationReason` is a
normative controlled vocabulary, but V1 does not enforce it with a database
CHECK constraint: the column is plain TEXT. Vocabulary enforcement remains an
application/domain responsibility (the codes are held in `SessionRevocations`
and in the handlers that predate it) until separately amended.

**Change-control items:**

1. **SES-C4 command split.** SES-C4 is split into two concrete commands because
   its frozen self/admin authorization contract cannot be represented by one
   fixed-permission command under pipeline behaviour 3. The command catalogue
   needs two command identities: `RevokeUserSessions` (administrator,
   `session.revoke`) and `SignOutEverywhere` (self). SES-C4 remains the
   capability grouping.
2. **SES-C4 self `Reason`.** Changed from a required input to an optional input,
   with the default explanation above.
3. **`SessionRevoked` Audit note.** *"Reason = RevocationReason"* should read that
   the audit Reason is the command's explanation and the controlled
   `RevocationReason` is carried in Before/After — consistent with the
   `audit_record.Reason` column's own definition ("a human explanation … never a
   code").
4. **UM `RevocationReason` vocabulary.** Formally establish the controlled
   vocabulary. Codes in use: `Logout` (SES-C2), `PasswordChanged` (CRD-C4),
   `AdminRevoked` (SES-C3, administrator SES-C4), `SignOutEverywhere` (self
   SES-C4), `UserDeactivated` (USR-C4, not yet implemented). The entity model's
   list currently ends open ("…").
5. **USR-C4 spelling.** Its command steps write `RevocationReason='User
   deactivated'` for role assignments and `'UserDeactivated'` for sessions;
   reconcile before USR-C4 is implemented.
6. **CRD-C4 departure.** CRD-C4 writes the code `PasswordChanged` into both the
   session's `RevocationReason` and the audit Reason. That contradicts R1, since
   the audit Reason should be an explanation. Known departure, deliberately not
   changed by SES-C3/SES-C4; reconcile in a follow-up.

## The unlock permission disagrees between the Audit and UM specifications

**The discrepancy.** Audit operator walkthrough §18.5 refers to
`credential.unlock` and describes it as operator-eligible, while the UM
permission catalogue and seed define `user.unlock` as human-only.

**What the code does.** CRD-C6 (`UnlockAccount`) follows the current UM
permission contract: `user.unlock`, `RequiresHumanActor = true`, granted through
the existing administrator authorisation model. No permission, seed or workbook
is changed.

**Deferred to:** resolve the discrepancy before AM-01/A8 introduces
PlatformOperator authorisation — both the permission's name and whether it is
operator-eligible.

## Password reuse cannot yet prove the algorithm column is honoured

**Rule (CRD-C3, frozen):** *"Reuse-check must compare against each history row
using ITS stored algorithm, not the current one."*

**What is proven.** `ResetPasswordCommandHandler` verifies every row with
`Verify(newPassword, row.PasswordHash, row.PasswordAlgorithm)`. The stored hash
is self-describing — `$pbkdf2-sha256$i=<iterations>$<salt>$<key>` — so each row
is verified at its own work factor and salt, never re-derived with current
parameters. `ResetPasswordIntegrationTests` proves it with a history row stored
at 1,000 iterations, and the mutation campaign kills both "hash afresh and
compare" and "verify at current iterations".

**What is not.** `PasswordHasher` implements one scheme, and `storedAlgorithm`
only decides `NeedsRehash`; it never changes `IsValid`. So passing the current
marker instead of `row.PasswordAlgorithm` is behaviourally indistinguishable
today, and no test can catch it. That mutant is excluded from the campaign
rather than reported as killed.

**Deferred to:** the release that introduces a second hashing scheme — which
must add the test this cannot have yet.

## MustChangePassword is recorded but not enforced

**Rule (entity model):** `credential.MustChangePassword` is *"Set after an
administrator-initiated reset."*

**What exists.** CRD-C5 sets it to `true` when it issues the token, and CRD-C3
sets it to `false` when the user completes a reset, because the user chose that
password. `AdminResetPasswordIntegrationTests` proves both transitions. CRD-C4
(`ChangePassword`) also clears it, as its catalogue row requires, and
`ChangePasswordIntegrationTests` proves that.

**What does not.** SES-C1 never reads the flag. Until the reset link is used,
the user's existing password still signs them in with no restriction. The
specification says when the flag is set, not what it means at sign-in —
refusing sign-in, or issuing a session that can only change the password, are
both authentication and session designs no story has defined.

**Deferred to:** its own story, which must decide the sign-in behaviour. The
owner ruled explicitly that CRD-C5 establishes the state and does not change
SES-C1.
**Where recorded:** the class doc of `AdminResetPasswordCommandHandler`.

## A failed activation mail has no recovery command

**The contradiction.** Notification's walkthrough (§11.4, §11.5) names CRD-C5
as the administrator's remedy when an activation mail fails or is abandoned —
*"a new token, a new row"*. CRD-C5 cannot be that remedy:

- it issues a `PasswordReset` token (N14), and CRD-C3 refuses an identity
  without a credential;
- the target of a failed activation has no credential — that absence *is* the
  pending-activation state (inv. 15);
- `AdminPasswordResetIssued` requires a Credential as its primary entity, so
  there is no audit record it could write.

CRD-C5 therefore refuses such a user, by ruling, rather than quietly issuing an
activation token and becoming a second activation path.

**Consequence.** A user whose activation token expires unused, or whose
activation mail was never delivered, cannot currently be recovered by any
command. USR-C1 cannot be re-run for the same address or username.

**Deferred to:** a separate story — a command that reissues an activation token
(its own permission, `TokenIssued`, `TokenInvalidated` per UT5, and an
`AccountActivation` notification) — with the Notification walkthrough's wording
corrected through that specification's own change control.

## N14(b) — no architecture test proves NOT-P1 has no public entry point

**Rule (Notification §10.1):** *"NOT-P1 has no public entry point: no type
outside the pipeline assembly references it, and the only call sites are the
three issuing commands' token-issuance steps."*

**What exists.** N14 itself — the type/token agreement — is enforced in the
single writer since CRD-C5: `INotificationEvents.Emit` takes the issued token,
and `ScopedNotificationEvents` refuses any pairing other than
AccountActivation↔Activation, PasswordReset↔PasswordReset and
AdminPasswordReset↔PasswordReset, with the unit test the specification requires.

**What does not.** Nothing asserts the call-site half. `INotificationEvents` is
a public interface, and a fourth caller could inject it and declare a
notification for a token it did not just issue; N14 would still hold for that
call, but N2(b)'s and N14's "only the three commands" premise would not.

**Deferred to:** its own story, which must decide how "outside the pipeline
assembly" is expressed and asserted (reflection over references, or an
analyzer).

## Activation tokens could not be delivered — RESOLVED

**State:** resolved. USR-C1's plaintext activation token had no consumer, so a created account could never be activated. Notification N1 built delivery and the web client's `/activate` page consumes the link, closing the loop end to end.

**How the trap was avoided.** The concern recorded here was that a queued row carrying an activation link necessarily carries the plaintext token — the exact disclosure that storing only a hash exists to prevent (UT7). It is not queued. `CreateUserCommandHandler` emits the plaintext to a **command-scoped in-memory collector**, and the persisted notification row carries no payload (D-NOTIF-01, D-N1-07). Nothing writes the plaintext to a column, and User Management still references no notification table.

**What resolved does not mean.** Delivery requires mail configuration. With none, the pump runs sweep-only and every notification records honestly that no attempt was observed — so on a default development host the token is still not delivered, and the account stays unactivated. `./bootstrap.sh` is the deliberate exception: it writes the first administrator's token to `.secrets/bootstrap.token` rather than emailing it, because the system has no users to mail from yet.

**Still owned by Notifications:** User Management does not own email infrastructure (`docs/architecture.md` §8).

## The audit catalogue has no working per-event versioning

**Rule (ET8):** `audit_event_type` is keyed `(code, version)`, and `audit_record`
carries a foreign key to `(event_type, event_version)` — so an event type's
definition can be revised by adding a new version beside the old one, leaving
existing records pointing at the definition they were written under.

**State:** the mechanism does not work, because nothing can set a per-event
version. `EventTypeSeed.Version` returns `AuditEventCatalogue.Version`, a single
constant shared by all 49 seeds, so raising it re-versions the entire catalogue
at once: 49 new rows at the new version, all 49 previous rows retired by
`RetireAbsentEventTypesAsync`, and every subsequent record written against the
new pair. There is no way to revise one event type's definition.

**Why it has not bitten yet.** Correcting `PermissionCatalogUpdated` for PRV-C2
was safe only because no emitter has ever existed for it, so no deployed record
can reference `(PermissionCatalogUpdated, 1)`. That argument does not generalise:
the same correction applied to `UserCreated` would silently restate the meaning
of every record already written under it.

**Deferred to:** AUD-C3, the release migration the version column exists for and
which is not yet implemented. Recorded here so the next event-definition
correction does not discover it the hard way.

## PRV-C2 — a provisioned tenant never receives new permissions — RESOLVED

**State:** resolved. `Ligature.CatalogueSync` runs as `migration_role` between
the audit schema and the host, and reconciles the release's permission, role and
grant catalogue with the database on every deployment. The requirement is
specified in full under **Requirements → PRV-C2 — Catalogue synchronisation**
above.

**The defect it closed:** `PlatformProvisioner.ProvisionAsync` returns as soon as
the System actor exists, before it reaches any catalogue seeding, so adding a
permission to `GetPermissionSeeds()` changed nothing for any existing tenant
database — silently. `CatalogueDriftTests` detected the divergence; nothing
fixed it, and the only remedy was hand-written SQL. Those tests remain, as the
POSTCONDITION of synchronisation rather than a report of an incurable state.

**`ProvisionAsync` is unchanged**, which was the point. Catalogue evolution was
removed from the first-provision lifecycle rather than bolted onto it.

**The security decision it carried:** `migration_role`, which held nothing at
all on the audit trail, gained INSERT — and only INSERT — on
`audit.audit_record` and `audit.audit_entity_ref`, plus SELECT on
`audit.audit_schema_version` so it can verify its own precondition. It can
append immutable evidence and can never afterwards read, alter or remove it
(script 005).

**PE2's premise is still not met.** `permission` is not yet writable only by a
migration role; that enforcement is tracked in the enforcement-layer entry below
and did not block this.

## Enforcement layers G1 and PE2 are not fully implemented

**Originally:** none of G1, G4, PE2, PH3 or SP2 held at the database level. No
`GRANT`/`REVOKE` statement and no trigger existed in any migration, and the
domain layer was the only thing enforcing them.

**Now largely closed, in two steps.**

`AddUserManagementPrivilegeModel` grants the ordinary tables to `app_role` and
`provisioning_role` in G1's shape — `SELECT`, `INSERT`, `UPDATE`, and no
`DELETE` for anyone. That closed G1's **grant** half.

`AddUserManagementImmutabilityTriggers` adds eleven `BEFORE UPDATE` guards, one
per User Management table, each `ENABLE ALWAYS` and each raising `P0001`. That
closes **G4**, **PH3** and **SP2**:

- **G4** — 72 immutable columns rejected on change, 11 write-once columns
  admitting `NULL` -> value once and nothing after. `AU7` rides along: the
  System actor row refuses any update at all, whatever the column's class.
- **PH3** — `password_history` refuses every `UPDATE`, no-ops included.
- **SP2** — `security_policy` likewise. Both tables are entirely immutable by
  classification, so a blanket refusal states insert-only and append-only in
  their own terms rather than leaving them to emerge from a column comparison.

The classification map lives in `UserManagementImmutabilityDriftTests`, which
holds it against both the live schema and the deployed function bodies — so a
column added later cannot silently escape G4, and a column omitted from a
hand-written `ROW()` list fails a test rather than going unnoticed.

**Still outstanding:**

- **G1** — `user_session` is named as the eventual purge exception; no purge
  exists, so no `DELETE` is granted for it either. Revisit when one is built.
- **PE2** — `permission` is granted `INSERT`/`UPDATE` to `provisioning_role`
  rather than being writable only by a migration role. G4 protects that table's
  `id`, `code`, `created_at` and `created_by`, but its six Release-controlled
  columns stay writable by design: that is a privilege rule, not a trigger one.

**Deferred to:** unscheduled, both.

## G4 constrains how an already-ended assignment may be revoked

**Rule:** `user_role.effective_to` is Write-once (frozen workbook), while the
same sheet's `revoked_at` note says "Revocation also sets EffectiveTo = now".
For an assignment created *with* an end date those two collide, and
`AddUserManagementImmutabilityTriggers` resolves them in favour of the property
both exist to protect: an authorisation window may close early, but may never
widen or reopen.

**Consequence:** `UserRole.Revoke` sets `EffectiveTo = revokedAt`
unconditionally. Revoking an assignment whose `effective_to` has **already
passed** therefore moves the value forward, and the database refuses it with
`P0001`.

**State:** not reachable today. The only assignment path,
`BootstrapAdministratorProvisioner`, passes `effectiveTo: null`, and agents —
for whom a finite end date is mandatory under UR8 — cannot be created in V1.

**Not worked around.** The domain is unchanged: no clamping to
`LEAST(effective_to, revoked_at)`, no special case in `Revoke`. The right
answer is probably that revoking an assignment which already ended is
meaningless and should be refused in the domain, but that is a decision for the
story that builds the revoke command, not for the story that added the trigger.

**Deferred to:** the role-revocation command, whenever it is built.

## CR1 — no unique constraint on credential.user_identity_id — RESOLVED

**Rule:** one credential per identity, 1:0..1.

**Was:** the index on `(user_identity_id, identity_type)` was not unique, so two
credential rows for one identity were possible. The composite foreign key
pinned `identity_type` to the identity and constrained cardinality not at all.

**Resolved** by `EnforceOneCredentialPerIdentity`, which makes that same index
unique. Unique on the pair rather than on `user_identity_id` alone because the
two are equivalent here — the foreign key forces `credential.identity_type` to
equal the identity's own, and an identity has exactly one — so it states the
rule exactly and adds no second index over overlapping columns.

Scoped separately from the consumption-contract correction it shipped
alongside, and for a different reason: that closed the one path which could
produce a second row, this closes the shape of the failure whatever path is
invented later.

**No data-cleanup step.** PostgreSQL validates every existing row as it builds
a unique index, so an environment holding duplicates fails the migration —
the wanted outcome. There is no way to know which of two password hashes its
owner uses, so deleting one silently would be worse than stopping.

## Integration-test fixtures can outlive a failed dispatch

**State:** the persistence integration tests assign their fixture handle outside the `try`, so when a dispatch throws, the `finally` cleanup never runs and rows are left in the shared database. Two such rows were found and removed during CRD-C1 validation.

Harmless to correctness — every test scopes by its own ids — but it accumulates.

**Intended fix:** establish the cleanup scope BEFORE any operation that can throw, rather than wrapping more code in another `try`/`finally`.
**Deferred to:** unscheduled.

## Execution-strategy replay leaves rolled-back entities looking persisted

**State:** when the execution strategy replays a unit of work after a `SaveChanges` failure, the change tracker is not reset. Entities the failed attempt inserted stay `Unchanged` — EF believes they are persisted, so the replay does not re-insert them — while the rollback has already removed the rows. Entities the failed attempt merely added stay `Added` and are written on the next attempt, still referencing the rolled-back rows.

Observed while implementing N1 Phase B, with the change tracker dumped at each save across a forced replay:

```text
attempt 1, inner save   User:Added, UserIdentity:Added, UserToken:Added
attempt 1, outer save   Notification:Added, User:Unchanged, UserIdentity:Unchanged, UserToken:Unchanged   ← fails, rolls back
attempt 2, inner save   UserIdentity:Added, User:Added, Notification:Added, UserToken:Added,
                        User:Unchanged, UserIdentity:Unchanged, UserToken:Unchanged
```

The leftover `Notification` is written against the leftover `UserToken`, which is never re-inserted, and the foreign key fails with `23503`.

`UnitOfWork` already states the requirement — "a retry re-invokes the operation on a change tracker that still holds the previous attempt's entries, so the operation must be idempotent in its database effects" — but nothing establishes tracker-safe replay, and the User Management handlers are not idempotent in that sense: a replay mints a fresh token id and strands the previous one.

This is a platform correctness gap, not a Notification defect. Notification exposed it by being the first dependent entity added to the same persistence graph; the notification path deliberately does not work around it, since detaching entities or clearing the tracker from module code would make a module responsible for a platform invariant.

**Not currently reachable:** `EnableRetryOnFailure` is off, so no replay occurs in production. This becomes live the day execution-strategy retries are enabled.

**Where recorded:** `NotificationEmissionBehaviorTests` class doc, which is why the per-attempt isolation invariant is proved at the behaviour level rather than through a replayed transaction.
**Deferred to:** unscheduled; it must be resolved before retries are enabled.

## Notification mail composition has two recorded RFC limits

**State:** the Gmail transport encodes the subject and the sender display name
as a single RFC 2047 encoded-word. RFC 2047 §2 caps an encoded-word at 75
characters, so a subject or display name longer than roughly 47 bytes is
non-conformant and a strict receiver renders the raw `=?utf-8?B?...?=` to the
user. The shipped activation subject is well inside that, and the templates are
release-controlled code assets rather than free text, so the limit cannot be
crossed without a code change.

**Deliberately not fixed:** a general encoded-word splitter is real work with no
current consumer. The constraint is that release-controlled templates stay
within the envelope.

**Deferred to:** the slice that first needs a long or non-ASCII subject — N2's
reset templates are the likeliest trigger.

## One notification send can take twice the transport timeout

**State:** the transport timeout is applied per HTTP request, and a send that
finds no cached access token makes two requests — the token mint and the message
send. Worst case for one send is therefore `2 × TransportTimeout` rather than
one, which understates the per-send term of the §5.4 grace-window inequality.

Not currently a violation of anything claimed: §5.4 is explicitly unclaimed
while the hard transaction bound `T` is unenforced (AUD-O17), and the 10-second
timeout is provisional pending the Phase F measurement.

**Deferred to:** Phase F, which measures the terms and fixes the constants. The
measurement must use the two-request worst case, not the cached-token case.

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
