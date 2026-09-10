-- =====================================================================
-- 004_provisioning_audit_write.sql
--
-- Realigns provisioning_role with what provisioning actually does after
-- E1/E2a/E2b. A NEW script rather than an edit to 002, for the reason 003
-- gives: the ledger records each script's checksum, and an already-applied
-- script whose content changes fails the deployment.
--
-- Applied by: Ligature.AuditSchema, as audit_owner.
-- =====================================================================


-- ---------------------------------------------------------------------
-- GRANT — provisioning now emits through the ordinary audit pipeline.
--
-- 002 gave provisioning_role "the catalogue only", which was correct when
-- it was written: AUD-C4 seeded catalogue rows and nothing else wrote a
-- record. E1 then made the command pipeline emit, and AUD-C4 legitimately
-- emits TenantProvisioned through it. Without these two grants PRV-C1
-- fails with 42501 on a clean install, so the privilege model, not the
-- provisioning path, is what was out of date.
--
-- INSERT only. provisioning_role gets no UPDATE and no DELETE on the trail
-- for the same reason app_role does not (AR19): a committed audit record
-- is not amendable by anything that can log in.
-- ---------------------------------------------------------------------
GRANT INSERT ON audit.audit_record     TO provisioning_role;
GRANT INSERT ON audit.audit_entity_ref TO provisioning_role;


-- ---------------------------------------------------------------------
-- REVOKE — the catalogue is no longer provisioning's to write.
--
-- The event catalogue moved into the deployment phase, applied by
-- Ligature.AuditSchema as audit_owner before the host starts, because
-- IMPL-08 makes it a precondition of the host rather than tenant data.
-- Provisioning still READS it to assemble TenantProvisioned, so SELECT
-- stays and only INSERT goes.
--
-- Leaving the INSERT would leave the deployment model asserting that
-- provisioning may mutate release-controlled catalogue data when it no
-- longer does, and a privilege nothing exercises is a privilege nothing
-- would notice being used.
-- ---------------------------------------------------------------------
REVOKE INSERT ON audit.audit_event_type   FROM provisioning_role;
REVOKE INSERT ON audit.audit_event_origin FROM provisioning_role;


-- ---------------------------------------------------------------------
-- Deliberately unchanged:
--
--   audit.audit_retention_policy  provisioning_role keeps SELECT, INSERT.
--                                 Retention v1 stays with AUD-C4 because
--                                 RT5 makes created_by a foreign key to
--                                 the System actor PRV-C1 creates, so it
--                                 CANNOT be written before the host.
--
--   every other role              app_role, migration_role, audit_owner
--                                 and audit_anonymiser are untouched.
-- ---------------------------------------------------------------------
