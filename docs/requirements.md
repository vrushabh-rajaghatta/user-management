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

**Deferred to:** its own story, before the first tenant.

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
