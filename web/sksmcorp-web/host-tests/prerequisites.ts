import { execFileSync } from "node:child_process";
import { existsSync } from "node:fs";
import { createConnection } from "node:net";
import path from "node:path";
import { chromium } from "@playwright/test";

/**
 * Everything `npm run test:host` needs, checked before anything is created.
 *
 * A missing prerequisite FAILS, naming which one and how to provide it. It
 * never skips: a suite that quietly passed because PostgreSQL was unreachable
 * would report that the cookie transport works when nothing was exercised
 * (AGENTS.md section 3 says the same about the .NET suites).
 */

export const APP_ROOT = path.resolve(import.meta.dirname, "..");

export const REPO_ROOT = path.resolve(APP_ROOT, "..", "..");

export const HOST_PORT = 5080;

export const WEB_PORT = 5173;

export const MAINTENANCE_URI = "postgresql://postgres:postgres@localhost:5432/postgres";

function onPath(command: string): boolean {
  try {
    execFileSync("which", [command], { stdio: "pipe" });

    return true;
  } catch {
    return false;
  }
}

function portInUse(port: number): Promise<boolean> {
  return new Promise((resolve) => {
    const socket = createConnection({ host: "127.0.0.1", port });

    socket.on("connect", () => {
      socket.destroy();
      resolve(true);
    });

    socket.on("error", () => {
      resolve(false);
    });
  });
}

export async function requirePrerequisites(): Promise<void> {
  const missing: string[] = [];

  if (!onPath("psql")) {
    missing.push(
      "psql is not on PATH. The harness uses it to create and drop its throwaway database.\n"
        + "    Install the PostgreSQL client tools, for example: brew install libpq && brew link --force libpq",
    );
  } else {
    try {
      execFileSync("psql", [MAINTENANCE_URI, "-c", "select 1"], { stdio: "pipe" });
    } catch {
      missing.push(
        `PostgreSQL is not reachable at ${MAINTENANCE_URI}.\n`
          + "    Start it, and make sure the postgres role can create databases.",
      );
    }
  }

  if (!onPath("dotnet")) {
    missing.push("The .NET SDK is not on PATH. See AGENTS.md section 3.");
  } else {
    try {
      execFileSync("dotnet", ["ef", "--version"], { stdio: "pipe", cwd: REPO_ROOT });
    } catch {
      missing.push("dotnet-ef is not installed. Install it with: dotnet tool install --global dotnet-ef");
    }
  }

  for (const file of ["localhost.pem", "localhost-key.pem"]) {
    if (!existsSync(path.join(APP_ROOT, ".certs", file))) {
      missing.push(
        `The development certificate is missing: .certs/${file}\n`
          + "    These tests run over HTTPS only, because the carrier cookie is Secure and __Host- prefixed.\n"
          + "    Create one once with mkcert (see AGENTS.md, Web client):\n"
          + "      brew install mkcert nss && mkcert -install\n"
          + "      mkcert -cert-file .certs/localhost.pem -key-file .certs/localhost-key.pem localhost",
      );

      break;
    }
  }

  if (!existsSync(chromium.executablePath())) {
    missing.push(
      "Playwright's Chromium is not installed. Install just that browser with:\n"
        + "      npx playwright install chromium",
    );
  }

  for (const port of [HOST_PORT, WEB_PORT]) {
    if (await portInUse(port)) {
      missing.push(
        `Port ${String(port)} is already in use, and this suite starts its own host and development server.\n`
          + "    Stop whatever is listening (a running `npm run dev`, or ./up.sh) and try again.",
      );
    }
  }

  if (missing.length > 0) {
    throw new Error(
      ["npm run test:host cannot run:", "", ...missing.map((reason) => `  - ${reason}`), ""].join("\n"),
    );
  }
}
