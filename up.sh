#!/usr/bin/env bash
#
# One command, from a clean clone, to a running system.
#
# The only thing this does that `docker compose up` cannot is produce a signing
# key. docs/architecture.md §17 forbids a default key, a committed development
# key and a key regenerated per restart, so it cannot live in compose.yaml and
# it cannot be minted by the host. It is generated ONCE here, into .env, which
# .gitignore excludes.

set -euo pipefail

cd "$(dirname "$0")"

ENV_FILE=".env"
KEY_NAME="LIGATURE_SIGNING_KEY_V1"

if [ ! -f "$ENV_FILE" ]; then
    echo "==> No .env found. Creating one for local development."

    cat > "$ENV_FILE" <<EOF
# Local development configuration. NOT tracked by Git.
#
# Compose reads $KEY_NAME from this file. The other values are for
# running the host directly on this machine with 'dotnet run', which talks to
# localhost rather than to the compose network:
#
#   set -a; source .env; set +a
#
# Quoted deliberately: the connection string contains semicolons, and an
# unquoted value would be truncated at the first one, silently.

LIGATURE_CONNECTION="Host=localhost;Port=5432;Database=ligature;Username=postgres;Password=postgres"
LIGATURE_SIGNING_KEY_CURRENT="v1"
LIGATURE_API_DOCUMENTATION="true"

# Generated once, on $(date -u +%Y-%m-%dT%H:%M:%SZ). Keep it: regenerating it
# invalidates every carrier already issued, which is the failure §17 names.
$KEY_NAME="$(openssl rand -base64 32)"
EOF

    chmod 600 "$ENV_FILE"

elif ! grep -q "^${KEY_NAME}=" "$ENV_FILE"; then
    echo "==> .env exists but has no ${KEY_NAME}. Appending one."

    printf '\n# Added by up.sh on %s.\n%s="%s"\n' \
        "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
        "$KEY_NAME" \
        "$(openssl rand -base64 32)" >> "$ENV_FILE"
fi

echo "==> Building and starting the database, applying migrations, starting the host."

docker compose up --build --detach

echo
echo "    Host          http://localhost:8080"
echo "    API reference http://localhost:8080/scalar"
echo "    OpenAPI       http://localhost:8080/openapi/v1.json"
echo "    PostgreSQL    localhost:55432  (your own 5432 is untouched)"
echo
echo "    The system has schema but no users yet. To create the bootstrap"
echo "    administrator and receive its one-time activation token:"
echo
echo "      ./bootstrap.sh --first-name Ada --last-name Lovelace \\"
echo "          --display-name 'Ada Lovelace' --email ada@example.test \\"
echo "          --username ada.lovelace"
echo
