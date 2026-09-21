import { execFileSync, spawn, type ChildProcess } from "node:child_process";
import { randomBytes, randomUUID } from "node:crypto";
import { mkdtemp, readFile, rm } from "node:fs/promises";
import { request } from "node:http";
import { request as secureRequest } from "node:https";
import { tmpdir } from "node:os";
import path from "node:path";
import { APP_ROOT, HOST_PORT, MAINTENANCE_URI, REPO_ROOT, WEB_PORT } from "./prerequisites.ts";

/**
 * A whole SKSMCorp installation, created for one run of this suite and removed
 * afterwards (docs/frontend-architecture.md §16).
 *
 * It is built with the PRODUCTION tooling, in the order AGENTS.md section 3
 * gives: migrate, deploy the audit schema and catalogue, then provision. There
 * is no test-only endpoint and no back door for tokens — the bootstrap
 * administrator's activation token comes out of the provisioning CLI exactly as
 * it does for a real installation.
 *
 * It never touches the developer's own database. A host that happened to be
 * running already is not reused, because it would be pointed at one.
 */

/** The password the .NET fixtures use for the three development roles. */
const ROLE_PASSWORD = "sksmcorp-test-role";

export const WEB_ORIGIN = `https://localhost:${String(WEB_PORT)}`;

export const HOST_ORIGIN = `http://localhost:${String(HOST_PORT)}`;

export interface HostEnvironment {
  readonly databaseName: string;
  readonly username: string;
  readonly activationToken: string;
}

let host: ChildProcess | undefined;
let web: ChildProcess | undefined;
let databaseName: string | undefined;
let workspace: string | undefined;

function connectionFor(role: string, database: string): string {
  return `Host=localhost;Port=5432;Database=${database};Username=${role};Password=${ROLE_PASSWORD}`;
}

function targetUri(database: string): string {
  return `postgresql://postgres:postgres@localhost:5432/${database}`;
}

function psql(sql: string, uri = MAINTENANCE_URI): void {
  execFileSync("psql", [uri, "-v", "ON_ERROR_STOP=1", "-c", sql], { stdio: "pipe" });
}

/** The environment a .NET tool runs with: no inherited SKSMCorp settings. */
function toolEnvironment(overrides: Record<string, string>): NodeJS.ProcessEnv {
  const inherited = Object.fromEntries(
    Object.entries(process.env).filter(([name]) => !name.startsWith("SKSMCORP_")),
  );

  return { ...inherited, ...overrides };
}

function run(command: string, args: string[], overrides: Record<string, string>): void {
  try {
    execFileSync(command, args, { cwd: REPO_ROOT, env: toolEnvironment(overrides), stdio: "pipe" });
  } catch (failure) {
    // execFileSync's own message says only that the command failed. The reason
    // is in the output, and without it a broken prerequisite looks like a
    // broken harness — NuGet warnings are not a diagnosis.
    const streams = failure as { stdout?: Buffer; stderr?: Buffer };

    const said = [streams.stdout?.toString() ?? "", streams.stderr?.toString() ?? ""]
      .join("\n")
      .split("\n")
      .filter((line) => line.trim() !== "" && !line.includes("NU1903"))
      .slice(-25)
      .join("\n");

    throw new Error(`${command} ${args.join(" ")} failed:\n${said}`);
  }
}

async function waitFor(what: string, probe: () => Promise<boolean>, seconds = 120): Promise<void> {
  const deadline = Date.now() + seconds * 1000;

  while (Date.now() < deadline) {
    if (await probe()) {
      return;
    }

    await new Promise((resolve) => setTimeout(resolve, 500));
  }

  throw new Error(`${what} did not become ready within ${String(seconds)}s.`);
}

function answered(origin: string): Promise<boolean> {
  return new Promise((resolve) => {
    const secure = origin.startsWith("https:");
    const send = secure ? secureRequest : request;

    const attempt = send(
      `${origin}/api/auth/sign-in`,
      // Readiness only. The proofs themselves are issued by a real browser that
      // validates the certificate; this just asks whether anything is listening.
      { method: "POST", rejectUnauthorized: false, timeout: 2000 },
      (response) => {
        response.resume();
        resolve(response.statusCode !== undefined);
      },
    );

    attempt.on("error", () => {
      resolve(false);
    });
    attempt.on("timeout", () => {
      attempt.destroy();
      resolve(false);
    });
    attempt.end();
  });
}

export async function createEnvironment(): Promise<HostEnvironment> {
  databaseName = `sksmcorp_web_${randomUUID().replace(/-/g, "")}`;
  workspace = await mkdtemp(path.join(tmpdir(), "sksmcorp-web-host-"));

  psql(`CREATE DATABASE "${databaseName}"`);

  // The roles foundation, run with the production script rather than a
  // hand-rolled imitation of it. Creating the roles is not enough: since
  // PostgreSQL 15 nothing may create objects in schema public by default, so
  // this is also what grants migration_role CREATE on it, the other two USAGE,
  // and installs btree_gist. It is the first step of a customer installation,
  // and it runs against the target database because those grants are
  // database-scoped.
  execFileSync(
    "psql",
    [
      targetUri(databaseName), "-v", "ON_ERROR_STOP=1",
      "-v", `database=${databaseName}`,
      "-v", `app_password=${ROLE_PASSWORD}`,
      "-v", `migration_password=${ROLE_PASSWORD}`,
      "-v", `provisioning_password=${ROLE_PASSWORD}`,
      "-f", path.join(REPO_ROOT, "docker", "roles.sql"),
    ],
    { stdio: "pipe" },
  );

  run("dotnet", ["ef", "database", "update", "--project", "src/Platform/SKSMCorp.Platform.Persistence"], {
    SKSMCORP_CONNECTION: connectionFor("migration_role", databaseName),
  });

  // The audit tables cannot be an EF migration: whoever runs CREATE TABLE owns
  // them, and an owner can disable its own triggers. This deploys them as
  // audit_owner, and the event catalogue with them — the host verifies its
  // compiled declarations against that catalogue and refuses to start without
  // it.
  run("dotnet", ["run", "--project", "src/Tools/SKSMCorp.AuditSchema"], {
    SKSMCORP_PRIVILEGED_CONNECTION: `Host=localhost;Port=5432;Database=${databaseName};Username=postgres;Password=postgres`,
  });

  const username = "ada.lovelace";
  const tokenFile = path.join(workspace, "bootstrap.token");

  run(
    "dotnet",
    [
      "run", "--project", "src/Tools/SKSMCorp.Provisioning", "--",
      "--first-name", "Ada",
      "--last-name", "Lovelace",
      "--display-name", "Ada Lovelace",
      "--email", "ada@example.test",
      "--username", username,
      "--activation-token-out", tokenFile,
    ],
    { SKSMCORP_CONNECTION: connectionFor("provisioning_role", databaseName) },
  );

  const activationToken = (await readFile(tokenFile, "utf8")).trim();

  // The host connects as app_role, never as a superuser: provisioning refuses a
  // tenant whose application role could write the audit trail, and that
  // property is what the boundary rests on.
  host = spawn("dotnet", ["run", "--project", "src/Host/SKSMCorp.Host"], {
    cwd: REPO_ROOT,
    env: toolEnvironment({
      SKSMCORP_CONNECTION: connectionFor("app_role", databaseName),
      SKSMCORP_SIGNING_KEY_CURRENT: "v1",
      SKSMCORP_SIGNING_KEY_V1: randomBytes(32).toString("base64"),
      ASPNETCORE_URLS: HOST_ORIGIN,
      ASPNETCORE_ENVIRONMENT: "Production",
    }),
    stdio: "pipe",
  });

  await waitFor("The host", () => answered(HOST_ORIGIN));

  // The real development server: HTTPS with the mkcert certificate, proxying
  // /api with the browser's Host header left alone.
  web = spawn("npx", ["vite"], {
    cwd: APP_ROOT,
    env: toolEnvironment({ SKSMCORP_WEB_API_ORIGIN: HOST_ORIGIN }),
    stdio: "pipe",
  });

  await waitFor("The development server", () => answered(WEB_ORIGIN));

  return { databaseName, username, activationToken };
}

export async function destroyEnvironment(): Promise<void> {
  for (const child of [web, host]) {
    child?.kill("SIGTERM");
  }

  web = undefined;
  host = undefined;

  // Give the host a moment to release its connections before the drop.
  await new Promise((resolve) => setTimeout(resolve, 2000));

  if (databaseName !== undefined) {
    try {
      psql(`DROP DATABASE IF EXISTS "${databaseName}" WITH (FORCE)`);
    } finally {
      databaseName = undefined;
    }
  }

  if (workspace !== undefined) {
    await rm(workspace, { recursive: true, force: true });
    workspace = undefined;
  }
}
