-- =====================================================================
-- 003_audit_retention_created_by.sql
--
-- Two additions found by the AUD-C4 analysis. A NEW script rather than an
-- edit to 001 or 002: the ledger records each script's checksum, and an
-- already-applied script whose content changes fails the deployment. That
-- rule exists precisely so the deployed trail and the source never
-- silently describe different things, and this is its first real use.
--
-- Applied by: Ligature.AuditSchema, as audit_owner.
-- =====================================================================


-- ---------------------------------------------------------------------
-- RT5 — audit_retention_policy.created_by references app_user.
--
-- The workbook lists RT5 as a PG-D foreign key; 001 declared the column
-- NOT NULL and stopped there. It matters now because AUD-C4 step 4 writes
-- retention v1 with CreatedBy = SYSTEM_UUID, and without the key nothing
-- verifies that is an actor at all. A cross-module reference, like AR10:
-- audit_owner holds REFERENCES on public.app_user from the deployer's
-- privileged prelude.
-- ---------------------------------------------------------------------
ALTER TABLE audit.audit_retention_policy
    DROP CONSTRAINT IF EXISTS fk_audit_retention_policy_rt5_created_by;

ALTER TABLE audit.audit_retention_policy
    ADD CONSTRAINT fk_audit_retention_policy_rt5_created_by
    FOREIGN KEY (created_by) REFERENCES public.app_user (id);


-- ---------------------------------------------------------------------
-- provisioning_role may READ the trail.
--
-- AUD-C4 steps 1-2 and AUD-S11 require provisioning to verify, before it
-- hands a tenant over, that the trail is in the state it claims — empty
-- and unconsumed today; "Sequence 1 = TenantProvisioned" once emission
-- exists. 002 granted provisioning_role the catalogue and retention tables
-- only, faithful to "neither writes the trail", and it still does not:
-- this is SELECT and nothing else.
--
-- The Design Specification's privilege matrix (section 13.2) does not list
-- provisioning_role at all, so this is an addition to it rather than a
-- departure from it. docs/architecture.md section 19 records it.
-- ---------------------------------------------------------------------
GRANT SELECT ON audit.audit_record     TO provisioning_role;
GRANT SELECT ON audit.audit_entity_ref TO provisioning_role;

-- SELECT on a sequence permits reading its state (currval, last_value,
-- is_called); it does not permit nextval. AUD-S11's handover probe reads
-- is_called to establish that nothing has consumed a value before the
-- tenant's first record — a rolled-back write still consumes one (AUD-D32).
GRANT SELECT ON SEQUENCE audit.audit_record_sequence_seq TO provisioning_role;
