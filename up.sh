#!/usr/bin/env bash
#
# One command, from a clean clone, to a running system.
#
# The only thing this does that `docker compose up` cannot is produce secrets.
# docs/architecture.md §17 forbids a default key, a committed development key
# and a key regenerated per restart, so the signing key cannot live in
# compose.yaml and cannot be minted by the host. The same reasoning applies to
# the database role passwords: the audit tamper boundary depends on the
# application NOT connecting as a superuser, and a committed development
# password is a credential in the repository. All of them are generated ONCE
# here, into .env, which .gitignore excludes.

set -euo pipefail

cd "$(dirname "$0")"

ENV_FILE=".env"
KEY_NAME="LIGATURE_SIGNING_KEY_V1"

# Every generated secret, so adding one is a single edit here. Each is created
# only if absent, so an existing .env is never rewritten: regenerating the
# signing key would sign every user out, and regenerating a role password would
# leave the database expecting the old one until `roles` ran again.
SECRETS=(
    "$KEY_NAME"
    "LIGATURE_APP_PASSWORD"
    "LIGATURE_MIGRATION_PASSWORD"
    "LIGATURE_PROVISIONING_PASSWORD"
)

generate() {
    # base64 without padding characters that complicate shell quoting; the
    # roles.sql :'name' form would quote them safely anyway, but .env is read
    # by Compose, not by a shell.
    openssl rand -base64 32 | tr -d '=+/' | cut -c1-32
}

if [ ! -f "$ENV_FILE" ]; then
    echo "==> No .env found. Creating one for local development."

    cat > "$ENV_FILE" <<EOF
# Local development configuration. NOT tracked by Git.
#
# Compose reads the generated secrets below from this file. The connection
# string is for running the host directly on this machine with 'dotnet run',
# which talks to localhost rather than to the compose network:
#
#   set -a; source .env; set +a
#
# Quoted deliberately: the connection string contains semicolons, and an
# unquoted value would be truncated at the first one, silently.
#
# Generated once, on $(date -u +%Y-%m-%dT%H:%M:%SZ). Keep them: regenerating
# the signing key invalidates every carrier already issued, which is the
# failure §17 names, and regenerating a role password leaves the database
# expecting the previous one until the roles step runs again.

LIGATURE_CONNECTION="Host=localhost;Port=5432;Database=ligature;Username=app_role;Password=CHANGE_ME"
LIGATURE_SIGNING_KEY_CURRENT="v1"
LIGATURE_API_DOCUMENTATION="true"
EOF

    chmod 600 "$ENV_FILE"
fi

for secret in "${SECRETS[@]}"; do
    if ! grep -q "^${secret}=" "$ENV_FILE"; then
        echo "==> Generating ${secret} into ${ENV_FILE}."

        printf '\n# Added by up.sh on %s.\n%s="%s"\n' \
            "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
            "$secret" \
            "$(generate)" >> "$ENV_FILE"
    fi
done

echo "==> Building and starting: database, roles, migrations, audit schema, host."

docker compose up --build --detach

echo
echo "    Host          http://localhost:8080"
echo "    API reference http://localhost:8080/scalar"
echo "    OpenAPI       http://localhost:8080/openapi/v1.json"
echo "    PostgreSQL    localhost:55432  (your own 5432 is untouched)"
echo
echo "    The application connects as app_role, which holds SELECT and INSERT"
echo "    on the audit trail and cannot update or delete a committed record."
echo
echo "    The system has schema but no users yet. To create the bootstrap"
echo "    administrator and receive its one-time activation token:"
echo
echo "      ./bootstrap.sh --first-name Ada --last-name Lovelace \\"
echo "          --display-name 'Ada Lovelace' --email ada@example.test \\"
echo "          --username ada.lovelace"
echo
