# Ligature

Identity and access for a regulated system: user accounts, credentials,
server-side sessions, roles and permissions.

The current scope is **User Management** and **Audit**, inside the Platform
module, with a **React web client**. Audit is database-owned: its tables live in
their own schema, owned by a role that cannot log in, and the application
appends to a trail it cannot amend.

Notifications, Logging, tenancy and the business domains described in the
architecture are target state and do not exist yet.

---

## Run it

You need:

| | |
| --- | --- |
| **Docker** | Docker Desktop, or Engine with Compose v2 |
| **mkcert** | only to create a local HTTPS certificate, once per machine |

You do **not** need the .NET SDK, Node, PostgreSQL or the EF tools. Everything
compiles and runs inside the containers.

### 1. Clone

```bash
git clone git@github.com:vrushabh-rajaghatta/user-management.git
cd user-management
```

### 2. Create the development certificate — once per machine

The web client is served over **HTTPS only**. The session cookie is `Secure`
and `__Host-` prefixed, and browsers disagree about accepting that over plain
`http://localhost`, so there is no HTTP fallback.

```bash
brew install mkcert nss
mkcert -install
```

`mkcert -install` adds a local certificate authority to your system trust store
and will ask for your password. That is why you run it and no script does it
for you.

```bash
mkdir -p web/ligature-web/.certs
cd web/ligature-web
mkcert -cert-file .certs/localhost.pem -key-file .certs/localhost-key.pem localhost
cd ../..
```

`.certs/` holds a private key and is gitignored. Nothing downloads mkcert or a
certificate at runtime, and the containers only ever read this one.

### 3. Start everything

```bash
./up.sh
```

The first run takes a few minutes: it pulls the base images, compiles the
solution and installs the client's packages. After that it is seconds.

It starts PostgreSQL, creates the database roles, applies the migrations,
deploys the audit schema and its event catalogue, then starts the API and the
web client — each step waiting until the previous one is genuinely ready. On
first run it also generates a signing key and the database role passwords into
a local `.env`, because the host refuses to start without a key and the
architecture forbids a committed or default one (`docs/architecture.md` §17).

| | |
| --- | --- |
| **Ligature** | <https://localhost:5173> |
| API | <http://localhost:8080> |
| API reference | <http://localhost:8080/scalar> |
| OpenAPI document | <http://localhost:8080/openapi/v1.json> |
| PostgreSQL | `localhost:55432` |

The database is on **55432**, not 5432, so it will not collide with a
PostgreSQL you already run.

`./up.sh` does not just start containers — it **proves** they are up: that each
one-shot step exited cleanly, that the API answers, and that
`https://localhost:5173` serves the application. If something is wrong it says
which check failed and how to look at that service's logs.

```bash
./up.sh --check      # check the prerequisites only, change nothing
```

The system is now running, with schema, and with no users in it.

### 4. Create the first administrator

```bash
./bootstrap.sh --first-name Ada --last-name Lovelace \
    --display-name "Ada Lovelace" --email ada@example.test \
    --username ada.lovelace
```

Use your own details. This is a separate command on purpose: seeding is not
schema, and this step mints a credential.

The one-time activation token is written to `.secrets/bootstrap.token`. **It is
never printed and cannot be recovered if lost** — only its hash is stored, and
a sentinel prevents a second administrator being issued.

### 5. Activate the account in the browser

```bash
echo "https://localhost:5173/activate#token=$(cat .secrets/bootstrap.token)"
```

Open that URL. It is exactly the link the system emails when mail is
configured: the token travels in the URL **fragment**, which browsers never
send to a server, so it stays out of access logs and `Referer` headers. The
page reads it once, removes it from the address bar, and asks you to choose a
password.

Choose one of at least 12 characters, then delete `.secrets/bootstrap.token`.

<details>
<summary>Without a browser</summary>

```bash
curl -X POST http://localhost:8080/api/account/activate \
  -H 'Content-Type: application/json' \
  -d "{\"token\":\"$(cat .secrets/bootstrap.token)\",\"newPassword\":\"choose-a-long-password\"}"
```

</details>

### 6. Sign in

Go to <https://localhost:5173/sign-in> and sign in with the username and the
password you just chose.

A successful sign-in returns **`204` with no body**. The session carrier
arrives as the `__Host-ligature` cookie — `HttpOnly`, `Secure`,
`SameSite=Strict` — so no script on the page can read it. A caller that is not
a browser takes the carrier from the `Set-Cookie` header and presents it as
`Authorization: Bearer <carrier>`.

### Stopping

```bash
docker compose -f compose.yaml -f compose.dev.yaml down       # keeps the data
docker compose -f compose.yaml -f compose.dev.yaml down -v    # deletes the database too — step 4 again
```

### If something does not work

| | |
| --- | --- |
| `./up.sh` complains about the certificate | You skipped step 2, or ran mkcert somewhere other than `web/ligature-web/.certs/` |
| The browser warns the certificate is untrusted | `mkcert -install` did not complete. Run it again and give it your password |
| A port is already in use | Something else holds 5173, 8080 or 55432 — often a `npm run dev` you left running |
| A step failed | `docker compose -f compose.yaml -f compose.dev.yaml logs <service>` |

---

## The API

| Method | Route | |
| --- | --- | --- |
| `POST` | `/api/auth/sign-in` | Sign in; sets the carrier cookie |
| `POST` | `/api/auth/sign-out` | End the session this request presented |
| `POST` | `/api/account/activate` | Activate an account and set its first password |
| `POST` | `/api/account/password-reset-request` | Ask for a password-reset link |
| `POST` | `/api/account/reset-password` | Set a new password using an emailed token |
| `POST` | `/api/account/change-password` | Change the signed-in account's password |
| `POST` | `/api/account/sign-out-everywhere` | End every session of the signed-in account |
| `POST` | `/api/identities/{id}/unlock` | Clear a live lockout (administrator) |
| `POST` | `/api/users` | Create a human user account |

Sign-in returns the same 401 for an unknown user, a wrong password, a locked
account, an inactive one, and a request that already presents a live session.
Activation and reset return the same 400 for every bad token. The
password-reset request always returns the same 200, whether or not an account
matches. That is deliberate: distinguishing them would tell a caller which
usernames, addresses and tokens exist.

The interactive reference at `/scalar` is opt-in and off unless
`LIGATURE_API_DOCUMENTATION` is `true`. The Docker environment turns it on
because it is a development environment.

---

## Working on the code

Editing `src/` or `web/ligature-web/src/` while `./up.sh` is running reloads
automatically — the API through `dotnet watch`, the client through Vite.

### The test suites

Running the tests is a different setup from running the app. The .NET suites
need the **.NET 10 SDK** and a **PostgreSQL on the default 5432**, not the
container.

```bash
dotnet build Ligature.slnx
dotnet test  Ligature.slnx
```

A reachable database is not enough for the persistence and host suites: it must
also be provisioned, or they will fail with an explicit message. `AGENTS.md` §3
describes how to validate a run honestly, and why a green exit code alone is
not evidence.

The web client's suite needs neither PostgreSQL nor .NET:

```bash
cd web/ligature-web
npm ci
npm run typecheck && npm run lint && npm test && npm run build
```

There is also a small suite that runs against a **real host** over the real
HTTPS path, proving the cookie transport and the cross-site protection. It
builds and drops its own throwaway database, and needs port 5173 to itself:

```bash
docker compose -f compose.yaml -f compose.dev.yaml stop web
npm run test:host
```

It tells you what is missing if a prerequisite is absent. Docker is the
development environment; this is a proof harness — neither replaces the other.

### Where things are

```text
src/
├── Platform/
│   ├── Ligature.Platform.Domain        aggregates, value objects
│   ├── Ligature.Platform.Application   commands, handlers, behaviors
│   └── Ligature.Platform.Persistence   EF Core, repositories, migrations
├── Host/Ligature.Host                  the deployable application: HTTP, carriers, composition
├── Tools/Ligature.Provisioning         seeds a database; deliberately NOT part of the host
├── Tools/Ligature.AuditSchema          deploys the audit schema and event catalogue
└── Shared/Ligature.SharedKernel        Entity, AggregateRoot, StronglyTypedId, ICommand

web/ligature-web/                       the React client (see docs/frontend-architecture.md)
docker/                                 Dockerfile and the database roles script
compose.yaml                            database, schema and API
compose.dev.yaml                        adds the web client, bind mounts and hot reload
```

### Before you change anything

Four documents govern this repository, and they are not decoration.

| | |
| --- | --- |
| **`AGENTS.md`** | How to work: the owner-driven workflow, commands, validation, branches, commits. Read §2 first. |
| **`docs/architecture.md`** | The architectural contract: module boundaries, packaging, established patterns. Authoritative on architectural questions. |
| **`docs/frontend-architecture.md`** | The same, for the web client: layering, the API boundary, authentication state, security-sensitive pages. |
| **`docs/requirements.md`** | The requirement catalogue, and the Known Gaps list. Being back-filled. |

The short version: analyse and plan before implementing, do not make
architectural decisions silently, and follow the patterns already in the code
rather than introducing new ones.
