#!/usr/bin/env bash
#
# One command, from a clean clone, to a usable SKSMCorp in the browser.
#
#   ./up.sh              bring the developer environment up and prove it is up
#   ./up.sh --check      check the prerequisites only, and change nothing
#
# The only thing this does that `docker compose up` cannot is produce secrets.
# docs/architecture.md §17 forbids a default key, a committed development key
# and a key regenerated per restart, so the signing key cannot live in
# compose.yaml and cannot be minted by the host. The same reasoning applies to
# the database role passwords: the audit tamper boundary depends on the
# application NOT connecting as a superuser, and a committed development
# password is a credential in the repository. All of them are generated ONCE
# here, into .env, which .gitignore excludes.
#
# It also brings up the DEVELOPMENT overlay (compose.dev.yaml): the web client,
# bind-mounted source and hot reload. Plain `docker compose up` keeps its
# production-shaped meaning — database, schema and API — and passing the overlay
# is an implementation detail of this script.
#
# SUCCESS IS PROVED, NOT ASSUMED. `docker compose up` returning 0 means the
# containers were created, which is not the same thing: this repository has had
# an API container sit "Up" for two days against a database that exited four
# days earlier. Every criterion below is probed.

set -euo pipefail

cd "$(dirname "$0")"

ENV_FILE=".env"
KEY_NAME="SKSMCORP_SIGNING_KEY_V1"

# Overridable so the prerequisite check itself can be tested.
CERTS_DIR="${SKSMCORP_CERTS_DIR:-web/sksmcorp-web/.certs}"

CERT_FILE="${CERTS_DIR}/localhost.pem"
KEY_FILE="${CERTS_DIR}/localhost-key.pem"

WEB_ORIGIN="https://localhost:5173"
API_ORIGIN="http://localhost:8080"

COMPOSE=(docker compose -f compose.yaml -f compose.dev.yaml)

# How long a container gets to become answerable before this is a failure.
READY_TIMEOUT=180

# Every generated secret, so adding one is a single edit here. Each is created
# only if absent, so an existing .env is never rewritten: regenerating the
# signing key would sign every user out, and regenerating a role password would
# leave the database expecting the old one until `roles` ran again.
SECRETS=(
    "$KEY_NAME"
    "SKSMCORP_APP_PASSWORD"
    "SKSMCORP_MIGRATION_PASSWORD"
    "SKSMCORP_PROVISIONING_PASSWORD"
)

generate() {
    # base64 without padding characters that complicate shell quoting; the
    # roles.sql :'name' form would quote them safely anyway, but .env is read
    # by Compose, not by a shell.
    openssl rand -base64 32 | tr -d '=+/' | cut -c1-32
}

# --------------------------------------------------------- prerequisites

# Collected and reported together, so one run tells a developer everything they
# have to install rather than one thing at a time. Nothing here creates a
# certificate: mkcert adds an authority to the system trust store, which needs
# the developer's password and is not something a script should do for them.
check_prerequisites() {
    local missing=()

    if ! command -v docker >/dev/null 2>&1; then
        missing+=("Docker is not installed, or not on PATH.")
    elif ! docker info >/dev/null 2>&1; then
        missing+=("Docker is installed but not running. Start Docker Desktop and try again.")
    fi

    if [ ! -f "$CERT_FILE" ] || [ ! -f "$KEY_FILE" ]; then
        missing+=("$(cat <<EOF
The development certificate is missing:
      ${CERT_FILE}
      ${KEY_FILE}

    The web client is served over HTTPS only, because the carrier cookie is
    Secure and __Host- prefixed. Create one once per machine with mkcert —
    you install it, not this script, because it adds a certificate authority
    to your system trust store:

      brew install mkcert nss
      mkcert -install
      mkdir -p ${CERTS_DIR}
      cd web/sksmcorp-web && mkcert -cert-file .certs/localhost.pem \\
          -key-file .certs/localhost-key.pem localhost
EOF
)")
    fi

    if [ "${#missing[@]}" -ne 0 ]; then
        echo "==> Cannot start the developer environment:" >&2
        echo >&2

        for reason in "${missing[@]}"; do
            echo "  - ${reason}" >&2
            echo >&2
        done

        return 1
    fi
}

# ------------------------------------------------------------- criteria

# The exit code of a one-shot service. Named containers rather than parsed JSON,
# so this needs no jq.
exit_code_of() {
    local container
    container="$("${COMPOSE[@]}" ps -aq "$1" 2>/dev/null | head -n1)"

    if [ -z "$container" ]; then
        echo "missing"
        return
    fi

    docker inspect --format '{{.State.ExitCode}}' "$container" 2>/dev/null || echo "missing"
}

# Any HTTP status at all. By docs/architecture.md, a host that answers has
# already verified its signing key and that its compiled audit declarations
# match the deployed catalogue — it refuses to start otherwise — so this proves
# considerably more than a listening socket.
api_answers() {
    local status
    status="$(curl -s -o /dev/null -m 3 -w '%{http_code}' "${API_ORIGIN}/" 2>/dev/null || true)"

    [ -n "$status" ] && [ "$status" != "000" ]
}

# TLS completes AND the application shell is served. No -k: the certificate is
# signed by the authority mkcert put in the system trust store, so a handshake
# that needs it disabled is a failure, not a detail. Both markers are required,
# because an empty 200 would satisfy a weaker check.
web_serves_the_shell() {
    local body
    body="$(curl -s -m 5 "${WEB_ORIGIN}/" 2>/dev/null || true)"

    [[ "$body" == *'id="root"'* ]] && [[ "$body" == *'/src/main.tsx'* ]]
}

# Docker's own verdict, not ours. The two probes above run from the developer's
# machine and would pass even if the container's healthcheck were wired wrongly
# — pointed at the wrong port, or written with a client the image does not have,
# which would report a working server as broken forever. This asserts that the
# check itself runs and passes.
#
# `starting` is not `healthy`: a service inside its grace period has not been
# judged yet, so this keeps waiting rather than accepting it.
reports_healthy() {
    local container status
    container="$("${COMPOSE[@]}" ps -aq "$1" 2>/dev/null | head -n1)"

    [ -n "$container" ] || return 1

    status="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{end}}' "$container" 2>/dev/null || true)"

    [ "$status" = "healthy" ]
}

web_is_healthy() {
    reports_healthy web
}

wait_for() {
    local description="$1" probe="$2" deadline=$((SECONDS + READY_TIMEOUT))

    while [ "$SECONDS" -lt "$deadline" ]; do
        if "$probe"; then
            echo "    ok    ${description}"
            return 0
        fi

        sleep 2
    done

    echo "    FAIL  ${description}" >&2
    return 1
}

# ------------------------------------------------------------------ main

if [ "${1:-}" = "--check" ]; then
    check_prerequisites
    echo "==> Prerequisites satisfied."
    exit 0
fi

if [ "${1:-}" != "" ]; then
    echo "Usage: ./up.sh [--check]" >&2
    exit 2
fi

check_prerequisites

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

SKSMCORP_CONNECTION="Host=localhost;Port=5432;Database=sksmcorp;Username=app_role;Password=CHANGE_ME"
SKSMCORP_SIGNING_KEY_CURRENT="v1"
SKSMCORP_API_DOCUMENTATION="true"
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

# The development mail sink's directory (docs/architecture.md §8). The sink
# never creates it, and the host refuses to start if it is missing.
mkdir -p .secrets/mail
chmod 700 .secrets/mail

echo "==> Building and starting: database, roles, migrations, audit schema, API, web client."

"${COMPOSE[@]}" up --build --detach

echo
echo "==> Proving it is actually up."

failed=0

for step in roles migrator audit-schema catalogue-sync; do
    code="$(exit_code_of "$step")"

    if [ "$code" = "0" ]; then
        echo "    ok    ${step} completed"
    else
        echo "    FAIL  ${step} exited with ${code} — docker compose logs ${step}" >&2
        failed=1
    fi
done

wait_for "the API answers on ${API_ORIGIN}" api_answers || failed=1
wait_for "${WEB_ORIGIN} serves the application" web_serves_the_shell || failed=1
wait_for "the web container reports itself healthy" "web_is_healthy" || failed=1

if [ "$failed" -ne 0 ]; then
    echo >&2
    echo "==> The environment did not come up. Inspect a service with:" >&2
    echo "      docker compose -f compose.yaml -f compose.dev.yaml logs <service>" >&2
    exit 1
fi

echo
echo "    SKSMCorp       ${WEB_ORIGIN}"
echo "    API            ${API_ORIGIN}"
echo "    API reference  ${API_ORIGIN}/scalar"
echo "    PostgreSQL     localhost:55432  (your own 5432 is untouched)"
echo
echo "    Editing src/ or web/sksmcorp-web/src/ reloads automatically."
echo
echo "    npm run test:host needs port 5173 to itself. Stop the web container"
echo "    first:  docker compose -f compose.yaml -f compose.dev.yaml stop web"
echo
echo "    Mail is not sent in development. Activation and reset links are"
echo "    written to .secrets/mail/ instead, one .eml file per message."
echo
echo "    The system has schema but no users yet. To create the bootstrap"
echo "    administrator and receive its one-time activation token:"
echo
echo "      ./bootstrap.sh --first-name Ada --last-name Lovelace \\"
echo "          --display-name 'Ada Lovelace' --email ada@example.test \\"
echo "          --username ada.lovelace"
echo
