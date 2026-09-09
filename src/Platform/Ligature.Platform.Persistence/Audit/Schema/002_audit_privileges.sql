-- =====================================================================
-- 002_audit_privileges.sql
--
-- The tamper boundary: privileges and triggers.
--
-- Implements: AG1, AR19-AR23, AE7, ET7, EO4, RT6, RT9; Audit Design
--             Specification sections 13.1, 13.2, 13.4.
-- Applied by: Ligature.AuditSchema, as audit_owner (SET ROLE already
--             issued). audit_owner owns every object here, so it is the
--             only identity that may grant on them.
--
-- The proposition this file exists to make true:
--
--   No non-superuser role that participates in application, migration,
--   provisioning or anonymisation execution can alter or delete a
--   committed audit record.
--
-- Three mechanisms, each of which must hold on its own:
--
--   1. PRIVILEGE   app_role is granted SELECT and INSERT. Not UPDATE, not
--                  DELETE. No role anywhere is granted DELETE.
--   2. OWNERSHIP   audit_owner is NOLOGIN with no members, so the object
--                  powers that no GRANT can remove — DISABLE TRIGGER,
--                  DROP, self-GRANT — have no reachable holder.
--   3. TRIGGER     ENABLE ALWAYS, so the guard fires even under
--                  session_replication_role = replica, which is otherwise
--                  a superuser's one-line route around it.
--
-- A cluster superuser can still defeat all of this, because PostgreSQL
-- places no constraint on a superuser. What (3) removes is the ability to
-- do so by flipping a session setting: it now requires an explicit DDL
-- statement against the trail.
-- =====================================================================


-- ---------------------------------------------------------------------
-- Reaching the schema at all.
--
-- USAGE is granted by audit_owner because audit_owner owns the schema —
-- which is the point of the schema existing. DROP TABLE permits the table
-- owner, THE SCHEMA OWNER, or a superuser, so audit objects sitting in a
-- schema owned by anyone else would be droppable by that owner regardless
-- of who owns the tables. In `public`, whose owner is the database owner,
-- migration_role could and did destroy audit tables it did not own.
-- ---------------------------------------------------------------------
GRANT USAGE ON SCHEMA audit
    TO app_role, migration_role, provisioning_role, audit_anonymiser;


-- ---------------------------------------------------------------------
-- Start from nothing. PUBLIC is a real grantee and the default ACL is
-- not always empty; saying so explicitly costs one statement.
-- ---------------------------------------------------------------------
REVOKE ALL ON audit.audit_record            FROM PUBLIC;
REVOKE ALL ON audit.audit_entity_ref        FROM PUBLIC;
REVOKE ALL ON audit.audit_event_type        FROM PUBLIC;
REVOKE ALL ON audit.audit_event_origin      FROM PUBLIC;
REVOKE ALL ON audit.audit_retention_policy  FROM PUBLIC;


-- ---------------------------------------------------------------------
-- app_role — the application runtime (AR19, AE7, ET7, EO4)
--
-- Append and read. Nothing else. The identity column's sequence needs no
-- separate USAGE grant: for GENERATED ... AS IDENTITY, PostgreSQL derives
-- the right from INSERT on the table.
-- ---------------------------------------------------------------------
GRANT SELECT, INSERT ON audit.audit_record           TO app_role;
GRANT SELECT, INSERT ON audit.audit_entity_ref       TO app_role;
GRANT SELECT         ON audit.audit_event_type       TO app_role;
GRANT SELECT         ON audit.audit_event_origin     TO app_role;

-- AUD-C2 creates retention versions through the application, so INSERT
-- here is the application's. RT6 below makes the rows immutable anyway.
GRANT SELECT, INSERT ON audit.audit_retention_policy TO app_role;


-- ---------------------------------------------------------------------
-- audit_anonymiser — the erasure worker, and nothing else (AR20)
--
-- Column-level UPDATE on exactly the AR20 set. Every other column is
-- outside any role's UPDATE privilege, so there is nothing to forbid
-- because there is nothing to permit.
--
-- NOLOGIN in AUD-S01: the role and its grants exist so the boundary is
-- complete and testable, but no credential can authenticate as it until
-- the erasure worker is built. IMPL-11 requires the application not to
-- hold that credential; right now nobody does.
-- ---------------------------------------------------------------------
GRANT SELECT, INSERT ON audit.audit_record     TO audit_anonymiser;
GRANT SELECT, INSERT ON audit.audit_entity_ref TO audit_anonymiser;
GRANT SELECT ON audit.audit_event_type         TO audit_anonymiser;
GRANT SELECT ON audit.audit_event_origin       TO audit_anonymiser;

GRANT UPDATE (
    actor_display_name,
    actor_username,
    actor_email,
    before,
    after,
    payload,
    anonymisation_audit_id
) ON audit.audit_record TO audit_anonymiser;


-- ---------------------------------------------------------------------
-- migration_role and provisioning_role — the catalogue only
--
-- Neither holds anything at all on audit.audit_record or audit.audit_entity_ref.
-- AUD-C3 (release) and AUD-C4 (provisioning) write the catalogue and the
-- first retention version; neither writes the trail.
-- ---------------------------------------------------------------------
GRANT SELECT, INSERT, UPDATE ON audit.audit_event_type   TO migration_role;
GRANT SELECT, INSERT, UPDATE ON audit.audit_event_origin TO migration_role;

GRANT SELECT, INSERT ON audit.audit_event_type       TO provisioning_role;
GRANT SELECT, INSERT ON audit.audit_event_origin     TO provisioning_role;
GRANT SELECT, INSERT ON audit.audit_retention_policy TO provisioning_role;

-- ET9 / EO7 — types and origins are retired by IsActive, never removed,
-- because historical records hold the foreign keys. No DELETE is granted
-- here to anybody, which is what makes AUD-C3's guard a backstop rather
-- than the only control.


-- ---------------------------------------------------------------------
-- AR21 / AR23 — the only permitted UPDATE is the anonymisation
-- transition
--
-- Four conditions, all required. (d) is separate and deferred, below.
-- ---------------------------------------------------------------------
CREATE OR REPLACE FUNCTION audit.audit_record_guard() RETURNS trigger AS $$
BEGIN
    -- (c) only the anonymiser may reach this statement at all
    IF current_user <> 'audit_anonymiser' THEN
        RAISE EXCEPTION
            'AR21c: % may not update audit.audit_record', current_user
            USING ERRCODE = 'raise_exception';
    END IF;

    -- (a) the transition is NULL -> value, once and only once
    IF NOT (OLD.anonymisation_audit_id IS NULL
            AND NEW.anonymisation_audit_id IS NOT NULL) THEN
        RAISE EXCEPTION
            'AR21a: anonymisation_audit_id must transition from NULL to a value'
            USING ERRCODE = 'raise_exception';
    END IF;

    -- (b) / AR23 — every column outside the AR20 set is unchanged.
    -- origin_kind is generated and cannot be assigned, so it is not listed.
    IF ROW(OLD.audit_id, OLD.sequence, OLD.occurred_at, OLD.captured_at,
           OLD.created_at, OLD.event_type, OLD.event_version, OLD.write_path,
           OLD.regulatory_classification, OLD.reason_required, OLD.reason,
           OLD.actor_user_id, OLD.actor_type, OLD.actor_identity_provider,
           OLD.actor_subject_id, OLD.authorizing_role_id,
           OLD.authorizing_role_name, OLD.authorizing_scope_type,
           OLD.authorizing_scope_id, OLD.authorizing_assignment_id,
           OLD.on_behalf_of, OLD.agent_context, OLD.platform_access_ref,
           OLD.actor_captured_at, OLD.entity_type, OLD.entity_id,
           OLD.operation_id, OLD.causation_id)
       IS DISTINCT FROM
       ROW(NEW.audit_id, NEW.sequence, NEW.occurred_at, NEW.captured_at,
           NEW.created_at, NEW.event_type, NEW.event_version, NEW.write_path,
           NEW.regulatory_classification, NEW.reason_required, NEW.reason,
           NEW.actor_user_id, NEW.actor_type, NEW.actor_identity_provider,
           NEW.actor_subject_id, NEW.authorizing_role_id,
           NEW.authorizing_role_name, NEW.authorizing_scope_type,
           NEW.authorizing_scope_id, NEW.authorizing_assignment_id,
           NEW.on_behalf_of, NEW.agent_context, NEW.platform_access_ref,
           NEW.actor_captured_at, NEW.entity_type, NEW.entity_id,
           NEW.operation_id, NEW.causation_id) THEN
        RAISE EXCEPTION
            'AR21b/AR23: a column outside the anonymisation set was changed'
            USING ERRCODE = 'raise_exception';
    END IF;

    RETURN NEW;
END $$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tg_audit_record_guard ON audit.audit_record;

CREATE TRIGGER tg_audit_record_guard
    BEFORE UPDATE ON audit.audit_record
    FOR EACH ROW EXECUTE FUNCTION audit.audit_record_guard();

-- The hardening that matters. Without ALWAYS, a superuser reaches every
-- row with one SET; with it, the guard fires regardless of
-- session_replication_role.
ALTER TABLE audit.audit_record ENABLE ALWAYS TRIGGER tg_audit_record_guard;


-- ---------------------------------------------------------------------
-- AUD-2 — deletion is refused by trigger as well as by privilege
--
-- No role is granted DELETE, so this should be unreachable. It exists
-- because "unreachable" depends on the grant matrix being correct, and a
-- second, independent mechanism is the difference between a control and
-- an intention. It also means a superuser must issue DDL against the
-- trail rather than a DELETE.
--
-- Beyond the letter of the workbook, which specifies AR21 for UPDATE
-- only. Recorded as an implementation hardening, not a model change: it
-- forbids nothing the model permits, since nothing may ever delete an
-- audit row.
-- ---------------------------------------------------------------------
CREATE OR REPLACE FUNCTION audit.audit_reject_delete() RETURNS trigger AS $$
BEGIN
    RAISE EXCEPTION
        'AUD-2: audit rows are append-only; % may not delete from %',
        current_user, TG_TABLE_NAME
        USING ERRCODE = 'raise_exception';
END $$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tg_audit_record_no_delete ON audit.audit_record;
CREATE TRIGGER tg_audit_record_no_delete
    BEFORE DELETE ON audit.audit_record
    FOR EACH ROW EXECUTE FUNCTION audit.audit_reject_delete();
ALTER TABLE audit.audit_record ENABLE ALWAYS TRIGGER tg_audit_record_no_delete;

DROP TRIGGER IF EXISTS tg_audit_entity_ref_no_delete ON audit.audit_entity_ref;
CREATE TRIGGER tg_audit_entity_ref_no_delete
    BEFORE DELETE ON audit.audit_entity_ref
    FOR EACH ROW EXECUTE FUNCTION audit.audit_reject_delete();
ALTER TABLE audit.audit_entity_ref ENABLE ALWAYS TRIGGER tg_audit_entity_ref_no_delete;

-- AE7 — audit.audit_entity_ref is insert-only for everyone, including the
-- anonymiser. Nothing in it is personal data: entity ids are identifiers,
-- preserved under invariant 12.
CREATE OR REPLACE FUNCTION audit.audit_entity_ref_guard() RETURNS trigger AS $$
BEGIN
    RAISE EXCEPTION
        'AE7: audit.audit_entity_ref is insert-only; % may not update it', current_user
        USING ERRCODE = 'raise_exception';
END $$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tg_audit_entity_ref_guard ON audit.audit_entity_ref;
CREATE TRIGGER tg_audit_entity_ref_guard
    BEFORE UPDATE ON audit.audit_entity_ref
    FOR EACH ROW EXECUTE FUNCTION audit.audit_entity_ref_guard();
ALTER TABLE audit.audit_entity_ref ENABLE ALWAYS TRIGGER tg_audit_entity_ref_guard;


-- ---------------------------------------------------------------------
-- AR21(d) — the row pointed at must be an AuditAnonymised record
--
-- Deferred to commit, because AUD-C1 updates the located rows first and
-- inserts the AuditAnonymised record afterwards, in the same transaction.
-- Checking immediately would refuse the model's own sequence.
-- ---------------------------------------------------------------------
CREATE OR REPLACE FUNCTION audit.audit_record_anon_ref() RETURNS trigger AS $$
DECLARE
    referenced_type varchar(200);
BEGIN
    SELECT event_type INTO referenced_type
    FROM audit.audit_record
    WHERE audit_id = NEW.anonymisation_audit_id;

    IF referenced_type IS DISTINCT FROM 'AuditAnonymised' THEN
        RAISE EXCEPTION
            'AR21d: anonymisation_audit_id must reference an AuditAnonymised '
            'record, but referenced %', coalesce(referenced_type, '<missing>')
            USING ERRCODE = 'raise_exception';
    END IF;

    RETURN NULL;
END $$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tg_audit_record_anon_ref ON audit.audit_record;

CREATE CONSTRAINT TRIGGER tg_audit_record_anon_ref
    AFTER UPDATE ON audit.audit_record
    DEFERRABLE INITIALLY DEFERRED
    FOR EACH ROW
    WHEN (NEW.anonymisation_audit_id IS NOT NULL)
    EXECUTE FUNCTION audit.audit_record_anon_ref();

-- Constraint triggers are ordinary triggers as far as
-- session_replication_role is concerned, so this one needs ALWAYS for the
-- same reason the others do. Every non-internal trigger in the audit schema
-- is ALWAYS, and a test asserts that rather than trusting this comment.
ALTER TABLE audit.audit_record
    ENABLE ALWAYS TRIGGER tg_audit_record_anon_ref;


-- ---------------------------------------------------------------------
-- RT6 — audit.audit_retention_policy is append-only
--
-- The command is CreateAuditRetentionPolicyVersion, never Update. A
-- correction is a new version whose Reason states the intent; the record
-- shows the truth late rather than a fiction on time.
-- ---------------------------------------------------------------------
CREATE OR REPLACE FUNCTION audit.audit_retention_policy_frozen() RETURNS trigger AS $$
BEGIN
    RAISE EXCEPTION
        'RT6: audit.audit_retention_policy is append-only; % may not % it',
        current_user, lower(TG_OP)
        USING ERRCODE = 'raise_exception';
END $$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tg_audit_retention_policy_frozen ON audit.audit_retention_policy;

CREATE TRIGGER tg_audit_retention_policy_frozen
    BEFORE UPDATE OR DELETE ON audit.audit_retention_policy
    FOR EACH ROW EXECUTE FUNCTION audit.audit_retention_policy_frozen();

ALTER TABLE audit.audit_retention_policy
    ENABLE ALWAYS TRIGGER tg_audit_retention_policy_frozen;
