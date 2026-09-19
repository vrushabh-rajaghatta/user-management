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

**Query IDs** — `<AREA>-Q<n>`, e.g. `USR-Q2`. The same areas as commands; `Q` means query and `C` means command, so a read is not forced into the command catalogue. Numbered independently of the area's commands: `USR-Q2` and `USR-C2` are unrelated. **Query IDs follow the frozen UM command catalogue's *Queries* sheet**, as every other query ID here already did (`AUT-Q2`, `AUT-Q5`). See *Reconciliation: USR-Q1 and USR-Q2* below.

### Reconciliation: USR-Q1 and USR-Q2

**Decided 2026-09-18 by owner decision (R1–R5).** This corrects an ID collision.

The frozen UM command catalogue defines `USR-Q1` as **GetUser**, the single-user detail read, and `USR-Q2` as **SearchUsers**, *"the main administration list"*. On 2026-09-17 this repository assigned `USR-Q1` to its user list, the first query given an ID here, without checking the catalogue's *Queries* sheet. Every other query ID in this document follows the catalogue. `USR-Q1` was the only one that did not.

The resolution:

| ID | Means | Status |
| --- | --- | --- |
| `USR-Q1` | **GetUser**, as the catalogue defines it | This reconciliation did not design it (R5). **Narrow v1 since USR-C2** (userId, firstName, lastName, displayName): see *USR-C2 — Update User Profile, and USR-Q1 GetUser (narrow v1)*. The catalogue's other fields each need their own evidence. |
| `USR-Q2` | **SearchUsers**: the Users list, `GET /api/users` | Implemented as a deliberately narrow first version: no filtering or search (owner decision, #51), and row fields admitted by the evidence rule. Being narrower than the catalogue's query does not make it a different query. |
| `USR-Q3` | Whatever the catalogue defines (GetUserAccessSummary) | Not used here, and not repurposed. |

**Rule 2, and why this is a recorded exception.** Rule 2 says *"Do not reuse an existing ID for a different requirement."* Returning `USR-Q1` to GetUser means that `USR-Q1` in this repository meant one thing before this date and another after it. The catalogue's assignment is older and frozen; this repository's was the collision. So the exception is recorded here, with the mapping below, rather than made quietly.

#### Historical mapping

> **Historical references to `USR-Q1` in commits, branches, PRs, and persisted test/audit data refer to the user administration list implemented during the pre-reconciliation period. That implementation is now identified as `USR-Q2 SearchUsers`. `USR-Q1` thereafter refers to the catalogue-defined `GetUser`.**

That covers:

- the commits and PRs #52, #54, #57 and #63, and the branches `feature/usr-q1-user-list` and `feature/usr-q1-activation-pending`;
- reason text written into the shared test database and its audit trail by earlier test runs, such as *"USR-Q2 P6."*. Audit records are immutable and are not rewritten.

> **Historical amendment mapping: The documents previously labelled `USR-Q1 Amendment 1` and `USR-Q1 Amendment 2` amended the repository's user-list implementation. Following ID reconciliation, those amendments are understood as amendments to `USR-Q2 SearchUsers`. They do not amend the catalogue-defined `USR-Q1 GetUser` contract.**

In particular, `status` (Amendment 2) and `activationPending` (Amendment 1) are fields of the **list** row. GetUser has no contract yet, and acquired neither.

**What was renamed, and what was not.** Every mention in current files was renamed: this document, `docs/architecture.md`, code comments and test strings. History was not: git commits, branch names, PR titles and persisted audit records stay as they were, and the mapping above is the bridge. Nothing in the code named the ID (no class, method, test, route or field), so no behaviour changed.

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

## PRV-C1 — Provisioning

**Status:** Partial entry. PRV-C1 predates this catalogue and is **not back-filled here**. Its definition is the frozen command catalogue's row *PRV-C1 ProvisionUserManagement*, whose inputs include the *"seeded role & permission catalog"*. This entry records only an amendment to that seed.

The frozen UM specification defines the structure of roles and permissions, not their entries (spec §1.2: *"This document defines the structure and ownership, not the entries"*). The three seeded roles, and the permissions each confers, were an implementation decision recorded in `PlatformProvisioner`. So amending them does not reopen a frozen contract. Rule IDs `RP1`–`RP6` belong to the frozen specification, so this amendment takes none.

### Amendment 1 — Security administrator role composition

**Decided 2026-09-18 by owner decision.** It closes the role-composition gap AUT-Q2 left open.

#### Decision

> **`security-administrator` includes `user.read`.** This is the minimum user-directory visibility required to exercise its role-management responsibility through the Users UI. The Users table remains the standard Users view, and each action is still gated by its own permission.

#### Requirement

The system-seeded `security-administrator` role SHALL include `user.read` in addition to its existing permissions.

The role's composition becomes, exactly:

| Role | Permissions |
| --- | --- |
| `user-administrator` | *unchanged:* `user.create`, `user.read`, `user.update`, `user.deactivate`, `user.reactivate`, `user.resetpassword`, `user.unlock`, `identity.read`, `identity.manage`, `session.read`, `session.revoke` |
| `security-administrator` | `role.read`, `role.manage`, `role.grant`, `role.revoke`, `securitypolicy.read`, `securitypolicy.change`, **`user.read`** |
| `access-reviewer` | *unchanged:* `accessreview.read`, `user.read`, `role.read`, `identity.read`, `session.read`, `securitypolicy.read` |

#### Why

The role's own seeded description is *"Defines what roles mean and who holds them, and maintains the tenant's security policy."* Deciding who holds a role requires knowing who the users are. Without `user.read`, a user holding only this role holds `role.grant` and `role.revoke` but cannot reach the Users table, where *Manage roles* lives.

This grants **visibility, not user-management capability**. The separation of the three roles stays clean:

- `user-administrator` manages users.
- `security-administrator` manages authorization and security policy, including who holds roles.
- `access-reviewer` has read-only visibility across users, roles, identities, sessions and policy.

The security administrator gains no `user.create`, `user.update`, `user.deactivate`, `user.reactivate`, `user.resetpassword`, `user.unlock`, `identity.*` or `session.*`.

Alternatives rejected:

- **A separate role-management entry point** gated by `role.read`. It still has to let the caller pick a user. A user picker that does not require `user.read` would be a second, weaker path into the user directory.
- **Documenting that operators should grant both administrator roles.** A tenant that sets up a pure security administrator would get a role whose main job is unreachable. That is a defect, recorded instead of resolved.

#### What does not change

- **The permission catalogue.** It still has 19 permissions and 3 roles. This changes role composition, not the catalogue: the number of seeded grants goes from 23 to 24.
- **No domain, migration, audit-catalogue or endpoint change.** `AuditEventCatalogue.Version` is not bumped.
- **Agent assignability.** `user.read` is not `RequiresHumanActor`, and `security-administrator` already holds human-only permissions (`role.grant`, `role.manage`, `role.revoke`, `securitypolicy.change`). It was not agent-assignable before and is not now. RP6 is not engaged.
- **PRV-C3.** The bootstrap administrator still holds both `user-administrator` and `security-administrator`.
- **The Users view.** There is no special view for a security administrator. The Users page is gated by `user.read`, and each row action by its own permission. Page-level and action-level access are independent.

#### One-way: accepted

> Adding `user.read` to the system-seeded `security-administrator` role is a monotonic permission-catalogue change. Existing tenants receive the permission through catalogue synchronization. Removal is not supported until role-permission mutation is implemented in Slice 4.

Concretely:

- A tenant provisioned before this amendment receives the grant on its next deployment, through PRV-C2's permitted mutation M3 (*insert a role-permission grant*). The grant is attributed to the System actor and recorded in that run's `PermissionCatalogUpdated` record.
- A later release that dropped the grant from the seed would be **refused** as `GrantMissingFromSeed` (PRV-C2, F5), and would commit nothing.
- The only command that retracts a grant is AUT-C8 *RemovePermissionFromRole*, which is Slice 4 and not implemented. Even then, the frozen model's RO3 says tenant administrators cannot modify system roles. So retracting this grant is a release-level decision with its own change control, not a tenant action.

The owner accepts this explicitly. It is a consequence of the provisioning model, not a reason to avoid the correction.

#### Acceptance Criteria

- **S1** The seed grants each of the three roles exactly the permissions in the table above. The test pins all three compositions, so a mutation to any one fails it. It asserts 19 permissions, 3 roles and 24 grants.
- **S2** A freshly provisioned tenant stores the amended composition: `security-administrator` holds an active `user.read` grant, granted by the System actor.
- **S3** In a tenant provisioned before this amendment (simulated by removing only that grant), catalogue synchronisation inserts exactly that one grant and nothing else, and converges. A second run inserts nothing.
- **S4** One-way: an active database grant that the seed does not list is refused as `GrantMissingFromSeed`, naming it, and nothing is committed. This also closes a gap in PRV-C2's own tests, where F5 was untested.
- **S5** Through the pipeline, a caller holding only `security-administrator` is authorised for USR-Q2, which requires `user.read`.
- **S6** Over HTTP, a caller holding only `security-administrator` gets `200` from `GET /api/users`. The same caller is refused by each of the following. A permission refusal is `400` with a *"does not have permission"* message (Known Gaps, *Authorization failures are not distinguishable from validation failures*), and the test asserts the message, so a validation `400` cannot pass for a refusal:
  - `POST /api/users` (`user.create`);
  - `POST /api/users/{id}/password-reset` (`user.resetpassword`);
  - `POST /api/users/{id}/activation-link` (`user.create`);
  - `POST /api/users/{id}/sign-out-everywhere` (`session.revoke`);
  - `POST /api/identities/{id}/unlock` (`user.unlock`);
  - `POST /api/sessions/{id}/revoke` (`session.revoke`).
- **S7** In the web client, a visitor holding exactly the `security-administrator` permissions sees Administration and the Users page. They get no Create user control, and every row offers *Manage roles* and no other action.
- **S8** Browser: a user holding only `security-administrator` in the dev environment sees the Users list, is offered only *Manage roles*, and can grant and revoke. Each state change needs owner approval.

#### Notes

- Existing databases converge through the existing tool, `dotnet run --project src/Tools/Ligature.CatalogueSync`, or the `catalogue-sync` Compose service. `CatalogueDriftTests` is the postcondition, and it fails against any database that has not yet been synchronised with this release.
- No role-composition test existed before this amendment: the drift tests compare the database with the seed, so a deleted seed line would have gone unnoticed. S1 closes that.

---

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

## USR-Q2 — User List Query

**Requirement ID:** `USR-Q2` SearchUsers, as the UM command catalogue defines it. It was assigned `USR-Q1` by the owner on 2026-09-17 and renumbered by the reconciliation on 2026-09-18 (*Reconciliation: USR-Q1 and USR-Q2*). History before that date calls it `USR-Q1`.

**Status:** Approved and frozen — the row projection, the endpoint and identifier semantics, pagination, sorting, filtering, and the implementation contract. All decided 2026-09-17 by owner decision. Implemented (#52).

**Amendment 1 (2026-09-18):** adds one derived field, `activationPending`, to the row. See *Amendment 1 — `activationPending`* below. It changes P1, the excluded-fields table and *Lifecycle status*, each explicitly (*Rules*, 4). Nothing else in this contract changes. Implemented by `UserListReader`.

### Requirement

A caller holding `user.read` can list the tenant's human users. Each row exposes the minimum a person needs to recognise a user and safely start an operation on them — nothing more.

The projection was built up from an empty row, not cut down from the `app_user` and `user_identity` models. A field is in the row because a supported operation needs it, never because a column exists. The authorization and audit posture are already decided in `docs/architecture.md` §11: the read declares `Required("user.read")`, its handler enforces it, and it is not audited.

### The row

| Field | Role | Why it is present |
| --- | --- | --- |
| `UserId` | **identity and routing** | Every operation a row can start is addressed by it: `POST /api/users/{userId}/password-reset` and `POST /api/users/{userId}/sign-out-everywhere` |
| `DisplayName` | recognition | A list of identifiers is not usable by a person |
| `Email` | disambiguation | `DisplayName` is not unique. Once the list is the entry point for password reset and sign-out-everywhere, two users with the same display name must be distinguishable before acting on either |
| `ActivationPending` | presentation | Amendment 1. Whether the user has never activated, so a client offers *Resend activation link* (CRD-C7) only where it can succeed. Informational; never authorization |
| `Status` | presentation | Amendment 2. `Active` or `Inactive`, the stored lifecycle status, so a client offers the lifecycle actions (USR-C4, USR-C5) where they apply and marks inactive users. Informational; never authorization |

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
| ~~`Status`~~ | **Admitted by Amendment 2** (USR-C4 and USR-C5 made it meaningful). See *Lifecycle status* below |
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

**`UserIdentityId` is not added to the row because unlock needs it.** That would design the read around a command whose contract says it is identity-scoped. Making unlock reachable from a user list needs its own decision. *(Later: unlock is reached from the **User detail page**, where the administrator chooses the identity from IDN-Q1's list. It is still not reached from a row. See* IDN-Q1 GetUserIdentities and Unlock on the User detail page*.)*

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
    { "userId": "…", "displayName": "…", "email": "…", "activationPending": false }
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

> **Superseded by Amendment 2.** USR-C4 and USR-C5 made status meaningful, so `status` is in the row; see *Amendment 2 — `status`*. The text below is the original reasoning. It was correct while nothing could deactivate a user.

`Status` is **not** in the row. Amendment 1 exposes exactly one derived lifecycle fact, `activationPending`, and nothing more; see below.

The `user.read` permission is described as *"View user accounts and their current lifecycle status."* That description states the authority `user.read` grants; it is **not** the response schema of every read that uses it:

> `user.read` authorizes viewing user accounts and their lifecycle information where a particular read exposes such information. This user-list projection does not expose lifecycle status.

Exposing `app_user.status` today would be misleading rather than informative. Nothing can deactivate a user, so the value would read `Active` in every row, while the lifecycle states that are meaningful — pending activation and locked — are derived from other tables and would be absent from it. A later read that exposes lifecycle information decides what "status" means there, and whether `user.read` is sufficient for it.

No permission catalogue change and no audit catalogue change accompany this.

### Amendment 1 — `activationPending`

**Decided 2026-09-18.** It closes the UI gap CRD-C7 left: which rows a client offers *Resend activation link* on. CRD-C7 rejected offering the action on every row and relying on refusals.

#### Definition

> **`activationPending` is `true` when the listed user holds at least one local identity and no credential on any identity, and `false` otherwise.**

No credential is the pending-activation state (inv. 15). A credential exists only for a local identity (enforced by the credential's composite foreign key), so *no credential on any identity* and *no local identity holds a credential* are the same condition.

The *at least one local identity* clause keeps a user who could never activate (no local identity, today unreachable through any command) from reading as pending. For every user a current command creates, the two readings coincide: USR-C1 creates exactly one local identity, and CRD-C1 gives it the credential.

#### What it is, and what it is not

- **Presentation data.** It tells a client whether to *offer* CRD-C7. It is **not authorization and not eligibility**: CRD-C7 re-verifies every rule itself (human, active, email, exactly one active local identity, no credential), and a `true` never makes a refused target eligible.
- **Not a promise that CRD-C7 will succeed.** A pending user may still be ineligible, for example with two local identities or no email. The command refuses them, as it would anyway.
- **A value at the moment of the read**, like every other field. Nothing snapshots it. A user who activates after the page was read still shows `true` until the next read, and CRD-C7 refuses them.
- **Not a lifecycle status.** It does not replace, imply or encode `Status`, and it does not say whether the user is locked or active.
- **`false` means only "not pending".** It does not mean the user has a usable password, can sign in, is unlocked or is otherwise fully active. A client infers nothing from it beyond the negation of the definition above.
- **No credential detail.** It reveals only whether a credential exists. It exposes nothing about the credential: not its hash, algorithm, age, lockout state, failed attempts or `MustChangePassword`. Nor does it reveal token state (whether a link exists, has expired, or was ever mailed).

#### Pagination, order and filtering are unaffected

The field is computed for the rows a page returns. It plays no part in which rows are returned or in what order:

- the order stays `DisplayName` under `"unicode"`, then `UserId`;
- offset, `pageSize + 1`, `hasMore` and the absence of a total are unchanged;
- no parameter filters by it or sorts by it. `?activationPending=true` is an unknown parameter and is ignored (*Request parameters*).

#### Authority: `user.read` is sufficient

*Lifecycle status* left it to the read that exposes lifecycle information to decide whether `user.read` suffices. For this fact, it does. `user.read` is described as *"View user accounts and their current lifecycle status"*, and whether an account has been activated is exactly that. Every `user.read` holder sees the field, including `access-reviewer`, which cannot act on it. No permission catalogue change.

#### The client rule

A client offers *Resend activation link* on a row **only when** `activationPending` is `true` **and** the caller holds `user.create`. Otherwise the action is absent, not merely disabled.

A client **does not offer** *Reset password* (CRD-C5) on a row whose `activationPending` is `true`: CRD-C5 refuses a user with no credential, so the action could only fail. *Sign out everywhere* is unaffected; for a pending user it ends no sessions and succeeds.

Stated as the client's whole logic, with no broader lifecycle inference:

```text
activationPending  and  holds user.create         →  offer Resend activation link
not activationPending  and  holds user.resetpassword  →  offer Reset password
holds session.revoke                                →  offer Sign out everywhere
```

`user.read` shows the field; `user.create` is what allows the resend. Those are separate concerns, so an access reviewer sees that a user is pending and is offered nothing.

Both are presentation rules. The commands' own refusals are unchanged, and remain the authority.

#### The evidence (*Adding a field later*)

1. **Concrete use.** CRD-C7's action is offered only where it can succeed, and CRD-C5's is withheld where it cannot.
2. **Data exposure.** One boolean per human user: whether the account has been activated. It carries no credential detail and no token state, and every holder of `user.read` is already authorised to view lifecycle status.
3. **Necessity.** Without it a client must either offer both actions on every row and rely on refusals (rejected in CRD-C7), or make one request per row, which does not exist and would be worse.
4. **Removal cost.** Once the Users table depends on it, removing it brings back an action on every row that routinely fails. That is the cost of any field. It is additive for existing clients: the current web schema strips unknown members, so adding it breaks nothing.
5. **Stable identity.** A boolean cannot be mistaken for an identifier. It is not a key, a route or a cache identity (P4 unchanged).

#### Query

The row is still read in one statement per page, with no count. **The page is chosen first**, and the derivation runs only over the rows it returns:

```sql
SELECT p.id, p.display_name, p.email,
       EXISTS (SELECT 1 FROM user_identity i
               WHERE i.user_id = p.id AND i.identity_type = 'Local')
       AND NOT EXISTS (SELECT 1 FROM user_identity i
                       WHERE i.user_id = p.id
                         AND EXISTS (SELECT 1 FROM credential c
                                     WHERE c.user_identity_id = i.id)) AS activation_pending
FROM (SELECT id, display_name, email
      FROM app_user
      WHERE actor_type = 'Human'
      ORDER BY display_name COLLATE "unicode", id
      OFFSET (page − 1) × pageSize
      LIMIT pageSize + 1) p
ORDER BY p.display_name COLLATE "unicode", p.id
```

The outer order is the contract order again: SQL does not carry a subquery's order out of it, and re-sorting at most 101 rows costs nothing measurable.

**Measured**, on the deployment image (`postgres:18-alpine`), 100,000 human users, each with one local identity, about half with a credential, and the existing indexes `user_identity (user_id, actor_type)` and `credential (user_identity_id, identity_type)`:

| Form | First page | Deep page (offset 89,900) |
| --- | --- | --- |
| Three fields, before the amendment | 15 ms | 103 ms |
| Derived in the same `SELECT` as the page | 87 ms | 560 ms |
| **Page first, then derived** (adopted) | **11 ms** | **88 ms** |

Derived in the same `SELECT`, PostgreSQL hashed both existence tests over the whole of `user_identity` and `credential` on every request. Page first, each test is an index probe per returned row (26 and 101 probes). The top-N sort is unchanged, and no new index is needed.

### Amendment 2 — `status`

**Decided 2026-09-18 (U1).** USR-C4 and USR-C5 made lifecycle status meaningful. Before them nothing could deactivate a user, so the value read `Active` on every row, and *Lifecycle status* above left the field out on exactly that ground. It left the decision to *"a later read that exposes lifecycle information"*. This amendment makes that decision for the list.

#### Definition

> **`status` is the user's stored lifecycle status, `app_user.status`, exactly: `"Active"` or `"Inactive"`.**

It is not derived and not combined with anything. In particular:

- **It is independent of `activationPending`.** All four combinations occur: active and pending, active and activated, inactive and pending, inactive and activated. Neither field implies the other, and a client infers nothing about one from the other.
- **It is not "locked".** A lockout is credential state, derived per identity, and stays excluded.
- **It carries no deactivation details.** Who deactivated the user, when, and why stay out of the row as *"security internals"* (*Excluded fields*). They are in the audit trail.

#### Authority: `user.read` is sufficient

`user.read` is described as *"View user accounts and their current lifecycle status."* The frozen command catalogue's own reads agree: its admin list filters by `Status` and its detail read returns it, both under `user.read`. Every `user.read` holder sees the field, including `access-reviewer`. There is no permission catalogue change.

#### Pagination, order and filtering are unaffected

Inactive users were already in the list, which scopes to human actors only. They were indistinguishable from active ones. Nothing about which rows are returned, or in what order, changes. There is **no status filter and no status sort** in this amendment; either would need its own decision. `?status=Inactive` is an unknown parameter and is ignored.

#### The evidence (*Adding a field later*)

1. **Concrete use.** The Users table offers *Deactivate* only on active rows and *Reactivate* only on inactive ones. It withholds actions an inactive user can only be refused (reset, resend, sign out everywhere, grant), and it marks inactive users so an administrator can tell them apart.
2. **Data exposure.** One of two values per human user, already within what `user.read` authorises. It carries no who, when or why.
3. **Necessity.** Without it a client must offer both lifecycle actions on every row and rely on refusals, or read each user separately. The first is the pattern CRD-C7 rejected. The second has no read to use.
4. **Removal cost.** Once the table depends on it, removing it brings back actions that routinely fail and makes inactive users look active. It is additive for existing clients: the web schema strips unknown members.
5. **Stable identity.** A two-valued status cannot be mistaken for an identifier (P4 unchanged).

#### Query

The inner page selection gains `status`. Nothing else changes: the page is still chosen first, and `activationPending` is still derived over the returned rows only. The column is on the row already read, so this adds no measurable cost.

#### Acceptance Criteria

- **S1** Every row has `status`, exactly `"Active"` or `"Inactive"`, equal to the stored value.
- **S2** Inactive users appear in the list, in the same order and paging as before.
- **S3** The row's field set is exactly `userId`, `displayName`, `email`, `activationPending` and `status`.
- **S4** An `access-reviewer` sees `status`.
- **S5** `status` is independent of `activationPending`: an inactive pending user reads `Inactive` and `true`.

### Adding a field later

A field joins the row only with the same evidence that admitted these three:

1. **Concrete use** — which supported operation or interface needs it.
2. **Data exposure** — what information it discloses.
3. **Necessity** — why the use cannot be met without it.
4. **Removal cost** — the compatibility consequence of withdrawing it later.
5. **Stable identity** — whether a client could mistake it for an identifier.

*"The column already exists"* is not evidence. A field is cheap to add and expensive to remove once a client depends on it, which is why the default is absence.

### Acceptance Criteria

- **P1** A user-list row contains exactly `UserId`, `DisplayName`, `Email` and, since Amendment 1, `ActivationPending`; no additional fields are exposed by the projection. *(Amended 2026-09-18: previously exactly the first three.)*
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
- **P15** `activationPending` is `true` for a human user who holds a local identity and no credential, and `false` for one who holds a credential. It is also `false` for a user with no local identity.
- **P16** `activationPending` is present on every row, as a JSON boolean, never `null`.
- **P17** Adding `activationPending` changes neither which rows a page returns nor their order, nor `hasMore`. No parameter filters or sorts by it.
- **P18** No row exposes credential or token detail under any name: no hash, algorithm, lockout, failed-attempt count, `MustChangePassword`, token or token state.
- **P19** A client offers *Resend activation link* on a row only when `activationPending` is `true` and the caller holds `user.create`, and does not offer *Reset password* on a row whose `activationPending` is `true`.
- **P20** `activationPending` grants nothing: CRD-C7 and CRD-C5 refuse by their own rules whatever a row said.

### Notes

**Not decided here:** a detail route, `Location` on create, a path from a row to unlock, and correlating a row with the caller.

---

## CRD-C7 — Reissue Activation Link

**Requirement ID:** `CRD-C7`, assigned by the owner 2026-09-18. `CRD` because the command's whole effect is token lifecycle.

**Status:** Approved and frozen, 2026-09-18 by owner decision. Implemented by `ReissueActivationLinkCommandHandler` at `POST /api/users/{userId}/activation-link`.

### Requirement

An administrator can send a user who has never activated their account a new activation link. The new link replaces every earlier one: after a successful reissue, the identity has exactly one usable activation link.

This is the recovery command the Known Gap *A failed activation mail has no recovery command — RESOLVED* deferred. Without it, a user whose activation mail was never delivered, or whose link expired unused, could not be recovered: USR-C1 cannot be re-run for the same address or username.

It **issues a token and nothing else**. It creates no user, identity or credential, changes no lifecycle status, and grants no access. The user still activates through the existing activation path, with the new link.

### Authorization: `user.create`

The command requires **`user.create`**. No new permission is introduced.

`user.create` is described as *"Create a new human user account and issue its activation token."* Reissuing that token finishes the work creation started. It reaches no one a creator could not already reach, and it confers no access, so a separate permission would add catalogue and role-grant decisions without a distinct capability boundary.

**This changes what the Known Gap said.** It deferred this to *"a command that reissues an activation token (its own permission, …)"*. The owner ruled that no separate permission is warranted, for the reason above. That is recorded here as a deliberate change (*Rules*, 4), not an oversight.

The command is human-actor only, as USR-C1 and CRD-C5 are.

### Eligibility — enforced by the server, always

The target is eligible only when **every** one of these holds:

1. the user exists;
2. its actor type is `Human`;
3. its status is `Active`;
4. it has an email address — the only delivery channel;
5. it holds **exactly one** local identity, and that identity is `Active`;
6. that identity has **no credential**: the pending-activation state (inv. 15).

Any other target is refused with **one** message, whichever rule failed. An administrator is authorised over users, so there is no anti-enumeration discipline (as for CRD-C5). But one message means the refusal never becomes a probe of which rule failed.

Rule 6 is what keeps this from becoming a second password-reset path. A user who has activated holds a credential and is served by CRD-C5, never by a new activation link.

> **Eligibility is the server's alone.** A client may one day know which users are pending, from a separate read contract (see *Not decided here*). That knowledge is a presentation optimisation. It is not authorization and not eligibility enforcement, and the command re-verifies every rule above whatever the client believed.

More than one local identity is refused rather than resolved, for CRD-C5's reason: no command creates a second one, and choosing between them would be a guess about whose mailbox controls the account.

### Effect — one transaction

Every eligibility read runs before the first write. A refused target therefore leaves no token, no audit record and no notification.

In one transaction, in this order:

1. **Invalidate every prior unused activation token** for the identity, including expired ones (UT5). An expired but unused token still holds UT4's slot, so invalidation must precede the insert.
2. **Issue one new `Activation` token.** Its lifetime is the effective `ActivationTokenLifetime`, as USR-C1's is. Its `CreatedBy` is the administrator. Only the hash is persisted (UT7).
3. **Declare the audit records** (below).
4. **Declare one `AccountActivation` notification** for the new token, addressed to the user's stored email.

Either the whole transaction commits or none of it does. A failure at any step, including audit or notification emission, leaves the prior tokens open and nothing written.

**Invariant.** After a successful reissue, the identity has exactly one open `Activation` token, and it is the one just issued. UT4's partial unique index guarantees there are never two, even under concurrent reissues. A concurrent request that loses fails without effect; its response surface is not specified here.

### Audit — existing events only

No new event type, and no audit catalogue change.

| Record | Primary | References | Payload | Reason |
| --- | --- | --- | --- | --- |
| `TokenInvalidated` — one per superseded token | `Token` (the prior token) | `Identity`/`Target`, `Token`/`SupersededBy` (the new token) | `TokenType: Activation`, `Reason: Superseded` | — |
| `TokenIssued` | `Token` (the new token) | `Identity`/`Target`, `User`/`Subject` | `tokenType: Activation`, `expiresAt` | **the administrator's reason** |

The actor of every record is the administrator. A reissue is distinguishable from creation's first issuance by what surrounds it: creation's `TokenIssued` accompanies `UserCreated` and `IdentityCreated`, and a reissue's does not. It also carries a reason, and may follow `TokenInvalidated` records.

`TokenIssued` does not *require* a reason (AR9 enforces only a required one), but it accepts one. A null reason is permitted by `ck_audit_record_ar9_reason` and a present one is recorded. **This command always supplies one.**

Zero superseded tokens is valid. A pending user whose every prior token is already invalidated gets a `TokenIssued` and no `TokenInvalidated`.

Neither the token nor its hash appears in any record.

### Reason

**Required.** A missing, empty or whitespace-only reason is refused before any database work, on the ordinary `400` surface. Otherwise it would surface only as a missing-reason emission defect, which is a `500` for what is the caller's mistake (CRD-C5's precedent). It is a human explanation from the administrator, never a code (AUD-7).

### Endpoint

**`POST /api/users/{userId}/activation-link`**, beside the existing per-user administrator commands and addressed by the same `UserId` that USR-Q2 rows and `POST /api/users` return.

Request body:

```json
{ "reason": "…" }
```

| Outcome | Status | Body |
| --- | --- | --- |
| Link issued | `202` | none |
| No established caller | `401` | the host's fixed message |
| Caller lacks `user.create` | `400` | `{ "error": … }`. Distinguishing this from a validation failure remains the Known Gap *Authorization failures are not distinguishable from validation failures* |
| Missing reason | `400` | `{ "error": … }` |
| Ineligible or unknown user | `400` | `{ "error": … }`, one message |

`202` with **no body**: the administrator never receives the token or the link. The mail to the user's own address is the only delivery, as for USR-C1 and CRD-C5.

### Delivery

The notification is the existing `AccountActivation` type and template, so the message is identical to creation's. Delivery is the Notification pipeline's, unchanged: with no mail configured the row records that no attempt was observed, and a development host with the mail sink writes the message to `.secrets/mail/`.

**A notification already queued for a superseded token is not rewritten or removed.** Its row stays exactly as it was, and CRD-C7 writes nothing to it. The Notification pipeline decides its fate, unchanged. Its eligibility gate (N15) reads the token immediately before transport, finds it invalidated, and closes the row `NotSent`/`TokenNotLive` without sending.

**Accepted consequence.** The old link can still reach the user only if its send had already passed the gate before the reissue committed. That link is then refused at activation, because the token it carries is invalidated. This is the same window CRD-C5 has, and it is accepted, not engineered around.

### Acceptance Criteria

- **R1** An eligible user receives exactly one new `Activation` token, created by the administrator, with the effective activation lifetime; the response is `202` with no body.
- **R2** Every prior unused activation token of the identity, expired or not, is invalidated. After success the identity has exactly one open `Activation` token, the new one.
- **R3** A superseded link can no longer activate the account; the new link can.
- **R4** One `TokenInvalidated` per superseded token, referencing the new token as `SupersededBy`, and one `TokenIssued` carrying the reason; the administrator is the actor of each. No other audit record.
- **R5** One `AccountActivation` notification for the new token, to the user's stored email. A notification already queued for a superseded token is left as it was, and the eligibility gate no longer finds its token live.
- **R6** A user who holds a credential is refused.
- **R7** A non-human user, a user who is not `Active`, a user with no email, a user with zero or several local identities, a user whose only local identity is not `Active`, and an unknown `UserId` are each refused with the same message.
- **R8** A missing, empty or whitespace-only reason is refused.
- **R9** A caller without `user.create` is refused, and no carrier is `401`.
- **R10** Every refusal, and every failure after eligibility, leaves the prior tokens open and writes no token, audit record or notification.
- **R11** No response and no audit record contains the token or its hash.

### Not decided here

- **Which users a client offers this command for.** USR-Q2's row stays exactly `UserId`, `DisplayName`, `Email`. A client that shows the action only to pending users needs a separate read contract, a USR-Q2 amendment for a derived `activationPending` flag with its own evidence (*Adding a field later*). The Users table is not changed until that contract exists. Offering the action on every row and relying on refusals was rejected.
- **The Notification specification's wording.** Its walkthrough (§11.4, §11.5) names CRD-C5 as the remedy for a failed activation mail. §10.1 speaks of *"the three issuing commands"*; with CRD-C7 there are four. Both are corrected through that specification's own change control, not here.

---

## AUT-C1 / AUT-C2 — Role Assignment

**Requirement IDs:** `AUT-C1` GrantRole and `AUT-C2` RevokeRole, as the UM command catalogue defines them. This entry records the v1 contract decided against the frozen specification (§6.11 `user_role`, invariants 7–9, 17a, 24) and the entity workbook (UR1–UR13).

**Status:** Approved and frozen, 2026-09-18 by owner decision. Implemented by `GrantRoleCommandHandler` and `RevokeRoleCommandHandler`, with migration `AlignUserRoleEffectivePeriodWithUR2`. Story 1 of two: the commands. Story 2 is the read (`AUT-Q2`), the list of grantable roles and the administration UI.

### Requirement

An administrator can give a human user a role, from now or from a future date, optionally until an end date, and can take it away again. Every grant and every revocation is a separate, attributable event carrying the administrator's reason.

Until now roles were assigned only by provisioning, so a created user could sign in and do nothing.

### Grant date and effective date are different things

> **The grant records when the administrative decision was made (`AssignedAt`/`AssignedBy`, and the audit record's timestamp). `EffectiveFrom` records when authorisation begins.** They are equal only when a grant takes effect immediately. Nothing in this contract, or in any later simplification of it, may assume that a grant becomes effective when it is made.

The specification keeps three timestamp pairs apart on purpose (§6.11): who decided and when, during what period access was valid, and who ended it and when. *"An administrator may grant on Monday access that becomes effective next month, and a different administrator may revoke it in between."*

### AUT-C1 GrantRole

| Input | Rule |
| --- | --- |
| Target user | Must exist, be a **human**, and be `Active`. Any other target is refused. A user who has not yet activated is `Active` and may be granted a role |
| Role | Must exist and be **active**. A retired role cannot receive new assignments |
| Scope | **Global only in v1.** The API takes no scope input; the command always writes `ScopeType = Global` and no `ScopeId` (spec §6.11: *"Global in V1"*) |
| `EffectiveFrom` | Optional; defaults to the server's current time. **Must not be in the past.** A backdated start would record access as valid before anyone decided to grant it |
| `EffectiveTo` | Optional; empty means open-ended (permitted for humans). When supplied, **`EffectiveTo > EffectiveFrom`, strictly**: a grant must create a period that can authorise. `2026-10-01 → 2026-12-01` and `2026-10-01 → (none)` are accepted; `2026-10-01 → 2026-10-01` is refused |
| Reason | **Required and not blank.** Recorded as `AssignmentReason` and on the audit record |

- **Overlap.** The same user, role and scope may not hold two assignments whose effective periods overlap (invariant 8, UR5). The database enforces it, because two concurrent grants would both pass an application check. The violation is translated to the ordinary refusal *"The user already holds this role for this scope in an overlapping period."*
- **No four-eyes approval** (open decision A1). A human holding `role.grant` may grant to anyone, including themselves; segregation of duties at action time belongs to the Workflow context (spec §1.2). A1's recommended practice — an external ticket reference — goes in the reason. User Management does not validate its format.
- **Agents are out of scope** (invariant 17a). The target must be human, so the agent rules (a finite end date, UR8; no human-only permission on the role, UR9) have no reachable path in v1. They remain enforced by the database and the domain for when agents exist.

> **Amended by USR-C4/C5 (D6).** GrantRole locks the target's `app_user` row (`SELECT … FOR UPDATE`) before its active-human check, so a grant cannot race a deactivation. The contract above is unchanged; see *USR-C4 / USR-C5*, *Concurrency*.

### AUT-C2 RevokeRole

The assignment is addressed by its own identifier (the catalogue's `UserRoleId`). The reason is **required and not blank**, recorded as `RevocationReason`.

| Assignment state at the moment of revocation | Outcome |
| --- | --- |
| Does not exist | Refused |
| Already revoked | **Refused explicitly** — a second revocation never reads as a success |
| Already ended (`EffectiveTo` at or before now) | **Refused explicitly** — there is nothing left to close |
| **Active** (`EffectiveFrom <= now`, not ended) | Revoked; **`EffectiveTo = revocation time`** |
| **Future** (`EffectiveFrom > now`) | Revoked; **`EffectiveTo = EffectiveFrom`** — an empty effective period |

Revocation closes the effective period (invariant 9) and records who ended it, when and why. The two outcomes are **distinct domain cases, stated as such**, not one formula chosen to satisfy the database:

- An active assignment stops authorising at the moment of revocation.
- A future assignment is closed before it opens. Its period `[EffectiveFrom, EffectiveFrom)` is empty: it never authorises, and it overlaps nothing, so a new grant for the same period is not blocked by it.

Revoking only ever moves `EffectiveTo` earlier, never later, and never clears it. The database's G4 guard refuses anything else.

There is no suspended state (UR13). Pausing access is a revocation now and a new grant later: two attributable events.

**Periods are judged at the precision they are stored at.** PostgreSQL keeps microseconds and .NET keeps 100-nanosecond ticks, so a grant whose end is one tick after its start would pass a strict check made in .NET and then be stored as an empty period, which the database admits. The commands round supplied instants down to the microsecond before the domain judges them.

### Three rules about the effective period, deliberately different

| Where | Rule | Why |
| --- | --- | --- |
| **Database** (`ck_user_role_effective_period`, frozen UR2) | `EffectiveTo IS NULL OR EffectiveTo >= EffectiveFrom` | Admits the empty period, which revocation needs |
| **GrantRole** | when `EffectiveTo` is supplied, `EffectiveTo > EffectiveFrom` | A new grant must be able to authorise. An assignment that can never authorise records a decision with no effect |
| **RevokeRole**, future assignment | sets `EffectiveTo = EffectiveFrom` | Cancelling a grant before it takes effect deliberately produces the empty period |

> **The database's `>=` does not make equal dates valid for GrantRole.** It exists so a revocation can close a future grant before it opens. The command is stricter than the constraint on purpose, and the empty period is reachable only through RevokeRole.

### The database constraint is relaxed to match the frozen model

The frozen entity workbook defines **UR2 as `CHECK (EffectiveTo IS NULL OR EffectiveTo >= EffectiveFrom)`**. The migrated constraint `ck_user_role_effective_period` is **stricter: `effective_to > effective_from`**. That stricter form makes the empty period impossible, and with it the specification's own example of revoking a future grant.

**This story relaxes the constraint from `>` to `>=` to restore alignment with frozen UR2.** It does not change the business contract to fit an implementation limit: it removes an implementation limit the contract never had. Nothing that the stricter constraint admitted is newly refused.

### Endpoints

**Grant:** `POST /api/users/{userId}/role-assignments`

```json
{ "roleId": "…", "effectiveFrom": "…", "effectiveTo": "…", "reason": "…" }
```

`effectiveFrom` and `effectiveTo` are optional. Success is **`201 Created`** with the new assignment's identifier, which revocation and the later read address it by:

```json
{ "userRoleAssignmentId": "…" }
```

**No `Location` header**, as `POST /api/users` has none: no single-assignment read exists, and none is invented to satisfy convention.

**Revoke:** `POST /api/role-assignments/{assignmentId}/revoke` with `{ "reason": "…" }`. Success is **`204 No Content`**, as `POST /api/sessions/{sessionId}/revoke` is.

Revocation is addressed by the **assignment**, not by `{userId}/{roleId}`: a user may hold the same role in several periods over time, and the assignment is the thing acted on.

Refusals are `400` with the host's `{ "error": … }` body; no carrier is `401`. A missing required field is refused where the request is bound, as on the existing endpoints.

### Authorisation

| Command | Permission | Actor |
| --- | --- | --- |
| AUT-C1 | `role.grant` | human only (closes the agent self-escalation loop, spec §6.9) |
| AUT-C2 | `role.revoke` | human only |

A caller without the permission is refused as every command's authorisation failure is today (`400`; the Known Gap *Authorization failures are not distinguishable from validation failures*).

### Audit — existing events

| Command | Event | Primary | References | Content | Reason |
| --- | --- | --- | --- | --- | --- |
| AUT-C1 | `RoleGranted` | `UserRoleAssignment` | `User`/`Subject`, `Role`/`GrantedRole` | After: the assignment as created | the assignment reason |
| AUT-C2 | `RoleRevoked` | `UserRoleAssignment` | `User`/`Subject`, `Role`/`RevokedRole` | Before and After: the assignment's period and revocation fields | the revocation reason |

Both are Transactional and require a reason (AR9). The administrator is the actor. Neither carries personal data. No audit catalogue change.

### Effect on authorisation

There is no effective-permission cache: permissions are resolved per request (UR12). A grant is honoured from `EffectiveFrom` and a revocation from its new `EffectiveTo`, on the next request. The catalogue's step *"invalidate the target's effective-permission cache"* has nothing to act on.

### Acceptance Criteria

- **G1** A human holding `role.grant` grants an active role to an active human; one assignment is written with Global scope, the given or defaulted `EffectiveFrom`, the given `EffectiveTo` or none, `AssignedBy` the administrator, and the reason.
- **G2** An omitted `EffectiveFrom` is the server's current time; a past `EffectiveFrom` is refused; an `EffectiveTo` before **or equal to** `EffectiveFrom` is refused.
- **G3** A future grant does not authorise before `EffectiveFrom` and does from it; an ended grant does not authorise.
- **G4** An overlapping grant for the same user, role and scope is refused with the overlap message, and writes nothing.
- **G5** A retired role, an unknown role, an unknown user, a non-human user and an inactive user are refused, and write nothing.
- **G6** A blank reason is refused before any database work.
- **G7** `RoleGranted` is written with the references and content above, the reason, and the administrator as actor.
- **R1** Revoking an active assignment sets `EffectiveTo` to the revocation time, records who, when and why, and the user loses the role's permissions on the next request.
- **R2** Revoking a future assignment sets `EffectiveTo = EffectiveFrom`; it never authorises, and a new grant over the same period is accepted.
- **R3** Revoking an unknown, already-revoked or already-ended assignment is refused, and writes nothing.
- **R4** A blank reason is refused.
- **R5** `RoleRevoked` is written with the references and content above, the reason, and the administrator as actor.
- **A1** A caller without `role.grant` / `role.revoke`, and a non-human caller, are refused and write nothing.
- **D1** `ck_user_role_effective_period` admits `effective_to = effective_from` and still refuses `effective_to < effective_from`.
- **E1** Grant is `201` with exactly `{ userRoleAssignmentId }` and no `Location`; revoke is `204` with no body; refusals are `400` with the error body; no carrier is `401`.

### Not decided here

- **The read and the UI.** `AUT-Q2` GetUserRoleAssignments, how an administrator picks a grantable role, and where in the Administration area this lives are Story 2.
- **Role definitions.** Creating, changing or retiring roles, and changing a role's permissions (AUT-C3–C8), are Slice 4.
- **Scoped assignments** and **agent assignments**, until their capabilities exist.

---

## AUT-Q2 — User Role Assignments, and the role-assignment UI

**Requirement ID:** `AUT-Q2` GetUserRoleAssignments, as the UM command catalogue defines it. The role list below has **no** requirement ID of its own (see *The grantable-role list is not AUT-Q5*).

**Status:** Approved and frozen, 2026-09-18 by owner decision (D1–D8 and two clarifications). Implemented: `UserRoleAssignmentsQueryHandler` and `GrantableRolesQueryHandler`, with the state derived by `RoleAssignmentStates` (which `UserRole.StateAt` also uses), and the Manage roles dialog. Story 2 of two: the read and the UI over AUT-C1 / AUT-C2.

### Requirement

An administrator can see which roles a user holds, has held and will hold, and grant and revoke them, from the Users table. An access reviewer can see the same assignments and is offered no action.

### AUT-Q2 — the read

**`GET /api/users/{userId}/role-assignments?includeInactive=`**, requiring **`role.read`**. Not audited: reads are not events (as USR-Q2).

| Parameter | Rule |
| --- | --- |
| `includeInactive` | Optional; absent means `false`. Exactly `true` or `false`; any other value, or a repeated parameter, is refused as an invalid request. `false` returns `Active` and `Future` assignments; `true` adds `Ended` and `Revoked` |

The handler establishes, in this order, as USR-Q2's does: authenticate (`401`), authorise `role.read` (`400`, the Known Gap on authorisation failures), then read. An unknown user is refused (`400`), as the commands addressed by `{userId}` refuse it.

```json
{
  "assignments": [
    {
      "assignmentId": "…",
      "roleId": "…",
      "roleName": "…",
      "effectiveFrom": "…",
      "effectiveTo": "…",
      "state": "Active",
      "assignedAt": "…",
      "assignedBy": { "userId": "…", "displayName": "…" },
      "assignmentReason": "…",
      "revokedAt": null,
      "revokedBy": null,
      "revocationReason": null
    }
  ]
}
```

- `effectiveTo`, `revokedAt`, `revokedBy` and `revocationReason` may be `null`; the rest never are. Scope is not in the row: it is Global in every v1 assignment.
- **Order:** `effectiveFrom` descending, then `assignmentId`, so the latest period comes first. No paging: one user's assignments are few.
- **Exposure (D4).** The row shows which administrator granted or revoked an assignment, and their reason, to every `role.read` holder. That is the purpose of the read — an access review asks who granted what, when and why — and it is accepted as such.

### `state` is derived at read time, never stored

> **`state` is calculated by the server at read time from the assignment's effective period and revocation data. It is never stored as a column and must not be independently supplied or calculated by the client.**

At the instant of the read (`now`), in this order — **revocation wins**:

| `state` | When |
| --- | --- |
| `Revoked` | `RevokedAt` is set |
| `Future` | not revoked, and `EffectiveFrom > now` |
| `Ended` | not revoked, and `EffectiveTo <= now` |
| `Active` | otherwise: `EffectiveFrom <= now` and (`EffectiveTo` empty or `> now`) |

This follows the catalogue (*"State derived from dates — there is no Status column … do not invent a status field client-side"*) and spec §6.11.

A cancelled future grant shows why the order matters:

```text
EffectiveFrom = 2026-10-01
EffectiveTo   = 2026-10-01
RevokedAt     = 2026-09-18
```

Read on 2026-10-02 its period has passed, but it is **`Revoked`**, not `Ended`: revocation wins. It never authorised anything, and the read must not suggest it lapsed naturally.

### The grantable-role list is not AUT-Q5

**`GET /api/roles`**, requiring **`role.read`**, returns the **active** roles, each `{ roleId, name, description }`, ordered by name under ICU `"unicode"`, then `roleId`.

> **Story 2 dependency:** provide the minimum active-role projection required by AUT-C1. This is an implementation slice supporting role assignment and **does not constitute completion of AUT-Q5**.

AUT-Q5 ListRoles, as catalogued, also returns permission and active-holder counts, takes `includeInactive` and `agentAssignableOnly`, and derives the agent-assignable flag. None of that is built here, and AUT-Q5 stays *Not started* in the tracker.

### The UI (D1, D7, D8)

- **Where:** a **Manage roles** action on each Users table row, opening a dialog for that user. No detail page (that needs the single-user read, `USR-Q1` GetUser, which is not yet specified).
- **Who sees what** — from the caller's **effective permissions** (`GET /api/me`), never from role names:

  | Holds | Sees |
  | --- | --- |
  | `role.read` | **Manage roles**, and the user's assignments |
  | `role.grant` (with `role.read`) | a **Grant role** form |
  | `role.revoke` (with `role.read`) | **Revoke** on each `Active` or `Future` assignment, and on no other |

  Hidden, never disabled. A row with no action at all has no Actions button (USR-Q2 amendment 1's rule).
- **The list** shows each assignment's role, `state` exactly as the server sent it, period, and who granted it, when and why (and revoked, when revoked). Current and future by default; **Show history** asks the server again with `includeInactive=true`. The client derives nothing from the dates.
- **Grant:** an active role from `GET /api/roles`; optional start and end, entered in the browser's local time and **sent as UTC**; a required reason. The server is authoritative: an overlap, a past start or an empty period is refused there, and its message is shown word for word. The client does not pre-check overlap.
- **Revoke:** a confirmation with a required reason, as the existing row actions.
- After a grant or a revocation the assignments are read again, so `state` stays the server's.

### Acceptance Criteria

- **Q1** `GET /api/users/{userId}/role-assignments` requires `role.read`; no carrier is `401`; a caller without it and an unknown user are refused.
- **Q2** Each row has exactly the fields above.
- **Q3** `state` is derived at read time with revocation winning. **One assignment read at three instants is `Future`, then `Active`, then `Ended`**; a future assignment revoked before it starts is `Revoked` at every instant, including after its period.
- **Q4** Without `includeInactive`, only `Active` and `Future` assignments are returned; with `true`, all; any other value is refused.
- **Q5** Rows are ordered by `effectiveFrom` descending, then `assignmentId`.
- **L1** `GET /api/roles` requires `role.read` and returns exactly the active roles, each with exactly `roleId`, `name` and `description`.
- **U1** Manage roles appears only for a `role.read` holder; Grant only with `role.grant`; Revoke only with `role.revoke`, and only on `Active` and `Future` assignments.
- **U2** The UI shows `state` as sent and derives none; history is fetched with `includeInactive=true`.
- **U3** A grant sends the chosen role, UTC instants for any dates entered, and the reason; a server refusal is shown word for word and the dialog stays open; success refreshes the assignments.
- **U4** A revocation sends exactly the reason to the assignment's revoke endpoint and refreshes the assignments.
- **U5** Browser: Ada grants and revokes on Vru Raj; Vr Ra (`access-reviewer`) sees assignments and no Grant or Revoke.

### Not decided here

- ~~**The role-composition gap.**~~ `security-administrator` held `role.*` but not `user.read`, so only someone holding both administrator roles could reach Manage roles from the Users table. **Decided** by PRV-C1 Amendment 1: `security-administrator` includes `user.read`.
- AUT-Q5 in full, AUT-Q3/Q4/Q6, and role definitions (AUT-C3–C8): Slice 4.

---

## USR-C4 / USR-C5 — Deactivate and Reactivate a User

**Status:** Contract frozen 2026-09-18 by owner decision (D1–D13). Backend story. The UI, and the list amendment it needs, are a following story (D1).

### Requirement

**USR-C4 DeactivateUser** takes a person out of the tenant in one controlled operation. **USR-C5 ReactivateUser** returns them, and restores nothing.

The lifecycle this story establishes is:

```text
Active  --USR-C4-->  Inactive  --USR-C5-->  Active, holding no role assignments
```

The frozen specification is the source (UM §6.1, §6.2, §11.7, §11.8; invariants 7, 25, 27; D3, D4). Deactivation is not a column update. *"Reactivation restores nothing"*: any access a returning user needs is granted afresh, by a named person, with a fresh reason (AUT-C1).

Three mechanisms fail safe independently, as §11.8 intends:

1. The per-request check already refuses an inactive user or identity (`CallerEstablisher`, `UserSessionRepository`, `AuthorizationService`), so a deactivated user loses access on their next request whatever the cascade does.
2. The cascade revokes sessions and assignments explicitly and attributes them, so the trail says why they ended.
3. Reactivation restores none of them.

### USR-C4 DeactivateUser

- **Caller:** a human holding `user.deactivate`, which is human-only. The command implements `IHumanActorOnlyCommand`.
- **Inputs:** `UserId` and `Reason`. The reason is required and non-blank, checked before any database work. There is no `NewOwnerUserId` input (D11).
- **Refusals** (each writes nothing):
  - **(a)** the target is the System actor, or is not a human (D9a);
  - **(b)** the target is already inactive (D9b);
  - **(c)** the target is the caller (D9c);
  - **(d)** the target does not exist.

  A pending user, meaning one whose account was never activated, **may** be deactivated (D9d).

**The self rule, stated exactly.** *A caller may not deactivate themselves.* That is the whole rule. It does **not** protect "the last administrator", and it does not decide what counts as an administrator. That would be a separate rule needing its own evidence and decision. It is not made here.

#### The transaction

Everything below happens in the pipeline's single transaction, in this order. Each step names the frozen command-steps row it implements, and where it departs from that row.

| # | Step | Detail |
| --- | --- | --- |
| 1 | Authorise | The pipeline checks `user.deactivate` and that the caller is human. |
| 2 | Lock and load the target | `SELECT … FOR UPDATE` on the target's `app_user` row, **before** reading its status (D6). The refusals above are evaluated on the locked row. |
| 3 | Owned agents | **Not performed; deferred to Slice 6** (D11). See *Agents* below. |
| 4 | Revoke role assignments | Every assignment the user holds is passed to `UserRole.Revoke`, the same domain rule AUT-C2 uses (D2): an **active** assignment ends now; a **future** one is closed at its own start, an empty period; an **ended** or already **revoked** one is not touched. `RevokedBy` = the System actor, and `RevocationReason` = `User deactivated` (D3, D4). *Amends catalogue step 4; see Catalogue amendments.* |
| 5 | Revoke sessions | Every active session of every identity the user holds, found the way SES-C4 finds them. Each is revoked at now, with `RevokedBy` = the System actor and `RevocationReason` = `UserDeactivated`, the controlled code (D3, D4, D8). |
| 6 | Deactivate identities | Every **active** identity is deactivated with the stamp (now, caller), the same stamp as the user's (D5). |
| 7 | Deactivate the user | `Status` = `Inactive`, and `DeactivatedAt`/`DeactivatedBy` = (now, caller). |
| 7a | Invalidate tokens | Every outstanding (`used_at IS NULL AND invalidated_at IS NULL`) `Activation` and `PasswordReset` token of every identity the user holds gets `invalidated_at` = now (D7). *An addition to the catalogue steps; see Catalogue amendments.* |
| 8 | Audit | See below. |
| 9 | Invalidate caches | **Nothing to do.** There is no effective-permission cache: permissions are resolved per request (UR12). |

"Now" is one instant for the whole operation, truncated to stored precision (`RoleAssignmentTime.AtStoredPrecision`), so every row and record the cascade writes carries the same time. **It is read after the lock is taken, not before.** An assignment granted while C4 waited for the lock was assigned later than a clock read taken before the wait, and `UserRole.Revoke` refuses a revocation dated before the assignment. K1 inserts its racing assignment at the real insert time (`clock_timestamp()`) to hold this.

The steps run in one transaction, so their order within it cannot be observed. The self rule (D9c) is evaluated first, by the domain's `User.Deactivate`, before anything is changed.

**Partial execution is the failure this prevents.** A refusal or a fault at any step rolls back every step. No application-level retry and no after-the-fact cleanup are used.

#### Tokens: state, not audit (D7, D13)

> **C4 SHALL invalidate all outstanding activation and password-reset tokens belonging to the deactivated user. C4 SHALL NOT emit `TokenInvalidated` for these invalidations, because the frozen `TokenInvalidated` event requires a `SupersededBy` token reference and no replacement token exists. The `UserDeactivated` audit event records the administrative action and its reason. Per-token invalidation is therefore not individually represented in the audit event stream in v1.**

This is why the invalidation exists at all. Token redemption already refuses an inactive user. But without invalidation, a reset or activation link issued **before** deactivation would work again **after** reactivation, which is a quiet way for old access to come back.

A future implementer must not "fix" the missing record by fabricating a superseding token, or by weakening the frozen `TokenInvalidated` definition. The gap is recorded as Audit change control (see Known Gaps, *USR-C4/C5 change control*). It closes by amending the Audit workbook, and by adding a definition through per-event versioning (AUD-C3) once that works.

A pending user who is later reactivated gets a fresh activation link through CRD-C7. A user who had a reset in flight asks again (CRD-C2) or is reset by an administrator (CRD-C5).

#### Audit

These are existing event types. No catalogue version bump and no definition change.

- **`UserDeactivated`**, one record: primary `User`, with the administrator's reason. Before and After hold exactly `Status`, `DeactivatedAt` and `DeactivatedBy`, as the frozen workbook specifies. No `NewAgentOwner` ref (D11).
- **`IdentityDeactivated`**, one per identity deactivated: primary `Identity`, ref `User`/`Subject`, the administrator's reason, and Before/After of the identity's `Status`, `DeactivatedAt` and `DeactivatedBy`.
- **`RoleRevoked`**, one per assignment revoked: the same shape AUT-C2 writes. The audit **Reason** is the administrator's reason; the row's `RevocationReason` is `User deactivated`.
- **`SessionRevoked`**, one per session revoked: the shape `SessionRevocations.Declare` writes. The controlled code `UserDeactivated` is in Before/After, and the audit Reason is the administrator's reason (D2/R1 of the session-revocation change control).
- **No `TokenInvalidated`** (D13, above).

**One operation, one story.** Every record above shares the command's `OperationId`. Each `IdentityDeactivated`, `RoleRevoked` and `SessionRevoked` carries `CausationId` = the `UserDeactivated` record's `AuditId` (Audit pipeline behaviour 18: *"USR-C4 cascade steps ← UserDeactivated"*). AUD-Q3 GetOperation reads it as one sequence.

All of it is written by the command's own transaction. The audit actor of every record is the **administrator**, while the revoked rows name the **System actor** in `RevokedBy` (D3). The two do not contradict each other: `RevokedBy` records that the deactivation ended them, not a person's separate decision, and the audit records say who deactivated.

### USR-C5 ReactivateUser

- **Caller:** a human holding `user.reactivate`, which is human-only.
- **Inputs:** `UserId` and `Reason`. The reason is required and non-blank, checked before any database work.
- **Refusals** (each writes nothing):
  - **(a)** the target is the System actor, or is not a human;
  - **(b)** the target is already active (D9e);
  - **(c)** the target's email is held by another human who is not inactive (D9e);
  - **(d)** the target does not exist.

**Steps, in one transaction:**

1. The pipeline authorises.
2. Lock the target's `app_user` row `FOR UPDATE` (D6), then evaluate the refusals.
3. **Email check.** Refuse if another human who is not inactive holds the same email, compared with `lower()` in the database (AU3). The check exists to give a clear message. **The unique index `ux_app_user_active_human_email` remains the guarantee**, and a race past the check is refused by it. *Corrected during implementation:* that refusal carries the translator's existing message for the index, *"A user with this email address already exists."*, not the pre-check's. The index is translated once, for every command, and the unit of work saves after the handler returns, so the handler cannot re-word it. The meaning is the same, and the reactivation is refused either way.
4. Reactivate every identity whose deactivation stamp equals the user's (`DeactivatedAt` and `DeactivatedBy` both equal) (D5). An identity deactivated separately, by a future IDN-C3, stays inactive.
5. Reactivate the user: `Status` = `Active`, and the deactivation stamp is cleared. This is lifecycle-controlled, per the frozen model.
6. Audit: `UserReactivated`, with Before/After of `Status`, `DeactivatedAt` and `DeactivatedBy`, and the reason; plus one `IdentityReactivated` per identity, with `CausationId` = `UserReactivated`. All share one `OperationId`.

**Restores nothing.** No role assignment, session or token is recreated. Revoked assignments stay revoked. Access is granted afresh through AUT-C1, and a password or activation link through CRD-C5 or CRD-C7. Credentials are untouched throughout, as §11.8 specifies.

**The email refusal has no remedy yet.** The holder's email can only change through USR-C3, which is blocked on decision A3. The refusal message says what is wrong, and the record is not reactivated.

### Concurrency: one lock, three commands (D6)

GrantRole (AUT-C1), USR-C4 and USR-C5 each take `SELECT … FOR UPDATE` on the **target user's** `app_user` row, **before** checking its status:

```text
GrantRole                         DeactivateUser
    │                                  │
    ├─ lock target user                ├─ lock target user
    ├─ verify active                   ├─ verify active
    ├─ create assignment               ├─ deactivate
    │                                  ├─ revoke roles
    │                                  └─ commit
    └─ commit
```

Whichever takes the lock first decides the order. If a grant commits first, deactivation then sees and revokes the new assignment. If deactivation commits first, the grant then sees `Inactive` and is refused. No grant can land between C4's check and its commit.

**This amends AUT-C1's implementation**, not its contract: GrantRole's existing "active human" check now runs on the locked row. Race-free ordering is not solved with retries or cleanup.

### Database: status and deactivation agree (D12)

A new migration adds, on both tables:

```sql
CHECK ((status = 'Inactive') = (deactivated_at IS NOT NULL))
```

- `ck_app_user_status_deactivation` on `app_user`
- `ck_user_identity_status_deactivation` on `user_identity`

This is the invariant C4 and C5 rely on, and today nothing but the domain enforces it. The existing pair checks (AU4, UI9) already tie `DeactivatedBy` to `DeactivatedAt`. No rule ID is assigned; the frozen AU/UI numbering belongs to the specification.

**The migration normalises nothing.** A database whose rows violate the check refuses the migration, and that is the correct outcome. The one known violator is a test fixture (`AuthenticationEndToEndTests`, which inserts an `Active` user with `deactivated_at` set), and **it is corrected at its source**, not repaired by the migration.

### Endpoints (D10)

| Route | Body | Success | Refusals |
| --- | --- | --- | --- |
| `POST /api/users/{userId}/deactivate` | `{ "reason": string }` | `204`, no body | `400` with `{ error }` for every refusal above and for a permission refusal (Known Gaps, *Authorization failures are not distinguishable from validation failures*); `401` without a carrier |
| `POST /api/users/{userId}/reactivate` | `{ "reason": string }` | `204`, no body | as above |

Both follow the existing user-command endpoints: bearer or cookie carrier, and the cross-site guard on unsafe methods.

### Agents (D11)

Invariant 25 stands, and **this story does not implement it**. V1 cannot create or activate an agent, so there is no ownership to transfer and no agent assignment to revoke. Building that behaviour now would manufacture state to satisfy a future rule. The obligation is kept explicit:

- Slice 6 (AGT-C1..C4) SHALL add catalogue step 3 to USR-C4: transfer to `NewOwnerUserId`, or the emergency revocation path, inside the same transaction, with the `NewAgentOwner` ref on `UserDeactivated`.
- It is listed under Known Gaps, *USR-C4 does not handle owned agents*.

### Catalogue amendments

Both amend the frozen UM command catalogue's USR-C4 rows and are recorded as change control (Known Gaps). No workbook is edited.

1. **Future role assignments (step 4).** The catalogue writes `EffectiveTo = now` for every assignment that has not ended. For a future assignment that violates UR2 (`EffectiveTo >= EffectiveFrom`) and contradicts AUT-C2. C4 uses the empty-period rule AUT-C1/C2 established (#59): a future assignment is closed at its own `EffectiveFrom`.
2. **Outstanding tokens (new step 7a).** The catalogue's USR-C4 row does not list `user_token`. C4 invalidates outstanding activation and reset tokens, without per-token audit records (D7, D13).

A third item is reconciled at the same time:

3. **`IdentityReactivated` emitters.** The frozen Audit workbook and `AuditEventCatalogue.cs` list its emitters as IDN-C4 and OPR-C1, but USR-C5 reactivates identities and emits it (D5). This is a **provenance correction**: USR-C5 is added to the emitter list in the code comment and recorded as Audit workbook change control. The event's definition (shape, refs, reason rule) does not change, so no version question arises.

The session-revocation change control's item 5 (*USR-C4 spelling*) is **resolved** by D4: `UserDeactivated` is the session code and `User deactivated` is the assignment reason.

### Acceptance Criteria

**USR-C4**

- **V1** Refused, writing nothing: the System actor; an inactive user; the caller themselves; an unknown user; a blank reason. A caller without `user.deactivate`, or a non-human caller, is refused by the pipeline.
- **V2** A pending user can be deactivated.
- **V3** The user is `Inactive` with stamp (now, caller); every identity that was active is `Inactive` with the same stamp.
- **V4** Role assignments: an active one ends at now; a future one gets an empty period at its own `EffectiveFrom`; an ended one and an already-revoked one are unchanged. Every revoked row has `RevokedBy` = System and reason `User deactivated`.
- **V5** Every active session is revoked with `RevokedBy` = System and code `UserDeactivated`. Sessions of other users are untouched.
- **V6** Every outstanding activation and reset token is invalidated at now. No `TokenInvalidated` record is written.
- **V7** Audit, all in one `OperationId`:
  - one `UserDeactivated` with the reason and exactly its Before/After;
  - one `IdentityDeactivated`, `RoleRevoked` and `SessionRevoked` per row changed;
  - each of those with `CausationId` = the `UserDeactivated` record;
  - no other event types.
- **V8** Atomicity: a fault injected after the cascade has begun leaves the user, identities, assignments, sessions and tokens exactly as they were, and writes no audit record.
- **V9** Afterwards the user cannot sign in, and a session that was live is refused on its next request.

**USR-C5**

- **R1** Refused, writing nothing: the System actor; an active user; an unknown user; a blank reason; an email held by another human who is not inactive. The pipeline refuses a caller without `user.reactivate`, or a non-human caller.
- **R2** The user is `Active` with no deactivation stamp. The identities this deactivation stamped are `Active`, and an identity carrying a **different** stamp stays `Inactive`.
- **R3** **Nothing is restored:** revoked assignments stay revoked, and the user holds no active assignment; revoked sessions stay revoked; invalidated tokens stay invalidated.
- **R4** Audit: one `UserReactivated` and one `IdentityReactivated` per identity reactivated, the latter caused by the former, all in one `OperationId`.
- **R5** Round trip: Active → Deactivated → Reactivated leaves the user able to receive a fresh grant (AUT-C1) and a fresh activation link (CRD-C7, for a pending user); pre-deactivation tokens still do not redeem.

**Concurrency and database**

- **K1** A grant and a deactivation of the same user, run concurrently in both orders, never leave an active or future assignment on an inactive user.
- **K2** GrantRole takes the target's row lock before checking status.
- **M1** The database refuses `Inactive` without a deactivation stamp, and `Active` with one, on both `app_user` and `user_identity`.
- **M2** The fixture that inserted an inconsistent row is corrected. The migration contains no data repair.

**Endpoints**

- **E1** Both routes answer `204` on success and `400 { error }` on each refusal. A permission refusal carries the permission message; no carrier gets `401`.
- **E2** Both routes are listed in the API documentation.

### Not decided here

- A **last-administrator** protection (see *The self rule*).
- ~~The Users-table UI for deactivate and reactivate, and how the list shows lifecycle status~~: see *USR-C4 / USR-C5 — the Users-table UI* and USR-Q2 Amendment 2.
- Agents (D11), and per-token audit records (D13).

---

## USR-C4 / USR-C5 — the Users-table UI

**Status:** Contract, awaiting owner review. The decisions (U1–U8) were settled by the owner on 2026-09-18. This is the UI story that USR-C4/C5 D1 deferred. It depends on USR-Q2 Amendment 2 (`status`).

### Requirement

An administrator deactivates and reactivates users from the Users table. The table shows who is inactive, and offers each row only what its lifecycle status and the caller's permissions allow. The consequences of each action are stated before it is confirmed. Neither action is a toggle: deactivation is a cascade, and reactivation restores nothing.

### Affordance, not authorization

> **Action visibility is a client-side affordance derived from the row's current status and caller permissions. It is not an authorization decision. The API remains authoritative and may refuse a mutation.**

A row may be stale: another administrator may have acted since it was read. Every refusal is shown word for word, through the existing error handling. The client branches on structured data (`status`, `activationPending`, the caller's permissions) and on status codes, **never on message text** (web client design v2).

### The caller's own row (U4)

> **The UI does not determine whether a row is the caller. Deactivate is therefore rendered according to row status and `user.deactivate`; self-deactivation remains a server-enforced refusal. The UI must display the server refusal using the existing status-code/error handling.**

This preserves USR-Q2's frozen rule, *"Whether a row is the signed-in user is out of scope"*, with `/me` identifying the caller by `UserIdentityId`. It does not reopen that rule. Exposing the caller's `UserId`, or an `isSelf` fact, would add an identity-resolution contract only to improve one menu item.

### The marker (U2)

An inactive row carries a visible **Inactive** marker beside the user's name, readable as text by assistive technology. An active row carries none. `activationPending` stays action-only, as today: there is no "Pending" marker.

### The action matrix (U3, U5)

Rows are Active or Inactive; the caller's permissions come from `GET /api/me`. *Hidden, never disabled*, and a row with no action has no Actions button, as in USR-Q2 Amendment 1.

| Row | Deactivate | Reactivate | Resend activation link | Reset password | Sign out everywhere | Manage roles |
| --- | --- | --- | --- | --- | --- | --- |
| **Active, pending** | `user.deactivate` | — | `user.create` | — | `session.revoke` | `role.read` |
| **Active, activated** | `user.deactivate` | — | — | `user.resetpassword` | `session.revoke` | `role.read` |
| **Inactive, pending** | — | `user.reactivate` | — | — | — | `role.read` |
| **Inactive, activated** | — | `user.reactivate` | — | — | — | `role.read` |

Each cell names the permission that shows the action; — means never shown.

> **Amended by *USR-C2 — the Edit profile UI* (U4).** Every row also offers **Edit profile** to holders of `user.update`, active and inactive alike. An inactive row therefore offers Reactivate, Manage roles and Edit profile. The table above is the original matrix; the amendment adds that column to every row.

- **Resend, Reset and Sign out everywhere are withheld from inactive rows.** Resend and Reset would be refused. Sign out everywhere would succeed and change nothing, because deactivation already revoked every session.
- **A pending user may be deactivated** (USR-C4 D9d).
- **The caller's own row** follows the matrix like any other (U4).

### Manage roles on an inactive user (U8)

The dialog still shows the assignments, and history on request. That includes the assignments deactivation revoked, which show `Revoked` by the System actor. **The Grant form is not shown for an inactive user**, because the server refuses a grant to one. The dialog receives the row's `status` to decide this. Revoke is still offered only on `Active` and `Future` assignments, and after USR-C4 an inactive user has none.

### The confirmations (U6)

Both are the existing confirmation-with-reason, and **a reason is required**.

**Deactivate *{name}*.** The description states the cascade and that it does not come back:

> They'll be signed out everywhere and won't be able to sign in. All of their current and future roles are revoked, and reactivating them later will not restore any of them. Any activation or password-reset link they have stops working.

Confirm: **Deactivate**. On success: *"{name} has been deactivated."*

**Reactivate *{name}*.** The description states what returns and what does not:

> They'll be able to sign in again, but they will have no roles: grant any access they need afresh. If they had activated their account, they sign in with their existing password. If they had not, send them a new activation link.

Confirm: **Reactivate**. On success: *"{name} has been reactivated."*

The copy claims no more than the server confirmed.

### Refresh (U7)

After a successful deactivation or reactivation, the client reads again:

- **the user list**, so the row's `status`, marker and actions are the server's. Until now no row action changed a listed field; these two do.
- **that user's role assignments**, because deactivation revoked them.

### The recovery paths, after reactivation (U5)

- **A pending user** comes back without a credential and without a link (USR-C4 invalidated it; USR-C5 restores nothing). The row reads Active and pending, so *Resend activation link* is offered again (CRD-C7).
- **An activated user** comes back with their credential, which §11.8 preserves, and can sign in with their existing password. They hold no roles. The row offers *Reset password*, as for any activated user.

### Acceptance Criteria

- **U-M1** A table-driven test over the matrix covers every combination of:
  - lifecycle: active or inactive, and pending or activated;
  - permissions: none, each relevant one, and read-only (`user.read` with `role.read`).

  It asserts the exact menu of every row.
- **U-M2** A row with no permitted action has no Actions button.
- **U-M3** An inactive row shows the Inactive marker; an active row does not.
- **U-D1** Each dialog shows the copy above. A blank reason sends nothing. The request carries exactly the reason to the right route.
- **U-D2** A server refusal, including self-deactivation, is shown word for word and the dialog stays open.
- **U-D3** Success announces the outcome, and refreshes the list and that user's role assignments.
- **U-R1** Manage roles on an inactive user shows the assignments and no Grant form. On an active user, Grant is shown as before (with `role.grant`).
- **U-A1** No accessibility violations with either dialog open.
- **U-B1** Browser, in the dev stack, each state change approved by the owner:
  - Active → Deactivate → the list refreshes → the Inactive marker shows, with only Reactivate and Manage roles offered;
  - → Reactivate → Active, with Resend or Reset per `activationPending`.

  Both recovery paths are shown: a pending user gets a new activation link, and an activated user signs in with their existing password.

### Not decided here

- A status **filter** or **sort** on the list.
- A **"Pending"** marker.
- ~~The **ID reconciliation**~~: done separately. See *Reconciliation: USR-Q1 and USR-Q2*. The list is `USR-Q2`.
- Whether a returning, activated user should be made to change their password. The frozen model preserves the credential (§11.8), and no rule forces a change today.

---

## USR-C2 — Update User Profile, and USR-Q1 GetUser (narrow v1)

**Status:** Contract frozen 2026-09-18 by owner decision (G1–G10, with the G3 and G8 refinements). The owner confirmed three further points: normalisation owned by the domain; USR-C1 not amended and no database name constraints; `POST` for the command. This is story 1, the backend. The UI (G9) is story 2.

### Requirement

An administrator corrects a user's first name, last name and display name. Email and lifecycle status are not touched: each has its own command (USR-C3; USR-C4 and USR-C5). The frozen catalogue's anti-features list forbids a generic `UpdateUser` (*"four commands, four meanings"*). To edit names, a client needs their current values, so a narrow single-user read, `USR-Q1 GetUser`, is introduced with exactly those fields.

### USR-C2 UpdateUserProfile

| | |
| --- | --- |
| **Caller** | An administrator holding **`user.update`** (G1). Not human-only: the catalogue does not mark `user.update` so, and the command is not added to the human-only list. |
| **Inputs** | `UserId`, `FirstName`, `LastName`, `DisplayName`. **No reason** (G4): the catalogue has none, and `UserProfileChanged` has `ReasonRequired: false`. The row actions' convention of a required reason does not apply here. |
| **Target** | Any existing user **except the System actor** (G8). Lifecycle status does not matter: an **inactive user's profile may be corrected** (G7). |
| **Touches** | `app_user.first_name`, `last_name` and `display_name` only. Not email, status, identities, credentials, sessions or role assignments. |
| **Result** | `204`, with no body. |

#### The System actor (G8)

> **USR-C2 must reject attempts to modify the System actor.**

This is a rule of the command, enforced explicitly in its handler, and not left to the database. The AU7 guard, which freezes the System row in PostgreSQL, stays as defence in depth. The wording a caller sees is the application's: *"This user's profile cannot be changed."*, served as `400`. The wording is not part of the domain rule.

#### Name rules (G3)

These apply to each of `FirstName`, `LastName` and `DisplayName`:

1. **Normalised** by removing leading and trailing whitespace. Interior whitespace is kept as it is. **Confirmed by the owner:** trim, then validate, then store the trimmed value. The domain owns this, so every client behaves the same way; the web form's `.trim()` becomes a convenience, not the rule.
2. **Required and non-blank** after normalisation.
3. **No control characters**, defined as .NET `char.IsControl`. That is the definition `EmailAddress` uses, whose ranges the email constraint was verified to agree with.
4. **At most 100 characters**, counted in **Unicode code points** after normalisation. That is what PostgreSQL's `char_length` counts, so a later database check could state the same rule exactly.

The rules live in the domain, in `User.UpdateProfile`, and the values stored are the normalised ones. A violation is refused with a message naming the field. Examples: *"First name is required."*, *"Display name must be at most 100 characters."*, *"Last name must not contain control characters."*

**Not amended here, confirmed by the owner:** USR-C1 (create) and the database. `User.CreateHuman` still checks only that first and last names are non-blank. Adopting these rules at creation would change USR-C1's behaviour, so it is a separate decision (Known Gaps, *Name rules differ between USR-C1 and USR-C2*). **The temporary state is intentional:** a value may be valid at creation under USR-C1 and invalid for a later edit under USR-C2. That is a known inconsistency, not an accidental one. There are no database CHECK constraints for the same reason: they would constrain creation too.

#### No change (G5)

If the normalised values equal the stored ones, the command succeeds with `204`. **Nothing is written and nothing is audited.** `User.UpdateProfile` already returns `false` in this case.

#### Audit

`UserProfileChanged` (existing; no catalogue change), on a change only. The primary is `User`. Before and After hold exactly `FirstName`, `LastName` and `DisplayName`, the paths the catalogue marks as personal data of the primary subject. There is no reason.

#### Concurrency (G6)

**Last write wins in v1.** Two administrators saving the same user in turn leave the second's values. Each save's Before/After is audited, so an overwritten edit is visible in the trail. Optimistic concurrency (a version or an expected-values check) is deferred and recorded (Known Gaps, *USR-C2 is last-write-wins*).

USR-C2 does not take the D6 row lock. That lock orders commands that depend on lifecycle status, and a profile edit does not (G7).

### USR-Q1 GetUser — narrow v1 (G2)

> **`GET /api/users/{userId}` returns exactly `userId`, `firstName`, `lastName` and `displayName`, and requires `user.read`.**

- **Why these four and nothing else.** USR-C2's client must show the current names before editing them. The list row deliberately excludes `FirstName` and `LastName` (USR-Q2, *Excluded fields*: *"DisplayName serves recognition"*). This read exists for that one use.
- **Not the catalogue's full GetUser.** The frozen catalogue gives GetUser status, actor type, identities and current assignments. **None of those is added here.** *(Later: v2 adds `email`, `status` and `activationPending`, and the USR-Q1 composition amendment moves identities and assignments out of USR-Q1. See* USR-Q1 GetUser v2 and the User detail page*.)* Each would need its own evidence, under the same rule as the list's fields (USR-Q2, *Adding a field later*). This is `USR-Q1` v1, narrow on purpose.
- **Who.** Human users only, as the list scopes. The System actor, and an unknown user, are refused as unknown: `400`, *"The user does not exist."*, following the convention of the reads and commands addressed by `{userId}`.
- **Authorization and audit.** It declares `Required("user.read")`. Refused, it is `400` (Known Gaps, *Authorization failures are not distinguishable from validation failures*); with no carrier, `401`. It is **not audited**: reads are not events (architecture §11), as USR-Q2.
- **Nullability.** `firstName` and `lastName` are non-null for humans, by `ck_app_user_human_names`. `displayName` is non-null.

### Endpoints

| Route | Body | Success | Refusals |
| --- | --- | --- | --- |
| `GET /api/users/{userId}` | — | `200` with exactly `{ userId, firstName, lastName, displayName }` | `400 { error }` for an unknown user, the System actor, or a missing `user.read`; `401` with no carrier |
| `POST /api/users/{userId}/profile` | `{ "firstName", "lastName", "displayName" }` | `204`, no body, whether or not anything changed | `400 { error }` for a missing or invalid field, the System actor, an unknown user, or a missing `user.update`; `401` with no carrier |

**`POST`, confirmed by the owner:** this is a command, not a generic resource replacement, and every existing command endpoint uses `POST`. It is covered by the cross-site guard. A body missing a field is `400` before dispatch, as the existing endpoints treat missing inputs.

### Change control (G1)

The frozen catalogue's USR-C2 row is *"Admin or self"*, with permission *"user.update / self"*. Under pipeline behaviour 3 a command carries one fixed permission, so the two cannot be one command. This is the problem the SES-C4 split solved. This story delivers **the administrator command only**. A self-service profile command, if one is wanted, is its own command with its own authorization, delivered with a My account page. It is recorded under Known Gaps, *USR-C2 change control*. No workbook is edited.

### Acceptance Criteria

**USR-C2**

- **P-A1** A caller with `user.update` changes all three names. The stored values are the normalised ones. One `UserProfileChanged` carries exactly those three fields in Before and After, with no reason.
- **P-A2** Normalisation: leading and trailing whitespace is removed and interior whitespace kept. A value that is only whitespace is refused as required.
- **P-A3** Each rule refuses, naming the field, and writes nothing: a blank value; a control character (tab, newline, U+0000, U+009F); 101 code points. **Exactly 100 code points is accepted, including astral characters that .NET counts as two units.**
- **P-A4** No change, including a submission that differs only by surrounding whitespace, is `204` with no write and no audit record.
- **P-A5** An inactive user's profile can be changed, and their status stays `Inactive`.
- **P-A6** The System actor is refused by the command and nothing is written. An unknown user is refused. A caller without `user.update` is refused by the pipeline.
- **P-A7** Email, status, identities, credentials, sessions and role assignments are unchanged by a profile update.
- **P-A8** Last write wins: two updates in turn leave the second's values, and both are audited.

**USR-Q1 GetUser**

- **G-A1** It returns exactly the four fields, with the stored values.
- **G-A2** It requires `user.read`; no carrier is `401`. An access reviewer may read it.
- **G-A3** The System actor and an unknown user are refused as unknown.
- **G-A4** It writes no audit record.

**Endpoints**

- **E-A1** Both routes behave as the table above, and are listed in the API documentation.

### Not decided here

- **The UI** (story 2): an Edit profile row action, the dialog prefilled from GetUser, and the shared unsaved-changes guard (architecture, *Unsaved changes*), which this would be the first form to use.
- **A self-service profile command** (G1).
- **USR-C1 adopting the name rules**, and database CHECK constraints for them.
- **GetUser's other catalogue fields**: status, actor type, identities and assignments.
- **Optimistic concurrency.**

---

## USR-C2 — the Edit profile UI (story 2)

**Status:** Contract frozen 2026-09-18 by owner decision (U1–U7). The owner also confirmed option (a) for Create user and a status note in the architecture document after the implementation lands. It builds on story 1 (#65): USR-C2 `POST /api/users/{userId}/profile` and USR-Q1 GetUser `GET /api/users/{userId}`.

### Requirement

An administrator edits a user's first, last and display name from the Users table. The form loads the current values, protects unsaved changes, and leaves every naming rule to the server. This is also where the web client's first **editing** form arrives, and with it the form library and the unsaved-changes guard that `docs/frontend-architecture.md` §12 set out for exactly this moment.

### The decisions

| # | Decision |
| --- | --- |
| **U1** | **Adopt React Hook Form now, with Zod.** This is the first form that edits data, exactly as W3 anticipated (*"the library arrives with that form, not ahead of it"*). `formState.isDirty` drives the unsaved-changes guard. **No W3 amendment:** the plan is being followed as written. |
| **U2** | **Build `useUnsavedChangesGuard` as a shared hook**, use it in Edit profile, **and retrofit Create user** in this story, since the approved design required the guard there. The Grant role form is a follow-up. |
| **U3** | **Client validation is presence only.** Each field must be a non-empty string. There is no trimming, no control-character check, no 100-character check, and no HTML `maxLength`. The server is authoritative. |
| **U4** | **Edit profile** for holders of `user.update`, on **active and inactive** rows. This explicitly amends the frozen USR-C4/C5 UI matrix. |
| **U5** | The dialog **opens by reading GetUser**. It shows a loading state, and a stated error with **Try again** if the read fails. The three fields are filled with the returned values. It submits exactly what was typed; the server normalises. |
| **U6** | **Every `204` is a successful save.** It announces *"Profile saved for {new display name}."* and refreshes both the list and that user's GetUser query. The client does not try to tell a change from a no-op. |
| **U7** | **Everything goes through the shared guard:** Cancel, Escape, the close button, in-app navigation, and reloading or closing the tab. It asks **"Discard changes?"** with **Keep editing** and **Discard**. It is off after a successful save. |

### The form lifecycle (U1)

```text
GetUser  →  React Hook Form  →  reset(serverValues)  →  formState.isDirty
        →  useUnsavedChangesGuard(isDirty && !saved)
        →  Zod: presence only  →  POST /api/users/{userId}/profile
```

React Hook Form owns the form's lifecycle and its dirty state. Zod carries only the presence check that §12 allows. **Neither replaces the domain's rules**: the server still trims, then validates, then stores (USR-C2 G3). The form is populated with `reset(serverValues)`, so the loaded values are the form's defaults, and `isDirty` means "differs from what the server returned".

**Dependencies.** `react-hook-form` and `@hookform/resolvers` (for `zodResolver`), pinned to exact versions as every dependency is. `@hookform/resolvers` must support Zod 4 (the app uses `zod` 4.6.5). The version is verified when the dependency is added.

### Why presence only (U3)

§12 says *"A schema never rejects input the server would accept."* The server's rules cannot be mirrored in the browser without breaking that:

- **Trimming differs.** JavaScript's `trim()` removes U+FEFF, which .NET's `Trim()` keeps; .NET removes U+0085, which JavaScript keeps. A value of only `"\uFEFF"` is blank to the browser and accepted by the server.
- **HTML `maxLength` counts UTF-16 units.** `maxLength="100"` would block 100 astral characters (200 units), which the server accepts.

So the client asks only that each field is present, as a non-empty string. Everything else is the server's, and its `400` is shown **word for word, at the form level**. The message names the field, but §7 forbids branching on message text, so it is not mapped to a field.

### The unsaved-changes guard (U2, U7)

`useUnsavedChangesGuard(dirty: boolean)`, in `shared/forms`, is the shared hook §12 describes. It takes a boolean, so it is independent of how a form computes dirtiness.

- **In-app navigation** while dirty is caught with React Router's `useBlocker` (the app uses a data router, `createBrowserRouter`). It opens `ConfirmAction`: **"Discard changes?"**, with **Keep editing** and **Discard**.
- **Reloading or closing the tab** while dirty triggers `beforeunload`, and the browser shows its own prompt.
- **Closing a dialog** while dirty, whether by Cancel, Escape or the close button, asks the same "Discard changes?" first. **Discard** closes; **Keep editing** returns to the form with the typed values intact.
- It is **off after a successful save**, and while any navigation that follows is in progress.

**Create user (retrofit), option (a), confirmed by the owner.** The same guard, on the Create user page. Create user stays a `useState` form: RHF is introduced for new editing forms, and this story is not a reason to refactor an existing one. Its dirty flag is computed explicitly as **"any editable field differs from its initial empty value"**, covering **every** editable field (first name, last name, display name, email, initial username), not only those the server requires. The guard answers *"has the user changed anything?"*, and must not inherit USR-C1's client/server validation differences.

**The guard is independent of any form library:** it accepts a `boolean`, never a React Hook Form object. Until the existing forms are migrated, the coexistence is deliberate:

```text
Edit profile  → React Hook Form + Zod
Create user   → useState + its existing Zod pattern
Grant role    → useState + its existing pattern (no guard yet)
```

### The action matrix, amended (U4)

> **Amends *USR-C4 / USR-C5 — the Users-table UI*, "The action matrix".** An inactive row gains **Edit profile**. The statement *"An inactive row offers only Reactivate and Manage roles"* becomes *"only Reactivate, Manage roles and Edit profile"*. The reasons still hold for the actions withheld: Resend, Reset and Sign out everywhere would be refused, or would change nothing.

Edit profile is shown to holders of `user.update` on every row, active and inactive, pending or not (USR-C2 G7). The System actor is never a row. Hidden, never disabled.

### The dialog (U5, U6)

- **Title:** *Edit profile for {display name}*. The fields are **First name**, **Last name** and **Display name**, each built with `FormField`. The buttons are **Save** and **Cancel**.
- **Opening** reads GetUser (a query key per user, under `userKeys`). While it loads, the form is not shown. If the read fails, the dialog states the error and offers **Try again**, the shared `ErrorState`'s retry, as every error state in the app does. That includes a `400` such as *"The user does not exist."* from a stale row, which is shown word for word.
- **Saving** sends exactly the three typed values. The button is busy while the request is in flight, and a repeated press sends once. On `204` the dialog closes, the guard is cleared, the page announces *"Profile saved for {new display name}."*, and the client **refreshes** the list and that user's GetUser query. *Corrected during implementation:* the announcement names the display name **trimmed of surrounding whitespace**, as it will be stored (typing `"  Countess Lovelace "` announces *"Profile saved for Countess Lovelace."*). Only the announcement is trimmed. **The request still carries exactly what was typed**, and the refreshed list shows the stored form.
- **A refusal** keeps the dialog open, with the typed values intact and the server's message shown word for word.
- **Focus** returns to the row's Actions button when the dialog closes, as for the other row actions.

### Acceptance Criteria

- **UI-1** Edit profile appears for `user.update` on active and inactive rows, and never without it. The USR-C4/C5 matrix test is updated to the amended table.
- **UI-2** Opening reads GetUser. The form shows the returned values; loading shows no form; a failed read shows the error and a **Try again** that reads again.
- **UI-3** Presence only. An empty field sends nothing and is flagged. A whitespace-only value, `"\uFEFF"`, a control character and 101 characters **are sent**, and the server's refusal is shown word for word. There is no `maxLength` attribute on the inputs.
- **UI-4** Save sends exactly the typed values (untrimmed) to `POST /api/users/{userId}/profile`. On `204` it announces, refreshes the list and that user's GetUser query, and closes. It is busy while sending, and sends once.
- **UI-5** Guard in the dialog: once dirty, Cancel, Escape and the close button each ask "Discard changes?". Keep editing keeps the values; Discard closes. When not dirty, they close without asking. After a successful save there is no prompt.
- **UI-6** Guard on navigation: a dirty form blocks in-app navigation with the same prompt, and `beforeunload` is registered while dirty and removed when clean.
- **UI-7** Create user: the guard applies once **any** editable field differs from empty, each field on its own, including email and initial username. It is off again when every field is back to empty, and after a successful create.
- **UI-8** No accessibility violations with the dialog open, and with the discard prompt open.
- **UI-9** Browser, in the dev stack, with each state change approved by the owner:
  - edit a profile, see the list refresh;
  - try to leave with unsaved changes;
  - see a server refusal shown word for word.

### After it lands

- **A status note in `docs/frontend-architecture.md` §12, beside W3** (confirmed by the owner). It says React Hook Form was introduced with USR-C2 as the first data-editing form, consistent with W3's deferred form-library decision. W3 itself is not rewritten: the history is kept and the current state recorded.

### Not included

- The Grant role guard.
- Migrating the other existing forms to React Hook Form.
- Copying the server's name rules into the client.
- Changes to USR-C1, database name constraints, lifecycle changes, and optimistic concurrency.

---

## CRD-C4 and SES-C4 (self) — the My account page

**Status:** Contract frozen 2026-09-18 by owner decision (M1–M10). **UI only:** both backends exist and are unchanged. CRD-C4 is `POST /api/account/change-password`, and SES-C4's self form is `POST /api/account/sign-out-everywhere`. See *A5 closed — CRD-C4 revokes other sessions* and *Session revocation — reason semantics and SES-C4 command split* under Known Gaps.

### Requirement

A signed-in person manages their own account from a **My account** page. They can change their password, sign out their other sessions, or sign out everywhere, including here. It is the web client's first page that is about the caller rather than about someone they administer, so it needs **no permission**: every authenticated caller may reach it, and the server decides whether each operation is allowed.

### The decisions

| # | Decision |
| --- | --- |
| **M1** | **Built despite the missing attempt limit.** CRD-C4 does not limit incorrect current-password submissions. The page is not blocked on that, because the endpoint already accepts the operation from any authenticated session. Attempt or rate limiting is a **separate security decision**, recorded under Known Gaps. Building the UI does **not** approve unlimited attempts as a security design. |
| **M2** | **`/account`, inside the signed-in shell.** A **My account** link sits in the shell's footer beside **Sign out**, supplied by `app/` exactly as the Sign out button is. **No sidebar entry:** the sidebar lists areas shown by permission, and this page belongs to every caller. |
| **M3** | **Ownership follows the backend's split.** `modules/platform/account` owns the page and change password: its API operation, hook and schema. `modules/platform/auth` owns sign-out-everywhere, which is session lifecycle: the API operation and a hook it exports. The page uses that hook and never auth's API operation (frontend architecture §6). |
| **M4** | **Three fields:** current password, new password, and a **confirmation that stays in the browser** and is never sent. The client checks only that each field is filled and that the confirmation matches. The server owns the password policy. There is **no show-password toggle**. |
| **M5** | On success the three fields are cleared and the page announces: ***"Your password has been changed. Any other sessions on this account have been signed out; you remain signed in here."*** This stays true whether or not another session existed, which the `204` does not say. |
| **M6** | **The shared unsaved-changes guard.** The form is dirty while **any** of the three fields is non-empty. A successful change clears the fields, so the guard is off. |
| **M7** | **Two sign-out actions, each behind `ConfirmAction`.** **Sign out other sessions** sends `keepCurrentSession: true`, stays on the page and announces. **Sign out everywhere** includes this session, then goes to sign-in. |
| **M8** | **No reason is asked for, and none is sent.** The server records its default explanation. |
| **M9** | **A failure is not treated as success.** A non-401 failure of either sign-out action is shown, and the caller stays signed in and on the page, because what was revoked is not known. A `401` means this session has already ended, so it is handled as signed out. |
| **M10** | **Change password is shown to every caller for now.** Only local identities exist today, and the server refuses any other kind with its uniform message. **To revisit** when external identities arrive (IDN-C1), since `/me` would then have to say which kind signed in. Recorded under Known Gaps. |

### Where the code lives (M3)

```text
modules/platform/account
├── /account page (My account)
└── change password ──► POST /api/account/change-password     (CRD-C4)
        │
        └── uses auth's hook, never auth's API operation
                │
modules/platform/auth
└── sign out everywhere ──► POST /api/account/sign-out-everywhere   (SES-C4, self)
```

`/account` is **chosen by the client**. It is not a backend contract path: no email links to it. It must not be confused with the account module's **public** routes (`/activate`, `/forgot-password`, `/reset-password`). Those stay in the public shell. `/account` is composed into the **signed-in** shell, so `RequireAuth` guards it and an unauthenticated visit goes to sign-in with a return path.

### The page

- **Title:** *My account*. It has two sections, **Change password** and **Sessions**, each a labelled region.
- **The footer link (M2).** **My account** links to `/account`, is marked as the current page when on it, and closes the phone sidebar sheet when followed, as the brand link does. The shell still does not import a module: `app/` passes the link into the footer slot beside the Sign out button.

### Change password (M4, M5, M6)

- **Fields**, each built with `FormField`:
  - **Current password**, with `type="password"` and `autoComplete="current-password"`;
  - **New password**, with `autoComplete="new-password"`;
  - **Confirm new password**, with `autoComplete="new-password"`, never sent.
  - The button is **Change password**. No `minLength` or `maxLength` attributes.
- **The form lifecycle.** React Hook Form with a Zod resolver, starting empty. The schema checks presence and the confirmation match **only**.
  - An empty field is flagged and nothing is sent.
  - A mismatch shows ***"The passwords do not match."***, the reset page's wording, and nothing is sent.
- **The request** carries exactly `{ currentPassword, newPassword }`, as typed and untrimmed. The button is busy while the request is in flight, and a repeated press sends once.
- **On `204`**, all three fields are cleared, the guard turns off, and the M5 sentence is announced in a polite live region. Nothing in the query cache changes, because `/me` describes the same caller as before.
- **A `400`** is shown word for word at the form level. That includes the uniform *"The password could not be changed."*, a policy refusal, and a reuse refusal. The typed values are kept so the caller can correct them, and the form stays dirty.
- **A `401`** means this session has ended. The app's normal handling applies: the caller is signed out and sent to sign-in.

### Sessions (M7, M8, M9)

The section says in one sentence what each action does. It lists no sessions, because that needs SES-Q2, which is not built.

| Action | Confirmation | Request | On `204` |
| --- | --- | --- | --- |
| **Sign out other sessions** | *"Sign out other sessions?"* — *"Every other session on your account will be signed out. You stay signed in here."* — confirm **Sign out other sessions** | `{ keepCurrentSession: true }` | Stay on the page; announce ***"Your other sessions have been signed out."*** |
| **Sign out everywhere** | *"Sign out everywhere?"* — *"Every session on your account will be signed out, including this one. You will need to sign in again."* — confirm **Sign out everywhere** | `{ keepCurrentSession: false }` | Signed out locally (the query cache is cleared), then sent to sign-in, replacing the history entry |

- **No `reason` field** is sent (M8).
- **A non-401 failure** (M9), whether a `4xx`, a `5xx`, a network failure or a contract violation, is shown on the page. Authentication state is **not** changed, and the caller stays on the page. **This differs from the Sign out button on purpose.** Sign out leaves the page even when its call fails, because the person asked to leave. Here, a failure means we cannot say what was revoked.
- **A `401`** means this session had already ended. It is handled as signed out and goes to sign-in, the same outcome as a successful **Sign out everywhere**.
- **The guard does not fire once the session has ended.** Signing out everywhere, or the Sign out button, while the password form is dirty does **not** ask "Discard changes?". The session is gone, and the typed passwords are discarded with it.
- **Before the session ends**, leaving `/account` by an in-app link while the form is dirty does ask, as M6 requires.

### Acceptance Criteria

- **UI-1** `/account` renders *My account* for an authenticated caller holding **no** permissions. An unauthenticated visit goes to sign-in with `/account` as the return path.
- **UI-2** The footer shows **My account** beside **Sign out** for every signed-in caller. It links to `/account` and is marked current there. It is not in the primary navigation.
- **UI-3** Change password:
  - an empty field, or a mismatched confirmation, sends nothing;
  - otherwise exactly `{ currentPassword, newPassword }` is sent, untrimmed and without the confirmation;
  - the button is busy while sending, and sends once;
  - there is no `minLength` or `maxLength`, and the `autocomplete` values are as specified.
- **UI-4** On `204` the fields are cleared and the M5 sentence is announced word for word. A `400` shows the server's message word for word and keeps the values.
- **UI-5** Guard:
  - while any field is non-empty, in-app navigation asks "Discard changes?";
  - `beforeunload` is registered while dirty and removed when clean;
  - after a successful change there is no prompt.
- **UI-6** Sign out other sessions:
  - the confirmation appears, and Cancel sends nothing;
  - confirming sends `{ keepCurrentSession: true }` with no `reason`;
  - on `204` the caller stays and the announcement is made.
- **UI-7** Sign out everywhere:
  - the confirmation appears, and Cancel sends nothing;
  - confirming sends `{ keepCurrentSession: false }` with no `reason`;
  - on `204` the caller is signed out and at sign-in;
  - with the password form dirty, no discard prompt appears.
- **UI-8** Failures:
  - a `500` or network failure from either sign-out action is shown, and the caller stays signed in on `/account`;
  - a `401` from either leads to sign-in.
- **UI-9** No accessibility violations on the page, with each confirmation open, and with the discard prompt open.
- **UI-10** Browser, in the dev stack, with each state change approved by the owner:
  - change a password, using an account other than Ada's so that her deliberate must-change-password state is kept;
  - see a refusal shown word for word;
  - sign out other sessions, and see a second browser session ended;
  - sign out everywhere, and land at sign-in.

### After it lands

- **An amendment note in `docs/frontend-architecture.md` §5**, beside W4 and the Administration shell amendment. It records that the footer carries a My account link for every caller, that `/account` is a signed-in, client-chosen path, and that the sidebar stays permission-driven areas only.

### Corrected during implementation

- **The shell's landmarks (owner decision, 2026-09-18).** UI-9's whole-page audit found that the shell's brand, the caller's display name and the footer controls sit outside every landmark. The brand and name predate this story; its My account link added a third. The existing shell tests had audited only the render container, so axe's *region* rule never ran. By owner decision both are fixed in this story: the brand is in the banner, and the footer is an **Account** navigation holding the caller's name, My account and Sign out. `app/accessibility.test.tsx` now audits the whole page. It is recorded in `docs/frontend-architecture.md` §5 and §15.
- **Where a sign-out failure is shown.** It is shown inside the confirmation, which stays open beside the action that failed, as `ConfirmAction` does for every refusal. The caller is still on `/account` and still signed in (M9). A request that never reaches the server shows the API boundary's *"The server could not be reached."*.
- **The unsaved-changes guard's focus, at phone width (owner decision, 2026-09-18).** UI-10 found a defect in the shared guard from #66. The link followed from a dirty form was in the sidebar sheet, which closes as a link is followed. Keep editing then had nowhere to return focus and left it on the document body. It is fixed in this story, and applies to Create user as well: if the element the person was on has gone, focus returns into the main content (`#main`, where the skip link sends it). jsdom cannot reproduce the defect, so the fix is proved in the browser. The guard's test checks the outcome.

### Not included

- Attempt or rate limiting for CRD-C4 (M1).
- External identities (M10).
- Editing your own profile, which needs its own command (*USR-C2 change control*).
- A session list, which needs SES-Q2.
- Enforcing must-change-password at sign-in (*MustChangePassword is recorded but not enforced*).
- Migrating the existing forms to React Hook Form.
- A show-password toggle.

---

## CRD-C4 — limiting current-password attempts per session

**Status:** Contract frozen 2026-09-18 by owner decision (L1–L8, and the mechanism under *How it is built*). It closes the Known Gap *CRD-C4 does not limit current-password attempts*, whose M1 restatement made it a separate security decision.

### Requirement

Someone holding an authenticated session, stolen or left unattended, must not be able to guess the account's current password through CRD-C4 without limit. The control is scoped to **the session showing the suspicious behaviour**. It never locks the credential, never touches the account's other sessions, and never changes sign-in's own lockout.

This complements A5. A **successful** password change ends the other sessions. **Repeated failed** attempts end the session that is making them.

### The decisions

| # | Decision |
| --- | --- |
| **L1** | **End the session after N consecutive wrong current passwords.** Not a credential lockout: no `AccountLocked`, no change to `FailedAttemptCount` or `LockedUntil`, and no effect on other sessions or on sign-in. |
| **L2** | **N is the effective `MaxFailedLoginAttempts`** (5 at baseline). *This setting governs both anonymous sign-in failures and the maximum consecutive failed current-password attempts permitted within one authenticated session.* The reuse is deliberate: a tenant that lowers it to 3 gets 3 for both. |
| **L3** | **A successful password change resets the count to 0.** Revoking the session ends the count with the session, because a revoked session cannot be used again. There is no time-based decay. |
| **L4** | **The Nth wrong attempt returns `401` and clears the carrier cookie.** Attempts 1 to N−1 return the existing uniform `400` (*"The password could not be changed."*). From the Nth on, the session cannot be used. |
| **L5** | **The new controlled revocation code is `PasswordChangeAttemptsExceeded`**, recorded as change control. The revocation is audited as `SessionRevoked`, with the account holder as actor and the code in Before/After. The wrong attempts themselves stay unaudited, consistent with CRD-C4 and AUD-D42. |
| **L6** | **The counter is `user_session.FailedPasswordChangeAttempts`**: an `INTEGER NOT NULL DEFAULT 0`, with `CHECK (>= 0)`. It belongs to the session, because the threat is a compromised session, not a compromised credential. It is purged with the session. |
| **L7** | **No UI change.** A `401` already means local sign-out, then sign-in (My account, M9). There is no special explanation in this story. |
| **L8** | **A refusal because the credential is locked counts too.** This is for side-channel consistency. Today a locked credential and a wrong password return the same message and pay the same hash cost, so time does not tell them apart. If only a wrong password wrote the counter, the extra write would make the two distinguishable by timing. Counting both keeps them alike. A locked credential cannot be changed in any case, so a session repeatedly trying is just as suspicious. |

### What counts, what resets

| CRD-C4 outcome | Counter | Response |
| --- | --- | --- |
| Wrong current password | **+1**, committed even though the command refuses | `400` below N; at N, **session revoked** and `401` |
| Credential locked (L8) | **+1**, as above | as above |
| Current password right, new password refused (policy or reuse) | unchanged | `400`, as today |
| Success | **reset to 0** | `204`, as today |
| The session is no longer active once the lock is held (see below) | unchanged | `401` |

"Consecutive" means consecutive **within the session**. A refusal of the new password proves knowledge of the current one, but it is not a success, so it neither counts nor resets.

**The counter's scope.** The counter represents consecutive failed current-password attempts **within the authenticated session**. It is not an account-wide counter and does not contribute to credential lockout. `MaxFailedLoginAttempts` is shared as the threshold (L2), but this is **not** a second account-lockout mechanism.

**CRD-C4 does not produce `AccountLocked`.** The only producer of `AccountLocked` remains SES-C1, sign-in.

### How it is built

**The invariant:** *a failed current-password attempt that is counted must commit the counter increment, even though the command itself is refused.*

1. **The refusal must commit.** CRD-C4 refuses today by throwing inside its transaction, which would roll the counter back with everything else. The wrong-password and locked branches instead **return an outcome**: `Refused` or `SessionEnded`. The transaction then commits the counter, and the revocation and its record with it. The endpoint maps `Refused` to the unchanged uniform `400`, and `SessionEnded` to `401` with the cookie cleared. The other refusals (no identity, not local, not active; the new password's policy and reuse checks) stay as they are.
2. **Atomic: exactly one request crosses N.** The handler first takes the **`app_user` row lock**, using the established *Serialising commands on one user* pattern (`IUserRepository.FindForUpdateAsync`, `docs/architecture.md`). It reads the clock after the lock, then loads the session and **re-checks that it is still active**. Two requests on one session at N−1 are therefore serialised: the first crosses N and revokes the session, and the second finds it revoked and answers `401` without counting. The same lock orders a wrong attempt against a concurrent success, and against USR-C4's cascade. **The same serialisation point governs competing password-change attempts and competing state-changing operations on the user.**

   **The lock must actually be held by the database transaction for the whole decision and update sequence:** lock, then clock, then session re-check, then credential evaluation, then increment or revoke, then commit. A lock call that exists is not the guarantee. The PostgreSQL integration tests (AC-8) must prove the behaviour under real concurrent transactions, not assert that the lock is requested.
3. **The revocation** uses `UserSession.Revoke(now, caller, PasswordChangeAttemptsExceeded)` and the shared `SessionRevocations.Declare`. The actor is the account holder. The audit Reason is the explanation *"Too many incorrect current passwords while changing the password"*. The code reaches the trail through Before/After (R1).
4. **Audit declarations.** `ChangePasswordCommand` already declares `SessionRevoked` (A5), so no catalogue seed changes and `AuditEventCatalogue.Version` is not bumped.
5. **The migration** adds the column with its default and CHECK. Existing sessions start at 0.

### Acceptance Criteria

- **AC-1** Wrong current password below N: the uniform `400`, and the counter is **persisted** (read back in a fresh context) as 1, 2, … N−1. The credential's `FailedAttemptCount` and `LockedUntil` are unchanged. No audit record is written.
- **AC-2** The Nth wrong attempt:
  - `401`, and the response clears the carrier cookie;
  - the session is revoked with code `PasswordChangeAttemptsExceeded`, `RevokedBy` = the account holder;
  - exactly one `SessionRevoked` is written, with the explanation above and the code in After;
  - any further request on that carrier is `401`.
- **AC-3** The blast radius:
  - the user's other sessions stay active;
  - the credential is not locked, and no `AccountLocked` is written;
  - sign-in with the right password still works.
- **AC-4** Policy: with an effective `MaxFailedLoginAttempts` of 3, the session is revoked on the 3rd wrong attempt, not the 5th.
- **AC-5** Reset: N−1 wrong attempts, then a success, sets the counter to 0. N−1 further wrong attempts do not revoke.
- **AC-6** A correct current password with a refused new password (policy or reuse) leaves the counter unchanged.
- **AC-7** (L8) A refusal because the credential is locked counts, and the Nth revokes.
- **AC-8** Concurrency, against PostgreSQL, with real concurrent transactions:
  - at N−1, two concurrent wrong attempts on one session produce **exactly one** revocation and **exactly one** `SessionRevoked`, and both answer `401`;
  - at N−2, two concurrent wrong attempts both count, and exactly one crosses.
- **AC-9** The database refuses a negative `FailedPasswordChangeAttempts` (CHECK).
- **AC-10** The web client: My account's existing test already asserts that a `401` from change-password leads to sign-in. No UI change.
- **AC-11** Browser, in the dev stack, with each state change approved by the owner: N wrong current passwords (typed by the owner) land on sign-in. The session row shows the code, and one `SessionRevoked` is in the trail.

### Change control

No workbook is edited. The following are outstanding against the frozen documents:

- the UM entity model gains `user_session.FailedPasswordChangeAttempts`;
- the `RevocationReason` vocabulary gains `PasswordChangeAttemptsExceeded` (*Session revocation — reason semantics*, item 4);
- `MaxFailedLoginAttempts` gains its second meaning (L2).

### Implementation notes

- **"Active" under the lock is the pipeline's own definition.** The re-check uses `IUserSessionRepository.FindActiveAsync`, which applies the idle timeout widened by the same enforcement tolerance `CallerEstablisher` applies. A session the pipeline has just accepted is therefore not refused by the re-check.
- **Nothing on the HTTP path tracks the session before the handler.** `CallerEstablisher` reads with `AsNoTracking`, and the activity write is a raw SQL `UPDATE`. So the handler's read under the lock comes from the database, not from a tracked copy (`docs/architecture.md`, *Lock first*).
- **G4 classification.** `user_session.failed_password_change_attempts` is classified as mutable, not governed by G4, in the immutability drift test, which forces every new column to be classified. `A_mutable_column_is_still_writable` covers it.

### Not included

- Behaviour 11: per-IP and per-address limiting for anonymous commands. CRD-C2's first-tenant blocker is untouched.
- Forwarded-headers handling.
- Any change to sign-in's lockout.
- A sign-in page explanation after the revocation (L7).
- Other password-change abuse. This limits guessing through an authenticated session only, and sign-in's lockout remains a separate control.

---

## Release security baseline — PasswordMinLength floor lowered to 6 (owner decision)

**Decision (owner, 2026-09-19).** The release baseline's `PasswordMinLength` floor goes from **12 to 6** (`SecurityBaseline`, commit `c5625d6`). It was committed by the owner on the CRD-C4 attempt-limit branch, and the owner chose to ship it in that PR.

**What the specification says.** The UM design specification and entity model define `PasswordMinLength` as a **floor**, with effective value = max(tenant, baseline). They fix **no number**. The earlier 12 was this repository's choice when the baseline was added (`c23e379`), and no decision record fixed it. So no workbook conflicts and no change control is outstanding.

**What changes, and what does not:**

- **Newly provisioned tenants** are seeded at 6.
- **Any tenant** may now configure 6 to 11, which the floor previously raised to 12.
- **Existing tenants keep 12**, because their stored policy (seeded at 12) is above the new floor and the effective value is the maximum. This covers the development database and the shared test database. Lowering an existing tenant needs a new policy version, which POL-C1 does not yet provide.
- **The minimum-length checks in CRD-C1, CRD-C3 and CRD-C4** read the effective policy. No handler hard-codes a length.

**For the record.** 6 is below the minimum of 8 that common guidance (NIST SP 800-63B) sets for user-chosen passwords. This was raised with the owner before the decision was confirmed.

**Tests.** `SecurityPolicyResolverTests` now runs on its own freshly provisioned database. Its premise is "a provisioned database with no overrides", and the shared test database, seeded at 12 under the earlier baseline, no longer meets it.

---

## USR-Q1 GetUser v2 and the User detail page (story 1)

**Status:** Contract frozen 2026-09-19 by owner decision (G1–G8, and the field and permission matrix with its six confirmed choices). This builds on USR-Q1 v1 (*USR-C2 — Update User Profile, and USR-Q1 GetUser (narrow v1)*), on AUT-Q2 (#60), and on the Users-table actions (USR-C4/C5, USR-C2 UI).

### Requirement

An administrator opens one user's page from the Users table and sees who that user is and what state they are in. The page offers the same actions the row offers. When the caller may read role assignments, it also shows the user's current roles.

The page is **composed from independently authorised reads**. The page needs `user.read`, and each section needs its own read permission, so two administrators can legitimately see different versions of the same page.

### The decisions

| # | Decision |
| --- | --- |
| **G1** | **Two stories.** Story 1, this one: GetUser v2, the detail page, the existing actions, and a Roles section. Story 2: identities (IDN-Q1) with lock state, and the unlock UI (CRD-C6). |
| **G2** | **Each section under its own read permission.** `user.read` authorises the core detail; `role.read` authorises Roles (AUT-Q2); `identity.read` will authorise Identities (IDN-Q1, story 2). **This is change control against the frozen catalogue.** See *USR-Q1 composition amendment*. |
| **G3** | **GetUser v2 adds `email`, `status` and `activationPending`** to `userId`, `firstName`, `lastName` and `displayName`. It adds no actor type, deactivation time, identities or assignments. Each new field carries the five-point evidence below. |
| **G4** | **The route is `/admin/users/:userId`**, a client-chosen path. **The display name in the Users table becomes a link to it.** An unknown user, or the System actor, shows the server's *"The user does not exist."* as a not-found state. |
| **G5** | **The page offers the same actions as the row**, under the same permissions and status rules, using the existing dialogs. After an action, the client **re-reads GetUser** and **invalidates the list**; it never guesses the new state locally. The row actions stay in the table. |
| **G6** | **A Roles section, only for holders of `role.read`.** Its source is AUT-Q2, current assignments only (the default: Active and Future). It is read-only and shows the server-derived state as sent. **Manage roles** opens the existing dialog. |
| **G7** | **A section the caller cannot read is hidden**: never disabled, and never shown as "denied". |
| **G8** | **Out of scope:** identities and unlock (story 2); sessions (SES-Q1); the access summary (USR-Q3); audit history (AUD-Q2); `Location` on create; agents and the System actor. |

### USR-Q1 composition amendment (G2, change control)

> **USR-Q1 composition amendment:** GetUser provides core user detail under `user.read`. Role assignments are not returned by USR-Q1; they are obtained through AUT-Q2 under `role.read`. Identities are similarly obtained through IDN-Q1 under `identity.read` in the subsequent story.

**Why.** The frozen catalogue's USR-Q1 row returns *"identities (provider, username, status), current assignments"* under `user.read`. Its sibling queries put exactly that data under separate permissions: IDN-Q1 under `identity.read`, and AUT-Q2 under `role.read`. The seeded roles separate these permissions deliberately:

| Seeded role | `user.read` | `identity.read` | `role.read` |
| --- | :---: | :---: | :---: |
| user-administrator | ✓ | ✓ | ✗ |
| security-administrator | ✓ | ✗ | ✓ |
| access-reviewer | ✓ | ✓ | ✓ |

Bundling the data under `user.read` would stop `role.read` controlling access to role assignments, and `identity.read` controlling access to identities. A user administrator would read assignments that AUT-Q2 refuses them. **The sibling contracts are not weakened;** USR-Q1 is narrowed instead.

**Outstanding change control, with no workbook edited:** the UM command catalogue's *Queries* sheet, USR-Q1 row, *Returns* column. It should list the core fields only, and name AUT-Q2 and IDN-Q1 as where assignments and identities come from.

### The field and permission matrix

| Page part | Data | Source | Permission | If not held |
| --- | --- | --- | --- | --- |
| **The page** | `userId`, `firstName`, `lastName`, `displayName`, `email`, `status`, `activationPending` | USR-Q1 GetUser v2, `GET /api/users/{userId}` | `user.read` | The route shows the established denied state, as other permission-gated routes do. The Users-table link is not offered without `user.read`, because the table itself needs it. |
| **Actions** | the row's actions | the existing commands | each action's own permission, as in the table | that action is hidden |
| **Roles** | current assignments: role name, effective from, effective to, state | AUT-Q2, `GET /api/users/{userId}/role-assignments` (default: Active and Future) | `role.read` | the section is hidden |
| **Manage roles** | opens the existing dialog | AUT-Q2, and AUT-C1 and AUT-C2 inside the dialog | `role.read`, as the table offers it *(corrected; see below)* | hidden |
| *Identities (story 2)* | — | IDN-Q1 | `identity.read` | hidden |

> **Corrected before the red tests (2026-09-19).** The frozen matrix named `role.grant` or `role.revoke` for Manage roles "as the table offers it". The table offers Manage roles to **`role.read`** holders: the dialog lists assignments (AUT-Q2) and shows Grant only with `role.grant`, and Revoke only with `role.revoke`. The governing rule is "as the table offers it", so the permission in brackets was wrong, and is now `role.read`. Nothing else changes.

**A missing page permission and a missing section permission are different things:**

- **Missing `user.read`:** the explicit **denied state**, because the route itself is permission-gated (`RequirePermission`, as every users route is). That is the page-level contract.
- **Missing `role.read`:** the Roles section is **hidden, and its request is not made**.
- **Missing an action's permission:** that action is **hidden**.
- **Missing `identity.read`** (story 2): the Identities section is **hidden, and its request is not made**.

A section's request is **never made** when its permission is not held. The client does not fetch a refusal only to hide it, and creates no needless authorization failures in the network layer.

### GetUser v2 (G3)

> **`GET /api/users/{userId}` returns exactly `{ userId, firstName, lastName, displayName, email, status, activationPending }`, and requires `user.read`.**

- **The added fields mean exactly what they mean on the list row (USR-Q2).** `email` is nullable because the column is. `status` is `"Active"` or `"Inactive"`, the stored lifecycle status (USR-Q2, amendment 2). `activationPending` is derived exactly as USR-Q2 derives it (amendment 1). False means only "not pending": never "activated", "has a password" or "can sign in".
- **Unchanged from v1:** human users only; the System actor and an unknown user are refused as unknown (`400`, *"The user does not exist."*); `401` without a carrier; not audited. The read is still one statement.
- **Additive for existing clients.** The web schema for v1 strips unknown members, so the Edit profile dialog is unaffected.

#### The evidence (*Adding a field later*)

| Field | 1. Concrete use | 2. Data exposure | 3. Necessity | 4. Removal cost | 5. Stable identity |
| --- | --- | --- | --- | --- | --- |
| `email` | The page identifies the user beyond the display name, as the row does. | Already served to every `user.read` holder on the row. | A deep link, a reload or a bookmark has no row to borrow from. | The page would lose the second identifying line. It is additive. | A label for people, never a key or a match (P4). |
| `status` | The Inactive marker, and the action matrix (Deactivate or Reactivate; actions withheld from inactive users). | Already served on the row, and lifecycle status is `user.read` data. | Without it the page must offer actions that are refused, or depend on list data it may not have. | The action matrix would return to offer-and-refuse. It is additive. | An enum value, not an identifier. |
| `activationPending` | The action matrix: Resend activation link offered, Reset password withheld. | One boolean, already on the row. It carries no credential detail and no token state. | As `status`. | As `status`. | A boolean cannot be mistaken for an identifier. |

### The page (G4, G5, G7)

- **Route:** `/admin/users/:userId`, under the Administration area. The *Users* navigation entry stays current on it, because the entry is current on its path and beneath. It is lazy-loaded, like every page.
- **Entry:** the display name in each Users-table row becomes a link to the page. The row's Actions menu stays as it is.
- **Header:**
  - **Title:** the display name.
  - Below it, the full name (first and last) and the email address, or *"No email address"* when null.
  - The **Inactive** marker, as the table shows it, and a **Pending activation** marker. *(Corrected before the red tests: the table shows no pending marker. The page's Pending activation marker, which the owner confirmed, is new here.)*
- **Loading and errors:**
  - While GetUser loads, a skeleton.
  - A refusal of GetUser shows its message word for word in the shared `ErrorState`, with **Try again**.
  - An unknown user or the System actor shows *"The user does not exist."* the same way. It is a stated not-found, never a redirect.
- **Actions (G5):** the same matrix as the row, with the same permissions and the same status and `activationPending` rules. They use the existing dialogs: the lifecycle actions, Resend, Reset, Sign out everywhere, Edit profile and Manage roles. After any action succeeds:
  - GetUser for this user is **invalidated and re-read**;
  - the Users list is **invalidated**;
  - for Manage roles, the Roles section's AUT-Q2 query is invalidated as well.

  The page shows only what the server then returns. Announcements are as each dialog already makes them.
- **Roles (G6):**
  - A section titled **Roles**, shown only with `role.read`.
  - It lists the current assignments (AUT-Q2's default) with the role name, **From**, **To** (or "No end date") and **State**, exactly as sent.
  - With none, it says *"No current roles."*
  - It has its own loading and error states, independent of the page.
  - **Manage roles** in the section opens the existing dialog, under the same condition as the row action.

### Acceptance Criteria

- **DV-1:** GetUser v2 returns exactly the seven fields, with `email` null where the column is. `status` and `activationPending` agree with the list row for the same user, across all four combinations. The System actor and an unknown user are refused as before.
- **DV-2:** a caller without `user.read` is refused (`400`). There is no audit record.
- **DV-3:** a Users-table display name links to `/admin/users/{userId}`. The page renders the header from GetUser alone, including on a direct load with no list read.
- **DV-4:** loading shows a skeleton, and a refusal shows its message with Try again. An unknown user shows *"The user does not exist."*.
- **DV-5:** the page offers exactly the row's actions for each permission, status and `activationPending` combination. The same matrix test is applied to the page.
- **DV-6:** after each action succeeds, GetUser is re-read and the list is invalidated. After Manage roles, AUT-Q2 is re-read as well. The page reflects the server's new state, and nothing changes locally before the re-read.
- **DV-7:** Roles is shown with `role.read`. Without it, the section is absent **and the test proves that no AUT-Q2 request was made**, not merely that nothing rendered. It lists AUT-Q2's current assignments with the server's state as sent, and shows *"No current roles."* when there are none. Its error does not break the page.
- **DV-8:** the permission split, against the **seeded** role compositions. This protects the G2 change control:
  - **User administrator** (`user.read` ✓, `role.read` ✗): user detail visible, **no Roles request**, Roles section absent.
  - **Security administrator** (`user.read` ✓, `role.read` ✓): user detail visible, **Roles request made**, Roles section visible.
- **DV-9:** no accessibility violations on the page, with a dialog open, and in the loading and error states.
- **DV-10:** browser check in the dev stack, each state change approved by the owner:
  - open a user from the table and by direct URL;
  - perform one action and see the page re-read;
  - compare the page as a user administrator and as a security administrator.

### Implementation notes

- **One projection for list and detail.** `UserLifecycleProjection` (Persistence) defines what a user is: the names, email, status and `activationPending`. Both the list reader (USR-Q2) and GetUser use it, so the two views cannot disagree. DV-1 seeds a user where the two clauses of `activationPending` part: an external-only identity with no credential. The list keeps its page-first query shape.
- **One action matrix.** `getUserActions({ user, can })` and `useUserActionPermissions()` (`users/components/userActions.ts`) are used by both the Users table and the detail page. Neither holds a rule of its own.
- **The page's actions** sit behind one **Actions** menu in the header. After a command dialog succeeds, the page invalidates GetUser for this user and the list. Edit profile's own save already did both. `ManageRolesDialog` gained an optional `onChanged`, called after a grant or a revocation, so the page re-reads GetUser and the list, while AUT-Q2 is re-read by the dialog's own hooks. The Users table does not use it.
- **Roles is not mounted without `role.read`**, which is how its request is never made. Dates are formatted by `formatInstant`, shared with Manage roles, so an assignment reads the same in both.

### Not included

- Identities and lock state, and the unlock UI (story 2).
- Sessions (SES-Q1), the access summary (USR-Q3), audit history (AUD-Q2).
- `Location` on `POST /api/users`.
- Agents and the System actor.
- Deactivation time, actor type, created-at, or any other GetUser field.

---

## IDN-Q1 GetUserIdentities and Unlock on the User detail page (story 2)

**Status:** Contract frozen 2026-09-19 by owner decision (I1–I9, the identity response, and the Unlock acceptance criteria, with the owner's two clarifications). This builds on story 1 (*USR-Q1 GetUser v2 and the User detail page*, #69) and on CRD-C6 `UnlockAccount` (`POST /api/identities/{identityId}/unlock`), which is unchanged.

### Requirement

On the User detail page, an administrator allowed to read identities sees how the user signs in, and whether each sign-in is locked. An administrator who may also unlock can clear a lock that is in force, with a reason, on any user but themselves.

This gives CRD-C6 the identity-selection contract its own requirements asked for (*Unlock is not reachable from a row*). The administrator chooses the identity from the list, so the command never has to choose between identities.

### The decisions

| # | Decision |
| --- | --- |
| **I1** | **The read is `GET /api/users/{userId}/identities`**, under `identity.read`. Human users only: an unknown user and the System actor get *"The user does not exist."*, as GetUser does. It returns **every** identity of the user (local and external, active and inactive), **oldest first**. Not audited. |
| **I2** | **The identity fields, from the catalogue:** `userIdentityId`, `type`, `provider`, `username`, `status`, `deactivatedAt`. **`subjectId` is deferred:** for a local identity it equals the identity's ID (UI5), and no external identity exists yet. |
| **I3** | **The lock state, beyond the catalogue:** `locked` and `lockedUntil`, **decided by the server at read time** using CRD-C6's rule, *a lock currently in force* (`LockedUntil > now`). An expired lock reads as `locked: false`, `lockedUntil: null`, never as a stale lock. **`failedAttemptCount` is not exposed:** it is an internal security signal, not something an administrator's decision needs. |
| **I4** | **Change control:** the IDN-Q1 amendment below. |
| **I5** | **A "Sign-in identities" section below Roles**, gated **only** by `identity.read`. Without that permission the section is absent **and its request is never made** (story 1's G7 rule). |
| **I6** | **Unlock is offered only when all of these hold:** the caller holds `user.unlock`; the identity is locked, local and active; the user is active; and the page is **not the caller's own**. Hidden, never disabled. **This is a UI affordance, not authorization:** see *Server authority*. |
| **I7** | **The Unlock dialog** follows the existing confirmation-with-reason pattern, and the **reason is required**. After a success, or after an *"is not currently locked"* refusal, the identities are **re-read**. |
| **I8** | **The seeded roles, proved:** a user administrator sees identities, with Unlock where eligible; a security administrator sees neither, and no request is made; an access reviewer sees identities without Unlock. |
| **I9** | **Out of scope:** identity lifecycle (IDN-C3, IDN-C4), username changes (IDN-C2), external-identity behaviour, the failure count, sessions, Unlock from the Users table, and reconciling the permission names. |

### IDN-Q1 amendment (I3, I4, change control)

> **IDN-Q1 amendment:** GetUserIdentities returns, per identity, `userIdentityId`, `type`, `provider`, `username`, `status` and `deactivatedAt`, and the **lock state** `locked` and `lockedUntil`, derived by the server at read time as CRD-C6 decides it. `subjectId` is deferred until external identities exist. `failedAttemptCount` is not returned.

**Outstanding change control, with no workbook edited:** the UM command catalogue's *Queries* sheet, IDN-Q1 row, *Returns* column. It gains the lock state and defers the subject.

#### The evidence for the lock state (*Adding a field later*)

| Field | 1. Concrete use | 2. Data exposure | 3. Necessity | 4. Removal cost | 5. Stable identity |
| --- | --- | --- | --- | --- | --- |
| `locked` | Decides whether Unlock is offered (I6), and shows the administrator why a person cannot sign in. | Whether sign-in is blocked right now, to holders of `identity.read`: the people who manage identities. There is no count and no history. | Without it the page must offer Unlock on every local identity and rely on *"This account is not currently locked."*: offer-and-refuse, the pattern the action matrix exists to avoid. | Unlock returns to offer-and-refuse. It is additive. | A boolean, not an identifier. |
| `lockedUntil` | Tells the administrator how long the lock would last on its own, so they can choose to wait instead of unlocking. | One instant, present only while locked. | Without it the administrator cannot weigh waiting against unlocking. | The page loses the "until" text. It is additive. | An instant, not a key. |

### The identity response

```json
{
  "identities": [
    {
      "userIdentityId": "…",
      "type": "Local",
      "provider": "Application",
      "username": "vr.ra",
      "status": "Active",
      "deactivatedAt": null,
      "locked": true,
      "lockedUntil": "2026-09-19T15:20:04Z"
    }
  ]
}
```

- **`type`** is `"Local"` or `"External"`. **`provider`** is `"Application"` for local identities, or the provider's name.
- **`username`** is a string for local identities and `null` for external ones.
- **`status`** is `"Active"` or `"Inactive"`. **`deactivatedAt`** is set exactly when `status` is `"Inactive"` (`ck_user_identity_status_deactivation`).
- **`locked` is a read-time projection, not persisted identity state.** No column holds it, on `user_identity` or anywhere else. It is computed at each read from the credential's `LockedUntil` and the server's clock. **`locked`** is `true` exactly when the identity has a credential whose `LockedUntil` is later than the server's clock at the read. **`lockedUntil`** is that instant when `locked`, and `null` otherwise. An identity with no credential (external, or pending activation) is `locked: false`.
- **Ordered** by the identity's creation, oldest first; ties are broken by `userIdentityId`.
- **Refusals:** `400 { error }` for an unknown user, the System actor, or a missing `identity.read`; `401` with no carrier.

### Server authority (I6)

**The client's self rule only hides the action. The server continues to enforce the prohibition.**

- **The client** reads the caller's session `userIdentityId` from `/api/me`. When it is among the listed identities, the page is the caller's own, and Unlock is not offered on **any** of its identities.
- **The server** keeps CRD-C6's rule unchanged: **an administrator cannot unlock any identity belonging to their own user**, whichever identity is named. The rule is user-level, not identity-level. It answers *"An administrator cannot unlock their own account."* whatever the client did, including when `/me` is stale or unavailable, or the client errs.
- The same holds for every other condition in I6. The client's check is an affordance, and CRD-C6's refusals (*"This account cannot be unlocked."*, *"This account is not currently locked."*) remain authoritative. They are shown word for word.

### The page (I5, I6, I7)

- **The section:** **Sign-in identities**, below Roles, mounted only for `identity.read`. It has its own loading, error and **Try again**, independent of the page and of Roles.
- **Columns:** **Type**, **Username** ("—" when null), **Status**, and **Lock**: *"Locked until {instant}"* when locked, *"Not locked"* otherwise. The instant uses the module's shared format.
- **Unlock:** a button **"Unlock {username}"** on an eligible row (I6).
- **The dialog:**
  - Title *"Unlock {username}"*. The description says it clears the lock now, the person can sign in again, and nothing else changes: no password, session or token.
  - It has a required **Reason**. The buttons are **Cancel** and **Unlock**, busy while sending, and sending once.
- **On 204:** the dialog closes, the identities are **re-read**, and the page announces *"{username} was unlocked."*. The row then shows *"Not locked"* and no Unlock.
- **On a refusal:** the dialog stays open with the message word for word. For *"This account is not currently locked."* the identities are **also re-read**, so the page stops offering an action that can no longer succeed.
- **Nothing else is re-read:** unlocking changes nothing that GetUser, the list or Roles show.

### Acceptance Criteria

- **ID-1:** the read returns exactly the eight fields per identity, oldest first, for a user with **local and inactive** identities, and **an external** identity is represented too (seeded directly). An unknown user and the System actor are refused as unknown. A missing `identity.read` is `400`, and no carrier is `401`. There is no audit record.
- **ID-2:** the lock state follows CRD-C6's rule at the read's instant:
  - a lock in force gives `locked: true` and its `lockedUntil`;
  - an **expired** lock gives `false` and `null`;
  - failures without a lock give `false` and `null`;
  - no credential gives `false` and `null`.

  `failedAttemptCount` never appears.
- **ID-3:** the section is shown only with `identity.read`. Without it the section is absent and **no identities request is made** (counted). It has its own loading, error and retry, and its failure leaves the page standing.
- **ID-4:** Unlock is offered exactly when every I6 condition holds. It is **absent** in each of these cases:
  - the caller lacks `user.unlock`;
  - the identity is not locked;
  - the identity is external;
  - the identity is inactive;
  - the user is inactive;
  - **the page is the caller's own**, on any of its identities, including a locked one.
- **ID-5:** the dialog requires a reason and sends nothing without one. It sends exactly `{ reason }` to `POST /api/identities/{identityId}/unlock`. It is busy while sending, and sends once. On `204`: close, re-read the identities, announce, and the row shows *"Not locked"* with no Unlock.
- **ID-6:** refusals are shown word for word, and the dialog stays open. *"This account is not currently locked."* also re-reads the identities. *"An administrator cannot unlock their own account."* is shown as the server words it, whatever the client decided.
- **ID-7, the server's authority:** over HTTP, independent of the client, an administrator is refused with *"An administrator cannot unlock their own account."*, and no `AccountUnlocked` is written, **in both forms of self-targeting**:
  - naming **the caller's own session `userIdentityId`**;
  - naming **any other identity belonging to the caller's own `userId`**.

  CRD-C6's rule is **user-level**, not identity-level. This criterion is kept even though the existing rule may already satisfy it, so that this story's server-authority contract is explicit.
- **ID-8 (I8), against the seeded roles:**
  - **user administrator:** section and request, and Unlock on an eligible locked identity;
  - **security administrator:** no section and **no request**;
  - **access reviewer:** section and request, and no Unlock.
- **ID-9:** no accessibility violations with the section loaded, with the Unlock dialog open, and in the loading and error states.
- **ID-10, browser check in the dev stack**, each state change approved by the owner:
  1. Lock a test account with five wrong sign-ins (the owner types them).
  2. On its page, as Ada, see **Locked until {instant}** and Unlock.
  3. Unlock with a reason: `204`, the identities re-read, *"Not locked"*, and Unlock gone.
  4. `AccountUnlocked` is in the trail with the reason.
  5. The unlocked user signs in.
  6. On **Ada's own page**, no Unlock is offered.

  Whether Unlock stays absent **while the caller's own identity is locked** is proved by **ID-4 (web) and ID-7 (server)**, not in the browser. A deliberate 15-minute lock on Ada would make the manual check fragile.

### Implementation notes

- **The lock state is decided in the reader.** It reads the credential's `LockedUntil` and compares it with the instant the handler takes **once** per read, so every identity in one response is judged at the same moment. Nothing about it is stored.
- **The page re-reads the identities when an unlock *settles*, success or refusal.** I7 names a success and *"not currently locked"*. Re-reading on every refusal covers both without branching on a message's text, which §7 forbids, and is harmless for the others. Nothing else is invalidated.
- **Unlock sits in the Lock cell**, keeping the four columns I5 names. The eligibility rule is `identityActions.unlockOffered`.
- **The self check** compares `useCallerIdentityId()` (from `/me`) with the listed identities, so it works at the user level: any match means the page is the caller's own. ID-7 proves that the server enforces the rule regardless.

### Not included

- Identity lifecycle (IDN-C3, IDN-C4), username changes (IDN-C2), and external-identity behaviour.
- The failure count, sessions (SES-Q1), and Unlock from the Users table.
- The `credential.unlock` / `user.unlock` naming (see *The unlock permission disagrees between the Audit and UM specifications*).

---

## Behaviour 11 — rate limiting the anonymous commands

**Status:** Contract frozen 2026-09-19 by owner decision (B1–B10, with the owner's seven contract details and red-test list). It closes the Known Gap *CRD-C2 is not first-tenant-ready: rate limiting is missing*, and gives CRD-C3 the same closure.

### Requirement

The four commands that run without a signed-in caller — SES-C1 `SignIn`, CRD-C1 `ActivateAccount`, CRD-C2 `RequestPasswordReset` and CRD-C3 `ResetPassword` — must not be callable at unlimited speed. Behaviour 11 of the command catalogue: *"Rate limiting · Anonymous commands · Per address and per IP on password reset and sign-in · Both are enumeration and brute-force surfaces."* CRD-C2's catalogue row makes it a **precondition** of the command, and D-NOTIF-03 and AUD-D41 accept the residual timing difference, and the absence of any record for an unknown address, **because** this control exists.

**Rate limiting measures request volume, not authentication correctness.** It is not a failed-login counter and not a second lockout.

### The decisions

| # | Decision |
| --- | --- |
| **B1** | **A command pipeline behaviour, `RateLimitBehavior`, registered FIRST** — before `AuthenticationBehavior`, and so before the transaction, the handler and any password derivation. A command declares its limits through a marker the behaviour tests; the behaviour names no command. |
| **B2** | **All four anonymous commands are limited per client IP. Sign-in and forgot password are also limited per normalised address.** Reset and activate get no per-token or per-address limit: the valid token is already the authorization boundary, and the IP limit covers their cost. |
| **B3** | **The address key is normalised before keying, and every request counts**, including one naming no account. |
| **B4** | **The limits** (table below), frozen. |
| **B5** | **Fixed release constants**, like `TransportTimeout`. Not configuration, and not `security_policy`: the frozen entity model has no rate-limit fields. |
| **B6** | **A refusal is `429 Too Many Requests`, with `Retry-After` and one fixed sentence**, identical for every command and every key. |
| **B7** | **An operational warning only.** No audit event, no catalogue change, no `AuditEventCatalogue.Version` bump (AUD-D41: enumeration telemetry belongs to the operational log, not the regulated trail; EO5 permits no new anonymous event). |
| **B8** | **Trusted-proxy configuration.** `X-Forwarded-For` is authoritative only when the immediate peer is a configured trusted proxy; otherwise the connection address is used. |
| **B9** | **The dev stack trusts its Vite container** as a proxy, and Vite sends the header, so the browser check exercises the real path. |
| **B10** | **Bounded, in-memory counters.** Correct for the single-process deployment (`docs/architecture.md` §4). A restart resets them. |

### The limits (B4)

| Command | Key | Limit | Window |
| --- | --- | ---: | ---: |
| SES-C1 Sign-in | normalised username | **10** | **15 minutes** |
| SES-C1 Sign-in | client IP | **30** | **1 minute** |
| CRD-C2 Forgot password | normalised email or username | **3** | **1 hour** |
| CRD-C2 Forgot password | client IP | **10** | **1 hour** |
| CRD-C3 Reset password | client IP | **10** | **15 minutes** |
| CRD-C1 Activate | client IP | **10** | **15 minutes** |

Each (command, key kind) pair is its own bucket: a sign-in and a forgot-password request from one IP draw on different buckets.

### Admission

```text
request → host resolves the client address (B8)
        → RateLimitBehavior: every applicable bucket has room?
              no  → 429, nothing else runs
              yes → count the request in every applicable bucket
                    → AuthenticationBehavior → … → handler
```

1. **Before anything costly.** A refused request reaches no later behaviour and no handler: no transaction, no lookup, no password derivation.
2. **The keys are an AND-gate.** A request is admitted only if **every** applicable bucket has room. If any is exhausted, it is refused.
3. **Admission is atomic across the keys.** An admitted request is counted in every applicable bucket; a refused request is counted in **none**. Concurrent requests at the limit admit exactly the limit, never more.
4. **Requests are counted, not failures.** A successful sign-in counts. A forgot-password request naming no account counts. The outcome of the command never changes a bucket.
5. **The window slides.** An admitted request counts against a bucket for exactly one window from the instant it was admitted. A bucket has room while fewer than *limit* admitted requests fall within the last window. Time is read from `IClock`, once per request.
6. **Refused requests do not extend the refusal.** Because a refusal counts nowhere, a caller that keeps retrying while refused is admitted again as soon as the window allows.
7. **Independent of lockout.** Rate limiting never changes `FailedAttemptCount` or `LockedUntil`, never writes `SignInFailed` or `AccountLocked`, and never writes `TokenRejected`. A `429` means *abuse protection fired*, not *authentication failed*. An admitted request is then judged exactly as today.

### The keys

- **Normalised address (B3).** Trimmed of surrounding whitespace and lower-cased with the invariant culture. This makes the key at least as coarse as the database's `lower(…)` match for ordinary input: `Ada`, `ada` and ` ada ` share a bucket. *Accepted limitation:* the in-memory fold is .NET's invariant one, not PostgreSQL's `lower()` under the database collation; for the few characters where those differ, two spellings the database treats as one could draw on separate buckets. Matching the database exactly would cost a database round-trip before admission, which is the thing B1 exists to avoid.
- **An empty normalised address has no address bucket.** A blank forgot-password request is limited by IP alone; the command refuses it as today.
- **Client IP.** The address the host resolved (B8). An IPv4-mapped IPv6 address (`::ffff:a.b.c.d`) is keyed as its IPv4 address. **An IPv6 address is keyed by its /64 prefix**, because one subscriber is routinely assigned a whole /64, and a per-address key would let that subscriber rotate through it.
- **No resolvable client address (owner detail 7).** The request is **not** placed in a shared "unknown" bucket; the IP gate simply does not apply to it, and its address bucket (if any) still does. This keeps integration tests — and a production host whose proxy configuration is wrong — from collapsing every caller into one global bucket. The command layer never invents an address: the host passes "unknown" (null) through.

### The refusal (B6)

- **Status `429`**, body `{ "error": "Too many attempts. Try again later." }` — the same sentence for every command and every exhausted key, so the response never says which key was responsible.
- **`Retry-After`, in whole seconds, rounded up, at least 1:** the time until **every** exhausted bucket for the request has room again — the latest of their availability instants. *(Owner detail 5 asked for the time until the earliest exhausted bucket becomes available. A retry succeeds only when all of them have room, so an earlier value would promise a retry that is refused again; the latest instant is the boundary that detail's intent — "a useful retry boundary" — requires.)*
- The refusal is thrown by the behaviour as a dedicated exception carrying the retry interval, and mapped by `ProblemMiddleware`, which today maps exactly three exceptions onto HTTP. This adds a fourth, on the same allowlist principle: nothing is read off the exception but the interval.
- **The web client needs no change.** Every anonymous page already shows a non-401 failure's server sentence word for word. Mutations are never retried, so the client does not hammer the limit.

### The operational warning (B7)

`ProblemMiddleware` logs one **Warning** per refusal: the method and path, the client address (or *unknown*), the key kinds that were exhausted (`ip`, `address`) and the retry interval. **It never logs the typed username or address**: that is attacker-supplied, possibly third-party PII (AUD-O15), and AUD-D41 keeps it out of every record. Nothing is written to the audit trail.

### The client address (B8, B9)

- **Configuration:** `LIGATURE_TRUSTED_PROXIES`, a comma-separated list of IP addresses and CIDR ranges. **Absent or empty means no proxy is trusted.** A malformed entry stops the host at startup, as a malformed signing key does.
- **Nothing is trusted implicitly — loopback included.** ASP.NET's forwarded-headers defaults trust loopback; those defaults are cleared.
- **Only `X-Forwarded-For` is read.** `X-Forwarded-Proto` and `X-Forwarded-Host` are ignored: the cross-site check depends on the request's own Host, and this story does not change it.
- **The walk:** starting from the immediate peer, while the current hop is a trusted proxy, the next address to the left in `X-Forwarded-For` becomes the candidate. The first address that is not a trusted proxy is the client. If the immediate peer is not trusted, the header is ignored entirely. An entry that is not an IP address ends the walk, and the address is the last one established by a trusted hop — never the malformed value.
- **The resolved address flows into the existing evidence fields**, unchanged in shape: `SignInSucceeded` and `SignInFailed` (`ipAddress`), `PasswordResetRequested` (`RequestIp`), and `user_session.IpAddress`. Behind a configured proxy they record the real caller rather than the proxy. **No audit schema, catalogue or payload shape changes.**
- **`ActivateAccountCommand` and `ResetPasswordCommand` gain the client address**, for rate limiting only. It is recorded nowhere: `TokenRejected` and the success records are unchanged.
- **Dev (B9):** `compose.dev.yaml` pins the web container's address on the dev network and sets `LIGATURE_TRUSTED_PROXIES` to exactly that address; Vite's `/api` proxy sends `X-Forwarded-For`. Outside the container nothing is configured, so the header Vite sends is ignored — the safe default.

### Storage (B10)

- A singleton store in the Application layer, keyed by (command, key kind, key value). Each key holds at most *limit* admission instants, so one key's memory is bounded by its limit.
- **A key whose admissions have all left the window is removed** — the store keeps nothing for callers that have gone quiet. Removal happens during admission, so no background timer is needed.
- A restart, or a second host instance, starts with empty counters. **Explicit non-decision:** more than one host instance would need a shared store; that is a deployment change, decided when it is made.

### Acceptance Criteria

**Admission and independence**

- **RL-1** Sign-in, **username gate:** with the IP bucket having room, the 11th request for one normalised username within 15 minutes is `429`; the first 10 are judged as today. The 11th reaches no handler: no `SignInFailed`, no `SignInSucceeded`, `FailedAttemptCount` and `LockedUntil` unchanged, no session.
- **RL-2** Sign-in, **IP gate:** with every username bucket having room (31 different usernames), the 31st request from one IP within 1 minute is `429`.
- **RL-3** Forgot password: the 4th request for one normalised address within 1 hour is `429` **whether or not the address names an account**; with distinct addresses, the 11th request from one IP within 1 hour is `429`. A refused request queues no mail, issues no token, writes no `PasswordResetRequested`.
- **RL-4** Reset password and activate: the 11th request from one IP within 15 minutes is `429`, and consumes no token.
- **RL-5** **Successes count:** 10 successful sign-ins for one username exhaust its bucket; the 11th, with the right password, is `429`.
- **RL-6** **Unknown accounts count:** requests for a username or address that names no account draw on its bucket exactly as a real one does, and the `429` is byte-identical for a real and an unknown address.
- **RL-7** **Normalisation:** `Ada`, `ada` and ` ada ` share one bucket, for sign-in and for forgot password.
- **RL-8** **Refusal changes no authentication state:** an exhausted bucket followed by a request with a wrong password leaves `FailedAttemptCount`, `LockedUntil` and the audit trail exactly as they were; an exhausted bucket never produces `AccountLocked`.
- **RL-9** **Atomic AND:** a request refused by one exhausted bucket is counted in none — the other bucket's remaining room is unchanged afterwards.
- **RL-10** **Concurrency:** with one request of room left in a bucket, concurrent requests admit exactly one.
- **RL-11** **The window slides, without sleeping:** driven by a controlled `IClock`, a bucket regains room exactly one window after its oldest counted admission, not before; and a caller that keeps being refused is admitted as soon as that instant passes (refusals do not extend it).
- **RL-12** **Position:** the behaviour runs before `AuthenticationBehavior` — a refused `IBearerAuthenticatedCommand` under an established caller is `429`, not `401` — and before any password derivation (a refused sign-in performs no hash verification).
- **RL-13** **Every anonymous command is limited:** a test enumerates the Application assembly's `IAnonymousCommand` implementations and requires each to declare its limits, so a future anonymous command cannot be added unlimited. Authenticated commands are never limited by this behaviour.

**The refusal**

- **RL-14** `429`, body exactly `{ "error": "Too many attempts. Try again later." }`, the same for every command and key.
- **RL-15** `Retry-After` is the whole seconds (rounded up, at least 1) until every exhausted bucket has room: with only the username bucket exhausted it follows that bucket; with both exhausted, the later of the two.
- **RL-16** One Warning is logged per refusal, carrying the path, the client address and the exhausted key kinds, and **not** containing the typed username or address. No audit record is written.
- **RL-17** Each of the four web pages (sign-in, forgot password, reset password, activate) shows the sentence word for word on a `429`.

**Keys and memory**

- **RL-18** An IPv4-mapped IPv6 address shares its IPv4 address's bucket; two IPv6 addresses in one /64 share a bucket; in different /64s they do not.
- **RL-19** **No resolvable address:** requests with no client address are not IP-limited — 31 sign-ins for 31 usernames with no address are all admitted — and their address buckets still apply.
- **RL-20** Once every admission of a key has left its window, the key is no longer held by the store.
- **RL-21** A new host instance starts with empty counters.

**The client address**

- **RL-22** **Untrusted `X-Forwarded-For` is ignored:** with no trusted proxies configured — including a request from loopback — the connection address is used and the header changes nothing, neither the bucket nor the recorded `ipAddress`.
- **RL-23** **A trusted proxy's `X-Forwarded-For` is used:** with the peer configured as trusted, the resolved client address keys the IP bucket and is what `SignInFailed`, `SignInSucceeded`, `PasswordResetRequested` and `user_session.IpAddress` record.
- **RL-24** **The walk:** a chain of two trusted proxies resolves the first untrusted address; a spoofed leftmost entry behind one trusted proxy is not reached; a malformed entry is never used as the address.
- **RL-25** **Configuration:** a malformed `LIGATURE_TRUSTED_PROXIES` entry stops the host at startup; absent or empty trusts nothing; IP addresses and CIDR ranges are both accepted.

**Browser, in the dev stack (RL-26),** each state change approved by the owner:

1. Sign in 11 times with a username that **names no account** (the owner types it): the first 10 answer *"Invalid username or password."*, the 11th *"Too many attempts. Try again later."*. No account is locked.
2. Forgot password 4 times for an address that **names no account**: the 4th is refused with the sentence. No mail is written to the dev sink.
3. The `SignInFailed` records from step 1 carry the **browser's** address as resolved through Vite, not the web container's.
4. Ada signs in normally throughout: her own bucket is untouched.

### Implementation notes

- **The behaviour** is `RateLimitBehavior`, registered first. It tests `IRateLimitedCommand`, normalises each declared value by its rule's kind (`RateLimitKeys`), reads `IClock` once, and asks `RateLimitStore` to admit. A refusal throws `RateLimitExceededException`, which carries the retry interval, the exhausted key kinds and the client address — never the typed value.
- **The store** keeps each key's admission instants in a queue under one lock: judge every key, then count in all or none. A full sweep of quiet keys runs during admission, at most once a minute.
- **The declarations** are explicit interface members on the four command records, so they do not appear in the records' equality or `ToString`. `RateLimitDeclarationTests` holds `IAnonymousCommand` and `IRateLimitedCommand` together in both directions.
- **The client address is resolved by the Host's own `ClientAddressMiddleware`, outermost in the pipeline,** and written to `Connection.RemoteIpAddress`, which the endpoints already pass into the commands. ASP.NET's `ForwardedHeadersMiddleware` was not used: with empty trusted lists it believes every header, and with no connection address it believes the first entry unchecked — both the opposite of B8.
- **`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` stops the host.** That framework switch installs a forwarded-headers filter that believes any peer, which would undo B8 beside this host's own list. Not in the frozen text; added as a guard, with a test.
- **A shorthand IPv4 address in `LIGATURE_TRUSTED_PROXIES`** (`10.5`, `1`) is refused as malformed rather than parsed into an address nobody meant.
- **The OpenAPI document** now lists `429` for the four endpoints, and their descriptions say they are rate limited.
- **Dev:** `compose.dev.yaml` fixes the network at `172.31.211.0/24`, hands out dynamic addresses from the lower half only, pins the web container at `172.31.211.200`, and trusts exactly that address. Vite's proxy sets `xfwd`, which also adds `X-Forwarded-Proto`, `-Port` and `-Host`; the host reads none of them. **An existing dev stack must be recreated once** (`docker compose -f compose.yaml -f compose.dev.yaml down`, then `./up.sh`); volumes, and the database, are kept.

### Accepted consequences

- **Anyone can make one username's sign-in wait**, by spending its 10 requests in 15 minutes. This is the same class of exposure as the existing lockout (five wrong passwords lock an account, as the #70 browser check showed), which this story does not change; it is noted, not solved.
- **A legitimate user who signs in more than 10 times in 15 minutes** — for example, across many devices — is refused until the window allows.
- **A restart resets every bucket.**

### Not included

- Deliberate lockout of another person's account, and any change to lockout policy.
- Authenticated commands: CRD-C4's attempt limit was closed by L1–L8, and CRD-C5 requires an administrator.
- The Notification specification's timing validation (*CRD-C2 response time for known vs unknown accounts … differs by less than the recorded residual*), a separate validation item.
- More than one host instance, and tenant-configurable limits.
- `X-Forwarded-Proto` and `X-Forwarded-Host`.

---

# Known Gaps and Deliberate Deferrals

Things the code knowingly does not do yet. An agent that encounters one of these should **not** "fix" it inside an unrelated story and should **not** report it as a defect — cite this section instead. Remove an entry when the deferral is closed.

## USR-C2 change control: "Admin or self" is two commands

**Rule:** the frozen UM command catalogue's USR-C2 row is *"Admin or self"*, with permission *"user.update / self"*.

**State:** pipeline behaviour 3 gives a command one fixed permission, so USR-C2 is the **administrator** command, with `user.update`. A self-service profile command, if wanted, is a separate command with its own authorization, delivered with a My account page. This is the SES-C4 precedent. **No workbook is edited.** It is outstanding change control against the catalogue.

## USR-C2 is last-write-wins

**State:** two administrators editing the same profile in turn: the later save wins. Each save's Before/After is in `UserProfileChanged`, so the overwritten edit is visible in the trail, but the second administrator is not told.

**Deferred:** optimistic concurrency, either a version on `app_user` or an expected-values check on the command. It is a contract change with a client consequence, decided when there is evidence it is needed.

## Name rules differ between USR-C1 and USR-C2

**State:** USR-C2 normalises names and enforces required, non-blank, no control characters, and at most 100 code points. `User.CreateHuman` (USR-C1) still checks only that first and last names are non-blank. A name can therefore be **created** in a form it could not be **edited** to, such as a blank display name or one over 100 characters. There is no database constraint for these rules.

**Deferred:** whether USR-C1 adopts the same rules, which amends its behaviour, and whether database CHECK constraints follow. USR-C1 is not yet written up in this catalogue, so the decision is taken when it is back-filled or next touched.

## USR-C4 does not handle owned agents

**Rule:** invariant 25. Deactivating a person who owns agents transfers ownership in the same operation. Where an emergency deactivation proceeds without a transfer, the owned agents' role assignments are revoked until a new owner attests.

**State:** USR-C4 accepts no `NewOwnerUserId` and performs no agent step. V1 cannot create or activate an agent, so there is nothing to act on.

**Deferred to:** Slice 6 (AGT-C1..C4), which SHALL add the step inside USR-C4's transaction (docs/requirements.md, *USR-C4 / USR-C5*, *Agents*).

## USR-C4/C5 change control against the frozen catalogues

Recorded by owner decision (D2, D5, D7, D13). **No workbook is edited.** Each item is outstanding change control:

1. **UM command catalogue, USR-C4 step 4.** `EffectiveTo = now` for a future assignment violates UR2. It should read: revoked by the AUT-C2 rule (a future assignment is closed at its own `EffectiveFrom`).
2. **UM command catalogue, USR-C4.** Add `user_token` to *Tables written*, and a step that invalidates outstanding activation and reset tokens.
3. **Audit workbook, `TokenInvalidated`.** It covers only supersession (`SupersededBy` required). Invalidation without a replacement token, as on deactivation, has no event, so v1 writes the state change with no per-token record. Closing this needs a non-supersession form, added through per-event versioning (AUD-C3), **not** by fabricating a superseding token or weakening the frozen definition.
4. **Audit workbook, `IdentityReactivated`.** Add USR-C5 to its emitters. It is a provenance correction; the definition is unchanged.

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

## PostgresExceptionTranslator — role-overlap and live-grant constraints not mapped — overlap RESOLVED

**State:** the role-overlap exclusion constraints (raise `23P01`) and RP2's live-grant index are not in the translator's map because no command can reach them yet.
**Deferred to:** AUT-C1 and AUT-C7, where their wording can be written against a real trigger.
**Resolution (role overlap):** resolved by AUT-C1. Both overlap constraints map to *"The user already holds this role for this scope in an overlapping period."* RP2's live-grant index is still unmapped and stays with AUT-C7.
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

## CRD-C2 is not first-tenant-ready: rate limiting is missing — RESOLVED

**State:** resolved by *Behaviour 11 — rate limiting the anonymous commands* (B1–B10). CRD-C2 is limited per address and per client address, and CRD-C3 per client address, before either handler runs; the client address is resolved behind trusted proxies only. What follows is the gap as it was recorded.

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

## CRD-C4 does not limit current-password attempts — RESOLVED

**State:** resolved by *CRD-C4 — limiting current-password attempts per session* (L1–L8). After the effective `MaxFailedLoginAttempts` consecutive wrong or locked current-password attempts, the session making them is ended (`PasswordChangeAttemptsExceeded`). The credential is never locked and the account's other sessions are untouched. What follows is the gap as it was recorded.

**This was a known security gap, recorded by owner ruling.**

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

> **Known limitation, restated for the My account page (M1, 2026-09-18).** CRD-C4 currently has no attempt limit for incorrect current-password submissions. The account page is not blocked on this limitation, because the endpoint already permits the operation to any authenticated session. Rate or attempt limiting is a separate security decision. **Building the UI does not approve unlimited attempts as a security design.**

> **Decided (L1–L7, 2026-09-18), and closed by that story.** The owner chose to end the session after N consecutive wrong current passwords, rather than lock the credential.

## My account offers Change password to every caller (M10)

**State:** the My account page shows Change password to every authenticated caller. Only local identities exist today (IDN-C1 is not built), and CRD-C4 refuses a non-local identity with its uniform *"The password could not be changed."*, so the server decides whether the operation applies.

**Revisit when:** external identities arrive (IDN-C1). A caller who signed in through an external identity provider would then be offered an action that can only fail. `/me` would have to say which kind of identity this session was established with, so the page can withhold the action.

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
   SES-C4), `UserDeactivated` (USR-C4), `PasswordChangeAttemptsExceeded` (CRD-C4's
   attempt limit, L5). The entity model's list currently ends open ("…").
5. **USR-C4 spelling — RESOLVED** by USR-C4/C5 D4: sessions use the code
   `UserDeactivated`, role assignments the reason `User deactivated`.
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

## A failed activation mail has no recovery command — RESOLVED

**State:** resolved by CRD-C7 — *Reissue Activation Link*. What follows is the gap as it was recorded.


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

**Resolution:** CRD-C7 — *Reissue Activation Link*, approved and implemented
2026-09-18. It reuses `user.create` rather than introducing its own
permission, by owner decision recorded there. The Notification walkthrough's
wording (§11.4, §11.5) and §10.1's "three issuing commands" still await that
specification's own change control.

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

**CRD-C7 is a legitimate fourth.** It declares an `AccountActivation`
notification for the token it has just issued, so N14 holds for it. The
premise's count is corrected through the Notification specification's own
change control (CRD-C7, *Not decided here*), and whatever call-site assertion
this story builds must admit four commands, not three.

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

## G4 constrains how an already-ended assignment may be revoked — RESOLVED

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

**Resolution:** resolved by AUT-C2. `UserRole.Revoke` refuses an already-ended assignment as meaningless, so the trigger is never reached by it. A future assignment is revoked by setting `EffectiveTo = EffectiveFrom`, which only moves a NULL or later end earlier.

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
