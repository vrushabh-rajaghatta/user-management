-- =====================================================================
-- 005_migration_audit_write.sql
--
-- PRV-C2's privilege expansion. A NEW script rather than an edit to 002 or
-- 004, for the reason 003 gives: the ledger records each script's checksum,
-- and an already-applied script whose content changes fails the deployment.
--
-- Applied by: Ligature.AuditSchema, as audit_owner.
-- =====================================================================


-- ---------------------------------------------------------------------
-- GRANT — catalogue synchronisation emits, and runs as migration_role.
--
-- 002 gave migration_role "nothing at all on the trail", which was correct
-- when it was written: migration applied schema and emitted nothing.
-- PRV-C2 makes a release-controlled migration operation change AUTHORIZATION
-- — it inserts permissions, roles and grants — and an authorization change
-- with no provenance is the one thing the trail exists to prevent. So the
-- privilege model, not the synchronisation path, is what is out of date.
--
-- This is the same shape as 004, which granted provisioning_role these two
-- when provisioning began emitting through the audit pipeline.
--
-- INSERT ONLY, and that is the whole of the concession:
--
--     migration_role is permitted to append audit evidence for
--     release-controlled migration operations. It has no authority to read,
--     modify, or delete audit records.
--
-- No SELECT, so it cannot read the trail it writes to. No UPDATE and no
-- DELETE, for the same reason app_role has neither (AR19): a committed audit
-- record is not amendable by anything that can log in. migration_role can
-- reshape the schema, which is exactly why the three that would let it
-- rewrite history stay denied.
--
-- audit_entity_ref comes with audit_record because a record's entity
-- references are written in the same insert; granting one without the other
-- would fail at the second statement rather than the first.
-- ---------------------------------------------------------------------
GRANT INSERT ON audit.audit_record     TO migration_role;
GRANT INSERT ON audit.audit_entity_ref TO migration_role;


-- ---------------------------------------------------------------------
-- GRANT — and the precondition that keeps the two grants above honest.
--
-- Catalogue synchronisation refuses to run against a database whose audit
-- schema is not current, for the same reason provisioning does: reconciling
-- first and discovering afterwards that the trail cannot be written would
-- COMMIT an authorization change and then fail to record it. Checking means
-- reading the deployment ledger, and 001 grants that to provisioning_role
-- alone.
--
-- SELECT only. The ledger is deployment metadata — script names, checksums,
-- when each was applied — and not the immutable trail, so reading it engages
-- nothing AR19 protects. Writing it is another matter and stays denied: this
-- role must not be able to tell a database that a script it never applied is
-- already there.
-- ---------------------------------------------------------------------
GRANT SELECT ON audit.audit_schema_version TO migration_role;


-- ---------------------------------------------------------------------
-- Deliberately unchanged:
--
--   audit.audit_event_type        migration_role already holds SELECT,
--   audit.audit_event_origin      INSERT and UPDATE. The catalogue is
--                                 release-controlled data and migration is
--                                 what deploys it (002).
--
--   every other role              app_role, provisioning_role, audit_owner
--                                 and audit_anonymiser are untouched.
-- ---------------------------------------------------------------------
