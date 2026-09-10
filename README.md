# Ligature

Identity and access for a regulated system: user accounts, credentials,
server-side sessions, roles and permissions.

The current scope is **User Management** and **Audit**, inside the Platform
module. Audit is database-owned: its tables live in their own schema, owned by
a role that cannot log in, and the application appends to a trail it cannot
amend.

Notifications, Logging, tenancy and the business domains described in the
architecture are target state and do not exist yet.

---

## Run it

You need **Docker** — Docker Desktop, or Engine with Compose v2. You do not
need the .NET SDK, PostgreSQL, or the EF tools. Everything compiles and runs
inside the containers.

### 1. Clone

```bash
git clone git@github.com:vrushabh-rajaghatta/user-management.git
cd user-management
```

### 2. Start everything

```bash
./up.sh
```

The first run takes a few minutes: it pulls the base images and compiles the
solution. After that it is seconds.

It starts PostgreSQL, applies the migrations, and starts the host, in that
order, each step waiting until the previous one is genuinely ready. It also
generates a signing key into a local `.env` on first run, because the host
refuses to start without one and the architecture forbids a committed or
default key (`docs/architecture.md` §17).

| | |
| --- | --- |
| Host | <http://localhost:8080> |
| API reference | <http://localhost:8080/scalar> |
| OpenAPI document | <http://localhost:8080/openapi/v1.json> |
| PostgreSQL | `localhost:55432` |

The database is on **55432**, not 5432, so it will not collide with a
PostgreSQL you already run.

The system is now running, with schema, and with no users in it.

### 3. Create the first administrator

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

### 4. Activate the account

```bash
curl -X POST http://localhost:8080/api/account/activate \
  -H 'Content-Type: application/json' \
  -d "{\"token\":\"$(cat .secrets/bootstrap.token)\",\"newPassword\":\"choose-a-long-password\"}"
```

The password must be at least 12 characters. Delete the token file afterwards.

### 5. Sign in

```bash
curl -X POST http://localhost:8080/api/auth/sign-in \
  -H 'Content-Type: application/json' \
  -d '{"username":"ada.lovelace","password":"choose-a-long-password"}'
```

The `accessToken` in the response is a signed carrier for a server-side
session. Send it as `Authorization: Bearer <token>`.

### Stopping

```bash
docker compose down       # stops everything, keeps the data
docker compose down -v    # also deletes the database — you will need step 3 again
```

---

## The API

| Method | Route | |
| --- | --- | --- |
| `POST` | `/api/account/activate` | Activate an account and set its first password |
| `POST` | `/api/auth/sign-in` | Sign in and obtain an access carrier |
| `POST` | `/api/auth/sign-out` | End the session this request presented |
| `POST` | `/api/users` | Create a human user account |

Sign-in returns the same 401 for an unknown user, a wrong password, a locked
account and an inactive one. Activation returns the same 400 for every bad
token. That is deliberate: distinguishing them would tell a caller which
usernames and tokens exist.

The interactive reference at `/scalar` is opt-in and off unless
`LIGATURE_API_DOCUMENTATION` is `true`. The Docker environment turns it on
because it is a development environment.

---

## Working on the code

Running the test suites is a different setup from running the app. They need
the **.NET 10 SDK** and a **PostgreSQL on the default 5432**, not the container.

```bash
dotnet build Ligature.slnx
dotnet test  Ligature.slnx
```

A reachable database is not enough for the persistence and host suites: it must
also be provisioned, or they will fail with an explicit message.
`AGENTS.md` §3 describes how to validate a run honestly, and why a green exit
code alone is not evidence.

### Where things are

```text
src/
├── Platform/
│   ├── Ligature.Platform.Domain        aggregates, value objects
│   ├── Ligature.Platform.Application   commands, handlers, behaviors
│   └── Ligature.Platform.Persistence   EF Core, repositories, migrations
├── Host/Ligature.Host                  the deployable application: HTTP, carriers, composition
├── Tools/Ligature.Provisioning         seeds a database; deliberately NOT part of the host
└── Shared/Ligature.SharedKernel        Entity, AggregateRoot, StronglyTypedId, ICommand
```

### Before you change anything

Three documents govern this repository, and they are not decoration.

| | |
| --- | --- |
| **`AGENTS.md`** | How to work: the owner-driven workflow, commands, validation, branches, commits. Read §2 first. |
| **`docs/architecture.md`** | The architectural contract: module boundaries, packaging, established patterns. Authoritative on architectural questions. |
| **`docs/requirements.md`** | The requirement catalogue, and the Known Gaps list. Being back-filled. |

The short version: analyse and plan before implementing, do not make
architectural decisions silently, and follow the patterns already in the code
rather than introducing new ones.
