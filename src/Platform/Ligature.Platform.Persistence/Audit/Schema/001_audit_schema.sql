-- =====================================================================
-- 001_audit_schema.sql
--
-- The five Audit tables.
--
-- Implements: Audit Entity Workbook v0.3 (AR, AE, ET, EO, RT constraint
--             families); Audit Design Specification section 13.3.
-- Applied by: Ligature.AuditSchema, under a privileged connection that has
--             already issued SET ROLE audit_owner. Every object below is
--             therefore OWNED BY audit_owner from the moment it is created.
--             Ownership is never transferred, because a transfer implies an
--             interval in which something else owned the trail.
--
-- Why this is not an EF migration: whoever runs CREATE TABLE owns the table,
-- and an owner can disable triggers and delete rows regardless of any GRANT.
-- EF migrations run as migration_role, so an EF-created audit.audit_record would be
-- owned — and therefore mutable — by the migration credential. See
-- docs/architecture.md, "Audit schema ownership".
--
-- Constraint names carry their model id (ar17, ae5, et3 ...). The rest of the
-- schema does not do this, but every rule here has a formal identifier in a
-- frozen artifact, and the negative test for each constraint asserts on the
-- name. It makes the DDL mechanically checkable against the workbook.
-- =====================================================================


-- ---------------------------------------------------------------------
-- audit.audit_event_type — the release-controlled event catalogue (ET1-ET5)
--
-- Seeded by AUD-C4/AUD-C3, not by this script: the catalogue is release
-- DATA, and this script is structure. AUD-S01 tests supply their own
-- fixture rows.
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS audit.audit_event_type
(
    code                       varchar(200) NOT NULL,
    version                    integer      NOT NULL,
    owning_context             varchar(50)  NOT NULL,
    name                       varchar(200) NOT NULL,
    description                text         NULL,
    default_classification     varchar(50)  NOT NULL,
    reason_required            boolean      NOT NULL,
    write_path                 varchar(20)  NOT NULL,
    shape                      varchar(20)  NOT NULL,
    primary_entity_type        varchar(100) NULL,
    primary_entity_required    boolean      NOT NULL,
    entity_ref_roles           jsonb        NOT NULL,
    pii_paths                  jsonb        NOT NULL,
    payload_schema_ref         varchar(200) NULL,
    is_active                  boolean      NOT NULL,

    CONSTRAINT pk_audit_event_type_et1
        PRIMARY KEY (code, version),

    CONSTRAINT ck_audit_event_type_et2_owning_context
        CHECK (owning_context IN
            ('UserManagement', 'Audit', 'Platform', 'DMS', 'Workflow', 'Submission')),

    CONSTRAINT ck_audit_event_type_et3_classification
        CHECK (default_classification IN
            ('SecurityEvent', 'IdentityLifecycle', 'AuthorisationChange',
             'ConfigurationChange', 'RegulatedRecordChange', 'WorkflowTransition',
             'AgentAction', 'AuditAdministration', 'Provisioning', 'Refusal')),

    CONSTRAINT ck_audit_event_type_et3_write_path
        CHECK (write_path IN ('Transactional', 'Autonomous')),

    CONSTRAINT ck_audit_event_type_et3_shape
        CHECK (shape IN ('BeforeAfter', 'Payload', 'None')),

    -- ET3: a primary entity type is required exactly when the catalogue says
    -- a primary entity is required.
    CONSTRAINT ck_audit_event_type_et3_primary_entity
        CHECK (primary_entity_required = false OR primary_entity_type IS NOT NULL)
);


-- ---------------------------------------------------------------------
-- audit.audit_event_origin — permitted origin kinds per event type (EO1-EO3, EO7)
--
-- This table is the whole of AUD-5: a record with no actor can only exist
-- for an event type that declares 'Anonymous'. audit.audit_record's composite FK
-- (AR5) is what makes that declarative rather than a code path.
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS audit.audit_event_origin
(
    code        varchar(200) NOT NULL,
    version     integer      NOT NULL,
    origin_kind varchar(20)  NOT NULL,
    is_active   boolean      NOT NULL,

    CONSTRAINT pk_audit_event_origin_eo3
        PRIMARY KEY (code, version, origin_kind),

    CONSTRAINT fk_audit_event_origin_eo1_event_type
        FOREIGN KEY (code, version)
        REFERENCES audit.audit_event_type (code, version),

    CONSTRAINT ck_audit_event_origin_eo2_origin_kind
        CHECK (origin_kind IN ('Authenticated', 'System', 'Anonymous'))
);


-- ---------------------------------------------------------------------
-- audit.audit_record — one row per audit event (AR1-AR27)
--
-- There is no status, no is_deleted, no updated_at. Nothing here is ever
-- updated except by the controlled anonymisation path, which sets
-- anonymisation_audit_id once and transforms the declared PII columns.
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS audit.audit_record
(
    -- Identity and order (AR1, AR2)
    audit_id                    uuid         NOT NULL,
    sequence                    bigint       GENERATED BY DEFAULT AS IDENTITY,

    -- Three clocks (AR3). Deliberately NO check orders them: skew is
    -- reported by AUD-Q9 and never corrected, so neither fact is corrupted.
    occurred_at                 timestamptz  NOT NULL,
    captured_at                 timestamptz  NOT NULL,
    created_at                  timestamptz  NOT NULL DEFAULT now(),

    -- Catalogue capture (AR4-AR9). Copied at write time, never joined at
    -- read time: the record states the rule in force when it was written.
    event_type                  varchar(200) NOT NULL,
    event_version               integer      NOT NULL,
    write_path                  varchar(20)  NOT NULL,
    regulatory_classification   varchar(50)  NOT NULL,
    reason_required             boolean      NOT NULL,
    reason                      text         NULL,

    -- Actor snapshot (AR10-AR13, AR24), persisted exactly as delivered.
    actor_user_id               uuid         NULL,
    actor_type                  varchar(20)  NULL,
    actor_display_name          varchar(200) NULL,
    actor_username              varchar(200) NULL,
    actor_email                 varchar(320) NULL,
    actor_identity_provider     varchar(100) NULL,
    actor_subject_id            varchar(200) NULL,
    authorizing_role_id         uuid         NULL,
    authorizing_role_name       varchar(200) NULL,
    authorizing_scope_type      varchar(50)  NULL,
    authorizing_scope_id        uuid         NULL,
    authorizing_assignment_id   uuid         NULL,
    on_behalf_of                jsonb        NULL,
    agent_context               jsonb        NULL,
    platform_access_ref         varchar(200) NULL,
    actor_captured_at           timestamptz  NULL,

    -- AR6: derived from the snapshot, stored so that AR5 can be a foreign
    -- key. A stored generated column is permitted on the referencing side of
    -- an FK provided the constraint carries no referential action that would
    -- write it, which is why AR5 below is NO ACTION.
    origin_kind                 varchar(20)
        GENERATED ALWAYS AS (
            CASE
                WHEN actor_user_id IS NULL   THEN 'Anonymous'
                WHEN actor_type = 'System'   THEN 'System'
                ELSE                              'Authenticated'
            END) STORED,

    -- Primary entity (AR14)
    entity_type                 varchar(100) NOT NULL,
    entity_id                   uuid         NULL,

    -- Correlation (AR15, AR16)
    operation_id                uuid         NOT NULL,
    causation_id                uuid         NULL,

    -- Content (AR17)
    before                      jsonb        NULL,
    after                       jsonb        NULL,
    payload                     jsonb        NULL,

    -- Anonymisation linkage (AR18). The only column any role may write
    -- after insert, and only NULL -> value, once.
    anonymisation_audit_id      uuid         NULL,

    CONSTRAINT pk_audit_record_ar1
        PRIMARY KEY (audit_id),

    CONSTRAINT uq_audit_record_ar2_sequence
        UNIQUE (sequence),

    CONSTRAINT fk_audit_record_ar4_event_type
        FOREIGN KEY (event_type, event_version)
        REFERENCES audit.audit_event_type (code, version),

    -- AR5 — the constraint that makes "anonymous only where declared" a
    -- database rule. NO ACTION is required: a referential action would have
    -- to write origin_kind, which is generated.
    CONSTRAINT fk_audit_record_ar5_event_origin
        FOREIGN KEY (event_type, event_version, origin_kind)
        REFERENCES audit.audit_event_origin (code, version, origin_kind)
        ON UPDATE NO ACTION ON DELETE NO ACTION,

    CONSTRAINT ck_audit_record_ar7_write_path
        CHECK (write_path IN ('Transactional', 'Autonomous')),

    CONSTRAINT ck_audit_record_ar8_classification
        CHECK (regulatory_classification IN
            ('SecurityEvent', 'IdentityLifecycle', 'AuthorisationChange',
             'ConfigurationChange', 'RegulatedRecordChange', 'WorkflowTransition',
             'AgentAction', 'AuditAdministration', 'Provisioning', 'Refusal')),

    CONSTRAINT ck_audit_record_ar9_reason
        CHECK (NOT reason_required OR reason IS NOT NULL),

    CONSTRAINT ck_audit_record_ar10_actor_type
        CHECK (actor_type IN ('Human', 'Agent', 'System', 'PlatformOperator')),

    -- AR24 — the snapshot group is all-present or all-absent. A partial
    -- snapshot is rejected outright rather than stored and puzzled over.
    CONSTRAINT ck_audit_record_ar24_snapshot_group
        CHECK (
            (actor_user_id IS NULL AND actor_type IS NULL
             AND actor_display_name IS NULL AND actor_captured_at IS NULL)
         OR (actor_user_id IS NOT NULL AND actor_type IS NOT NULL
             AND actor_display_name IS NOT NULL AND actor_captured_at IS NOT NULL)),

    -- AR11 — email is descriptive personal data that only humans have.
    CONSTRAINT ck_audit_record_ar11_email_human_only
        CHECK (actor_email IS NULL
            OR actor_type IS NOT DISTINCT FROM 'Human'),

    -- AR11 — a non-system actor was vouched for by some identity provider.
    CONSTRAINT ck_audit_record_ar11_identity_present
        CHECK (
            actor_user_id IS NULL
         OR actor_type = 'System'
         OR (actor_identity_provider IS NOT NULL AND actor_subject_id IS NOT NULL)),

    -- AR12 — the authorising role group is both-or-neither, and a global
    -- scope carries no scope id.
    CONSTRAINT ck_audit_record_ar12_role_group
        CHECK (
            (authorizing_role_id IS NULL AND authorizing_role_name IS NULL
             AND authorizing_scope_type IS NULL)
         OR (authorizing_role_id IS NOT NULL AND authorizing_role_name IS NOT NULL
             AND authorizing_scope_type IS NOT NULL)),

    CONSTRAINT ck_audit_record_ar12_global_scope
        CHECK (authorizing_scope_id IS NULL
            OR (authorizing_scope_type IS NOT NULL
                AND authorizing_scope_type <> 'Global')),

    -- AR13 — delegation is reserved and unused in V1.
    CONSTRAINT ck_audit_record_ar13_on_behalf_of
        CHECK (on_behalf_of IS NULL),

    CONSTRAINT ck_audit_record_ar13_agent_context
        CHECK (agent_context IS NULL
            OR actor_type IS NOT DISTINCT FROM 'Agent'),

    -- AR13 — the platform access reference is present exactly when the actor
    -- is a platform operator. It is the only correlation to the platform
    -- trail, and no foreign key crosses a database.
    CONSTRAINT ck_audit_record_ar13_platform_access_ref
        CHECK ((actor_type IS NOT DISTINCT FROM 'PlatformOperator')
             = (platform_access_ref IS NOT NULL)),

    CONSTRAINT fk_audit_record_ar16_causation
        FOREIGN KEY (causation_id) REFERENCES audit.audit_record (audit_id),

    -- AR17 — a record is a state pair or a payload, never both.
    CONSTRAINT ck_audit_record_ar17_shape
        CHECK (payload IS NULL OR (before IS NULL AND after IS NULL)),

    -- AR18 — deferred, because the AuditAnonymised record that these rows
    -- point at is inserted after the rows are updated, in the same
    -- transaction.
    CONSTRAINT fk_audit_record_ar18_anonymisation
        FOREIGN KEY (anonymisation_audit_id) REFERENCES audit.audit_record (audit_id)
        DEFERRABLE INITIALLY DEFERRED,

    -- AR27 — the operator exception to AUD-17 cannot be widened to tenant
    -- readers by a pipeline defect.
    CONSTRAINT ck_audit_record_ar27_inspected_operator_only
        CHECK (event_type <> 'AuditInspected'
            OR actor_type IS NOT DISTINCT FROM 'PlatformOperator')
);


-- ---------------------------------------------------------------------
-- audit.audit_entity_ref — every entity an event involved beyond its primary
-- entity, with the role it played (AE1, AE2, AE6)
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS audit.audit_entity_ref
(
    audit_id    uuid         NOT NULL,
    entity_type varchar(100) NOT NULL,
    entity_id   uuid         NOT NULL,
    ref_role    varchar(100) NOT NULL,

    CONSTRAINT pk_audit_entity_ref_ae2
        PRIMARY KEY (audit_id, entity_type, entity_id, ref_role),

    CONSTRAINT fk_audit_entity_ref_ae1_record
        FOREIGN KEY (audit_id) REFERENCES audit.audit_record (audit_id)
);


-- ---------------------------------------------------------------------
-- audit.audit_retention_policy — versioned, append-only retention floor
-- (RT1-RT5)
--
-- V1 defines purge ELIGIBILITY only. There is no purge command and no role
-- holds DELETE on the trail.
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS audit.audit_retention_policy
(
    id                        uuid        NOT NULL,
    policy_version            integer     NOT NULL,
    effective_from            timestamptz NOT NULL,
    minimum_retention_months  integer     NOT NULL,
    reason                    text        NOT NULL,
    created_at                timestamptz NOT NULL,
    created_by                uuid        NOT NULL,

    CONSTRAINT pk_audit_retention_policy_rt1
        PRIMARY KEY (id),

    CONSTRAINT uq_audit_retention_policy_rt2_version
        UNIQUE (policy_version),

    CONSTRAINT uq_audit_retention_policy_rt2_effective_from
        UNIQUE (effective_from),

    CONSTRAINT ck_audit_retention_policy_rt3_months
        CHECK (minimum_retention_months > 0)
);


-- ---------------------------------------------------------------------
-- Cross-module foreign keys (AR10, AR12)
--
-- These reference User Management tables, which are owned by
-- migration_role. Creating them requires REFERENCES on the target, which
-- the privileged deployer grants to audit_owner before it hands over.
--
-- docs/architecture.md section 9 says a module must not read or write
-- another module's tables. A foreign key is neither: it is a structural
-- assertion, checked by the database, that the actor named on a record was
-- a real actor of that exact type at the moment the row was written. AR10
-- is the reason the workbook gives for it, and the alternative — trusting
-- the pipeline to have looked — is the thing the model refuses everywhere
-- else. Recorded here because it is the only place Audit's DDL depends on
-- another module's schema.
--
-- Added as ALTER TABLE rather than inline so the dependency is visible in
-- one block instead of scattered through the column list.
-- ---------------------------------------------------------------------

-- AR10 — proves (actor_user_id, actor_type) matched app_user at write time.
-- Possible only because AU8 gives app_user a unique key on (id, actor_type)
-- and actor_type is immutable there.
ALTER TABLE audit.audit_record
    DROP CONSTRAINT IF EXISTS fk_audit_record_ar10_actor;

ALTER TABLE audit.audit_record
    ADD CONSTRAINT fk_audit_record_ar10_actor
    FOREIGN KEY (actor_user_id, actor_type)
    REFERENCES public.app_user (id, actor_type);

-- AR12 — the role and assignment that authorised the action.
ALTER TABLE audit.audit_record
    DROP CONSTRAINT IF EXISTS fk_audit_record_ar12_role;

ALTER TABLE audit.audit_record
    ADD CONSTRAINT fk_audit_record_ar12_role
    FOREIGN KEY (authorizing_role_id) REFERENCES public.role (id);

ALTER TABLE audit.audit_record
    DROP CONSTRAINT IF EXISTS fk_audit_record_ar12_assignment;

ALTER TABLE audit.audit_record
    ADD CONSTRAINT fk_audit_record_ar12_assignment
    FOREIGN KEY (authorizing_assignment_id) REFERENCES public.user_role (id);


-- ---------------------------------------------------------------------
-- Indexes (AR22, AE6) — each is named by the inspection query that relies
-- on it, so a query plan regression has an obvious owner.
-- ---------------------------------------------------------------------

-- AUD-Q1 GetActorActivity
CREATE INDEX IF NOT EXISTS ix_audit_record_ar22_actor
    ON audit.audit_record (actor_user_id, occurred_at);

-- AUD-Q2 GetEntityHistory (primary half)
CREATE INDEX IF NOT EXISTS ix_audit_record_ar22_entity
    ON audit.audit_record (entity_type, entity_id, occurred_at);

-- AUD-Q3 GetOperation
CREATE INDEX IF NOT EXISTS ix_audit_record_ar22_operation
    ON audit.audit_record (operation_id);

-- AUD-Q4 GetCausalChain
CREATE INDEX IF NOT EXISTS ix_audit_record_ar22_causation
    ON audit.audit_record (causation_id);

-- AUD-Q7 GetSignInHistory, AUD-Q8 GetRefusals
CREATE INDEX IF NOT EXISTS ix_audit_record_ar22_event_type
    ON audit.audit_record (event_type, occurred_at);

-- AUD-Q5 ExportAuditTrail, AUD-Q9 GetClockSkewReport
CREATE INDEX IF NOT EXISTS ix_audit_record_ar22_occurred_at
    ON audit.audit_record (occurred_at);

-- AUD-Q10 GetPlatformAccessActivity. Partial, so the index covers operator
-- rows only rather than every row in the trail.
CREATE INDEX IF NOT EXISTS ix_audit_record_ar22_platform_access
    ON audit.audit_record (platform_access_ref, occurred_at)
    WHERE platform_access_ref IS NOT NULL;

-- AUD-Q2 GetEntityHistory (referenced half), AUD-Q11 GetDataSubjectRecords
CREATE INDEX IF NOT EXISTS ix_audit_entity_ref_ae6_entity
    ON audit.audit_entity_ref (entity_type, entity_id);
