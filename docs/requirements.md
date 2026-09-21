# SKSMCorp Requirement Catalogue

**Status:** In progress — being back-filled from committed code

This document is the authoritative index of SKSMCorp requirements.

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

- Existing databases converge through the existing tool, `dotnet run --project src/Tools/SKSMCorp.CatalogueSync`, or the `catalogue-sync` Compose service. `CatalogueDriftTests` is the postcondition, and it fails against any database that has not yet been synchronised with this release.
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

### Amendment 1 — Tenant-owned roles and grants

**Decided 2026-09-20 by owner decision (PRV1–PRV7).** It removes the dependency that blocks AUT-C3: today the synchroniser refuses **any** role absent from the release seed, so the first tenant-created role would fail the next deployment.

#### Decision

> **The release catalogue reconciles release-owned authorization state. Tenant-owned roles and their grants are application-managed state and lie outside catalogue reconciliation. The permission catalogue itself remains release-owned.**

```text
                  Release-owned            Tenant-owned
role              reconciled               outside reconciliation
role_permission   reconciled               outside reconciliation
permission        always release-owned (PE2) — no exemption
```

**Ownership is read from `IsSystemRole`, and nothing else** (PRV1):

```text
IsSystemRole = true   -> release-owned
IsSystemRole = false  -> tenant-owned
```

No new provenance field. That flag already governs who may modify, deactivate or reactivate a role (RO3), it is immutable in the database — the update guard refuses a change, as it does for `code`, `created_at` and `created_by` — and the command catalogue already says AUT-C3 creates roles with it false. **A grant's ownership follows its role** (PRV5): a grant on a tenant role is tenant-owned, whatever permission it grants.

#### What changes, exactly

**Only the database → seed pass** (PRV2). The seed → database pass is untouched.

| Database row | In the release seed | Result |
| --- | --- | --- |
| **System** role | No | **Refused**, `RoleMissingFromSeed` (unchanged — PRV3) |
| **Tenant** role | No | **Allowed**, and ignored entirely |
| **System** role | Yes | Existing reconciliation (insert, inactive, metadata, drift) |
| **Tenant** role | Yes | **Refused**, `SecuritySemanticDrift` — the existing seeded-code check (unchanged) |
| Active grant on a **system** role | No | **Refused**, `GrantMissingFromSeed` (unchanged) |
| Active grant on a **tenant** role | No | **Allowed**, and ignored entirely |
| Any **permission** | No | **Refused**, `PermissionMissingFromSeed` (unchanged) |

The fourth row is why the exemption is scoped to one pass: a tenant may not take a code the product later owns and have it silently accepted. A release-owned role that disappears from the seed — the bad merge or rename F2 exists to catch — still carries `IsSystemRole = true` and is still refused.

#### The forbidden mutations, one by one

Tenant state is **outside the reconciliation set**, which is not the same as "the synchroniser may now touch it" (PRV6).

| # | Rule | After this amendment |
| --- | --- | --- |
| **F1** | DELETE anything | Unchanged. The synchroniser deletes nothing, tenant or release. |
| **F2** | A permission or role in the database but not the catalogue | **Amended.** It applies to permissions (all of them) and to **system** roles. A tenant role is not drift; it is state the catalogue never described. |
| **F3** | Deactivate an existing permission or role | Unchanged. Nothing deactivates anything, and a tenant role is never examined. |
| **F4** | Reactivate an existing permission or role | Unchanged; `InactiveCatalogueEntry` concerns seeded codes only, which tenant rows are not. |
| **F5** | Revoke an existing grant | Unchanged as a prohibition. Its refusal — an active grant absent from the seed — now applies to grants on **system** roles. |
| **F6** | Re-create a revoked grant | Unchanged; `RevokedGrantInSeed` concerns seeded grants, which are grants on system roles. |
| **F7** | Modify an immutable or security-semantic field, `IsSystemRole` among them | Unchanged, and load-bearing: it is what refuses a tenant role wearing a seeded code. |
| **F8** | Modify an existing `role_permission` row | Unchanged. The synchroniser modifies no grant, tenant or release. |

**No new refusal reason code, and no change to `PermissionCatalogUpdated`.** Its counts remain counts of what the run committed, which is release-owned work only; tenant state is invisible to the event, as it is to the run.

#### Acceptance Criteria

The frozen criteria this amendment restates are **A5** (a permission or role absent from the catalogue refuses) and the grant half of **A9**/**A15**'s neighbourhood; every other criterion stands as written.

- **PRV-A1** A **system** role in the database and absent from the seed refuses with `RoleMissingFromSeed`, naming it, and the run commits nothing. *(PRV3; the reason code has no test today.)*
- **PRV-A2** A **tenant** role absent from the seed neither refuses nor is modified: the run succeeds, does its additive work, and leaves the role's row byte for byte as it was.
- **PRV-A3** A **tenant** role whose code IS in the seed refuses with `SecuritySemanticDrift`, naming the code (PRV2).
- **PRV-A4** An active grant on a **tenant** role, absent from the seed, neither refuses nor is modified — including a grant of a release-owned permission, and including one whose pair the seed lists for a different role.
- **PRV-A5** An active grant on a **system** role, absent from the seed, still refuses with `GrantMissingFromSeed` (PRV5).
- **PRV-A6** A **revoked** grant on a system role that the seed lists still refuses with `RevokedGrantInSeed` (F6), and a seeded grant is never modified (F8), whatever tenant rows exist beside it.
- **PRV-A7** A **permission** in the database and absent from the seed still refuses with `PermissionMissingFromSeed` (PE2: permissions have no tenant half).
- **PRV-A8** A run over a database holding tenant roles and tenant grants, with the release catalogue otherwise current, is a **successful no-op**: no insert, no update, and an event whose counts are all zero.
- **PRV-A9** A refused run still commits nothing to `permission`, `role` or `role_permission` — tenant rows included (A9 unchanged).

#### What this does not decide

- **AUT-C7 on a release-owned role.** Nothing in the command catalogue or the domain stops an administrator adding a permission to a **system** role, and such a grant would then refuse every later deployment as `GrantMissingFromSeed` — correctly, by PRV5. Whether AUT-C7 must refuse system roles outright is **AUT-C7's gate**, not this one. It is recorded as a Known Gap.
- **AUT-C3 and after.** This amendment removes the blocker; it introduces no command, and nothing here creates a tenant role.
- **PE2's enforcement.** Unchanged and still a Known Gap.


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
- **Agents are out of scope** (invariant 17a). The target must be human, so the agent rules (a finite end date, UR8; no human-only permission on the role, UR9) have no reachable path in v1.
  - **Corrected at the AUT-C7/C8 gate (RG10).** This previously claimed both "remain enforced by the database and the domain". Only **UR8** is: `ck_user_role_agent_finite` refuses an agent assignment without an end. **UR9 is enforced nowhere** — no constraint, trigger or domain rule references `requires_human_actor`, and GrantRole refuses every non-human target outright without reading the role's permissions. What holds today is that edge's *absence of a reachable path*, plus **UR10**, which is enforced twice: in the authorisation predicate per permission, and in `HumanActorBehavior` per command.

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

**The email refusal has no remedy yet.** The holder's email can only change through USR-C3, which is blocked on decision A3. The refusal message says what is wrong, and the record is not reactivated. *Since USR-C3 (2026-09-19):* the remedy is to change the returning user's address with USR-C3, which allows an inactive target (CE8), and then reactivate; CE-A9 proves it.

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

> **Superseded for the list (MY8, 2026-09-19):** the section now lists the caller's sessions above the two actions — see *SES-Q2 GetMySessions on the My account page*. The actions below are unchanged.

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
- A session list, which needs SES-Q2 (since built: *SES-Q2 GetMySessions on the My account page*).
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

- **Configuration:** `SKSMCORP_TRUSTED_PROXIES`, a comma-separated list of IP addresses and CIDR ranges. **Absent or empty means no proxy is trusted.** A malformed entry stops the host at startup, as a malformed signing key does.
- **Nothing is trusted implicitly — loopback included.** ASP.NET's forwarded-headers defaults trust loopback; those defaults are cleared.
- **Only `X-Forwarded-For` is read.** `X-Forwarded-Proto` and `X-Forwarded-Host` are ignored: the cross-site check depends on the request's own Host, and this story does not change it.
- **The walk:** starting from the immediate peer, while the current hop is a trusted proxy, the next address to the left in `X-Forwarded-For` becomes the candidate. The first address that is not a trusted proxy is the client. If the immediate peer is not trusted, the header is ignored entirely. An entry that is not an IP address ends the walk, and the address is the last one established by a trusted hop — never the malformed value.
- **The resolved address flows into the existing evidence fields**, unchanged in shape: `SignInSucceeded` and `SignInFailed` (`ipAddress`), `PasswordResetRequested` (`RequestIp`), and `user_session.IpAddress`. Behind a configured proxy they record the real caller rather than the proxy. **No audit schema, catalogue or payload shape changes.**
- **`ActivateAccountCommand` and `ResetPasswordCommand` gain the client address**, for rate limiting only. It is recorded nowhere: `TokenRejected` and the success records are unchanged.
- **Dev (B9):** `compose.dev.yaml` pins the web container's address on the dev network and sets `SKSMCORP_TRUSTED_PROXIES` to exactly that address; Vite's `/api` proxy sends `X-Forwarded-For`. Outside the container nothing is configured, so the header Vite sends is ignored — the safe default.

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
- **RL-25** **Configuration:** a malformed `SKSMCORP_TRUSTED_PROXIES` entry stops the host at startup; absent or empty trusts nothing; IP addresses and CIDR ranges are both accepted.

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
- **A shorthand IPv4 address in `SKSMCORP_TRUSTED_PROXIES`** (`10.5`, `1`) is refused as malformed rather than parsed into an address nobody meant.
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

## SES-C1 — enforcing MustChangePassword at sign-in

**Status:** Contract frozen 2026-09-19 by owner decision (MC1–MC9, with the owner's refinements to MC4 and MC5). It closes the Known Gap *MustChangePassword is recorded but not enforced*, and with it SES-C1 for local credentials.

### Requirement

After an administrator resets a user's password (CRD-C5), **the old password must no longer establish a session.** Only proof of mailbox control — a reset link, completed through CRD-C3 — replaces it. Until then the flag is outstanding, and sign-in with the old password is refused exactly as a wrong password is.

```text
administrator reset (CRD-C5)
    → MustChangePassword = true
    → the old password cannot establish a session
    → a reset token is required (the administrator's link, or Forgot password)
    → a new password is chosen (CRD-C3)
    → MustChangePassword = false
    → normal sign-in
```

**Why not a restricted session.** An administrator reset commonly follows a suspected compromise. A session that could only change the password would let whoever holds the old password — including the person it was compromised to — choose the new one and clear the flag, bypassing the very reset the administrator issued. Refusing sign-in needs no new session concept, and matches CRD-C5's rule that *"issuing a token is the only permitted mechanism."*

### The decisions

| # | Decision |
| --- | --- |
| **MC1** | **Sign-in is refused while `MustChangePassword` is outstanding.** No restricted session. |
| **MC2** | **The refusal is the same generic `401`** as every other sign-in failure: the same status, body and absence of a cookie. A distinct message would confirm to the caller that the old password was correct. |
| **MC3** | **The check runs after the lock check and after the password verifies, and before the caller is established.** A wrong password therefore gets exactly today's outcome, and a refusal is recorded with an Anonymous origin (EO5), as every `SignInFailed` is. |
| **MC4** | **A correct password with the flag outstanding is not a failed attempt,** and it is not a successful one either. See *What a refused sign-in changes*. |
| **MC5** | **CRD-C5 does not revoke existing sessions,** and this story does not change CRD-C5. Sessions established before the reset remain valid until they end or are revoked (for example with *Sign out everywhere*). |
| **MC6** | **What clears the flag is unchanged:** CRD-C3 (any reset link, administrator-issued or self-service) and CRD-C4 (from a session that already exists). CRD-C6 `UnlockAccount` does not. Already proved by `ResetPasswordIntegrationTests`, `ChangePasswordIntegrationTests` and `UnlockAccountIntegrationTests`. |
| **MC7** | **No client change is required.** The sign-in page already shows its fixed rejection for any `401`. |
| **MC8** | **Change control**, recorded below. No schema change, no new audit event, no `AuditEventCatalogue.Version` bump. |
| **MC9** | **SES-C1 is Done for local credentials** when this lands. External identity-provider sign-in is deliberately deferred to IDN-C1 / IDN-Q2. |

### What a refused sign-in changes (MC4)

```text
password correct
    → MustChangePassword = true
    → SignInFailed, failureCategory = PasswordChangeRequired
    → generic 401
```

- **Written:** one `SignInFailed` (Autonomous, Anonymous origin, no actor), with the identity as primary entity and `FailureCategory = "PasswordChangeRequired"`.
- **Not written:** no session, no `SignInSucceeded`, no `AccountLocked`.
- **The credential is untouched by the refusal:** `FailedAttemptCount` is **not** incremented, and is **not** cleared either; `LockedUntil` is not set; no rehash happens even if the stored algorithm is stale; `PasswordHash`, `PasswordAlgorithm`, `PasswordChangedAt` and `MustChangePassword` itself are unchanged. A correct password must not make the attempt behave partly like a successful sign-in.
- **Repeated refusals never lock the account**, however many there are.
- **Unchanged, and independent of the flag:** an **expired** lock is still cleared before the password is checked (SES-C1's existing rule: the window it belonged to has ended). That happens whether the password is right or wrong, so it is not a side effect of correctness.

**The order, in full:**

1. Resolve the local identity (unusable → `IdentityNotUsable`, decoy derivation).
2. A live lock → `AccountLocked`, decoy derivation. An expired lock is cleared.
3. Verify the password (wrong → `CredentialsRejected`, counted; may lock).
4. **New:** `MustChangePassword` outstanding → `PasswordChangeRequired`, refused. Nothing else changes.
5. Establish the caller; rehash if needed; clear the counters; create the session; `SignInSucceeded`.

**The timing difference is accepted.** A refused correct password writes no counter, as a live lock writes none today, while a wrong password does. The difference is one row update inside a transaction; behaviour 11 limits each username to 10 attempts in 15 minutes.

### Change control (MC8)

1. **SES-C1's catalogue precondition** reads *"Identity active, user active, credential exists and not locked, password verifies."* It gains: **"and no password change required by an administrator reset is outstanding (MustChangePassword)."** The workbook is not edited here.
2. **`SignInFailed.failureCategory`** gains the value **`PasswordChangeRequired`**. It is an internal payload value — the response stays generic — so the event type, its version and the payload's shape are unchanged. (The catalogue's list of categories already differs from the values the code records: `IdentityNotUsable`, `AccountLocked`, `CredentialsRejected`. That reconciliation is outstanding change control too, and not changed here.)
3. **The entity model's `credential.MustChangePassword`** — *"Set after an administrator-initiated reset"* — gains its meaning: **"While true, the password on file cannot establish a session; it is cleared when a new password is set through a reset link (CRD-C3) or a password change (CRD-C4)."**

### Implementation notes

- **One branch in `SignInCommandHandler`**, directly after the failed-verification branch and before `IBearerActorEstablisher.EstablishAsync`. It calls only `DeclareFailure` — the same Anonymous `SignInFailed` helper every refusal uses — and returns `SignInResult.Failure()`. Everything that belongs to a completed sign-in sits below it and is never reached: caller establishment, the rehash, `Credential.Unlock()`, the session and `SignInSucceeded`.
- **Before it, unchanged:** identity resolution, the live-lock check, the expired-lock clearing, and `IPasswordHasher.Verify`, which compares and writes nothing.
- **The endpoint needed no change:** `AuthEndpoints` already maps every `SignInResult.Failure()` to the one `401` body, so MP-7's byte-for-byte equality holds by construction, and the test keeps it.
- **CRD-C5's class note** now says SES-C1 enforces the state it records.

### UI guidance (not part of the security contract)

Optional, and not built in this story unless the owner asks:

- A fixed help sentence under **every** sign-in failure, such as *"If an administrator reset your password, use the link in their email."* It is shown whatever the cause, so it reveals nothing.
- In the Reset password dialog: *"This doesn't sign the person out. Use Sign out everywhere if the account may be compromised."*

Neither is coupled to the authentication behaviour.

### Acceptance Criteria

**The refusal (MC1–MC4)**

- **MP-1** A correct password with `MustChangePassword` outstanding is refused: `Succeeded = false`, no session, no `SignInSucceeded`.
- **MP-2** Exactly one `SignInFailed` is recorded, with `FailureCategory = "PasswordChangeRequired"`, the identity as primary entity, Anonymous origin and no actor.
- **MP-3** The credential is unchanged by the refusal: a seeded `FailedAttemptCount` of 2 stays 2; `LockedUntil` stays null; `PasswordHash`, `PasswordAlgorithm`, `PasswordChangedAt` and `MustChangePassword` are unchanged — including when the stored algorithm is stale, so no rehash happens.
- **MP-4** Six refused sign-ins in a row (more than the baseline `MaxFailedLoginAttempts`) never lock the account and write no `AccountLocked`; the counter is unchanged.
- **MP-5** A **wrong** password with the flag outstanding is exactly today's `CredentialsRejected`: the counter increments.
- **MP-6** A **live lock** with the flag outstanding is exactly today's `AccountLocked` refusal: the lock check comes first.
- **MP-7** Over HTTP, the refusal is **byte-identical** to a wrong password's: `401`, the same body, and no carrier cookie set.

**Recovery (MC5, MC6)**

- **MP-8** **Through a reset link:** with the flag outstanding, the old password is refused; CRD-C3 with a reset token succeeds; the flag is then false; the new password signs in (`204`), and `SignInSucceeded` is recorded.
- **MP-9** **From an existing session:** a session established before the flag was set still authenticates (`/api/me` answers `200`); CRD-C4 through it clears the flag; the new password then signs in.

**Browser, in the dev stack (MP-10),** each state change approved by the owner:

1. As Ada, **Reset password** for V R, with a reason.
2. V R's **old password** is refused with *"Invalid username or password."* — and V R's account is **not locked**, and its failed-attempt counter has **not** increased.
3. V R opens the link from `.secrets/mail/` and sets a new password.
4. The credential shows `MustChangePassword = false`; the new password signs in normally, `SignInSucceeded` is recorded, and V R's normal access works.

### Not included

- A restricted, change-password-only session.
- Revoking sessions at CRD-C5 (MC5), and any change to CRD-C5 or its email.
- Password expiry (`PasswordChangedAt` *"supports any future expiry policy"*).
- External identity-provider sign-in (IDN-C1, IDN-Q2), deferred by MC9.
- The optional UI guidance above.

---

## SES-Q1 GetActiveSessions and Revoke on the User detail page (story 3)

**Status:** Contract frozen 2026-09-19 by owner decision (SS1–SS10, with the owner's two clarifications on `current` and `idleExpiresAt`). This builds on the User detail page (stories 1 and 2, #69 and #70) and on SES-C3 `RevokeSession` (`POST /api/sessions/{sessionId}/revoke`), which is **unchanged**.

### Requirement

On the User detail page, an administrator allowed to read sessions sees where the user is signed in. An administrator who may also revoke sessions can end one, with a reason — except the one they are using, which the page marks as theirs.

The read and the command judge "active" identically, so the page lists exactly what Revoke can act on.

### The decisions

| # | Decision |
| --- | --- |
| **SS1** | **The read is `GET /api/users/{userId}/sessions`, under `session.read`, for one user.** Human users only: an unknown user and the System actor get *"The user does not exist."*, as GetUser and IDN-Q1 do. Not audited. The catalogue's system-wide form (SES-Q1 with `UserId` omitted) is deferred. |
| **SS2** | **"Active" is exactly the existing canonical test** — the one the per-request session check and SES-C3 apply, owned by `IUserSessionRepository` (not revoked, before absolute expiry, within the idle timeout widened by the enforcement tolerance, an Active identity of an Active human user) — across **every** identity of the user. The read reuses `FindActiveForUserAsync`, which SES-C4 already uses; nothing is restated. An inactive user has no active sessions. |
| **SS3** | **Eight fields per session:** `sessionId`, `createdAt`, `lastActivityAt`, `expiresAt`, `idleExpiresAt`, `ipAddress`, `userAgent`, `current`. No identity is returned: identity data stays under `identity.read`. See *The two derived fields*. |
| **SS4** | **Most recently active first**; ties broken by session id. |
| **SS5** | **An "Active sessions" section below Sign-in identities, mounted only with `session.read`** — without it the section is absent **and its request is never made**. Columns: **Signed in**, **Last active**, **IP address**, **Browser** (the raw user-agent string), and the Revoke action. The caller's own row is marked **This session**. With none: *"No active sessions."* Its loading, error and retry are its own. |
| **SS6** | **Revoke is offered only when the caller holds `session.revoke` and the row is not `current`.** Hidden, never disabled. **A UI affordance, not authorization:** SES-C3 gains no self rule — revoking one's own session is signing out, which the account footer already offers. |
| **SS7** | **The Revoke dialog** is the existing confirmation-with-reason pattern; the **reason is required**; it sends exactly `{ reason }`. Refusals are shown word for word. When it **settles — success or refusal** — the sessions are **re-read** (an already-ended session answers `204` and the re-read drops it). Nothing else is re-read. |
| **SS8** | **Sign out everywhere and Deactivate also re-read the sessions**, because both end them. |
| **SS9** | **The seeded roles, proved:** a user administrator sees the list and Revoke; an access reviewer sees the list without Revoke; a security administrator sees no section, and **no request is made**. |
| **SS10** | **Out of scope:** the system-wide list; SES-Q2 My sessions (the next story, which reuses this read); parsing user agents, IP geolocation; ended sessions and history (the audit trail, AUD-Q7); an identity column. |

### The two derived fields (owner clarifications)

**`current` is server-derived, and is a session-level fact.** It is `true` for exactly one row at most: the session **whose id is the caller's own session id** — not every session of the caller's identity, which would mark all of an administrator's sessions on their own page. The caller's session id reaches the query the way it reaches `MeQuery` and SES-C2: **the Host passes `CurrentCarrier.SessionId` as an explicit query input**, recovered from the carrier it verified. It is not put into the execution context (`CurrentCarrier` records why: the application layer stays free of access-token transport state), it is not added to `/me`, and **the client never names it or infers it** — it trusts `current`.

**`idleExpiresAt` keeps `/me`'s conservative semantics.** The server computes `lastActivityAt + the effective SessionIdleTimeout`, **without** the enforcement tolerance — exactly as `/me` does. It is an informational instant, not a deadline: activity writes are throttled and enforcement carries a tolerance, so a listed session may still be accepted slightly after it. The browser displays it as an instant and **never computes it or counts down from it**. (Because the list uses the tolerant canonical test, a session inside the tolerance window can be listed with an `idleExpiresAt` already past; that is the same conservatism, not a contradiction.)

### The response

```json
{
  "sessions": [
    {
      "sessionId": "…",
      "createdAt": "2026-09-19T11:40:31Z",
      "lastActivityAt": "2026-09-19T11:52:07Z",
      "expiresAt": "2026-09-19T19:40:31Z",
      "idleExpiresAt": "2026-09-19T12:22:07Z",
      "ipAddress": "192.168.65.1",
      "userAgent": "Mozilla/5.0 …",
      "current": false
    }
  ]
}
```

`ipAddress` and `userAgent` may be `null` (both are nullable security evidence).

### Change control

Outstanding against the frozen catalogue; no workbook is edited:

1. **SES-Q1 *Returns*:** "computed idle remaining" is served as **`idleExpiresAt`**, a conservative instant with `/me`'s semantics, and SES-Q1 gains **`current`** — which the catalogue gives only to SES-Q2 — derived server-side from the caller's session.
2. **SES-Q1 *Parameters*:** `UserId` is required in this slice; the system-wide form is deferred (SS1).

No schema change, no new audit event, and SES-C3 is unchanged.

### Acceptance Criteria

**The read (SS1–SS4)**

- **SQ-1** For a user with sessions on **two identities**, the read returns exactly the sessions the canonical test accepts: an active session on each identity is listed; a **revoked**, an **expired** and an **idle-beyond-tolerance** session are not. Each row has exactly the eight fields, most recently active first, ties by id.
- **SQ-2** The read and SES-C3 agree: every listed session is one `FindActiveAsync` accepts, and every seeded session that is not listed is one SES-C3 treats as already ended.
- **SQ-3** `idleExpiresAt` equals `lastActivityAt` plus the effective `SessionIdleTimeout`, with no tolerance added.
- **SQ-4** An **inactive** user has no active sessions (an empty list, not a refusal). An unknown user and the System actor are refused with *"The user does not exist."*. Without `session.read` the read is refused (`400`); without a carrier, `401`. No audit record is written.
- **SQ-5** **`current` is session-level and server-derived:** with two sessions of the caller's own identity, reading as one of them marks **only that one** `current`; reading as the other flips it. A session of another user is never `current`. Over HTTP, `current` follows the carrier presented, and nothing the client sends can set it.

**The page (SS5–SS9)**

- **SQ-6** The section is shown only with `session.read`. Without it the section is absent and **no sessions request is made** (counted). It has its own loading, error and retry, and its failure leaves the page standing. With no sessions it says *"No active sessions."*.
- **SQ-7** The columns are Signed in, Last active, IP address and Browser; a missing IP or user agent shows "—". The `current` row is marked **This session**. `idleExpiresAt` is never used to count down.
- **SQ-8** Revoke is offered exactly when the caller holds `session.revoke` **and** the row is not `current`: absent without the permission, and absent on the `current` row.
- **SQ-9** The dialog requires a reason and sends nothing without one; it sends exactly `{ reason }` to `POST /api/sessions/{sessionId}/revoke`; it is busy while sending and sends once. On `204`: close, re-read the sessions, announce. A refusal is shown word for word, the dialog stays open, and the sessions are re-read too.
- **SQ-10** After **Sign out everywhere** and after **Deactivate** succeed on the page, the sessions are re-read.
- **SQ-11** (SS9) Against the seeded roles: **user administrator** — section, request and Revoke; **access reviewer** — section and request, no Revoke; **security administrator** — no section and **no request**.
- **SQ-12** No accessibility violations with the section loaded, with the dialog open, and in the loading and error states.

**Browser, in the dev stack (SQ-13),** each state change approved by the owner:

1. As Ada, V R's page lists V R's live sessions.
2. Revoke one with a reason: `SessionRevoked` is recorded with **`AdminRevoked`** in the session's After and **the reason preserved** as the audit Reason; after the re-read the row is gone; a request presenting that session's carrier receives the existing session-invalid response (`401`).
3. On **Ada's own page**, her current session is marked **This session**, and **no Revoke is rendered for it**.
4. After **Sign out everywhere** for V R, the section re-reads to *"No active sessions."*.

### Implementation notes

- **No new session semantics.** `UserSessionsQueryHandler` reads through `IUserSessionRepository.FindActiveForUserAsync` — the method SES-C4 already uses — with the effective `SessionIdleTimeout`, so the list and SES-C3 apply one test. It adds only the unknown-user refusal (a missing user or a non-human actor), the order (`LastActivityAt` descending, then session id), and the two derived fields.
- **`current`** is `x.Id == query.CallerSessionId`. The endpoint passes `CurrentCarrier.SessionId`, exactly as the `/me` endpoint builds `MeQuery`. Nothing about sessions was added to the execution context or to `/me`.
- **The page:** `UserSessionsSection` is mounted after Sign-in identities when `useCan(readSessions)`. **This session** sits in the Signed in cell. The Revoke column exists only for a `session.revoke` holder, so an access reviewer sees four columns, not an empty fifth. Each Revoke button is named *"Revoke session signed in {time}"*, so rows are distinguishable to assistive technology. The eligibility rule is `sessionActions.revokeOffered`.
- **Re-reads (SS7, SS8):** `useRevokeSession` invalidates the sessions `onSettled`. Sign out everywhere invalidates them on success. Deactivate and Reactivate share one lifecycle mutation, so both re-read them; after a reactivation the re-read is simply unchanged.

### Not included

- The system-wide session list; SES-Q2 My sessions.
- Any change to SES-C3, SES-C4, `/me` or the execution context.
- User-agent parsing, IP geolocation, session history, an identity column.

---

## SES-Q2 GetMySessions on the My account page

**Status:** Contract frozen 2026-09-19 by owner decision (MY1–MY9, with the owner's refinement to MY2). It follows *SES-Q1 GetActiveSessions and Revoke on the User detail page* (#73), whose session semantics it shares, and fills the list M7 left out of the My account page.

### Requirement

On My account, every signed-in person sees where they are signed in, with the session they are using marked. They end other sessions with the existing **Sign out other sessions** and **Sign out everywhere**; no new command is introduced.

**SES-Q1 and SES-Q2 share session semantics, not HTTP or web implementation.**

### The decisions

| # | Decision |
| --- | --- |
| **MY1** | **The read is `GET /api/account/sessions`**: authenticated, **no permission** (self, as `/me` and CRD-C4). It returns the sessions of the **caller's own user**, taken from the execution context; nothing in the request names a user. Not audited. |
| **MY2** | **Identical semantics to SES-Q1, through one Application-level listing service** (see *One listing, two access rules*). The two handlers differ only in whose sessions they list and how access is decided. |
| **MY3** | **`current` is SES-Q1's rule:** `session.Id == CurrentCarrier.SessionId`, the id the Host passes explicitly. The browser renders it and never works it out from timestamps, addresses, browsers or anything else. The caller is signed in, so exactly one row is current. |
| **MY4** | **No per-session self sign-out.** The catalogue has no self command to end one other session, and SES-C3 is administrator-only. **Sign out other sessions** remains the mechanism. |
| **MY5** | **The list sits inside the existing Sessions section, above the two buttons.** Columns: **Signed in**, **Last active**, **IP address**, **Browser**; a missing value shows "—"; the current row is marked **This session**. **No action column.** Its loading, error and retry are its own, and its failure leaves both buttons working. |
| **MY6** | **The list is re-read after a successful Sign out other sessions** and **after a successful password change** (A5 ends the other sessions). Sign out everywhere leaves the page, so nothing is re-read. |
| **MY7** | **The `account` web module owns its read:** its own API operation, schema, hook and table, importing nothing from `users` internals. `formatInstant` moves to `shared/` as a formatting-only utility — no session or business logic — so both pages show instants identically, and neither module depends on the other. |
| **MY8** | **Contract linkage:** M7's *"It lists no sessions, because that needs SES-Q2, which is not built"* now points here; the catalogue's *"Powers the sign-out-everywhere screen"* holds. |
| **MY9** | **Out of scope:** per-session self sign-out (MY4), user-agent parsing, session history. |

### One listing, two access rules (MY2)

```text
SES-Q1 handler: authenticate → authorise session.read → the target must be a human user ─┐
                                                                                         ├─ ActiveSessionListing
SES-Q2 handler: authenticate → the caller's own UserId (self; no permission) ───────────┘
```

`ActiveSessionListing` is an **Application-level service**, not an HTTP or web abstraction. It is the **only** implementation of:

- the active-session predicate (through `IUserSessionRepository.FindActiveForUserAsync`, the canonical test);
- traversal of every identity of the user;
- the order (most recently active first, then session id);
- `idleExpiresAt` (last activity plus the effective idle timeout, no tolerance);
- `current` (`session.Id == callerSessionId`);
- the projection to the eight fields.

SES-Q1's handler is refactored onto it in this story, so there is one implementation, not two that agree today. Its behaviour is unchanged, and its tests stay as they are.

### The response

Exactly SES-Q1's shape: `{ "sessions": [ … ] }` with `sessionId`, `createdAt`, `lastActivityAt`, `expiresAt`, `idleExpiresAt`, `ipAddress`, `userAgent`, `current`.

### Acceptance Criteria

**The read (MY1–MY3)**

- **MS-1** For a caller with sessions on **two identities**, the read returns exactly the caller's sessions the canonical test accepts — revoked, expired and idle-beyond-tolerance sessions are not listed — most recently active first, and **no other user's session ever appears**.
- **MS-2** **One semantics:** for the same user and the same caller session, the SES-Q2 read (as that user) and the SES-Q1 read (as an administrator) return **identical** lists, field for field and in the same order.
- **MS-3** `current` is session-level: with two sessions of the caller, reading as one marks only that one; reading as the other flips it. Exactly one row is current.
- **MS-4** **No permission is needed:** a caller holding no role reads their own sessions. Without a caller the read is refused (`401` over HTTP). No audit record is written.
- **MS-5** Over HTTP: exactly the eight fields; `current` follows the carrier presented; a `userId` or any other parameter in the request cannot make it read another user.
- **MS-6** Over HTTP, after **Sign out other sessions**, the read returns exactly one session, marked `current`; after a **password change** from one of two sessions, likewise.

**The page (MY5–MY7)**

- **MS-7** Every signed-in caller sees the list in the Sessions section, above **Sign out other sessions** and **Sign out everywhere**, with columns Signed in, Last active, IP address and Browser; a missing value shows "—"; the current row says **This session**; there is **no Revoke or other per-row action**.
- **MS-8** The list has its own loading, error and retry; when it fails, both buttons still work.
- **MS-9** After **Sign out other sessions** succeeds, the list is re-read; after a **password change** succeeds, the list is re-read. **Sign out everywhere** still signs out and goes to sign-in, as M7 decided.
- **MS-10** No accessibility violations with the list loaded, while it loads, and when its read failed.
- **MS-11** The User detail page's Active sessions (SQ-6 to SQ-12) is unchanged, and still shows instants the same way through the moved `formatInstant`.

**Browser, in the dev stack (MS-12),** each state change approved by the owner, **after a fresh host restart**:

1. V R signs in, in two browsers. On My account in one of them: **two rows**, **This session** on that browser's own row.
2. **Sign out other sessions** in that browser:
   - the **other browser** is refused on its next request and lands on sign-in;
   - the **current browser** stays signed in;
   - the list re-reads to **exactly one** session;
   - that row is marked **This session**.
3. (Optional) With a second session open again, **change the password**: the list re-reads to one row.

### Implementation notes

- **`ActiveSessionListing`** (`Users/Queries/UserSessions`) is the shared service, registered scoped. `UserSessionsQueryHandler` now authenticates, authorises `session.read`, refuses a non-human target, and delegates. `MySessionsQueryHandler` authenticates and delegates with the execution context's `UserId`. Neither handler touches the repository or the security policy any more.
- **SES-Q1's tests were not changed** and pass on the shared service. MS-2 compares the two reads' `UserSessionView` lists by record equality.
- **`GET /api/account/sessions`** sits in `AccountEndpoints` and passes `CurrentCarrier.SessionId`, as the `/me` endpoint does.
- **The page:** `MySessionsList` is rendered in the Sessions section above `SessionActions`. On success, **Sign out other sessions** calls `useRefreshMySessions` from the account module, because its mutation hook belongs to the auth module. `useChangePassword` invalidates the list on success itself.
- **`formatInstant`** is now `src/shared/format/formatInstant.ts`, which is formatting only. The users module imports it from there.

### Not included

- Ending one other session of one's own (MY4); any new command.
- Any change to SES-C2, SES-C4, CRD-C4, `/me` or SES-Q1's behaviour.
- User-agent parsing and session history.

---

## IDN-Q3 CheckUsernameAvailable on Create user

**Status:** Contract frozen 2026-09-19 by owner decision (UN1–UN10, with the owner's clarification of UN6 and ruling on surrounding whitespace). Read-only: USR-C1, IDN-C2 and the username index are unchanged.

**Amended 2026-09-19** by *Local usernames refuse surrounding whitespace* (UN2, UN5, UA-2, UA-5, and a `refused` state in UN6): the client sends the username exactly as typed, and a value the shared domain rule refuses is refused rather than looked up.

### Requirement

On Create user, an administrator allowed to read identities learns, before submitting, whether the username they typed is already taken — and cannot submit one the server has already said is taken. USR-C1 stays the authority at submit.

**IDN-Q3 and USR-C1 answer the same question the same way:** `IUserIdentityRepository.ExistsWithUsernameAsync` is the compatibility seam between them, proved by a contract test, not merely shared for convenience.

### The decisions

| # | Decision |
| --- | --- |
| **UN1** | **`POST /api/identities/username-availability` with `{ "username": "…" }`.** A read with no side effects, carried in the body so the typed value never reaches request URLs — access logs, proxies or browser history — as a query string would. |
| **UN2** | **Exactly `ExistsWithUsernameAsync`**: PostgreSQL's `lower()`, local identities, **every status** — the UI7 index's own terms. The value checked is the value sent, untransformed, as USR-C1 receives it; the client sends the trimmed value it would also submit. |
| **UN3** | **`{ "available": true \| false }` and nothing else** — never who holds it, nor whether the holder is active (D2 makes that irrelevant). A blank username is `400` *"A username is required."*. |
| **UN4** | **`identity.read`**, refused otherwise with the usual `400`. Not audited. Not rate limited (behaviour 11 covers anonymous commands; this caller is an authenticated administrator). |
| **UN5** | **The page checks when focus leaves the Username field**, only for a caller holding `identity.read` — without it there is no check **and no request**, and USR-C1's refusal at submit still applies. Not on every keystroke. A blank field is not checked. |
| **UN6** | **Taken blocks Create; the block belongs to the value it was about.** See *The availability states*. |
| **UN7** | **A failed check is ignored:** no message, and submit proceeds for the server to decide. |
| **UN8** | **The result is carried by `FormField`** — the in-use message as the field's error, the available message as its description — tied to the input with `aria-describedby`, and announced politely. |
| **UN9** | **Out of scope:** IDN-C2's UI, suggesting alternatives, username format rules, and **surrounding whitespace** (see *Not included*). |
| **UN10** | **No change control:** the catalogue's Boolean is served as `{ available }`; POST is an HTTP detail. |

### The availability states (UN6, as clarified by the owner)

```text
value edited ─────────────────────────────► unchecked (no message; nothing blocked)
unchecked ── blur, non-blank, identity.read ─► checking (nothing blocked)
checking ── failed ─► unchecked (UN7: ignored)
checking ── { available: false } ─► taken     → "This username is already in use."; Create blocked
checking ── { available: true }  ─► available → "Username available."; Create allowed
taken / available ── any edit ─► unchecked
```

- **Any edit clears the previous result**, so a taken result never blocks a different value, and the form is never left permanently invalid.
- **A result that arrives for a value the field no longer holds is discarded.** An answer about `v.r` never applies to `v.r2`.
- **"Available" is not a promise.** Someone else may take the name between the check and the submit; the server's refusal is then shown word for word, as today.
- Blocking on *taken* does not breach §12's "a schema never rejects input the server would accept": under D2 a taken username can never become available, and USR-C1 refuses it.

### Acceptance Criteria

**The read (UN1–UN4)**

- **UA-1** `available` is `false` for a username held by an **active** local identity, by an **inactive** local identity, and for **case variants** of either; `true` for an unused username; and `true` for a value held only as an **external** identity's username, which the local-only index does not constrain.
- **UA-2** **IDN-Q3 and USR-C1 agree**: for the same set of values — active, inactive, case variants, unused, external-held — IDN-Q3 answers `available: true` exactly when USR-C1 accepts that username, and `false` exactly when USR-C1 refuses it with *"A user identity with this username already exists."*.
- **UA-3** Without `identity.read` the read is refused (`400`); without a caller, `401`; a blank username is `400` *"A username is required."*. No audit record is written.
- **UA-4** Over HTTP the response is exactly `{ "available": … }`; the route accepts only POST with a body; and the typed username appears in **no log line** the host writes for the request.

**The page (UN5–UN8)**

- **UA-5** With `identity.read`, leaving the Username field sends exactly `{ username }` — the trimmed value — once. A blank field sends nothing.
- **UA-6** **Taken:** *"This username is already in use."* is shown on the field and Create sends nothing. **Editing the field** clears the message and the block; the next Create is sent to the server.
- **UA-7** **Available:** *"Username available."* is shown; Create is sent. A server refusal at submit is still shown word for word.
- **UA-8** **A failed check** (a `5xx`, a network failure or a contract violation) shows nothing and does not block Create.
- **UA-9** **A late result is discarded:** when the field changes while a check is in flight, that result neither shows a message nor blocks.
- **UA-10** **Without `identity.read`, no availability request is ever made** — on blur or on submit — and Create works as before.
- **UA-11** No accessibility violations with the in-use message and with the available message shown.

**Browser, in the dev stack (UA-12),** after a fresh host restart, each state change approved by the owner:

1. As Ada on Create user, type `v.r` and leave the field: *"This username is already in use."*, and Create is blocked.
2. `V.R`: in use too.
3. An unused name: *"Username available."*.

### Implementation notes

- **The handler** authenticates, authorises `identity.read`, refuses a blank value, then returns `!ExistsWithUsernameAsync(username)`: the method USR-C1 calls, unchanged. UA-2 proves the two agree value by value.
- **The route** sits in `IdentityEndpoints`. A missing body or field is sent as the blank case and refused by the query. The request record is never logged, and UA-4 checks that no host log line contains the typed value.
- **The page** keeps an `Availability` value of `{ username, state }`:
  - **Blur:** a blur with `identity.read` and a non-blank trimmed value sends one check, unless that exact value is already answered.
  - **Late answers:** each answer is applied only if the field still holds that value, tracked in a ref that every edit updates. Every edit also clears the answer.
  - **Submit:** it is refused locally only when the current value is the one answered *taken*.
  - **Failures:** a failed check clears its own "checking" state and nothing else.
  - **After a successful create:** the form clears the answer along with the fields.
  - **The request:** it is a mutation with `retry: false`, never cached.
- **Announcements (UN8):** *"This username is already in use."* is the field's error. `FormField` renders errors with `role="alert"`, so it is announced immediately, as every field error in the application is. *"Username available."* is the field's description. `FormField`'s description paragraph now carries `aria-live="polite"`, so a description that changes is announced; static descriptions never change, so for them it announces nothing.

### Not included

- **Surrounding whitespace in local usernames** — ruled a separate small story, since delivered as *Local usernames refuse surrounding whitespace*. This story checks exactly the value the client submits.
- IDN-C2's UI, suggestions, format rules.
- Any change to USR-C1, IDN-C2 or the UI7 index.

---

## Local usernames refuse surrounding whitespace (USR-C1, IDN-C2, IDN-Q3, PRV-C3)

**Status:** Contract frozen 2026-09-19 by owner decision (WS1–WS10, with the owner's clarification of WS9). Closes the Known Gap *Local usernames accept surrounding whitespace* and the entities workbook's UI6. **Amends IDN-Q3** (UN2, UN5, UA-2, UA-5); see *IDN-Q3, as amended*.

### Requirement

A local username is an identifier. **Leading or trailing whitespace makes it invalid**: every path that creates or changes a local username refuses it, and nothing silently trims it. Case still does not distinguish usernames.

```text
"ada"                    → valid
" Ada", "Ada ", " Ada "  → invalid: "A username cannot begin or end with whitespace."
"ada" vs "ADA"           → the same identifier (UI7, unchanged)
```

### The decisions

| # | Decision |
| --- | --- |
| **WS1** | **Whitespace is .NET's `char.IsWhiteSpace`.** A username is valid on this rule exactly when `username == username.Trim()`. The set is 25 characters: U+0009–U+000D, U+0020, U+0085, U+00A0, U+1680, U+2000–U+200A, U+2028, U+2029, U+202F, U+205F and U+3000. Whitespace **inside** a username is not affected. |
| **WS2** | **One domain rule, the single source of truth:** `UserIdentity.ValidateUsernameBoundary`. `CreateLocal` and `ChangeUsername` apply it; USR-C1, IDN-Q3 and provisioning call it rather than reproducing it. It refuses a blank username with the existing *"Username cannot be empty."*, then surrounding whitespace with *"A username cannot begin or end with whitespace."*, as a `DomainException`, so over HTTP it is `400 { "error": … }`. |
| **WS3** | **A database CHECK is the backstop, not the definition.** `ck_user_identity_local_username_no_surrounding_whitespace` names the same 25 characters explicitly, as the email control-character CHECK does, rather than a locale-dependent class. A test proves the installed constraint and the domain rule agree on every character. |
| **WS4** | **UI6 is closed in the same migration:** `ck_user_identity_local_username_required`, meaning a local identity's username is not NULL and not empty. External identities are unaffected by both constraints. **No data step:** the dev database (3 local identities) and the shared test database (438) hold no violating row, and a database that did would refuse the migration, which is the wanted outcome. |
| **WS5** | **Validate before the lookup.** USR-C1 applies the rule after parsing the email address and **before** the username uniqueness lookup. An invalid username is refused with the rule's sentence and never queried, so `" v.r"` is refused for its whitespace, never as *already exists*. *Found while writing this contract:* today a blank username reaches `ExistsWithUsernameAsync`, whose argument guard throws, so `""` or `"   "` in USR-C1 is a `500`. Validating first makes it the domain's `400` *"Username cannot be empty."*. |
| **WS6** | **IDN-Q3 uses the same rule before its lookup.** After authentication, authorisation and its own blank refusal (*"A username is required."*, UN3 unchanged), it applies the rule: a spaced value is `400` with the rule's sentence and is never looked up. IDN-Q3 and USR-C1 therefore agree on invalid values as well as on valid ones. |
| **WS7** | **The Create user page stops trimming the username.** It sends exactly what was typed, to IDN-Q3 and to USR-C1. The username is checked for presence only, as the Edit profile form checks its fields. JavaScript's whitespace is not .NET's, so a client-side copy of the rule could refuse a value the server accepts. The other four fields keep today's trimming. |
| **WS8** | **Provisioning stops trimming `--username`** and refuses a spaced value while parsing its arguments: a usage error with exactly the rule's sentence, before any database is touched, never a stack trace. The other options keep their trimming. |
| **WS9** | **The rule is narrow and deterministic.** Invisible format characters such as U+200B and U+FEFF are not `char.IsWhiteSpace` characters, so **this rule accepts them**. Prohibiting them is a separate username-character policy that no specification defines. That is recorded as its own Known Gap, not treated as a whitespace defect. Also out of scope: whitespace inside a username, username length and format rules, the IDN-C2 command and UI, and `ChangeUsername`'s treatment of a case-only change. |
| **WS10** | **No change control.** The catalogues say nothing about whitespace, audit shapes are unchanged, and `AuditEventCatalogue.Version` is not bumped. |

### Every path applies the same rule

```text
UserIdentity
 ├── CreateLocal ────┐
 └── ChangeUsername ─┴── ValidateUsernameBoundary        database CHECKs: the backstop

USR-C1        parse email → ValidateUsernameBoundary → lookups → CreateLocal
IDN-Q3        authenticate → authorise → blank → ValidateUsernameBoundary → ExistsWithUsernameAsync
PRV-C3 CLI    parse options → ValidateUsernameBoundary (usage error) → provision → CreateLocal
IDN-C2        (not built) inherits it through ChangeUsername
```

- An **invalid** username is refused by the rule and **never reaches a uniqueness lookup**.
- A **valid** one goes to `ExistsWithUsernameAsync` exactly as before: available or taken.

### IDN-Q3, as amended

- **UN2:** the value checked is still the value sent. The client now sends exactly what was typed, **not a trimmed value**, and a value the rule refuses is refused rather than looked up.
- **UN5 and UA-5:** the page's blur check sends exactly the typed value. A field that is blank or holds only whitespace is still not checked, since sending it can only produce a refusal the submit will show anyway.
- **UA-2:** agreement now covers invalid values too. For a spaced value, **both** refuse with *"A username cannot begin or end with whitespace."*.
- **UN6 gains a state:** `refused`. See *The page*.

### The page (WS7)

```text
checking ── 400 refusal ─► refused → the server's sentence on the field; Create blocked for that value
refused ── any edit ─► unchecked
```

- **A `400` from the check is a refusal, not a failure.** Its sentence is shown word for word as the field's error, and Create sends nothing while the field holds that value. USR-C1 would refuse the same value with the same sentence, so blocking does not breach §12.
- **Every other failure** (a `5xx`, a network failure, a contract violation) is still ignored, as UN7 says.
- **Submit:** the username is sent untrimmed. A value holding only whitespace passes the presence check, is sent, and USR-C1's *"Username cannot be empty."* is shown word for word.

### Acceptance Criteria

**The rule (WS1, WS2, WS9)**

- **UW-1** The characters the rule treats as whitespace are exactly the 25 listed in WS1. A test enumerates every UTF-16 code unit.
- **UW-2** `CreateLocal` and `ChangeUsername` each:
  - refuse every one of the 25 characters at the start and at the end, with *"A username cannot begin or end with whitespace."*;
  - accept each of them inside a username;
  - refuse a blank value with *"Username cannot be empty."*;
  - accept `"\u200Bada"` and `"\uFEFFada"` (WS9).
  A refused `ChangeUsername` leaves the username unchanged.

**The paths (WS5, WS6, WS8): one invariant**

- **UW-3** **Every path refuses with the same sentence.** For one shared set of spaced values (a space, a tab, a line feed, U+00A0, U+2028 and U+3000, each leading and each trailing):
  - `UserIdentity.CreateLocal` and `ChangeUsername` refuse;
  - **USR-C1** refuses, and writes nothing: no user, identity, token, audit record or notification;
  - **IDN-Q3** refuses;
  - the **PRV-C3** provisioner, on a fresh database, refuses and commits nothing;
  - the **provisioning CLI** refuses the value as `--username`.
- **UW-4** **USR-C1 and IDN-Q3 never look up an invalid username.** A recording repository sees no `ExistsWithUsernameAsync` call for a spaced value, from either path.
- **UW-5** **USR-C1:**
  - `" {held}"`, where `{held}` is a username in use, is refused for its whitespace, not as *already exists*;
  - `""` and `"   "` are `400` *"Username cannot be empty."*, not `500`;
  - a username with an inner space is accepted.
- **UW-6** **IDN-Q3:** UA-2 as amended. Each candidate is available exactly when USR-C1 accepts it, taken exactly when USR-C1 refuses it as *already exists*, and refused with the rule's sentence exactly when USR-C1 refuses it with that sentence. The spaced candidates are among them.

**The database (WS3, WS4)**

- **UW-7** **Equivalence.** The installed `ck_user_identity_local_username_no_surrounding_whitespace` expression is evaluated for a local username starting with, and ending with, every UTF-16 code unit except NUL and the surrogates. It refuses **exactly** the values `ValidateUsernameBoundary` refuses.
- **UW-8** **Enforcement:**
  - A raw `INSERT` of a local identity with a spaced username fails with `23514` naming that constraint, and so does an `UPDATE` to one.
  - A local identity with a NULL or empty username fails with `23514`, naming `ck_user_identity_local_username_required`.
  - An **external** identity with a spaced or NULL username is accepted.

**Over HTTP**

- **UW-9** `POST /api/users` and `POST /api/identities/username-availability` answer a spaced username with `400 { "error": "A username cannot begin or end with whitespace." }`.

**Provisioning (WS8)**

- **UW-10** `--username " ada"` or `--username "ada "`:
  - `Parse` returns no options and exactly the rule's sentence as its error;
  - the CLI exits with the usage-error code, writes no stack trace, and never reads the connection setting.
  - The other options are still trimmed.

**The page (WS7)**

- **UW-11** Create sends `initialUsername` exactly as typed: `"  ada.lovelace  "` is sent with its spaces, while the other four fields are still trimmed. A whitespace-only username is sent, and the server's sentence is shown word for word.
- **UW-12** Leaving the field sends exactly the typed value to IDN-Q3. A blank or whitespace-only field sends nothing.
- **UW-13** A `400` from the check shows its sentence on the field, and Create then sends nothing. An edit clears it, and the next Create is sent. A `5xx` is still ignored.
- **UW-14** No accessibility violations with the refusal shown. The refusal is tied to the input by `aria-describedby`.

**Browser, in the dev stack (UW-15),** after a fresh host restart, each state change approved by the owner. As Ada on Create user:

1. Type `" v.r2"` and leave the field: *"A username cannot begin or end with whitespace."*, and Create is blocked.
2. `"v.r2 "`: the same.
3. `"v.r2"`: *"Username available."*

Nothing is submitted, so no user is created.

### Implementation notes

- **The rule** is `UserIdentity.ValidateUsernameBoundary`. A blank value is refused with *"Username cannot be empty."*; then a value that `Trim()` would change (an ordinal comparison) is refused with the rule's sentence.
  - `CreateLocal` and `ChangeUsername` call it in place of their own blank checks, so their blank message is unchanged.
  - `ChangeUsername` applies it **before** its "only the case differs, so nothing changes" comparison.
- **USR-C1** calls the rule immediately after parsing the email address, before either uniqueness lookup. `CreateLocal` applies it again later, harmlessly.
- **IDN-Q3** calls the rule after its own blank refusal and before `ExistsWithUsernameAsync`.
- **The constraints** are declared in `UserIdentityConfiguration` with `HasCheckConstraint`, so the model snapshot carries them, and created by the migration `AddLocalUsernameChecks`.
  - The character class is one constant in that configuration, written with the regex engine's `\u` escapes.
  - The shared test database was migrated out of band, as the migrator role, as for earlier migrations.
- **Provisioning:** `ProvisioningOptions.Parse` calls the rule after its required-options check. A whitespace-only `--username` is therefore still reported as missing, and a spaced one is refused with the rule's sentence. The other options are trimmed as before.
- **The page:**
  - `Availability` gains `refused`, carrying the server's sentence.
  - Only an `ApiError` with status `400` is a refusal. A `5xx`, a network failure and a contract violation are still ignored.
  - An error that arrives after the field changed is discarded, like a late answer.
  - The blur check skips a value that JavaScript's `trim()` leaves empty. That only suppresses a request and never blocks Create, so the difference between JavaScript's and .NET's whitespace cannot refuse anything the server accepts.
- **Tests:**
  - Before the rule existed, the HTTP test's spaced create succeeded. It hands any `201` to the harness's cleanup, so no red run can leave a spaced username in the shared database for the new constraint to refuse at migration.
  - The CLI half of the UW-3 invariant lives in `SKSMCorp.Provisioning.Tests`, which is a separate project.

### Not included

- **Invisible format characters** such as U+200B and U+FEFF (WS9, its own Known Gap).
- Whitespace inside a username, length and format rules.
- The IDN-C2 command and UI, and `ChangeUsername`'s treatment of a case-only change.
- The other Create user fields' trimming, and the name rules (Known Gap *Name rules differ between USR-C1 and USR-C2*).

---

## USR-C3 ChangeUserEmail — the administrator command, and its UI

**Status:** Contract frozen 2026-09-19 by owner decision (CE1–CE13, with the owner's changes to CE2 and CE9 and a clarification of CE7). Closes open decision **A3** for its administrator half.

### Requirement

An administrator holding `user.update` changes another human's email address. The change takes effect immediately and is attributable in the trail. Links already sent to the old address stop working. Nothing is sent anywhere by this command.

```text
USR-C3 — an administrator changes a human user's email
   ├── validate the address
   ├── lock the target user
   ├── no-op if it is the same effective address
   ├── refuse if another non-inactive human holds it
   ├── change the email
   ├── invalidate open activation and reset tokens
   └── UserEmailChanged
```

### The decisions

| # | Decision |
| --- | --- |
| **CE1** | **A3 is closed as option (c), administrator half.** An administrator's change is immediate and audited. **Self-service** (a user changing their own address, which A3 (c) requires to be verified) is **not built**: it is its own future command, recorded as a Known Gap, as USR-C2's "Admin or self" split was. |
| **CE2** | **No rule about who the target is** (owner's change). This is the administrator command, with the administrator command's single meaning. It does not quietly acquire a second, self-service semantics, and it does not carry an "except yourself" refusal. The self-service path is deferred entirely. |
| **CE3** | **`POST /api/users/{userId}/email`** with `{ "email": "…", "reason": "…" }` → `204`, under **`user.update`**. Like USR-C2 under the same permission, the command is not marked human-only; the catalogue's *"Human actors only"* is about the **target** (CE8). `email` is required. `reason` is optional, as the catalogue says: a missing, empty or whitespace-only reason means none. Any other reason is recorded as the audit record's Reason, exactly as sent. |
| **CE4** | **The address is `EmailAddress.Create`**, exactly as USR-C1: trimmed, structurally checked, no control character. An invalid one is `400` *"Email address has an invalid format."*. |
| **CE5** | **Uniqueness among other non-inactive humans, whatever the target's status.** The *holder* is what matters: if another human who is not inactive holds the address (compared with `lower()`), the change is refused with *"A user with this email address already exists."*. An address held only by an inactive human, or by nobody, is available. The pre-check excludes the target itself. `ux_app_user_active_human_email` remains the guarantee, and a race past the pre-check is refused by it with the same sentence. |
| **CE6** | **The same effective address is no change.** When the new address equals the current one ignoring case (the domain's existing `EmailAddress` equality), the answer is `204`. Nothing is written, no audit record is made, and no token is touched. |
| **CE7** | **Open tokens are invalidated, and nothing replaces them** (owner's clarification). Every outstanding activation and password-reset token of the user's identities is invalidated at the command's instant (`InvalidateOutstandingForUserAsync`, as USR-C4 does). **No `TokenInvalidated` record is written**, following the D13 precedent: the frozen event requires a superseding token, and none is fabricated. **Changing an email never issues a replacement token.** For a user who has not activated, CRD-C7 Resend activation is how a new link reaches the new address. A notification still pending for an invalidated token is held back by the existing eligibility gate (`TokenNotLive`). |
| **CE8** | **Inactive users are allowed.** This is the remedy for the USR-C5 refusal *"Another active user now has this user's email address."*. The System actor and agents are refused with *"This user's email cannot be changed."*, and an unknown user with *"The user does not exist."*. |
| **CE9** | **The target's row is locked** (`FindForUpdateAsync`, the D6 lock) before anything about the user is read, and the clock is read after the lock. **CRD-C7 is not changed** (owner's change). Its race with this command is recorded as a Known Gap for its own concurrency story. |
| **CE10** | **Sessions are untouched**: an email is not a credential. **No notice is sent** to the old or new address, because non-secret notifications are not V1. That is recorded as a Known Gap. |
| **CE11** | **`UserEmailChanged` v1**: `Primary("User")`, `Before { Email }`, `After { Email }` (both PII paths of the primary subject), and the Reason when given. `AuditEventCatalogue.Version` is unchanged. |
| **CE12** | **One story, backend and UI.** See *The UI*. |
| **CE13** | **Out of scope:** the self-service command and verification (A3's other half), an `EmailVerification` notification type, notices to either address, any change to sign-in (usernames only), and any change to CRD-C7. |

### Order of work in the handler

1. **Parse the address** (CE4). Normalise the reason (CE3).
2. **In the pipeline's transaction:** lock and load the target (CE9). Refuse an unknown user, the System actor or an agent (CE8).
3. **Read the clock**, after the lock.
4. **No-op check** (CE6).
5. **Uniqueness** (CE5): the pre-check excludes the target.
6. `User.ChangeEmail`.
7. **Invalidate outstanding tokens** (CE7).
8. **Declare `UserEmailChanged`** (CE11).

### Change control

**No workbook is edited.** Each of these is outstanding change control, like the USR-C2 and USR-C4/C5 items:

1. **Command catalogue, USR-C3.** The row reads *"Admin or self, `user.update` / self"*. This is the **administrator** command. The self-service command, verified per A3 (c), is separate and not yet built.
2. **Command catalogue, USR-C3.** The row lists `user_token` only *"if re-verification adopted"*. This command writes `user_token` (invalidation) **without** re-verification.
3. **Open decisions, A3.** Closed as (c) for administrator changes; the self-service half is open.
4. **Audit workbook, `TokenInvalidated`.** As for USR-C4 (D13), invalidation without a superseding token has no per-token record.

### The UI (CE12)

- **Change email** is a row action for `user.update`, on **active and inactive** rows. It appears in the list and on User detail, through the shared action matrix, after Edit profile and before Deactivate. The USR-C4/C5 matrix test is updated to the amended table.
- **The dialog, titled *"Change email for {display name}"*:**
  - It shows the current address, then **New email** and **Reason (optional)**.
  - **Presence only:** an empty New email sends nothing and says *"An email address is required."*.
  - **What is sent:** the typed email exactly, and the typed reason exactly, omitted when the field is empty. The client never copies the server's address rules.
  - **For a user whose activation is pending,** the dialog also says: *"Their current activation link will stop working. Use Resend activation to send a new one to the new address."* This is guidance only; the command issues nothing.
- **Saving:**
  - The button is busy while the request is in flight, and a repeated press sends once.
  - On `204` the dialog closes, the page announces *"Email changed for {display name}."*, and the client refreshes the list and that user's GetUser query.
  - **A refusal** keeps the dialog open with the typed values, and shows the server's sentence word for word.
- **The unsaved-changes guard,** as in Edit profile: once either field is typed into, Cancel, Escape and the close button ask *"Discard changes?"*.
- **Focus** returns to the Actions button when the dialog closes.

### Acceptance Criteria

**The command**

- **CE-A1** An administrator changes an active user's email. The stored address is the new one, trimmed. One `UserEmailChanged` is written, with the administrator as actor, `Before.Email` and `After.Email`, and the Reason when one is given. A blank reason records none.
- **CE-A2** **No change.** For the same address, and for the same address in a different case, the answer is accepted and there is no write, no audit record and no token change.
- **CE-A3** **Uniqueness:**
  - an address held by another **active** human, in any case, is refused with *"A user with this email address already exists."*, and nothing is written;
  - an address held only by an **inactive** human is accepted;
  - an **inactive** target is refused an address an active human holds.
- **CE-A4** An invalid address is refused with *"Email address has an invalid format."*, and nothing is written.
- **CE-A5** An unknown user, the System actor and an agent are refused. A caller without `user.update` is refused by the pipeline.
- **CE-A6** **Tokens:**
  - every outstanding activation and reset token of the user is invalidated at the command's instant;
  - a used token is untouched;
  - no `TokenInvalidated` record is written;
  - **no new token and no notification** are created;
  - **the old activation link no longer activates the account** (CRD-C1 refuses it).
- **CE-A7** **The lock:** while another transaction holds the target's row lock, the command waits, and it completes once the lock is released.
- **CE-A8** **Nothing else changes:** names, status, identities, credentials, sessions and role assignments.
- **CE-A9** **An inactive user's email can be changed** (CE8), and it is the remedy: once a colliding inactive user's address is changed, USR-C5 reactivates them.
- **CE-A10** **No target rule** (CE2): an administrator's change to their own record is accepted like any other, and the record names them as actor and subject.

**Over HTTP**

- **CE-A11** `POST /api/users/{userId}/email` answers `204` on a change and on no change. A missing `email` is `400`. No carrier is `401`. A refusal is `400 { "error": … }` with the sentences above.

**The UI**

- **CE-U1** Change email is offered for `user.update` on active and inactive rows, never without it, in the list and on User detail.
- **CE-U2** **The request:** Save sends exactly `{ email }`, or `{ email, reason }` when a reason was typed, untrimmed, to `POST /api/users/{userId}/email`. An empty New email sends nothing and is flagged.
- **CE-U3** **On `204`:** the dialog announces, refreshes the list and GetUser, and closes. It is busy while sending, and sends once.
- **CE-U4** **A refusal** is shown word for word, and the dialog stays open with the typed values.
- **CE-U5** **The activation guidance** appears exactly when the user's activation is pending.
- **CE-U6** **The guard:** once dirty, Cancel, Escape and the close button ask "Discard changes?". When the form is clean, they close it.
- **CE-U7** No accessibility violations with the dialog open.

**Browser, in the dev stack (CE-U8),** each state change approved by the owner:

1. As Ada, change V R's email. The list shows the new address, and one `UserEmailChanged` record is written.
2. Try an address another active user holds. The refusal is shown word for word.
3. Change V R back.

### Implementation notes

- **The handler** is `ChangeUserEmailCommandHandler`, in the contract's order of work.
  - **Refusals:** the address is parsed with `EmailAddress.Create` before the transaction opens, so an invalid address never takes the lock. Its refusal is the domain's, `400` like every other.
  - **The no-op** is `EmailAddress` equality, which ignores case. It returns before the uniqueness check and before the tokens.
  - **The record** is declared only after the change and the invalidation, so a refusal or a no-op declares nothing.
- **Uniqueness:** `IUserRepository.ExistsOtherActiveHumanWithEmailAsync` is a new method with the existing pre-check's predicate, the index's own terms, plus `id <> target`. The existing method is unchanged, and so are its callers, USR-C1 and USR-C5.
- **Tokens:** `InvalidateOutstandingForUserAsync`, the USR-C4 method, stamped with the clock read after the lock.
- **Audit:** `ChangeUserEmailCommand` is registered in `AuditDeclarations` with `UserEmailChanged` only, and the declaration test's list of every declared code gains it.
- **The route** sits in `UserEndpoints`. A missing `email` is refused before dispatch, *"email is required."*, as the other routes treat missing inputs.
- **The page:**
  - **Wiring:** `ChangeEmailDialog` is built as Edit profile is. The action is `change-email` in the shared matrix, with its own `changeEmail` permission flag (`user.update`), after Edit profile.
  - **The address field is text with `inputMode="email"`, not `type="email"`.** *Found during implementation:* the browser sanitises an email input's value, stripping surrounding whitespace, so what was sent differed from what was typed. That breaks CE12's "sent exactly as typed" and hides the server's own trimming.
  - **The reason** is omitted from the request when the field is empty.
  - **The current address** comes from the row or GetUser the dialog was opened from, and is not re-read.

### Not included

- **The self-service command and its verification** (A3's self half), and an `EmailVerification` notification type.
- **Notices** to the old or new address.
- **Any change to CRD-C7,** including its race with this command (Known Gap).
- **Sign-in:** it is by username, and is unchanged.

---

## Role administration read: AUT-Q5, AUT-Q3 and AUT-Q6, and the Roles screen

**Status:** Contract frozen 2026-09-20 by owner decision (RA1–RA10). **Read-only.** No role or permission mutation, no member management, no catalogue change, and no PRV-C2 change.

### Requirement

A `role.read` holder can inspect role definitions: which roles exist, what each one currently carries, how many people hold each, and what the permission catalogue contains. This is the authoritative read model the role-definition mutations (AUT-C3–C8) will later act on.

```text
AUT-Q5 ListRoles            the administration list, with derived counts
AUT-Q3 GetRolePermissions   what one role carries, now or at an instant
AUT-Q6 ListPermissions      the release-owned catalogue, read-only
```

### The decisions

| # | Decision |
| --- | --- |
| **RA1** | **`includeInactive` changes the result set only.** `false` (the default) returns active roles; `true` also returns inactive ones. It does not change `permissionCount`, `activeHolderCount` or `agentAssignable`, which stay independently defined. |
| **RA2** | **`activeHolderCount` is assignment state, never user status.** A holder counts when their `user_role` assignment is **Active** at the query instant by the existing `RoleAssignmentStates.At` derivation: not revoked, `EffectiveFrom <= now`, and `now < EffectiveTo` when there is one. Future, Ended and Revoked do not count. **No `app_user.Status` condition is added:** that would be a second derivation of assignment state, able to diverge from authorisation. The deactivation cascade already revokes assignments. |
| **RA3** | **`permissionCount` counts live grants only:** `role_permission` rows with `RevokedAt IS NULL`. **It does not filter on `permission.IsActive`** — the question is what the role is currently granted, not which catalogue entries are current. A grant of a retired permission still counts. |
| **RA4** | **`agentAssignable` is the frozen derivation, exactly:** true when the role has **no live grant to a permission with `RequiresHumanActor = true`**. Never stored, no agent lookup, and no dependence on whether agents exist. `agentAssignableOnly=true` filters on that derived value. A role with no live grants is agent-assignable (vacuously). This is the same derivation AUT-C7/C8 must preserve. |
| **RA5** | **AUT-Q3 takes `asOf`**, defaulting to the current instant; the screen asks only for the current state in this slice. A grant is **live at `asOf`** when `GrantedAt <= asOf` and (`RevokedAt IS NULL` or `RevokedAt > asOf`). |
| **RA6** | **`GET /api/roles` is kept, unchanged.** It is the assignment workflow's narrow grantable-role reader, with one consumer, and its own comment says it is not AUT-Q5. The administration list is a separate route. |
| **RA7** | **The screen is a roles list and a role detail.** The list shows name, code, active or inactive, agent-assignable, permission count and holder count. The detail shows the role's metadata and its permissions through AUT-Q3. **AUT-Q4 GetRoleMembers stays out**, with AUT-C5/C6, where the catalogue needs it for the stranding warning. |
| **RA8** | **A Roles module owns `role.read`.** The users module carries that code today only because no roles module existed; it now imports it from the roles module's public surface, as one module uses another's. The users module keeps the user-management codes. |
| **RA9** | **`role.manage` is not used anywhere in this gate.** The three reads are `role.read`. `role.manage` stays reserved for AUT-C3–C8: `role.read` inspects a definition, `role.manage` changes one. |
| **RA10** | **PRV-C2 is parked.** The catalogue synchroniser refuses any role absent from the release seed (`RoleMissingFromSeed`), with no exemption for tenant roles. That is created by **AUT-C3**, not by this slice, and is resolved before AUT-C3 is implemented — not here. |
| **RA11** | **An unknown role is `404`, not an empty list** (owner's change). An empty list means *this role exists and currently has no matching grants*; a role that does not exist is a different condition, and the reader must be able to tell them apart. The sentence follows the existing wording: *"The role does not exist."*, as *"The user does not exist."* elsewhere. `asOf` projects the grants only once the role is known to exist. |

### The routes and their shapes

**My choices, not the catalogue's** — the query catalogue defines queries, not HTTP. They follow the existing routes' conventions.

| Query | Route |
| --- | --- |
| **AUT-Q5** | `GET /api/roles/administration?includeInactive=&agentAssignableOnly=` |
| **AUT-Q3** | `GET /api/roles/{roleId:guid}/permissions?asOf=` |
| **AUT-Q6** | `GET /api/permissions?resource=&requiresHumanActor=` |

- **`/api/roles/administration` rather than widening `/api/roles`** (RA6). The `{roleId:guid}` constraint keeps the two `/api/roles/...` routes apart.
- **Parameters are parsed strictly**, as `includeInactive` already is on the assignments route: a boolean must be `true` or `false`, supplied once, or the answer is `400`. `asOf` must be one ISO-8601 instant, or `400`. `resource` is matched exactly.
- **All three require a carrier and `role.read`**; without a carrier `401`, without the permission `400`, as every other read. **None is audited.**

**AUT-Q5** answers `{ "roles": [ … ] }`, each role exactly:

```text
roleId, code, name, description, isSystemRole, isActive,
agentAssignable, permissionCount, activeHolderCount
```

- **`description` and `isSystemRole` are included because there is no GetRole query in the catalogue.** The detail view is composed from this row plus AUT-Q3.
- **Ordered by `name`** under the ICU `unicode` collation, then `roleId`, as the grantable list and the user list are ordered, so the order does not depend on the database's default collation.
- **No paging.** The catalogue defines none for ListRoles, and roles are few by design.
- **One instant** is read for the whole query, and every holder state is judged against it.

**AUT-Q3** answers `{ "permissions": [ … ] }`, each grant exactly:

```text
rolePermissionId, permissionId, code, name, resource, action,
requiresHumanActor, grantedAt, revokedAt
```

- The rows are the grants **live at `asOf`** (RA5). `revokedAt` is therefore null for a current read, and may carry a value for a historical one — which is the "granted/revoked info" the catalogue asks for.
- **Ordered by `code`.** A role that exists with no live grants answers `200` and an empty list; **an unknown role answers `404` with *"The role does not exist."*** (RA11).
- **How the `404` is produced.** `ProblemMiddleware` maps exceptions to `400`, `401`, `429` and `500` only, and this story does not widen that allowlist. The query's result distinguishes "no such role" from "no grants", and the route maps that to `404 { "error": … }`. **This is the host's first `404` with a body.**
- **A recorded divergence:** AUT-Q2, the sibling read, answers `400` *"The user does not exist."* for an unknown user. Aligning the two is not this gate's work; see the Known Gap.

**AUT-Q6** answers `{ "permissions": [ … ] }`, each permission exactly:

```text
permissionId, code, name, resource, action, requiresHumanActor, isActive
```

- **Ordered by `code`.** `isActive` is included so a retired catalogue entry is visibly retired; the catalogue is release-owned (PE2) and this route never writes.
- **No screen consumes AUT-Q6 in this slice.** It is the surface AUT-C7 will pick permissions from. *Open point for the owner:* leave it API-only, or add a Permissions page under the Roles area now.

### The screen (RA7, RA8)

- **A new `modules/platform/roles`**, owning `role.read` and contributing one navigation item, **Roles** → `/admin/roles`, to the Administration area beside Users. The users module imports the code from it for its Manage roles action.
- **`/admin/roles` — the list.** Columns: Name, Code, Status, Agent-assignable, Permissions, Holders. A **Show inactive roles** control drives `includeInactive`. **`agentAssignableOnly` is served by the query but not surfaced** in this slice; the column shows the value.
- **`/admin/roles/{roleId}` — the detail.** The role's metadata, then its permissions from AUT-Q3: code, name, resource, action, human-only, granted. Composed from the list row and AUT-Q3, since no GetRole exists; a role id that is not in the list shows a not-found state, never an empty page.
- **Nothing on either page mutates anything**, and neither offers an action.

### Acceptance Criteria

**AUT-Q5 (RA1–RA4)**

- **RA-A1** Each role is answered with exactly the nine members above, with its stored values, ordered by name.
- **RA-A2** **`includeInactive`:** absent or `false` returns active roles only; `true` also returns inactive ones. **The counts and `agentAssignable` of a role are identical under both.**
- **RA-A3** **`activeHolderCount`:** a role held by an Active assignment counts 1; Future, Ended and Revoked assignments count 0; two assignments for one user count 1. A holder whose user is inactive but whose assignment is somehow still Active **is counted** — the count is the assignment's state, not the user's.
- **RA-A4** **`permissionCount`:** live grants only. A revoked grant does not count; a live grant of a permission whose `IsActive` is false **does** count.
- **RA-A5** **`agentAssignable`:** false when any live grant requires a human actor; true when none does; true for a role with no live grants; unaffected by revoked grants of human-only permissions. **`agentAssignableOnly=true`** returns exactly the roles whose derived value is true, and combines with `includeInactive`.

**AUT-Q3 (RA5)**

- **RA-A6** Without `asOf`, the current live grants are returned, each with exactly the nine members above, ordered by code.
- **RA-A7** With `asOf`, the grants live at that instant are returned: one revoked after `asOf` appears with its `revokedAt`; one granted after `asOf` does not appear; one revoked before `asOf` does not appear.
- **RA-A8** **A role with no live grants answers `200` and an empty list; an unknown role answers `404`** with *"The role does not exist."* (RA11). The `404` carries that body, which is what distinguishes it from an unmapped route.

**AUT-Q6**

- **RA-A9** Every permission is answered with exactly the seven members above, ordered by code, including inactive ones.
- **RA-A10** `resource` and `requiresHumanActor` filter the list, together and separately.

**All three**

- **RA-A11** Each requires `role.read`: an access reviewer may read them, a caller without the permission is refused, and no carrier is `401`. **No audit record is written by any of them.**
- **RA-A12** Malformed parameters are `400`: a boolean that is not `true`/`false` or supplied twice, and an `asOf` that is not an instant.

**The screen (RA7, RA8)**

- **RA-U1** Roles appears in the Administration navigation for a `role.read` holder, and never without it.
- **RA-U2** The list shows a row per role with its six columns, an Inactive marker, and Agent-assignable as a plain yes or no. Show inactive roles re-reads with `includeInactive=true`.
- **RA-U3** A failed read shows the server's sentence and a Try again that reads again; loading shows no table.
- **RA-U4** The detail shows the role's metadata and its permissions, and a human-only permission is marked as such.
- **RA-U5** A role id that is not in the list shows a not-found state.
- **RA-U6** Neither page offers any action, and neither sends anything but its reads.
- **RA-U7** No accessibility violations on either page.

**Browser, in the dev stack (RA-U8),** with the owner's approval. As Ada:

1. Open Roles: three roles, each with its counts. `access-reviewer` is agent-assignable; the other two are not.
2. Open `user-administrator`: its permissions are listed, human-only ones marked.
3. Turn on Show inactive roles: the list is unchanged, because no role is inactive.

### Implementation notes

- **Three readers, one statement each.** `RoleAdministrationReader`, `RolePermissionReader` and `PermissionCatalogueReader` sit beside the existing readers and are registered with them.
  - **AUT-Q5** computes the three derived values as correlated sub-queries on the role row, then applies `includeInactive` and `agentAssignableOnly` to the result — so neither parameter can reach a count (RA1).
  - **`activeHolderCount`** is `DISTINCT user_id` over assignments that are not revoked and whose half-open period contains the handler's instant. There is deliberately **no join to `app_user`** (RA2); a mutant that added one is killed by the inactive-holder test.
  - **`agentAssignable`** is `NOT EXISTS(live grant of a human-only permission)`, so a role with nothing granted is agent-assignable.
- **Collations are explicit and different on purpose.** Role names order under ICU `unicode`, as the user list and grantable list do. **Permission codes order under `C`** — byte order — because a code is an identifier, not prose, and byte order is the same on every server.
- **AUT-Q3 looks the role up first** and returns null when it is absent; the route maps that to `404` with its sentence. `ProblemMiddleware` is untouched (RA11).
- **The routes** live in `RoleEndpoints`, beside `RoleAssignmentEndpoints`, which is unchanged (RA6). Booleans are parsed exactly as the assignments route parses `includeInactive`, and `asOf` must be one ISO-8601 instant.
- **The web module** owns `role.read`; the users module imports it for its Manage roles action, and its own `readRoles` code is gone.
  - **One rename during implementation:** a role's grants are `grants` in the client, because the lint rule reserves `.permissions` for a caller's *effective* permissions (frontend-architecture §9). The server's field name is unchanged; the page destructures it.
  - **The detail page** reads AUT-Q5 with inactive roles included, so an inactive role's page is not a dead end, and AUT-Q3 in its current-state form only (RA5).
- **Found while writing the tests:** the seeded catalogue stores `Resource` and `Action` capitalised (`User`, `Create`), not lowercase. The `resource` filter matches exactly, so it is `?resource=User`. Nothing was changed to accommodate the tests.

### Not included

- **Any mutation:** AUT-C3–C8, and any change to the permission catalogue (PE2: release-owned).
- **AUT-Q4 GetRoleMembers**, which belongs with AUT-C5/C6.
- **Any change to `/api/roles`** (RA6), to PRV-C2 (RA10), or any use of `role.manage` (RA9).
- **A Permissions screen** (see AUT-Q6 above).

---

## AUT-C3 CreateRole — the tenant role, and its dialog

**Status:** Contract frozen 2026-09-20 by owner decision (RC1–RC9). Deploy-safe because of PRV-C2 Amendment 1 (#79), which is its prerequisite: without it the first tenant role would fail the next deployment.

### Requirement

An administrator holding `role.manage` creates a role the tenant owns: a name, a code and an optional description. The role is created **active, with no permissions and no holders**, and it is immediately grantable through AUT-C1.

```text
CreateRole
   role.manage + human actor
   -> validate code, name, description
   -> refuse a code that collides, exactly or by case alone
   -> create the role, IsSystemRole = false
   -> RoleCreated
   -> 201
```

### The decisions

| # | Decision |
| --- | --- |
| **RC1** | **The code is refused, never normalised.** Non-blank; **no leading or trailing whitespace**, by the same `char.IsWhiteSpace` boundary rule usernames use; otherwise stored exactly as supplied. Uniqueness stays the database's **case-sensitive** index, and in addition **a code differing from an existing role's code only by case is refused** — checked case-insensitively before the insert, with the index as the exact-match backstop. **No lower-casing**, and **no general code grammar**: there is no evidence one is needed. |
| **RC2** | **Name and description use the USR-C2 family**, so this story does not start a third validation dialect. Both are **trimmed and stored trimmed**, must contain **no control characters**, and are at most **100 Unicode code points**. **The name must be non-blank; the description may be empty or absent.** The normalised values are what is stored and what is audited. |
| **RC3** | **No reserved-code mechanism.** A tenant code that a future release claims is detected at deployment by PRV-C2 (`SecuritySemanticDrift`) and remediated then. Inventing a prefix or namespace now would add a lifecycle mechanism to prevent a problem that is already detected safely. |
| **RC4** | **`POST /api/roles` → `201`**, under `role.manage`, human actors only. It does not disturb `GET /api/roles`, the grant form's list. The body is exactly `{ roleId, code, name, description, isSystemRole, isActive }` — the created role as stored, narrow: no permissions, no members, no counts. |
| **RC5** | **One refusal for every collision:** *"A role with this code already exists."* — the same sentence whether the clash was exact or case-only, so the API never exposes the database's case semantics. The unique index is mapped to that sentence too, so a race past the pre-check reads the same. |
| **RC6** | **`RoleCreated` with `After` only**, carrying `Code`, `Name` and `Description`. **`IsSystemRole` is not recorded**: it is a fixed invariant of this command, not change information. No audit catalogue change, and `AuditEventCatalogue.Version` is not bumped. |
| **RC7** | **The dialog ships with the command.** The Roles administration screen exists, and a backend-only command would leave it unable to do the thing being added. It follows the existing dialog conventions — Name, Code, Description, presence checks, the server's refusal shown word for word, and the list refreshed on success. **No permission assignment in this dialog.** |
| **RC8** | **Out of scope:** AUT-C4 metadata, AUT-C5/C6 lifecycle, AUT-Q4 members, AUT-C7/C8 permissions, deletion of any kind, and the system-role question that belongs to AUT-C7's gate. |
| **RC9** | **This is the only application write path for roles, and it cannot create a release-owned one.** The command takes no `IsSystemRole`; the domain sets it false. `IRoleRepository.AddAsync` is infrastructure, not an authorization bypass: the application and domain boundary decides ownership, as PRV-C2 Amendment 1 assumes. |

### The rules, and what each refusal says

| Input | Rule | Refusal |
| --- | --- | --- |
| **Code** | required, non-blank | *"A role code is required."* |
| | no surrounding whitespace (`char.IsWhiteSpace`), never trimmed | *"A role code cannot begin or end with whitespace."* |
| | unique, and unique ignoring case | *"A role with this code already exists."* |
| **Name** | trimmed, then required | *"A role name is required."* |
| | no control characters | *"A role name must not contain control characters."* |
| | at most 100 code points | *"A role name must be at most 100 characters."* |
| **Description** | trimmed; empty or absent is allowed | — |
| | no control characters | *"A role description must not contain control characters."* |
| | at most 100 code points | *"A role description must be at most 100 characters."* |

**The domain's two existing blank messages are restated into this family.** `Role.Create` says *"Role name cannot be empty."* and *"Role code cannot be empty."* today; no test or caller depends on either wording, and leaving them would put two dialects in one entity.

### Order of work in the handler

1. **Validate** the code, name and description — in the domain, before any lookup, as USR-C3 validates before its uniqueness lookup.
2. **Refuse a colliding code**, compared case-insensitively. The index remains the guarantee, and is mapped to the same sentence.
3. **Create** the role: `IsSystemRole = false`, `IsActive = true`, `CreatedBy` the administrator (RC9).
4. **Declare `RoleCreated`** with `After` only.
5. **Answer `201`** with the created role.

### Change control

**No workbook is edited.** One item is outstanding change control:

- **RO2** says a code is immutable *once the role is referenced*. The database is already stricter — `role.code` is immutable from creation, by the update guard — and this story does not relax that. Nothing here makes a code editable, and AUT-C4 does not change codes either.

### The dialog (RC7)

- **A New role action on the Roles page**, for `role.manage` holders only, and never shown without it. The page stays read-only for everyone else.
- **The dialog, titled *"New role"*:** Name, Code and Description (optional).
  - **Presence only** on Name and Code — the server owns every other rule, and its refusal is shown word for word.
  - **What is sent** is exactly what was typed, untrimmed, as the Create user form sends a username.
  - **The unsaved-changes guard**, as Edit profile and Change email use it.
- **On `201`:** the dialog closes, the page announces *"Role created: {name}."*, and the roles list is re-read. The new row shows **Active**, **0 permissions**, **0 holders**, and **Agent-assignable: Yes** — it has no human-only grant, because it has no grants at all.
- **Focus** returns to the New role button.

### Acceptance Criteria

**The command**

- **RC-A1** A role is created with the normalised name and description and the code exactly as supplied; `IsSystemRole` is false, `IsActive` is true, and `CreatedBy` is the administrator. The answer carries exactly the six members of RC4.
- **RC-A2** **One `RoleCreated`** is written, with the administrator as actor, the role as primary entity, `After` carrying exactly `Code`, `Name` and `Description`, and **no `IsSystemRole`**. No other record is written.
- **RC-A3** **The code:** a blank code, and one with leading or trailing whitespace — each of the 25 whitespace characters, at either end — are refused with their sentences, and nothing is written. An inner space is accepted.
- **RC-A4** **Name and description:** a blank name, a control character in either, and 101 code points in either are refused with their sentences. A trimmed name and description are stored trimmed, and a description that is absent, empty or whitespace-only is stored as null.
- **RC-A5** **Collision:** an existing code, and the same code in a different case, are both refused with *"A role with this code already exists."*, and nothing is written. The collision is refused for a **system** role's code and for a **tenant** role's code alike.
- **RC-A6** **The index is the guarantee:** inserting the same code concurrently — the pre-check passed — surfaces the same sentence, not a `500`.
- **RC-A7** **Authorisation:** a caller without `role.manage` is refused by the pipeline, and so is a non-human caller. `role.read` alone does not create a role.
- **RC-A8** **The created role is usable:** it appears in AUT-Q5 with `permissionCount` 0, `activeHolderCount` 0 and `agentAssignable` true, in `GET /api/roles`, and AUT-C1 can grant it.
- **RC-A9** **Nothing else changes:** no permission, no grant, no assignment, and no other role.

**Over HTTP**

- **RC-A10** `POST /api/roles` answers `201` with the six members. A missing `code` or `name` is `400`. A refusal is `400 { "error": … }` with the sentences above. No carrier is `401`.

**The dialog**

- **RC-U1** New role is offered on the Roles page for `role.manage`, never without it.
- **RC-U2** Save sends exactly `{ code, name, description }` as typed, untrimmed, to `POST /api/roles`. An empty Name or Code sends nothing and is flagged.
- **RC-U3** On `201` the dialog announces, re-reads the list, and closes; the new row shows Active, 0 permissions, 0 holders and Agent-assignable Yes. It is busy while sending, and sends once.
- **RC-U4** A refusal is shown word for word and the dialog stays open with the typed values.
- **RC-U5** The guard asks before discarding a dirty form, and a clean form closes without asking.
- **RC-U6** No accessibility violations with the dialog open.

**Browser, in the dev stack (RC-U7),** with the owner's approval, and **each step is permanent**: a created role cannot be deleted or deactivated until AUT-C5 exists, and its audit record is permanent.

1. As Ada, create a role — code `dev-smoke-reviewer`, name `Dev Smoke Reviewer`. It appears Active, 0 permissions, 0 holders, agent-assignable.
2. Try the same code again: *"A role with this code already exists."*
3. Try `DEV-SMOKE-REVIEWER`: the same sentence.

### Implementation notes

- **RC2's name and description rules govern TENANT roles only.** `Role.Create` applies them when `isSystemRole` is false and leaves a seed's text alone. This was forced, not chosen: two seeded descriptions are 102 and 158 characters, and PRV-C2 **refuses** role metadata drift rather than reconciling it, so shortening a seed would refuse deployment on every existing database. RC2 governs what a tenant may write; a seed's text is the release's, as the permission catalogue is (PE2). Two mutants attack the gate from both sides — applied to seeds as well, and inverted.
- **A code is refused, never normalised** (RC1). `ValidateCode` is `Role.Create`'s first statement, so by the time the code reaches the constructor it already equals its own `Trim()`. A mutant that trims it there therefore survives, provably equivalently, and is kept.
- **The collision pre-check and the index say the same sentence.** `ExistsWithCodeAsync` is raw SQL comparing `lower("code")` database-side, mirroring `IX_role_code`; the index is the guarantee, the pre-check is the courtesy. `PostgresExceptionTranslator` now maps `IX_role_code`, so a caller who loses a race reads *"A role with this code already exists."* rather than a 500 — the same words as the pre-check (RC5).
  - **One existing test was repointed:** `An_unknown_unique_violation_survives_untranslated` used `IX_role_code` as its example of an unmapped constraint. It now uses `ux_role_permission_active` (RP2), which the translator still deliberately leaves unmapped.
- **The route answers with what was STORED, not what was sent** (RC4): the result carries the domain's values, so a trimmed name and a null description come back as they will be read. The dialog announces the same stored name; both are pinned by mutants.
- **`AuditDeclarations` needed the new code.** `RoleCreated` is declared on the command and registered in the catalogue; IMPL-08 verifies the two agree at start-up, and `AuditDeclarationsTests.EveryDeclaredCode` lists it.
- **The web form sends the typed values untrimmed** (RC-U2) and lets the server own every rule, as the create-user form does for usernames. Zod checks presence only.
- **New role sits in the page header's `actions` slot**, inline with the heading, as Users and User detail do — it was briefly below the heading, which was the only page out of line with the shared `Page` component.

### Not included

- **AUT-C4–C8**, AUT-Q4, and any deletion or deactivation.
- **Any permission assignment**, in the command or the dialog.
- **Any reserved-code mechanism** (RC3), and any general code grammar (RC1).
- **Any change to `GET /api/roles`**, to PRV-C2, or to the system-role rules.

---

## AUT-C4 UpdateRoleMetadata — renaming a tenant role

**Status:** Contract frozen 2026-09-20 by owner decision (RM1–RM10). It follows AUT-C3 (#80), which created the tenant role this command edits.

### Requirement

An administrator holding `role.manage` changes a **tenant** role's name and description. Nothing else: not the code, not ownership, not activity, not permissions, not holders.

```text
UpdateRoleMetadata
   role.manage + human actor
   -> load the role
   -> refuse a system role
   -> normalise and validate name and description
   -> no change after normalisation: write nothing, record nothing, answer 200
   -> otherwise update, RoleUpdated with Before and After
   -> 200 with the stored role
```

**Renaming is safe, and the product is already built for it.** The command catalogue's note says so — *"ActorSnapshot captures both RoleId and RoleName at action time"* — and the code already honours it: `AuthorizationService` and `AuthorizingAssignment` both carry the authorising role's **name** beside its id, so renaming a role never rewrites the authority recorded against acts already performed.

### The decisions

| # | Decision |
| --- | --- |
| **RM1** | **A role name need not be unique.** No frozen requirement, database constraint, authorization lookup or existing behaviour establishes name uniqueness; the workbook leaves `Name`'s Key column empty while marking `Code` `UNIQUE`. **The code is the identifier; the name is metadata.** Two tenant roles may share a name. **No new uniqueness constraint and no pre-check** — this story does not invent an invariant for AUT-C4. |
| **RM2** | **The code is not an input.** The frozen command's inputs are `RoleId, Name, Description`, and its precondition says *"Code is NOT updatable here."* AUT-C4 therefore does not expose a code field at all, so **there is no legitimate AUT-C4 request containing a code to refuse**. The catalogue's *"attempting to change Code must be refused, not silently ignored"* binds the domain and database boundary, which already refuses it (the G4 update guard, immutable from creation); it does not require manufacturing a client input the command contract excludes. **The existing PostgreSQL immutability mechanism is not touched by this story.** |
| **RM3** | **A no-op is a domain rule, not a UI optimisation.** `Role.UpdateMetadata` becomes change-aware and returns `bool changed`, as `User.UpdateProfile` does. After normalisation, the same name and the same description mean **no database write, no audit record, and a successful answer** — the G5 semantics USR-C2 already established. |
| **RM4** | **A system role is refused with the domain's existing words.** `"System roles cannot be modified."`, unchanged, answered as **`400`**. The UI **does not offer Edit** on a release-owned role: an action the caller cannot legitimately perform is not presented. The server stays authoritative regardless of what the UI shows. |
| **RM5** | **No row lock, last write wins.** USR-C2 deliberately takes no D6 lock for this class of mutation: the command depends on no lifecycle state, authorization state, token state or other mutable resource. RM1 introduces no uniqueness, so there is no concurrency reason to differ. |
| **RM6** | **`POST /api/roles/{roleId}/metadata` → `200`** with the **stored representation**, exactly `{ roleId, code, name, description, isSystemRole, isActive }`. The verb and shape follow the command-style convention every user mutation already uses (`POST /api/users/{userId}/profile`); no `PUT`/`PATCH` exists in this host. Returning the stored values — not the submitted ones — resolves the inconsistency between AUT-C3, which announces the server's stored name, and USR-C2, which answers `204` and re-derives from the typed value. The client is thereby independent of client-side normalisation. `code` comes back unchanged and `isSystemRole` reports the role as it is; neither is editable. |
| **RM7** | **Edit lives on the Role detail page**, in the `actions` slot the shared `Page` already provides. The list is the administration index; editing metadata is a detail-level operation. It is gated on `role.manage` and offered **only for tenant roles**. **RA-U6 is rewritten, not retired:** its present wording ("offers no action on either page") is too broad now, and it becomes a test of the **read-only caller** — a holder of `role.read` without `role.manage` sees no role-management action — with the complementary `role.manage` cases added. |
| **RM8** | **No reason.** The catalogue gives AUT-C4 no `Reason` input, and `RoleUpdated` is seeded `ReasonRequired: false`. AUT-C5 and AUT-C8 have reasons because their contracts require them; being an administrative mutation is not itself grounds for one. |
| **RM9** | **No `GET /api/roles/{id}`.** The detail page keeps reading AUT-Q5, as it does today. Adding an individual-role read would turn a metadata mutation into a second read-contract story with no evidence the product needs one; if a later requirement shows it is independently useful, it gets its own query contract. |
| **RM10** | **`IsSystemRole` is never accepted from the client and cannot be changed here.** The command has no such input and the domain refuses a system role outright. The evidence for this boundary is unusually strong and mutually reinforcing: the domain's refusal, the database's immutability guard, and PRV-C2 Amendment 1's ownership rule all rely on it. |

### The rules, and what each refusal says

The name and description rules are **AUT-C3's, unchanged** (RC2) — this story starts no new dialect, and the values that are stored are the values that are audited.

| Input | Rule | Refusal |
| --- | --- | --- |
| **RoleId** | names an existing role | *"The role does not exist."* |
| | names a **tenant** role | *"System roles cannot be modified."* |
| **Name** | trimmed, then required | *"A role name is required."* |
| | no control characters | *"A role name must not contain control characters."* |
| | at most 100 code points | *"A role name must be at most 100 characters."* |
| | **need not be unique** (RM1) | — |
| **Description** | trimmed; empty or absent is allowed, and stored as null | — |
| | no control characters | *"A role description must not contain control characters."* |
| | at most 100 code points | *"A role description must be at most 100 characters."* |
| **Code** | **not an input** (RM2) | — |

**An unknown role is `400`, not `404`,** matching USR-C2's *"The user does not exist."* for the same shape of command. The reads answer `404` for an unknown role; that divergence is already recorded as a Known Gap and this story neither widens nor resolves it.

### Order of work in the handler

1. **Load** the role — tracked, because this one changes it. No row lock (RM5).
2. **Refuse an unknown role**, then **refuse a system role** (RM4), before any validation: ownership is the coarser gate.
3. **Capture `Before`** — the stored name and description.
4. **Normalise and validate** in the domain, which reports whether anything changed (RM3).
5. **Nothing changed:** return the stored role. **No write, no record.**
6. **Otherwise** declare **`RoleUpdated`** with `Before` and `After`, each carrying exactly `Name` and `Description`.
7. **Answer `200`** with the stored role.

**`RoleUpdated` already exists** in the audit catalogue, seeded and active, with `PrimaryEntityType: "Role"`, `"BeforeAfter"` and `ReasonRequired: false`. This story adds the `AuditDeclarations` entry and the hand-maintained declared-code list; **`AuditEventCatalogue.Version` is not bumped.**

### Change control

**No workbook is edited.** Four items are outstanding change control:

- **The `Command steps` sheet has no AUT-C4 sequence**, unlike AUT-C1 and AUT-C7. The order above is this contract's, derived from USR-C2's established shape.
- **`RO1`–`RO4` are cited by the entity workbook but defined nowhere available.** The workbook points to "Part E of the frozen document"; the frozen specification in that folder has no Part E. Every RO rule this project has relied on comes from the workbook's one-line glosses. Recorded as a Known Gap.
- **The specification's event list omits every role-definition event.** `RoleCreated`, `RoleUpdated`, `RoleDeactivated` and `RoleReactivated` exist only in the command catalogue. AUT-C3 already shipped against that asymmetry. Recorded as a Known Gap.
- **The specification says tenants cannot "modify or deactivate" system roles**, which is broader and vaguer than AUT-C4's precondition *"Role is not a system role."* The implemented rule is the stricter reading and satisfies both.

### The Edit dialog (RM7)

- **An Edit action on the Role detail page**, in the page header, for `role.manage` holders only, and **only on a tenant role**. A release-owned role's page offers nothing, for anyone.
- **The dialog, titled *"Edit role"*:** Name and Description. **The code is shown, read-only, as context** — it is not a field, and the page already tells the reader it cannot be changed.
- **The form opens on the role's stored values**, so "dirty" means "differs from what the server returned", as Edit profile establishes.
- **Presence only** on Name — the server owns every other rule, and its refusal is shown word for word.
- **What is sent** is exactly what was typed, untrimmed, as the New role form sends.
- **The unsaved-changes guard**, on every close path.
- **On `200`:** the dialog closes, the page announces *"Role updated: {name}."* using the **server's stored name** (RM6), and the role is re-read. A save that changed nothing is still a save to the client, which does not predict a no-op.
- **Focus** returns to the Edit button.

### Acceptance Criteria

**The command**

- **RM-A1** A tenant role's name and description are changed to the normalised values; `code`, `isSystemRole`, `isActive`, `createdAt` and `createdBy` are untouched. The answer carries exactly the six members of RM6, read from what was stored.
- **RM-A2** **One `RoleUpdated`** is written, with the administrator as actor, the role as primary entity, and `Before`/`After` each carrying exactly `Name` and `Description`. No other record is written, and `AuditEventCatalogue.Version` is unchanged.
- **RM-A3** **A no-op writes and records nothing** (RM3): resubmitting the stored values, and submitting values that normalise to them (padded, or a description that trims to empty when it is already null), answer `200` with the stored role while leaving `updated_at`, `updated_by` and the audit sequence untouched.
- **RM-A4** **Name and description rules** are AUT-C3's exactly: a blank name, a control character in either, and 101 code points in either are refused with their sentences and nothing is written; values are stored trimmed; a description that is absent, empty or whitespace-only is stored as null.
- **RM-A5** **A system role is refused** with *"System roles cannot be modified."*, and nothing is written — including when the submitted values are identical to what is stored, so the refusal precedes the no-op check.
- **RM-A6** **An unknown role is refused** with *"The role does not exist."*, and nothing is written.
- **RM-A7** **Duplicate names are allowed** (RM1): a tenant role may be renamed to a name another role already holds — a tenant role's **and** a system role's — and both roles keep their own code.
- **RM-A8** **Authorisation:** a caller without `role.manage` is refused by the pipeline, and so is a non-human caller. `role.read` alone cannot edit a role.
- **RM-A9** **The code cannot be changed** (RM2, RM10): the command has no code input, and the stored code after any accepted edit is the code before it. Equally, `IsSystemRole` is unchanged and unchangeable.
- **RM-A10** **Nothing else changes:** no permission, no grant, no assignment, no other role, and an **inactive** tenant role can still be edited — activity is AUT-C5/C6's concern, not this one.
- **RM-A11** **The rename is visible where it should be and invisible where it should not:** AUT-Q5 reports the new name; an existing audit record's captured role name is unchanged; and AUT-Q3's grants for the role are unaffected.

**Over HTTP**

- **RM-A12** `POST /api/roles/{roleId}/metadata` answers `200` with the six members. A missing `name` is `400`. A refusal is `400 { "error": … }` with the sentences above. No carrier is `401`. `GET /api/roles`, AUT-Q5 and AUT-Q3 are undisturbed.

**The dialog**

- **RM-U1** Edit is offered on the Role detail page for `role.manage` on a **tenant** role, and never otherwise — not for `role.read` alone, and not on a system role for anyone.
- **RM-U2** The form opens on the role's stored name and description, and sends exactly what was typed, untrimmed, to the metadata route. An empty Name sends nothing and is flagged.
- **RM-U3** On `200` the dialog closes, the page announces using the **server's stored name**, and the role is re-read. It is busy while sending, and sends once.
- **RM-U4** A refusal is shown word for word and the dialog stays open with the typed values.
- **RM-U5** The guard asks before discarding a dirty form, and a clean form closes without asking.
- **RM-U6** **A read-only caller sees no role-management action** on either the list or the detail page (RA-U6, rewritten).
- **RM-U7** No accessibility violations with the dialog open.

**Browser, in the dev stack (RM-U8),** with the owner's approval. Unlike AUT-C3, these steps are **reversible** — a role can be renamed back — but **each edit's audit record is permanent**.

1. As Ada, open `dev-smoke-reviewer` and edit its name to `Dev Smoke Reviewer (renamed)`. The detail page and the Roles list both show the new name.
2. Save again with nothing changed: it succeeds, and no second audit record appears.
3. Open a **system** role, `access-reviewer`: no Edit action is offered.

### Implementation notes

- **`Role.UpdateMetadata` was dead code before this story**, and so are `Deactivate`/`Reactivate` until AUT-C5/C6. It already refused a system role, which is exactly what PRV-C2 relies on to **refuse** role metadata drift instead of reconciling it; making it change-aware did not disturb that, because the refusal still comes first.
- **`IRoleRepository.FindTrackedAsync` is new, and is not a row lock.** `FindAsync` is `AsNoTracking` — *"the commands that use it do not change the role"* — so an update path could not use it. The new method only drops the no-tracking, deliberately not `FOR UPDATE`: RM5, and the same reasoning USR-C2 records for taking no D6 lock.
- **Two test fakes needed the new interface member**, as AUT-C3's two did for `ExistsWithCodeAsync`.
- **A code or `isSystemRole` in the payload binds to nothing** (RM2, RM10): the request record has only `Name` and `Description`, so extra fields are ignored by model binding rather than treated as an attempted mutation. An endpoint test pins that the stored code and ownership survive such a payload with a `200`.
- **`RoleUpdated` needed no catalogue change.** It was already seeded and active, with `PrimaryEntityType: "Role"`, `"BeforeAfter"` and `ReasonRequired: false`; the story adds the `AuditDeclarations` entry and the hand-maintained declared-code list. `AuditEventCatalogue.Version` is unchanged.
- **The no-op is proven by provenance, not by appearance.** `updated_at` and `updated_by` are stamped by an interceptor on every write, so an unchanged pair is positive evidence that no write happened — stronger than observing that the values still look the same. Five mutants attack the comparison from different angles, including one that folds case, which would silently swallow a capitalisation fix.
- **RM-A11's sharper half is unreachable today, and the test says so.** `audit_record` stores `authorizing_role_name` in its own column beside `authorizing_role_id`, copied at the time, so a rename cannot reach it; a design that joined for the name would fail the test. But renaming *the very role that authorised a recorded act* cannot be staged: only release-owned roles carry permissions, so a tenant role never authorises anything, and a release-owned role cannot be renamed at all (RM4). The test proves the copied-column property and records the gap rather than implying a stronger guarantee.
  - The check compares the authority captured by the **creation**, not every record for the role — the rename writes a record of its own, so "all records" is not a stable set.
- **RA-U6 was rewritten, not retired** (RM7). It read *"offers no action on either page"* while rendering with `role.read` alone, so a `role.manage`-gated Edit would have passed it silently: the test would have kept passing while no longer testing its own claim.
- **The Edit dialog needs no read of its own**, unlike Edit profile, which mounts its editor only after GetUser answers. The detail page already holds the role from AUT-Q5, so the form's defaults are the server's values from the first render — which is what makes `isDirty` mean *"differs from what the server returned"* (RM9: no per-role query key, no GetRole).

### Not included

- **AUT-C5/C6 lifecycle**, AUT-Q4 members, **AUT-C7/C8 permissions**, and deletion of any kind.
- **Any change to the code**, to `IsSystemRole`, or to the immutability guard (RM2, RM10).
- **A `GET /api/roles/{id}`** (RM9), any per-role query key, and any change to AUT-Q5, AUT-Q3 or `GET /api/roles`.
- **Any name-uniqueness rule** (RM1), and any reason input (RM8).

---

## AUT-C5 DeactivateRole and AUT-C6 ReactivateRole — the role's lifecycle

**Status:** Contract frozen 2026-09-20 by owner decision (RD1–RD9). One gate for the pair: AUT-C6 is the inverse, and shipping deactivation without a way back would leave an incomplete lifecycle.

### Requirement

An administrator holding `role.manage` retires a **tenant** role, and brings it back. Retiring is `IsActive = false` — **never a delete** (RO4, invariant 11).

```text
DeactivateRole                      ReactivateRole
   role.manage + human actor           role.manage + human actor
   -> refuse a blank reason            -> load the role
   -> load the role                    -> refuse the unknown
   -> refuse the unknown               -> refuse a system role
   -> refuse a system role             -> already active: 200, nothing written
   -> already inactive: 200,           -> otherwise IsActive = true
      nothing written                  -> RoleReactivated, Before/After
   -> otherwise IsActive = false       -> 200 with the stored role
   -> RoleDeactivated, Before/After
      and the reason
   -> 200 with the stored role
```

### The conceptual correction this gate makes

The command catalogue's failure-mode note says a role with many active holders should *"warn, not silently strand them"*. **That instinct points the wrong way, and the running system already knows it.** `role.IsActive` is deliberately absent from the authorisation predicate — invariant 7, UR12 and AUT-Q1's full predicate all omit it, `AuthorizationService` says so in three comments, and **two positive regression tests pin it**. Nobody loses access when a role is deactivated.

> **Deactivation is a change to role-assignment ELIGIBILITY, not an access revocation.**

So the warning this story shows is not a danger notice. It is the plain consequence, stated so an administrator is not surprised in either direction: new assignments stop, and current holders keep what they have. **The word "strand" does not appear in any user-facing copy**, because it implies a loss of access that does not occur.

### The decisions

| # | Decision |
| --- | --- |
| **RD1** | **AUT-C5 and AUT-C6 ship together.** AUT-C6 already has its domain method, its seeded audit event and its own catalogue row, and is "a straightforward inverse". Deactivation without a way back is an incomplete lifecycle. AUT-C6's contract stays the simpler of the two. |
| **RD2** | **The holder count comes from AUT-Q5, and AUT-Q4 is not built.** The catalogue says to "consider surfacing active-holder count first", and `activeHolderCount` is already a member of the role administration read. AUT-Q4 GetRoleMembers stays deferred, as RA7 froze it. **No member endpoint, no threshold, no definition of "many", no force flag.** The count is **information, not a blocking safety rule**: nothing about it can refuse the command. |
| **RD3** | **The confirmation states the actual effect.** *"This role has N active holders. Deactivating it will prevent new assignments, but existing holders will keep their current access."* With no holders the holder sentence is dropped. The copy never says "strand". |
| **RD4** | **Reaching the state that is already held is a successful no-op**, not a refusal — the convention AUT-C4 established (RM3). Already inactive, or already active, answers **`200` with the stored role, writes nothing and records nothing.** `Role.Deactivate()` and `Role.Reactivate()` therefore become **change-aware**, returning `bool`, and their `"Role is already inactive."` / `"Role is already active."` refusals are **removed**. This is a deliberate departure from the domain as written; it gives C5 and C6 symmetric lifecycle semantics and matches the rest of the platform. |
| **RD5** | **AUT-C5 requires a reason; AUT-C6 takes none.** The catalogue gives C5 `RoleId, Reason` and C6 `RoleId`, and `RoleDeactivated` is seeded `ReasonRequired: true` while `RoleReactivated` is `false`. The reason is **refused before any database work** (G6) with *"A reason is required to deactivate a role."* — this matters concretely: AR9 in the audit assembler would otherwise turn a missing reason into an `InvalidOperationException`, which `ProblemMiddleware` only catches as a catch-all, i.e. a **500**. GrantRole already guards against exactly that. **No reason is added to C6 for symmetry's sake.** |
| **RD6** | **`POST /api/roles/{roleId}/deactivate` and `POST /api/roles/{roleId}/reactivate`** → `200` with the **stored role**, exactly `{ roleId, code, name, description, isSystemRole, isActive }`. The verb and shape follow USR-C4/C5 and AUT-C4; the caller gets the authoritative post-mutation state without a second read. |
| **RD7** | **`Before` and `After`, both sides.** `RoleDeactivated` carries `Before { IsActive: true }` → `After { IsActive: false }` plus the reason; `RoleReactivated` carries `Before { IsActive: false }` → `After { IsActive: true }` and no reason. `BeforeAfter` permits one side alone (AR17), but here both sides communicate the transition. **Both events are already seeded and active; `AuditEventCatalogue.Version` is not bumped.** |
| **RD8** | **One state-dependent action on the Role detail page.** A tenant role shows **Deactivate** when active and **Reactivate** when inactive — never both. **Edit stays available in both states**, as RM-A10 requires. **Nothing is added to the roles list**: the detail page is already the role-management surface, and lifecycle controls there keep the list from becoming an action-heavy table. A release-owned role is offered **neither** action, and the server stays authoritative regardless. Deactivate uses the **`destructive`** button variant — its first use in this client; Reactivate uses the ordinary treatment. |
| **RD9** | **No row lock, for either command.** There is no lifecycle-sensitive cascade: deactivation revokes no assignment, changes no existing authorisation, and the only transition is `IsActive`. The known race — `GrantRole` reads the role without a lock, so a grant can interleave with a deactivation — is recorded as a **Known Gap** rather than solved with locking the authorisation semantics deliberately tolerate. |

### The rules, and what each refusal says

| Input | Rule | Refusal |
| --- | --- | --- |
| **Reason** (C5 only) | required, not blank, **before any database work** | *"A reason is required to deactivate a role."* |
| **RoleId** | names an existing role | *"The role does not exist."* |
| | names a **tenant** role — C5 | *"System roles cannot be deactivated."* |
| | names a **tenant** role — C6 | *"System roles cannot be reactivated."* |
| **Already in the target state** | **not a refusal** (RD4) | — |

The two system-role sentences are the domain's own, **unchanged**. An unknown role is `400` *"The role does not exist."*, as AUT-C4 answers it.

### Order of work in the handlers

1. **AUT-C5 only: refuse a blank reason**, as the first statement, **outside the transaction** — no transaction is opened, so no database work occurs (G6).
2. **Load** the role tracked. No row lock (RD9).
3. **Refuse an unknown role**, then let the domain refuse a **system role** — ownership before state, as AUT-C4 orders it.
4. **Capture `Before`**, then call the change-aware domain method.
5. **Nothing changed:** return the stored role. **No write, no record.**
6. **Otherwise** declare the event with `Before`, `After`, and — C5 only — `.WithReason(command.Reason)`.
7. **Answer `200`** with the stored role.

### Change control

**No workbook is edited.** Four items are outstanding change control:

- **The `Command steps` sheet has no sequence for AUT-C5 or AUT-C6.** The order above is this contract's, derived from AUT-C1's reason handling and AUT-C4's shape.
- **The catalogue's "warn, not silently strand them" is answered by RD3, not implemented literally.** Stranding does not occur; the copy states what does.
- **"Consider surfacing active-holder count first"** is satisfied from AUT-Q5 rather than by AUT-Q4 (RD2), whose deferral RA7 already recorded.
- **The frozen `role` entity has no column for a deactivation reason, timestamp or actor**, unlike `app_user`. The reason reaches the `RoleDeactivated` audit record and nowhere else. AUT-C8 has the identical shape. This story adds no column.

### The actions (RD8)

- **On the Role detail page**, for `role.manage` holders and **tenant roles only**: **Deactivate** while the role is active, **Reactivate** while it is inactive. Beside the existing **Edit role**, which is offered in both states.
- **Deactivate** opens a confirmation carrying:
  - the consequence, stated per RD3, with the holder count from the role already on the page;
  - a **required Reason** field, as Revoke role and Deactivate user have;
  - a confirm button labelled **Deactivate**, in the `destructive` variant, and **Deactivating…** while it sends.
- **Reactivate** opens a confirmation with no reason field, stating that the role may be assigned again and that existing holders are unaffected, confirmed by **Reactivate**.
- **On `200`:** the dialog closes, the page announces — *"Role deactivated: {name}."* / *"Role reactivated: {name}."* using the **server's** stored name — and the role is re-read. A call that changed nothing is still a success to the client, which does not predict a no-op.
- **Focus** returns to the action that opened the dialog.

### Acceptance Criteria

**The commands**

- **RD-A1** Deactivating an active tenant role sets `is_active` false and answers the six members of RD6 with `isActive` false; `code`, `name`, `description`, `isSystemRole`, `createdAt` and `createdBy` are untouched. Reactivating an inactive one is the exact inverse.
- **RD-A2** **One `RoleDeactivated`** is written, with the administrator as actor, the role as primary entity, `Before { IsActive: true }`, `After { IsActive: false }` and **the supplied reason**. **One `RoleReactivated`** is written with the inverse pair and **no reason**. No other record is written, and `AuditEventCatalogue.Version` is unchanged.
- **RD-A3** **The no-op** (RD4): deactivating an already-inactive role, and reactivating an already-active one, answer `200` with the stored role while leaving `updated_at`, `updated_by` and the audit sequence untouched.
- **RD-A4** **A blank reason is refused before any database work** (G6): no transaction is opened and no collaborator is touched, for `""` and for whitespace. AUT-C6 has no reason input at all.
- **RD-A5** **A system role is refused** for both commands, with their own sentences, and nothing is written — including when it is already in the target state, so ownership precedes the no-op check.
- **RD-A6** **An unknown role is refused** for both commands with *"The role does not exist."*, and nothing is written.
- **RD-A7** **Authorisation:** a caller without `role.manage` is refused, and so is a non-human caller, for both commands.
- **RD-A8** **Deactivation changes no assignment and no access.** After deactivating a role that has holders: every `user_role` row is untouched, and the holder's authorisation is unchanged on **both** views — `IsAllowedAsync` and the effective set. This is AUT-C5's central claim and is asserted positively, not as the absence of a filter.
- **RD-A9** **Eligibility does change:** after deactivation the role is refused by AUT-C1 (*"This role cannot be granted."*), is absent from the grantable list `GET /api/roles`, and is absent from AUT-Q5 unless `includeInactive` is set — where it appears with `isActive` false and its derived values unchanged. After reactivation all three are restored.
- **RD-A10** **An inactive tenant role can still be edited** (RM-A10), and reactivating it later keeps the edited metadata.
- **RD-A11** **Nothing else changes:** no permission, no grant, no other role, and no user.

**Over HTTP**

- **RD-A12** Both routes answer `200` with the six members. A missing reason on `/deactivate` is `400`, including for an absent body. A refusal is `400 { "error": … }` with the sentences above. No carrier is `401`. `GET /api/roles`, AUT-Q5 and AUT-Q3 are otherwise undisturbed.

**The actions**

- **RD-U1** Deactivate is offered on a tenant role's detail page to a `role.manage` holder while the role is active; Reactivate while it is inactive; **never both**, never to a `role.read` holder, and neither on a release-owned role for anyone.
- **RD-U2** **Edit role remains offered in both states**, and the roles list gains no lifecycle action.
- **RD-U3** The Deactivate confirmation states the consequence with the holder count, and **never uses the word "strand"**. With no holders the holder sentence is absent.
- **RD-U4** A blank reason is flagged client-side and **nothing is sent**; the reason is otherwise sent as the server's own rule decides.
- **RD-U5** On `200` the dialog closes, the page announces with the **server's** stored name, and the role is re-read, so the action flips to its inverse. It is busy while sending, and sends once.
- **RD-U6** A refusal is shown word for word and the dialog stays open.
- **RD-U7** No accessibility violations with either confirmation open.

**Browser, in the dev stack (RD-U8),** with the owner's approval. These steps are **reversible** — that is the point of the pair — but **each transition's audit record is permanent**.

1. As Ada, open `dev-smoke-reviewer` and **Deactivate** it with a reason. It reads Inactive, the action becomes Reactivate, and Edit is still offered.
2. It disappears from the Roles list until **Show inactive roles** is ticked, and from the grant form's role list.
3. **Reactivate** it. It reads Active again and returns to both lists.

### Implementation notes

- **`Role.Deactivate` and `Role.Reactivate` were dead code**, and RD4 changed them rather than only calling them: the `"Role is already inactive."` and `"Role is already active."` refusals are **gone**, replaced by a `bool`. Keeping them beside an idempotent command would have been two answers to one question.
- **The blank-reason check is the handler's first statement, outside the transaction** (G6). This is not ceremony: `RoleDeactivated` is seeded `ReasonRequired`, and a blank reason reaching `AuditRecordAssembler` fails AR9 as an emission defect — an `InvalidOperationException`, which `ProblemMiddleware` catches only as a catch-all, so a **500** rather than a refusal. GrantRole carries the same guard and says so. Three mutants attack it: removed, moved inside the transaction, and weakened to a null check so whitespace slips through.
- **Neither handler touches a `user_role` row**, and that is the story. `Deactivation_changes_eligibility_and_leaves_access_untouched` proves it through the **command**; `AuthorizationServiceTests` already proved it against the **column**. A handler that reached for `role.IsActive`, or cascaded into assignments, passes there and fails here.
  - That test stages a live `role_permission` row with SQL. AUT-C7 does not exist, and a role carrying no permissions cannot demonstrate *keeping* any — the assertion would pass vacuously.
  - **A holder needs an active IDENTITY as well as an active user** (invariant 7), checked before the predicate runs. Seeding only `app_user` made the test fail on its own setup; had it asserted only the post-deactivation state, it would have "passed" while proving nothing.
- **One domain fixture reaches past the API under test, deliberately.** A system role that is *already inactive* cannot be built through the domain, because the domain refuses to retire one — so the ordering claim, ownership before the no-op, would be untestable. The state is staged by reflection; the refusal is still what must come out. The database half is staged with SQL.
- **`ConfirmAction` gained a `destructive` prop**, its first caller being Deactivate (RD8). The variant existed in the button component and had never been used anywhere in the client.
- **The roles module owns its own three-line reason schema.** Importing the users module's was refused by the module-boundaries rule, and moving it to `shared/forms` was refused again because that layer bans `zod` outright (*"FormField knows nothing of schemas"*). Two rules pointing the same way: modules stay independent, and the form primitives stay ignorant of validation. The users module is untouched.
- **The announcement uses the name the SERVER answered.** The mutation campaign's one survivor was the local name, which is identical unless someone renamed the role since this page read it — a stale page, not a contrived case. Closed with a test, matching the convention RC-U3 and RM-U3 already pinned.
- **Two PostgreSQL renderings caught tests, not the product:** `boolean::text` gives `true`/`false`, not psql's display `t`/`f`; and `JsonElement.ToString()` capitalises booleans where `GetRawText()` reflects what is stored.

### Not included

- **AUT-Q4 GetRoleMembers** and any member list or per-holder detail (RD2).
- **AUT-C7/C8 permissions**, and deletion of any kind.
- **Any change to the authorisation predicate**, to AUT-C1's eligibility rule, to AUT-Q5, AUT-Q3 or `GET /api/roles`.
- **Any lifecycle column on `role`** — no `deactivated_at`, `deactivated_by` or reason column.
- **Any locking** (RD9), and any resolution of the grant/deactivation race, which is recorded as a Known Gap.

---

## AUT-C7 AddPermissionToRole and AUT-C8 RemovePermissionFromRole — what a role may do

**Status:** Contract frozen 2026-09-20 by owner decision (RG1–RG10). One gate for the pair: they write the same table, share one index, and the release-owned question could not be answered for one without the other.

### Requirement

An administrator holding `role.manage` adds a permission to a **tenant** role, and takes it away. A grant is never deleted: AUT-C8 closes the row by setting `RevokedAt`/`RevokedBy`, and a later re-grant is a **new row** (RP1, invariant 11).

```text
AddPermissionToRole                    RemovePermissionFromRole
   role.manage + human actor              role.manage + human actor
   -> load the role                       -> refuse a blank reason
   -> refuse the unknown                  -> load the grant
   -> refuse a system role                -> refuse the unknown
   -> load the permission                 -> refuse a system role
   -> refuse the unknown or inactive      -> refuse one already revoked
   -> refuse an existing live grant       -> close the row
   -> RP6: refuse if the role has an      -> PermissionRevokedFromRole,
      ACTIVE agent assignment and the        Before and After, with the reason
      permission requires a human actor   -> 204
   -> insert a NEW row
   -> PermissionGrantedToRole, After only
   -> 201
```

### The decisions

| # | Decision |
| --- | --- |
| **RG1** | **Both commands refuse a release-owned role**, with the ownership sentence the domain already uses: *"System roles cannot be modified."* The argument is not symmetry but PRV-C2: AUT-C7 on a system role creates `GrantMissingFromSeed` and AUT-C8 creates `RevokedGrantInSeed`, and **both refuse every later deployment**. Allowing either would let one application command produce a state the synchroniser is deliberately forbidden to reconcile (F5, F6). This is **one shared ownership rule**, not two independently invented ones. |
| **RG2** | **RP6 is an application-level precondition, checked inside the command's transaction, and AUT-C1 is not amended.** Making it atomic would need a lock on the role in **both** AUT-C7 and GrantRole, changing AUT-C1's frozen concurrency contract (RD9, RM5) to close a race that **cannot occur in v1** — agents cannot be created (invariant 17a, AU11). The cross-command race is recorded as a **Known Gap** stating the limit of the guarantee honestly. |
| **RG3** | **RP6 is implemented literally: an *ACTIVE* agent assignment.** A *future-dated* agent assignment would pass the check and become a mixed role at its own `effective_from`, with no command running — a real hole that no locking closes. **It is not silently widened here.** The frozen catalogue says "active", and the other commands' "live" semantics are not grounds to redefine a frozen invariant while implementing against it. Recorded as a **parked contract amendment** for its own decision. |
| **RG4** | **An inactive permission cannot be granted.** A new authorization edge may only be created against the **current** catalogue. The resulting state would also be internally misleading: AUT-Q5's `agentAssignable` counts live grants **without** filtering `Permission.IsActive`, while authorisation requires it — so a live grant of a retired human-only permission makes a role non-agent-assignable while authorising nobody. Refusal: *"The permission is not active."* |
| **RG5** | **AUT-C8 requires a reason; AUT-C7 takes none.** The catalogue gives C8 `RolePermissionId, Reason` and C7 `RoleId, PermissionId`, and the seeds agree — `PermissionRevokedFromRole` is `ReasonRequired: true`, `PermissionGrantedToRole` is `false`. The reason is refused **before any database work** (G6): *"A reason is required to revoke a permission from a role."* **No reason column is added to `role_permission`** — the frozen entity has none, so the reason lives only in the audit record, as AUT-C5's does. |
| **RG6** | **An application pre-check with the database as the backstop**, exactly as AUT-C3 did once `IX_role_code` became reachable. `ux_role_permission_active` is **mapped in the translator now** — the deferral reserved it for this story — so a race past the pre-check reads the same sentence: *"The role already has this permission."* `ExceptionTranslationTests` must move its unmapped example to another genuinely unreachable constraint. |
| **RG7** | **Two narrow abstractions, created because the commands need them.** `IRolePermissionRepository` — find a live grant for a pair, find a grant by id (tracked), add. And a **narrow** permission read for the command path, returning only what AUT-C7 decides on: existence, `IsActive`, `RequiresHumanActor`. **The command does not depend on AUT-Q6's DTO reader**, which is query infrastructure and is keyed by neither id nor lock. |
| **RG8** | **`PermissionGrantedToRole` carries `After` only** — a grant is a newly created edge with no meaningful before-state — and **`PermissionRevokedFromRole` carries `Before` and `After`**, the lifecycle transition of an existing row, plus the reason. Both carry their two **required** entity references: `Role` as `Target`, and `Permission` as `Granted` / `Revoked`. Both events are already seeded and active; **`AuditEventCatalogue.Version` is not bumped.** |
| **RG9** | **The Role detail page's permissions section becomes operational**: the existing table, an **Add permission** action, and **Revoke** on each live grant row. **No Permissions page is created** — AUT-Q6 stays API-only as a standalone surface and is consumed by the picker, which closes the open point RA recorded. Edit role's description is corrected: it currently says a role's permissions are not its to change, which this story makes false. |
| **RG10** | **The UR9 claim in this document is corrected, and UR9 is not implemented here.** `docs/requirements.md` states that UR8 and UR9 "remain enforced by the database and the domain". UR8 is (`ck_user_role_agent_finite`); **UR9 is not** — no constraint, trigger or domain rule references `requires_human_actor`, and GrantRole refuses every non-human target without ever reading the role's permissions. The claim is corrected to say what is actually enforced. Implementing UR9 belongs to its own slice. |

### The rules, and what each refusal says

| Command | Input | Rule | Refusal |
| --- | --- | --- | --- |
| **Both** | **Role** | exists | *"The role does not exist."* |
| | | is a **tenant** role (RG1) | *"System roles cannot be modified."* |
| **AUT-C7** | **Permission** | exists | *"The permission does not exist."* |
| | | is active (RG4) | *"The permission is not active."* |
| | **The pair** | has no live grant (RP2, RG6) | *"The role already has this permission."* |
| | **RP6** | the role has no **active** agent assignment, when the permission requires a human actor | see below |
| **AUT-C8** | **Reason** | required, before any database work | *"A reason is required to revoke a permission from a role."* |
| | **Grant** | exists | *"The role permission does not exist."* |
| | | is not already revoked | *"Role permission has already been revoked."* (the domain's own) |

**RP6's refusal carries the remediation**, because the catalogue's step 3 requires it *in the error message*:

> *"This role is held by an agent, so it cannot be given a permission that requires a human actor. Revoke the agent's assignment, add the permission, then grant the agent an agent-safe role."*

### Order of work in the handlers

**AUT-C7** — no reason, so nothing precedes the transaction.

1. **Load the role** tracked; refuse the unknown, then refuse a **system role** (RG1) — ownership before everything, as AUT-C4 and AUT-C5 order it.
2. **Load the permission**; refuse the unknown, then the **inactive** (RG4).
3. **Refuse an existing live grant** (RG6). The index remains the guarantee.
4. **RP6** (RG2, RG3): if the permission requires a human actor, refuse when the role has an **active** agent assignment.
5. **Insert a new row** — never an update of a revoked one (RP1).
6. **`PermissionGrantedToRole`**, `After` only, with both references.
7. **`201`** with the new grant's id.

**AUT-C8**

1. **Refuse a blank reason**, first, **outside the transaction** (G6).
2. **Load the grant** tracked; refuse the unknown.
3. **Refuse a system role** — the grant's role decides (RG1).
4. **Close the row** through the domain, which refuses one already revoked.
5. **`PermissionRevokedFromRole`**, `Before` and `After`, with both references and the reason.
6. **`204`**.

**The answers follow AUT-C1 and AUT-C2**, the grant/revoke pair for `user_role`, rather than the roles module's stored-representation convention: `201 { rolePermissionId }` and `204`. Nothing about the role's own state changes, and the page re-reads AUT-Q3 either way.

### Change control

**No workbook is edited.** Five items are outstanding change control:

- **AUT-C8 has no `Command steps` sequence**; AUT-C7's five steps are followed, with ownership and the permission's state inserted ahead of them because the catalogue's step list begins after authorisation and says nothing about either.
- **RP6's temporal scope is a recorded defect, parked** (RG3). See the Known Gap.
- **AUT-C7's step 5 requires cache invalidation. There is no cache** — none in `src/`, and the word appears **zero times** in the frozen specification. Recorded as **non-applicable**; no cache infrastructure is created to satisfy an aspirational sentence.
- **Neither command's catalogue row mentions system roles**, conspicuously, where AUT-C3, AUT-C4 and AUT-C5 all do. RG1 is decided from PRV-C2's refusal codes, not from the command rows.
- **AUT-C8 does not cite D1**, and D1's wording is scoped to revoking an *assignment*. RG5 requires the reason on the catalogue's own authority — the `Reason` key input and the event's `ReasonRequired` seed — not by extending D1.

### The permissions section (RG9)

- **On the Role detail page**, for `role.manage` holders and **tenant roles only** — the same `editable` gate the page already applies, so a release-owned role offers nothing to anyone.
- **Add permission** opens a dialog with a **picker** of the permission catalogue (AUT-Q6), showing each permission's code, name and whether it is human-only. **Permissions the role already holds are not offered.** Retired permissions are not offered (RG4).
- **Revoke** on each live grant row, in a sixth column appended for `role.manage` holders as the users module appends its Actions column, opening a confirmation with a **required Reason**.
- **On success:** the dialog closes, the page announces, and the permissions list is re-read. Focus returns to the action that opened the dialog — and, for a revoke, to the table's heading, because the row leaves with the grant.
- **Edit role's description is corrected** — it says a role's permissions are not its to change.

### Acceptance Criteria

**AUT-C7**

- **RG-A1** A live grant is created on a tenant role: a **new row**, `RevokedAt` null, `GrantedBy` the administrator, and the answer carries its id. The role's own row is untouched.
- **RG-A2** **One `PermissionGrantedToRole`** with the administrator as actor, the grant as primary entity, `Role`/`Target` and `Permission`/`Granted` references, `After` only and **no reason**. No other record.
- **RG-A3** **A re-grant after a revocation creates a SECOND row** (RP1) — the revoked row is untouched, both rows survive, and only the new one is live.
- **RG-A4** **The pair is refused when a live grant exists** (RG6), and the index refuses a race past the pre-check with **the same sentence**, not a 500.
- **RG-A5** **An unknown permission and an inactive permission are refused** with their sentences, and nothing is written.
- **RG-A6** **RP6:** granting a human-only permission to a role with an **active agent assignment** is refused, with the remediation in the message, and nothing is written. Granting a **non**-human-only permission to the same role is accepted, and granting a human-only permission to a role whose agent assignment is **revoked or ended** is accepted.
- **RG-A7** **A system role is refused** (RG1), and so is an unknown role; nothing is written in either case.
- **RG-A8** **Authorisation:** a caller without `role.manage` is refused, and so is a non-human caller.

**AUT-C8**

- **RG-A9** A live grant is closed: `RevokedAt` and `RevokedBy` set, **the row is not deleted**, and the row count is unchanged.
- **RG-A10** **One `PermissionRevokedFromRole`** with both references, `Before` and `After` showing the revocation, and **the reason**.
- **RG-A11** **A blank reason is refused before any database work** (G6): no transaction is opened and no collaborator is touched.
- **RG-A12** **An unknown grant, an already-revoked grant, and a grant on a system role are each refused**, and nothing is written.
- **RG-A13** **Authorisation:** as RG-A8.

**Both**

- **RG-A14** **The effect on authorisation is real and immediate:** after AUT-C7 the role's holders gain the permission on both views; after AUT-C8 they lose it. No other holder is affected.
- **RG-A15** **The derived reads follow** without changing their definitions (RA4): `permissionCount` and `agentAssignable` in AUT-Q5, and AUT-Q3's list. The four frozen member arrays are unchanged.
- **RG-A16** **Nothing else changes:** no role row, no assignment, no permission, and no other role's grants.

**Over HTTP**

- **RG-A17** `POST /api/roles/{roleId}/permissions` answers `201` with the grant's id; `POST /api/role-permissions/{rolePermissionId}/revoke` answers `204`. A missing permission id or reason is `400`, including for an absent body. Refusals are `400 { "error": … }` with the sentences above. No carrier is `401`. AUT-Q3, AUT-Q5, AUT-Q6 and `GET /api/roles` are undisturbed.

**The screen**

- **RG-U1** Add permission and Revoke are offered on a tenant role's detail page to a `role.manage` holder, and to nobody else — not to a `role.read` holder, and not on a release-owned role.
- **RG-U2** The picker offers the catalogue **minus the role's live grants**, and minus retired permissions, and shows which permissions are human-only.
- **RG-U3** A blank reason on the revoke confirmation is flagged and **nothing is sent**.
- **RG-U4** On success each dialog closes, the page announces, and the permissions list is re-read so the row appears or leaves. Each is busy while sending and sends once.
- **RG-U5** A refusal is shown word for word — including RP6's remediation — and the dialog stays open.
- **RG-U6** No accessibility violations with either dialog open.
- **RG-U7** Focus returns to the action that opened the dialog, and to the permissions heading when the revoked row has left.

**Browser, in the dev stack (RG-U8),** with the owner's approval. A grant can be revoked, so these steps are reversible; **each transition's audit record is permanent**.

1. As Ada, open `dev-smoke-reviewer` and **add** `user.read`. It appears in the permissions table; the role stays agent-assignable.
2. **Add** `user.create`, which is human-only. The role's AUT-Q5 row now reads **Agent-assignable: No**.
3. **Revoke** `user.create` with a reason. It leaves the table and the role is agent-assignable again.

### Implementation notes

- **This story changes the domain not at all.** `RolePermission.Create` and `Revoke` already said everything the pair needed, and `Revoke` had never been called by anything. The four new rules — RP6, ownership, the live-grant check and the permission's activity — each need a row the aggregate cannot see, so all four are the handler's (RG7). `RolePermissionRulesTests` is green on arrival and pins what AUT-C8 now depends on.
- **Two abstractions were created, narrow on purpose.** `IRolePermissionRepository` and an `IPermissionRepository` returning existence, `IsActive` and `RequiresHumanActor`. The commands deliberately do **not** use AUT-Q6's reader: it is query infrastructure, keyed by neither id nor lock, shaped for a table.
- **The RP6 query is the first in this codebase to filter `user_role.actor_type`.** Nothing else reads that column — AUT-Q5's holder count deliberately ignores actor type, and the authorisation predicate takes the actor type from `app_user`. Four mutants attack the query's shape, and one of them found a real gap: without the `ActorType == Agent` filter, **any** holder blocks the grant, so a human-held role could not acquire a human-only permission. A test now covers that ordinary case.
- **The translator's unmapped example moved for the second time.** `An_unknown_unique_violation_survives_untranslated` used `IX_role_code` until AUT-C3 made it reachable and `ux_role_permission_active` until this story did. It now asks for a second System actor **through the change tracker**, colliding on `PK_app_user`. A first attempt used raw SQL and failed correctly: raw SQL bypasses `SaveChanges`, which is where the translator runs.
- **A test was passing for the wrong reason.** `A_race_past_the_pre_check_reads_the_same_sentence` inserted the duplicate row *before* calling the command, so the **pre-check** caught it and the index was never reached — which is why unmapping RP2's index survived mutation. It now injects a repository blind to live grants, so the insert genuinely reaches `ux_role_permission_active`.
  - **One mutant is equivalent and kept:** removing the pre-check leaves the index refusing with the identical sentence, which is RG6 working as designed. The two paths are indistinguishable by construction.
- **AUT-Q6 had no client consumer at all** until AUT-C7's picker, which is what closed the open point recorded at the role administration slice. It stays API-only as a standalone surface: no Permissions page (RG9).
- **`EditRoleDialog`'s description said a role's permissions are not its to change.** That is no longer true, and the copy is corrected.
- **Two web traps worth remembering.** The roles module exports a `RolePermissions` **const** of permission codes *and* a `RolePermissions` **type** for one role's grants; a page needing both must alias one. And **JSX attribute strings do not process escapes** — `busyLabel="Adding\u2026"` is literal text, where `busyLabel={"Adding\u2026"}` is not.
- **Four PostgreSQL and audit-schema facts caught tests, not the product:** `concat_ws` skips nulls rather than leaving an empty field; `user_role` has no `created_at`/`created_by`; the audit reference column is `ref_role`; and after a revocation a grant is the subject of **two** records, so a references assertion must name the event type.

### Not included

- **UR9's enforcement** (RG10) — only the documentation claim is corrected.
- **Any widening of RP6 to future-dated assignments** (RG3).
- **Any cache**, and any invalidation machinery.
- **A Permissions page** (RG9), and any change to AUT-Q6's response.
- **Any change to AUT-C1's concurrency contract** (RG2), to the authorisation predicate, or to the `agentAssignable` derivation.
- **Any reason column** on `role_permission` (RG5), and any deletion of a grant.

---

## AUT-Q4 GetRoleMembers — who holds this role, and the Holders section

**Status:** Contract frozen 2026-09-21 by owner decision (RH1–RH14). **Read-only.** No assignment is created, revoked or altered, no role or permission changes, and no audit record is written.

### Requirement

A caller holding **both `role.read` and `user.read`** reads the people who hold one role at an instant, with each holding's dates and the reason it was granted.

```text
AUT-Q4 GetRoleMembers    the role-scoped read of who holds a role
```

**The purpose is the access-review question, not a safety check before deactivation.**

> AUT-Q4 is the role-scoped operational read of role assignments. Its primary purpose is role administration and access-review visibility; it is **not** a prerequisite for role deactivation.

This is stated because the query catalogue's note — *"Call before AUT-C5 to warn about stranding holders"* — was examined at the AUT-C5/C6 gate and **rejected**. RD2 took the count from AUT-Q5 instead, and RD3 established that deactivation revokes nobody's access, so there is no stranding to warn about. RA7's deferral of AUT-Q4 *"with AUT-C5/C6"* therefore expired without being needed. The justification that survives is the design specification's own question — *"Who holds which role, where, and for how long?"* — and slice 4's goal of *"access review reports available"*.

### The decisions

| # | Decision |
| --- | --- |
| **RH1** | **AUT-Q4 is built now, on the access-review justification above.** The stranding argument is not revived, and does not appear in this contract or in any user-facing copy. |
| **RH2** | **AUT-Q4 and REV-Q1 are two query contracts over one derivation.** REV-Q1 `GetAccessReviewReport` asks the same question of the same tables (`ScopeId, RoleId, asOf`) under `accessreview.read`, and is the broader, exportable compliance surface. They differ in audience, authorisation and output — legitimately — but **must not maintain independent assignment-state derivations.** Both resolve assignment state through `RoleAssignmentStates.At`, the platform's single derivation, and REV-Q1 is expected to reuse this story's reader rather than write a second one. Recorded now so REV-Q1 cannot reinvent AUT-Q4 by accident. |
| **RH3** | **`ScopeId` is kept in the query contract, and only the global scope exists.** The catalogue names the parameter, and removing it would be a contract change for no gain. V1 has one scope: `GrantRoleCommandHandler` hardcodes `ScopeType.Global`, and nothing else creates an assignment. **No non-global scope semantics are introduced here:** the query applies no scope predicate, projects no scope, and **refuses a supplied `scopeId`** — a value names a scope that cannot exist. |
| **RH4** | **`asOf`, and no `includeInactive`.** The catalogue is explicit — *"Active at asOf"*. AUT-Q2's `includeInactive` model is deliberately **not** imported: it answers *"this user's assignments, optionally including inactive ones"*, where AUT-Q4 answers *"who holds this role at this instant"*. Only assignments **Active at `asOf`** are returned. `asOf` defaults to the current instant. |
| **RH5** | **One row per active assignment; `activeHolderCount` is distinct users.** The dates and the reason are properties of the **assignment**, not of the person, so the assignment is the result identity. The count keeps RA2's meaning — distinct holders — rather than quietly becoming a row count. |
| **RH6** | **The holder's `status` is returned and never filters.** Membership is determined by the assignment (`user_role`), exactly as RA2 determines the count; `app_user.Status` is projection only. The count deliberately does not join `app_user`, and the member read necessarily does, because it must name the person — **a projection difference, not a change to the membership predicate.** An anomalous holder is therefore made visible rather than hidden. |
| **RH7** | **No paging**, following AUT-Q5. The complete membership of the requested role at `asOf` is returned. A generic pagination model is not introduced on the strength of a hypothetical large tenant; if evidence later requires paging, that is its own contract decision. |
| **RH8** | **An unknown role is `404` — *"The role does not exist."*** AUT-Q3 is the direct sibling: both take a role as their primary resource, and RA11 already established the mechanism. The standing divergence with AUT-Q2's `400` is a recorded Known Gap and is **not** reconciled inside this story. |
| **RH9** | **`role.read` AND `user.read`, both required.** Deliberately stricter than the catalogue's single `role.read`. This is the first read that turns **a role into named people**, and the precedent is already in the platform: PRV-C1 Amendment 1 granted `security-administrator` `user.read` *for visibility*, one-way. `role.read` establishes authority to inspect the role; `user.read` establishes authority to see the people in the answer. **This is AND, not OR** — either alone is refused. It keeps a role-administration permission from becoming an indirect user directory. Recorded as a change-control amendment to the query catalogue. |
| **RH10** | **A Holders section on the role detail page**, read-only, with no separate Role members page. Each row links to that user's detail page, and **the link states its permission condition explicitly** rather than relying on the incidental fact that RH9 already required `user.read`. |
| **RH11** | **No action on a holder row.** Revoking belongs to AUT-C2, whose commands and `role.revoke` permission are the users module's. *AUT-Q4 reports who holds the role; AUT-C2 remains responsible for changing that.* |
| **RH12** | **Not audited**, as every read is (RA-A11). Exposing people does not by itself make a read an audit event; authorisation controls the disclosure. |
| **RH13** | **A query may declare more than one required permission, and all must hold.** `QueryAuthorization` is single-code by construction, and declaring `role.read` while checking `user.read` inside the handler would make the declaration a half-truth — the precise failure architecture §11 exists to prevent (*"a handler that forgets its authorization check fails silently — it serves the data"*). The classification is therefore extended: a set-based factory beside the existing `Required(string)`, an exposed `PermissionCodes`, and a verifier that rejects an empty set or a blank code anywhere in one. **Existing single-permission queries are unchanged.** Architecture §11 is amended to say an authorised query may declare one or more codes, **all** of which must be satisfied. |
| **RH14** | **The per-assignment contract stands, and V1 cannot observe the difference.** `ex_user_role_global_no_overlap` excludes overlapping `[effective_from, effective_to)` ranges for the same user, role and scope — revoked rows included — so at global scope **each user contributes at most one active assignment for a role at any instant, and member-row count and distinct-holder count coincide today.** That is a data-model property, not a term of the query contract, and it is recorded so nobody reads RH5 as describing a live defect. The contract stays correct when scope semantics expand. |

### The instant is resolved once (RH14, time alignment)

**`asOf` is the authoritative instant for the whole Holders view.** The count and the rows are never evaluated at two different instants:

- **AUT-Q4 returns its own `activeHolderCount`**, derived from the rows it is returning — `DISTINCT userId` over them — so the list and the count cannot disagree **by construction**, not merely by luck of timing.
- **The Holders section does not use AUT-Q5's `activeHolderCount`**, which is computed at that query's own instant. The role detail page's `Holders` metadata row is replaced by this section; AUT-Q5's count stays on the roles **list**, where it is that query's own value at that query's own instant, unchanged.

### The route and its shape

**My choice, not the catalogue's** — the query catalogue defines queries, not HTTP. It follows AUT-Q3's route, its sibling on the same resource.

| Query | Route |
| --- | --- |
| **AUT-Q4** | `GET /api/roles/{roleId:guid}/members?asOf=&scopeId=` |

The response:

```text
{ asOf, activeHolderCount, members: [ … ] }
```

Each member exactly:

```text
assignmentId, userId, displayName, email, status,
effectiveFrom, effectiveTo, assignedAt, assignedBy { userId, displayName },
assignmentReason
```

- **`asOf` is echoed** because it is the instant the whole answer was judged at, and a caller that sent none cannot otherwise know which instant it received.
- **No `revokedAt`, `revokedBy` or `revocationReason`.** Only assignments Active at `asOf` are returned (RH4), so all three would be permanently null. A field that is always null is not a contract, it is decoration.
- **No `actorType`, and no scope member.** Agents cannot exist (invariant 17a, AU11) so `actorType` would always be `Human`, and RH3 introduces no scope semantics. The spec's access-review concern about an agent holding a review role is real, and belongs to the slice that makes agents exist.
- **`email` is nullable** because the column is, exactly as USR-Q2's row records. It is included so an access reviewer can tell two people with the same display name apart.
- **Ordered by `displayName`** under ICU `unicode`, then `assignmentId` — as the roles list, the user list and the grantable list are ordered, so the order does not depend on the database's default collation.
- **Parameters are parsed strictly**, as AUT-Q3's are: `asOf` must be one ISO-8601 instant or the answer is `400`; **a supplied `scopeId` is `400`** — *"Only the global scope exists."* (RH3).
- **A carrier is required** (`401` without one), and **both `role.read` and `user.read`** (`400` without either). **The refusal does not say which permission was missing**, so it discloses nothing about the caller's own permissions.
- **A role with no holders answers `200`**, an empty list and a count of zero; **an unknown role answers `404`** with *"The role does not exist."* (RH8), produced by the route from the query result. `ProblemMiddleware`'s allowlist is untouched, exactly as AUT-Q3 leaves it.
- **Not audited** (RH12).

### The screen (RH10, RH11)

- **`/admin/roles/{roleId}` gains a Holders section**, below Permissions, mirroring its structure: a heading, the count, and a table of rows. There is no separate page and no new route.
- **The `Holders` row leaves the metadata list** and becomes this section's count, from AUT-Q4 (time alignment, above).
- **The section is mounted only for a caller holding both `role.read` and `user.read`**, so its request is never made without them. The roles module reads `UserPermissions.read` from the users module's public surface — the mirror of RA8, and the same move `userActions.ts` already makes in the other direction.
- **Each row's display name links to `/admin/users/{userId}`**, under an explicit `user.read` condition (RH10).
- **Columns:** Holder, Email, Status, From, To, Reason. `To` reads *"No end date"* when there is none, as the user detail page's roles table already does.
- **The section offers no action** (RH11). Its loading, error and retry are its own, so a failure there leaves the rest of the page standing — as every other section does.

### Acceptance Criteria

**The read (RH4, RH5, RH6)**

- **RH-A1** Each member is answered with exactly the ten members above, ordered by display name, and `asOf` is echoed.
- **RH-A2** **Active at `asOf` only:** an assignment whose period contains `asOf` and which is not revoked is returned; **Future, Ended and Revoked assignments are not**, each proven separately.
- **RH-A3** **`asOf` projects:** an assignment revoked after `asOf` **is** returned, and one that had not yet begun at `asOf` is not. Without `asOf`, the current instant is used.
- **RH-A4** **`activeHolderCount` is `DISTINCT userId` over the returned rows**, and is not the row count. The member projection is assignment-based, carrying `assignmentId`.
- **RH-A5** **A holder whose user is Inactive is returned**, with `status` showing it. Nothing about `app_user.Status` removes a row.
- **RH-A6** A role with no holders answers `200`, an empty list and a count of zero.

**The route (RH3, RH8, RH9, RH12)**

- **RH-A7** **Both permissions are required and it is AND:** a caller with `role.read` and no `user.read` is refused; a caller with `user.read` and no `role.read` is refused; a caller with both succeeds. No carrier is `401`. **The refusal sentence is the same in both directions** and names neither permission.
- **RH-A8** An unknown role answers **`404`** with *"The role does not exist."*, and that body is what distinguishes it from an unmapped route.
- **RH-A9** A malformed `asOf` is `400`; **a supplied `scopeId` is `400`** with its own sentence.
- **RH-A10** **No audit record is written**, whatever the outcome.

**The declaration (RH13)**

- **RH-A11** A query declaring several permissions is accepted by registration and by start-up verification; **an empty set and a blank code anywhere in a set are both refused**, as a blank single code already is.
- **RH-A12** Every existing single-permission query is unchanged, and start-up verification still passes for all of them.

**The screen (RH10, RH11)**

- **RH-U1** The Holders section appears for a caller holding both permissions, and never when either is absent.
- **RH-U2** It shows a row per holder with its six columns, and the count from AUT-Q4 — not from the roles list.
- **RH-U3** A holder's display name links to that user's detail page, under an explicit `user.read` condition.
- **RH-U4** A role with no holders shows an empty state, not an empty table.
- **RH-U5** A failed read shows the server's sentence and a Try again that reads again, and leaves the Permissions section standing.
- **RH-U6** The section offers no action, and sends nothing but its read.
- **RH-U7** No accessibility violations on the page.

**Browser, in the dev stack (RH-U8),** with the owner's approval. As Ada:

1. Open a role with holders: the Holders section lists them with their dates and reasons, and the count matches the rows.
2. Follow a holder's link to the user detail page, and come back.
3. Open a role with no holders: the section shows its empty state and a count of zero.

### Implementation notes

- **One reader, shaped for REV-Q1 to reuse** (RH2). It returns stored facts only — no state, no count — exactly as `UserRoleAssignmentReader` does for AUT-Q2; the handler derives state once through `RoleAssignmentStates.At` and counts distinct holders from the rows it keeps. REV-Q1 later filters the same reader differently and authorises differently.
- **AUT-Q4 is AUT-Q2 transposed:** the same table, the same join to `app_user` for the granter's display name, the `where` moved from `UserId` to `RoleId`. The revoker join is not needed, because a revoked assignment is never returned.
- **The role is looked up first** and the handler returns null when it is absent; the route maps that to `404` with its sentence, as AUT-Q3's does. `asOf` projects the membership only once the role is known to exist.
- **`QueryAuthorization` gains a set** (RH13). `Required(string)` stays, so no existing query's declaration changes; `PermissionCodes` is the member both the handler and the verifier read, and the single-code factory produces a one-element set. `QueryAuthorizationVerification.ProblemWith` refuses an empty set and a blank code in any position.
- **The handler enforces every declared code**, refusing on the first that fails with a sentence naming none of them.
- **Why RH14's assertion can still be mutated.** A divergence between distinct holders and row count is unreachable through the command surface, because `GrantRole` writes only global assignments and the exclusion constraint forbids overlap. The persistence test that pins `DISTINCT userId` therefore constructs a second, scoped assignment **directly through the entity**, which `ex_user_role_scoped_no_overlap` permits beside a global one. **That test deliberately fabricates a state no command can produce**, and says so, so that a mutant replacing the distinct count with a row count is killed rather than surviving as equivalent.

### Not included

- **REV-Q1 GetAccessReviewReport** and anything under `accessreview.read`; **AUT-Q7 WhoCanDo**, which asks the reverse question and is its own gate.
- **Any assignment mutation** (RH11), and any change to AUT-C1/C2.
- **Any non-global scope behaviour** (RH3), and any scope member in the response.
- **Paging** (RH7), and any generic paged result.
- **Any reconciliation of the `400`/`404` divergence** with AUT-Q2 (RH8).
- **Any change to AUT-Q5**, whose `activeHolderCount` keeps its definition and stays on the roles list.
- **`actorType` in the response**, which waits for agents to exist.

---

## AUT-Q7 WhoCanDo — the reverse lookup, and point-in-time authorisation

**Status:** Contract frozen 2026-09-21 by owner decision (RW1–RW12). **Read-only.** No assignment, role, grant or catalogue entry changes, and no audit record is written.

### Requirement

A caller holding **both `role.read` and `user.read`** asks the reverse of the authorisation question: not *"may this actor do this?"* but *"who could do this, and why?"*, at an instant.

```text
AUT-Q7 WhoCanDo    all users, one permission, at one instant
```

The catalogue's example is the inspection question this exists to answer: *"Who could approve submissions for Product X in March?"*

### The central rule

> **AUT-Q7 evaluates authorisation through the shared authorisation resolver.** `asOf` is the evaluation instant for **every authorisation fact the model represents temporally**. The query **must not substitute current state for historical state merely because the current state is easier to obtain**. Where the model holds no historical representation of a fact, that limitation is **explicit** rather than silently presented as historical truth. User and identity status have no historical representation and are therefore **not** retroactively reconstructed by AUT-Q7. Full point-in-time reconstruction remains **REV-Q6's** responsibility.

### Which facts the model represents temporally

This table is the contract's foundation, and it was settled from the schema rather than assumed (RW3, RW4, RW9).

| Authorisation fact | Stored as | Historical? | AUT-Q7 resolves it |
| --- | --- | --- | --- |
| Assignment effective period | `user_role.effective_from` / `effective_to` | **Yes** | at `asOf` — already does |
| Assignment revocation | `user_role.revoked_at` (an instant) | **Yes** | at `asOf` — **changed** |
| Role-permission grant | `role_permission.granted_at` / `revoked_at` | **Yes** | at `asOf` — **changed** |
| Permission catalogue state | `permission.is_active` — a boolean, with only `created_at` and **no deactivation instant** | **No** | current state, stated (RW9) |
| User / identity status | `app_user.status`, `user_identity.status`, each tied to `deactivated_at` by a CHECK that makes the column **null whenever the row is Active** | **No** | current state, stated (RW4) |

**Why status is not a history.** The CHECK `("status" = 'Inactive') = ("deactivated_at" IS NOT NULL)` means reactivation (USR-C5) erases the timestamp. A user deactivated in February and reactivated in April carries **no trace of February** today. The column records the *current* deactivation, not a sequence, so *"was she active in March?"* is not answerable from these tables at any fidelity — and AUT-Q7 does not pretend otherwise.

### Two temporal defects this story fixes in the shared predicate

`AuthorizationService.Candidates` is the shared evaluation. Two of its conditions ignore the instant they are asked about:

```csharp
&& assignment.RevokedAt == null      // not "was it revoked by then?"
&& grant.RevokedAt == null           // and GrantedAt is not checked AT ALL
```

The grant condition is the sharper of the two: `Candidates` references `GrantedAt` **zero times**, so a grant made *after* `asOf` would count towards authority at `asOf`. AUT-Q3 already defines the correct predicate (RA5), and AUT-Q7 adopts it verbatim.

**Both changes are behaviour-preserving for every existing caller**, and this is checked rather than asserted: all **12** construction sites of `AuthorizationRequest` pass the current instant. At `now`, `revoked_at <= now` always holds (a revocation is stamped when it happens) and `granted_at <= now` always holds, so the narrowed and widened forms agree exactly. This is the same argument, and the same class of defect, as the `RoleAssignmentStates.At` fix that AUT-Q4 forced — in the SQL predicate rather than the domain derivation.

### The decisions

| # | Decision |
| --- | --- |
| **RW1** | **Build AUT-Q7 now**, as the first consumer requiring point-in-time authorisation evaluation — **not** as an implementation of REV-Q6. Deferring it would leave the shared evaluation unresolved while later queries already depend on its shape. |
| **RW2** | **Generalise the shared resolver; do not write a second authorisation reader.** Three views over one evaluation: `IsAllowedAsync` (one user, one permission), `EnumerateAsync` (one user, all permissions), `WhoCanDoAsync` (**all users, one permission**). **Actor eligibility becomes part of the shared evaluation** rather than a per-caller precondition — otherwise AUT-Q7 inevitably grows its own actor-status implementation, which is the drift the extracted predicate exists to prevent. |
| **RW3** | **Temporal consistency, not fabricated history.** The central rule above. Time domains are never silently mixed: a fact the model dates is resolved at `asOf`, and a fact it does not date is a current-state gate that the contract names. |
| **RW4** | **User and identity status are current-state gates, and are never retroactively reconstructed.** They remain part of who is authorised, stated explicitly as current state, and the limitation is recorded for REV-Q6 rather than solved with an invented status history. |
| **RW5** | **One row per user, with the authorising roles as a collection.** The catalogue asks for *users*, and `EnumerateAsync` already distincts them. A user who holds the permission through three roles is **one** row naming three roles — the answer to *"who can do this, and why?"* rather than a row per path. **Deliberately the opposite of RH5**, where AUT-Q4 keyed on the assignment because the dates and reason belonged to it. |
| **RW6** | **`role.read` AND `user.read`**, on the RH9 precedent, and **no third permission**. The model already distinguishes role administration from user visibility, and two codes express the boundary. The multi-permission classification built for RH13 exists for exactly this. |
| **RW7** | **`ScopeType` and `ScopeId` are kept and refused**, as RH3 did: both stay in the contract because the catalogue names them, and a non-Global scope is refused until the platform supports one. |
| **RW8** | **An unknown permission code is `404`** — *"The permission does not exist."* — following AUT-Q3 and AUT-Q4. A permission nobody holds is a different answer from a permission that does not exist, and for an inspection query that distinction is the point. |
| **RW9** | **Retired is not unknown.** A retired permission **exists**: it answers `200`, reports `isActive: false`, and returns **no holders**, because the shared predicate requires an active permission and AUT-Q7 does not resurrect one using today's flag. Historical reconstruction of a retired permission is **REV-Q6's**, once a historical catalogue model exists. |
| **RW10** | **API-only.** No Permissions page, as RG9 already decided for AUT-Q6. No existing screen's semantics require this; a later access-review surface consumes it. |
| **RW11** | **USR-Q3 is not pulled into this gate.** It shares the evaluation and nothing else: different subject, permission boundary, response and purpose. *"Do not duplicate the logic"* means reuse the evaluation, not implement three query contracts in one story. |
| **RW12** | **Not audited**, keeping RA-A11. A sensitive result does not by itself make a read an audit event; authorisation controls the disclosure, and introducing audit here would need its own contract. |

### The route and its shape

**My choice, not the catalogue's** — the query catalogue defines queries, not HTTP.

| Query | Route |
| --- | --- |
| **AUT-Q7** | `GET /api/permissions/{permissionCode}/holders?asOf=&scopeType=&scopeId=` |

A sub-resource of AUT-Q6's `/api/permissions`, as `/members` is of a role. The response:

```text
{ asOf, permission: { permissionId, code, name, isActive }, holders: [ … ] }
```

Each holder exactly:

```text
userId, displayName, email, status, roles: [ { roleId, name } ]
```

- **The permission is echoed, including `isActive`.** It is what makes RW9 legible: an empty list beside `isActive: false` says *"retired, so nobody"*, where an empty list beside `isActive: true` says *"nobody holds it"*. Without it the two are indistinguishable, and for an inspection query that would be a defect.
- **`asOf` is echoed**, as AUT-Q4 echoes it: it is the instant the whole answer was judged at.
- **No holder count.** AUT-Q4 needed one because its rows were assignments and could exceed the holders; here a row **is** a holder, so the array length is the count and a second number could only ever disagree with it.
- **`roles` names why**, and carries the role's **name as stored now**. The authorising role's historical name belongs to the audit record's `ActorSnapshot`, which exists precisely so renaming a role (AUT-C4) does not rewrite authority already recorded.
- **Ordered by `displayName`** under ICU `unicode`, then `userId`, as AUT-Q4, the user list and the roles list are ordered. `roles` within a holder are ordered by name under the same collation.
- **Parameters are parsed strictly**: `asOf` must be one ISO-8601 instant or `400`; `scopeType` other than `Global` is `400`; any `scopeId` is `400` (a Global assignment has no scope id). `permissionCode` is matched **exactly**, as AUT-Q6's `resource` filter is.
- **A carrier is required** (`401`), and **both `role.read` and `user.read`** (`400`), with **one sentence naming neither code**, as RH9 established.
- **A permission nobody holds answers `200` and an empty list; an unknown code answers `404`** with its sentence, produced by the route from the query result. `ProblemMiddleware`'s allowlist is untouched.
- **Not audited** (RW12).

### Acceptance Criteria

**The evaluation (RW2)**

- **RW-A1** Each holder is answered with exactly the five members above, ordered by display name, with `asOf` and the permission echoed.
- **RW-A2** **The three temporal facts resolve at `asOf`**, each proven separately: an assignment outside its period at `asOf` does not authorise; an assignment **revoked after `asOf`** *does*; a grant **revoked after `asOf`** *does*; and a grant **made after `asOf`** does **not**.
- **RW-A3** **The two current-state gates behave as current state**, and are asserted as such rather than left ambiguous: a user or identity inactive **now** is absent whatever `asOf` says, and a permission retired **now** yields no holders.
- **RW-A4** **One row per user.** A user holding the permission through three roles appears once, with three roles named; two users holding it through one role appear as two rows.
- **RW-A5** **The three views agree.** For the same user, permission and instant, `WhoCanDo` contains a user **iff** `IsAllowedAsync` allows them and **iff** `EnumerateAsync` lists the code — a regression test over the shared evaluation, which is what stops the third view drifting.
- **RW-A6** **`role.IsActive` is still absent from the predicate.** A holder of a deactivated role still appears, because AUT-C5 changes eligibility and never access. Generalisation must not quietly reintroduce the filter.
- **RW-A7** **UR10 still holds per actor:** a non-human actor never appears for a `RequiresHumanActor` permission, the third edge of the two-edge check.
- **RW-A8** **Existing callers are unchanged.** `IsAllowedAsync` and `EnumerateAsync` return exactly what they returned before the predicate was widened, for every case their suites already cover.

**The route (RW6, RW7, RW8, RW9, RW12)**

- **RW-A9** **Both permissions, and it is AND:** `role.read` without `user.read` is refused, `user.read` without `role.read` is refused, both succeed, no carrier is `401`, and **the refusal sentence is identical in both directions and names neither code**.
- **RW-A10** **An unknown permission code is `404`** with *"The permission does not exist."*; **a retired permission is `200`** with `isActive: false` and no holders; **a live permission nobody holds is `200`** with `isActive: true` and no holders. All three are distinguishable by the caller.
- **RW-A11** A malformed `asOf` is `400`; a `scopeType` other than `Global` is `400`; any `scopeId` is `400`.
- **RW-A12** **No audit record is written**, whatever the outcome.

### Implementation notes

- **`Candidates` gains two optional narrowings and loses one mandatory one.** `userId` becomes nullable — the same "null means do not narrow" convention `scopeType` and `permissionCode` already use — and **actor eligibility moves into the query** (RW2), joining `app_user` and `user_identity` rather than being resolved per caller beforehand. `EligibleActorTypeAsync` currently supplies `actorType` to the UR10 check from outside; once the actor is a row in the query, UR10 reads `app_user.actor_type` directly.
- **The two temporal corrections are made in the shared predicate**, not in AUT-Q7, so `IsAllowedAsync` and `EnumerateAsync` inherit them. They are behaviour-preserving at `now` (all 12 callers), and RW-A8 pins that.
- **The grant predicate is AUT-Q3's, verbatim:** `GrantedAt <= asOf && (RevokedAt == null || asOf < RevokedAt)` (RA5). One definition of "live at an instant", used by both.
- **The permission is looked up first**, so `404` and "nobody holds it" stay distinguishable, exactly as AUT-Q3 and AUT-Q4 do it.
- **Grouping is in memory**, over rows already narrowed to one permission: the set is bounded by the tenant's holders of a single permission, and no paging is introduced (RW10 keeps this API-only).
- **`QueryAuthorization.Required("role.read", "user.read")`**, the second use of the multi-permission classification built for RH13. The handler enforces every declared code.

### Not included

- **REV-Q6 GetPointInTimeAccess** and any historical model for the permission catalogue or for user/identity status (RW3, RW4, RW9).
- **USR-Q3 GetUserAccessSummary** (RW11), which reuses the evaluation in its own story.
- **REV-Q1** and anything under `accessreview.read`.
- **Any screen** (RW10), and any change to AUT-Q6's response.
- **Any non-global scope behaviour** (RW7), and any scope inheritance — a Global assignment still does not imply narrower scopes.
- **Any change to `role.IsActive`'s absence** from the predicate (RW-A6), to UR10, or to the audit boundary (RW12).

---

# Known Gaps and Deliberate Deferrals

Things the code knowingly does not do yet. An agent that encounters one of these should **not** "fix" it inside an unrelated story and should **not** report it as a defect — cite this section instead. Remove an entry when the deferral is closed.

## The frozen documents do not define RO1-RO4

The user management entity workbook cites `RO1`, `RO2`, `RO3` and `RO4` against
the `role` entity and says "Full definitions in Part E of the frozen document".
The frozen specification in that folder — *User Management, Design
Specification, v2.1* — **has no Part E**; its sections end at Appendix A.

So every RO rule this project has relied on comes from the workbook's one-line
glosses, not from a definition: `RO1` unique code, `RO2` a code immutable once
referenced, `RO3` tenants cannot modify or deactivate system roles, `RO4` retire
by `IsActive = false` rather than deletion. The implementations are deliberately
**stricter** than the glosses where they differ — `role.code` is immutable from
creation, not from first reference — and that is recorded at each site.

Found while gathering evidence for the AUT-C4 gate. Nothing is blocked; an agent
should cite the gloss and the site, and should not infer a rule the gloss does
not state.

## The specification's event list omits the role-definition events

Section 7.1 of the frozen specification lists, for authorisation: role granted,
role revoked, permission granted to role, permission revoked from role.
`RoleCreated`, `RoleUpdated`, `RoleDeactivated` and `RoleReactivated` appear
only in the command catalogue, which is where AUT-C3 through AUT-C6 take them
from — and all four are seeded in `AuditEventCatalogue`.

AUT-C3 shipped against this asymmetry and AUT-C4 does too. The general
obligation the specification does state is invariant 16, that every lifecycle
action produces an audit event carrying an actor snapshot, which these satisfy.
The two documents disagree about completeness, not about content.

## A grant can race a role's deactivation

`GrantRoleCommandHandler` reads the role with `FindAsync` — no row lock — while
locking only the **target user** row (D6). `DeactivateRoleCommandHandler` takes
no lock either (RD9). So a grant that has passed its eligibility check can
commit against a role another transaction is retiring, leaving a fresh
assignment on an inactive role.

**Deliberately tolerated, not overlooked.** The consequence is small and
self-correcting in the direction that matters: the assignment is valid and its
holder has access, which is exactly what AUT-C5 guarantees every existing
holder anyway. Nothing is stranded and nothing is silently revoked. Locking the
role on the grant path would add contention to the hottest write in the
authorisation model to prevent an outcome the model already accepts.

Recorded at the AUT-C5/C6 gate (RD9). If a later requirement makes a fresh
assignment on a retired role genuinely wrong, the fix belongs with that
requirement, and it is a lock on the role in both commands.

## RP6 cannot be guaranteed atomically across commands

**Recorded by owner decision at the AUT-C7/C8 gate (RG2).**

AUT-C7 enforces RP6 as an application-level precondition inside its own
transaction. It reads `user_role` to look for an active agent assignment;
`GrantRole` writes `user_role`. The two transactions share **no row and no
constraint**: `GrantRole` locks only the target's `app_user` row (D6), AUT-C7
locks nothing, isolation is Read Committed, and RP6 spans two tables so no
index or exclusion constraint can express it.

> **The honest statement of the guarantee:** RP6 is enforced as an
> application-level precondition, but concurrent `GrantRole` and AUT-C7
> operations do not share a serialisation point, so the application cannot
> guarantee RP6 atomically under concurrent writes.

**Deliberately tolerated.** Closing it means locking the role in **both**
commands, which amends AUT-C1's frozen concurrency contract (RD9, RM5) to
prevent a race that **cannot occur in v1**: agents cannot be created
(invariant 17a, AU11), so no agent assignment can be written concurrently with
anything. The remedy, if a later slice makes it reachable, is a role lock in
both commands — the same remedy already recorded for the grant/deactivation
race, and it belongs to the slice that makes agents real.

## RP6 does not cover a future-dated agent assignment

**Recorded by owner decision at the AUT-C7/C8 gate (RG3), as a parked contract
amendment.**

The frozen catalogue defines the prohibited state as *"ANY **active** Agent
assignment"*, and AUT-C7 implements that literally. Every definition of
"active" in this codebase agrees: `revoked_at IS NULL AND effective_from <= now
< effective_to`. A **future-dated** agent assignment is therefore not active,
passes AUT-C7's check, and then becomes effective at its own `effective_from`
— producing the mixed role the catalogue forbids, **with no command running**.

No locking closes this; only widening the rule does. It is **not widened here**,
because the other commands' "live" semantics are not grounds to redefine a
frozen invariant while implementing against it.

**The decision to take:** should a future-dated agent assignment block granting
a `RequiresHumanActor` permission, even though RP6 defines the prohibited
assignment as active? If yes, RP6's wording changes and AUT-C7 follows it.
Unreachable in v1 either way, since agents cannot be created.

## AUT-C7's cache invalidation is not applicable

**Recorded at the AUT-C7/C8 gate.** The command catalogue's step 5 for AUT-C7
is *"Emit audit + invalidate … invalidate effective permissions for ALL holders
of this role"*, and AUT-Q1's note says to cache the resolver and invalidate on a
role-permission change.

**There is no cache.** No `IMemoryCache`, no distributed cache, nothing in
`src/`, and the word does not appear in the frozen specification at all — it
is a command-catalogue and delivery-slice aspiration. No cache infrastructure
was created to satisfy it. If one is introduced, invalidation becomes a
declared consequence of AUT-C7, AUT-C8, AUT-C1, AUT-C2 and the user lifecycle
commands together, which is that story's work and not this one's.

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

**State:** resolved. `src/Tools/SKSMCorp.Provisioning` runs PRV-C1 and PRV-C3
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
contract; `src/Host/SKSMCorp.Host` issues and verifies the carrier and
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

## MustChangePassword is recorded but not enforced — RESOLVED

**State:** resolved by *SES-C1 — enforcing MustChangePassword at sign-in* (MC1–MC9). While the flag is outstanding, the password on file cannot establish a session; a reset link (CRD-C3), or a change from a session that already existed (CRD-C4), clears it. What follows is the gap as it was recorded.

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

**State:** resolved. `SKSMCorp.CatalogueSync` runs as `migration_role` between
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

**State:** the project file is `SKSMCorp.Sharedkernel.csproj` (lower-case `k`) while `SKSMCorp.Platform.Domain.csproj` and `SKSMCorp.Platform.Application.csproj` reference `SKSMCorp.SharedKernel.csproj`. Case-insensitive filesystems resolve it; Linux will not.
**Intended fix:** rename the file to match the references. Trivial, but touches the solution file; do it as its own commit.

---

# Traceability

```text
Requirement ID → Story → Implementation plan → Branch → Commit(s) → Pull Request → Owner approval → Merge
```

## Invisible format characters in local usernames are not prohibited

**Recorded by owner decision (WS9 of *Local usernames refuse surrounding whitespace*, 2026-09-19).** The surrounding-whitespace rule is deliberately `char.IsWhiteSpace` and nothing more. Invisible Unicode format characters such as U+200B ZERO WIDTH SPACE and U+FEFF are not whitespace to it, so `"\u200Bada"` is a valid username that looks like `ada` but never matches it.

**This is not a whitespace defect.** Prohibiting such characters is a username-character policy, and no specification defines one. It is decided, if ever, with username format rules.

## USR-C3 self-service email change is not built

**Recorded by owner decision (CE1 and CE2 of *USR-C3 ChangeUserEmail*, 2026-09-19).** A3 (c) makes a user's change of their own address subject to verification: pending until confirmed through a token sent to the new address. Only the administrator command exists. A self-service command, its permission, an `EmailVerification` notification type and the verification flow are a separate story. Until then the catalogue's *"Admin or self"* row is outstanding change control, as USR-C2's is.

## No notice is sent when an email address changes

**Recorded by owner decision (CE10).** USR-C3 tells neither the old address nor the new one. Non-secret notifications are not V1 (Notification specification, §1). The change is visible in the audit trail as `UserEmailChanged`.

## CRD-C7 does not serialise against USR-C3

**Recorded by owner decision (CE9).** CRD-C7 Resend activation does not currently serialise against USR-C3 email changes, so an activation token could be issued to the previous address during the race. USR-C3 takes the target's row lock; CRD-C7 does not. Closing this is its own concurrency-hardening story: CRD-C7 taking the same D6 lock. USR-C3 does not change CRD-C7.

## Unknown resources answer 400 in one read and 404 in another

**Recorded by owner decision (RA11 of *Role administration read*, 2026-09-20).** AUT-Q3 answers `404` *"The role does not exist."* for an unknown role, because an empty list must keep meaning "this role has no grants". AUT-Q2, the sibling read, answers `400` *"The user does not exist."* for an unknown user, as every other read and command in the platform does — `ProblemMiddleware` maps refusals to `400`.

**Deferred:** whether unknown-resource reads answer `404` everywhere, which would touch AUT-Q2, USR-Q1 and the middleware's allowlist, and which interacts with the Known Gap *Authorization failures are not distinguishable from validation failures*. AUT-Q3's `404` is deliberate and is not to be "made consistent" inside an unrelated story.

## AUT-C7 may grant a permission to a release-owned role

**Recorded by owner decision (PRV-C2 Amendment 1, 2026-09-20).** Neither the command catalogue's AUT-C7 row nor `RolePermission.Create` prevents an administrator adding a permission to a **system** role. Catalogue synchronisation would then refuse every later deployment with `GrantMissingFromSeed`, which is the correct behaviour for release-owned authorization state (PRV5) — but it means a single application command can stop deployments.

**Deferred to AUT-C7's own gate:** whether AUT-C7 and AUT-C8 refuse system roles outright, as the domain already refuses to modify their metadata (RO3). Nothing is to be relaxed in the synchroniser to accommodate such a grant.
