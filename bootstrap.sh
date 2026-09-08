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

# Built explicitly. `docker compose up` and a bare `docker compose build` both
# skip this service because it sits behind a profile, so without this a source
# change would leave a stale provisioner image that `run` would happily reuse.
docker compose --profile bootstrap build provisioner

# The tool writes the token owner-only and refuses to clobber an existing file.
# It is idempotent: on an already-provisioned database it reports so, changes
# nothing, and leaves any previous token alone.
docker compose --profile bootstrap run --rm provisioner \
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
