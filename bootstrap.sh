#!/usr/bin/env bash
#
# Creates the bootstrap administrator and writes its one-time activation token.
#
# Separate from ./up.sh on purpose. Seeding is not schema, and AGENTS.md §3
# keeps the two apart so they stay separately auditable and can eventually be
# separately privileged. This step also mints a credential, which should be a
# thing somebody chose to do.
#
# Usage:
#   ./bootstrap.sh --first-name Ada --last-name Lovelace \
#       --display-name 'Ada Lovelace' --email ada@example.test \
#       --username ada.lovelace

set -euo pipefail

cd "$(dirname "$0")"

if [ "$#" -eq 0 ]; then
    sed -n '3,14p' "$0" | sed 's/^# \{0,1\}//'
    exit 1
fi

TOKEN_DIR=".secrets"
TOKEN_FILE="bootstrap.token"

mkdir -p "$TOKEN_DIR"

# Whether a token already existed decides what we may claim afterwards. On an
# already-provisioned database the tool writes nothing and leaves any previous
# token alone, and saying otherwise would send someone hunting for a fresh
# credential that was never minted.
if [ -f "${TOKEN_DIR}/${TOKEN_FILE}" ]; then
    TOKEN_EXISTED=yes
else
    TOKEN_EXISTED=no
fi

refuse() {
    echo "    $1" >&2
    echo >&2
    echo "    Recreate the environment, which keeps the database, and try again:" >&2
    echo "      docker compose -f compose.yaml -f compose.dev.yaml down" >&2
    echo "      ./up.sh" >&2
    exit 1
}

# The project must be addressed with the files it was CREATED with. ./up.sh
# adds compose.dev.yaml, which pins the default network's subnet, so the base
# file alone describes a different network: Compose stops the database to
# recreate it, fails because the API and web containers are still attached, and
# never runs the provisioner. Compose records the files on every container it
# creates, so they are read back rather than guessed — hard-coding the overlay
# would break a project started from the base file in exactly the same way.
COMPOSE=(docker compose)

CONTAINERS="$(docker compose ps --all --quiet)"

if [ -n "$CONTAINERS" ]; then
    # Unquoted on purpose: one identifier per word.
    # shellcheck disable=SC2086
    FILE_SETS="$(docker inspect \
        --format '{{ index .Config.Labels "com.docker.compose.project.config_files" }}' \
        $CONTAINERS | sort -u)"

    if [ -z "$FILE_SETS" ]; then
        refuse "The project's containers do not say which Compose files created them."
    fi

    if [ "$(printf '%s\n' "$FILE_SETS" | wc -l)" -ne 1 ]; then
        refuse "The project's containers were created from different sets of Compose files."
    fi

    IFS=',' read -r -a FILES <<< "$FILE_SETS"

    for file in "${FILES[@]}"; do
        if [ ! -f "$file" ]; then
            refuse "The project was created with ${file}, which no longer exists."
        fi

        COMPOSE+=(-f "$file")
    done
fi

# Built explicitly. `docker compose up` and a bare `docker compose build` both
# skip this service because it sits behind a profile, so without this a source
# change would leave a stale provisioner image that `run` would happily reuse.
"${COMPOSE[@]}" --profile bootstrap build provisioner

# The tool writes the token owner-only and refuses to clobber an existing file.
# It is idempotent: on an already-provisioned database it reports so, changes
# nothing, and leaves any previous token alone.
"${COMPOSE[@]}" --profile bootstrap run --rm provisioner \
    "$@" --activation-token-out "/secrets/${TOKEN_FILE}"

echo
if [ "$TOKEN_EXISTED" = yes ]; then
    echo "    ${TOKEN_DIR}/${TOKEN_FILE} already existed and was left untouched."
    echo
elif [ -f "${TOKEN_DIR}/${TOKEN_FILE}" ]; then
    echo "    Activation token written to ${TOKEN_DIR}/${TOKEN_FILE}"
    echo
    echo "    It is never printed and cannot be recovered if lost — only its"
    echo "    hash is stored. Deliver it, activate the account, then delete it:"
    echo
    echo "      curl -X POST http://localhost:8080/api/account/activate \\"
    echo "        -H 'Content-Type: application/json' \\"
    echo "        -d \"{\\\"token\\\":\\\"\$(cat ${TOKEN_DIR}/${TOKEN_FILE})\\\",\\\"newPassword\\\":\\\"<a password>\\\"}\""
    echo
fi
