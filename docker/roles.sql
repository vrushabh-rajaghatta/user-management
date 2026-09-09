-- =====================================================================
-- The database foundation: the general application roles.
--
-- Runs ONCE per cluster, as an administrator, before any migration. It is
-- the first step of a customer installation and one of only two steps
-- needing cluster authority; the other is the Audit schema deployment.
--
-- These three roles belong to the deployment, not to any one capability.
-- The Audit-specific roles — audit_owner and audit_anonymiser — are NOT
-- created here: Ligature.AuditSchema owns them, because they are part of
-- the tamper boundary rather than of the database foundation. It verifies
-- these three exist and refuses with their names if they do not.
--
-- Passwords arrive as psql variables rather than being written here. The
-- :'name' form quotes them as SQL literals, so a generated password
-- containing a quote cannot terminate the statement. There is no default
-- and no committed development password, for the reason
-- docs/architecture.md section 17 gives about the signing key.
--
--   psql -v database=ligature \
--        -v app_password=... -v migration_password=... \
--        -v provisioning_password=... -f roles.sql
--
-- The conditionals are psql's, not the server's: psql does not interpolate
-- variables inside a dollar-quoted DO block, so the passwords would arrive
-- as the literal text ":'app_password'".
--
-- Idempotent: re-running resets the passwords to what was supplied and
-- changes nothing else.
-- =====================================================================

\set ON_ERROR_STOP on


SELECT NOT EXISTS (
    SELECT 1 FROM pg_roles WHERE rolname = 'app_role') AS create_app \gset

-- The application runtime. It reaches the audit trail with SELECT and
-- INSERT only; see 002_audit_privileges.sql.
\if :create_app
CREATE ROLE app_role LOGIN PASSWORD :'app_password';
\else
ALTER ROLE app_role LOGIN PASSWORD :'app_password';
\endif


SELECT NOT EXISTS (
    SELECT 1 FROM pg_roles WHERE rolname = 'migration_role') AS create_migration \gset

-- Owns the ordinary application schema and runs EF migrations. It must
-- never own the audit schema or anything in it: an owner can disable
-- triggers and drop what it owns regardless of any GRANT, and probing
-- confirmed it destroying audit tables when it did.
\if :create_migration
CREATE ROLE migration_role LOGIN PASSWORD :'migration_password';
\else
ALTER ROLE migration_role LOGIN PASSWORD :'migration_password';
\endif


SELECT NOT EXISTS (
    SELECT 1 FROM pg_roles WHERE rolname = 'provisioning_role') AS create_provisioning \gset

-- Seeds release-controlled data. Separate from the migration role because
-- AGENTS.md section 3 keeps schema and seed separately auditable and,
-- eventually, separately privileged (PE2).
\if :create_provisioning
CREATE ROLE provisioning_role LOGIN PASSWORD :'provisioning_password';
\else
ALTER ROLE provisioning_role LOGIN PASSWORD :'provisioning_password';
\endif


-- None of these may create databases, create roles, or bypass row-level
-- security. Stated rather than assumed, because CREATE ROLE defaults can
-- be changed cluster-wide.
ALTER ROLE app_role          NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
ALTER ROLE migration_role    NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
ALTER ROLE provisioning_role NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;

GRANT CONNECT ON DATABASE :"database"
    TO app_role, migration_role, provisioning_role;

-- migration_role creates the ordinary schema; the other two only read and
-- write within it.
GRANT USAGE, CREATE ON SCHEMA public TO migration_role;
GRANT USAGE          ON SCHEMA public TO app_role, provisioning_role;

-- Extensions are database infrastructure, not application schema, so they are
-- installed here rather than by a migration. The alternative is GRANT CREATE
-- ON DATABASE to migration_role, which also lets it create schemas — a wider
-- privilege than "run our migrations" needs, granted to work around one
-- statement.
--
-- AddUserManagementPostgresConstraints issues CREATE EXTENSION IF NOT EXISTS
-- for this, which becomes a no-op once it is already present.
CREATE EXTENSION IF NOT EXISTS btree_gist;
