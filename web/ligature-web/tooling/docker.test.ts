import { spawnSync } from "node:child_process";
import { existsSync, readFileSync } from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";
import { parse } from "yaml";

/**
 * The development overlay, proved by its STRUCTURE rather than by its text
 * (AGENTS.md, Docker).
 *
 * These assertions parse the Compose files, so a reordered key or a reformatted
 * block changes nothing here, while a missing read-only flag or a vanished
 * volume fails. Nothing in this file runs Docker: `npm test` must stay free of
 * it.
 */

const REPO_ROOT = path.resolve(import.meta.dirname, "..", "..", "..");

const BASE = path.join(REPO_ROOT, "compose.yaml");

const OVERLAY = path.join(REPO_ROOT, "compose.dev.yaml");

const DOCKERIGNORE = path.join(REPO_ROOT, ".dockerignore");

interface ComposeService {
  readonly build?: { readonly target?: string };
  readonly ports?: readonly string[];
  readonly volumes?: readonly string[];
  readonly environment?: Readonly<Record<string, string>>;
}

interface ComposeFile {
  readonly services?: Readonly<Record<string, ComposeService>>;
  readonly volumes?: Readonly<Record<string, unknown>>;
}

function read(file: string): ComposeFile {
  return parse(readFileSync(file, "utf8")) as ComposeFile;
}

/** The pattern list, without comments or blank lines. */
function ignorePatterns(): string[] {
  return readFileSync(DOCKERIGNORE, "utf8")
    .split("\n")
    .map((line) => line.trim())
    .filter((line) => line !== "" && !line.startsWith("#"));
}

/**
 * The five projects the host is built from. The obj/ and bin/ of each is a
 * named volume, so the container's Linux intermediates never meet the
 * developer's macOS ones in the bind-mounted tree.
 */
const PROJECTS = [
  "src/Host/Ligature.Host",
  "src/Platform/Ligature.Platform.Application",
  "src/Platform/Ligature.Platform.Domain",
  "src/Platform/Ligature.Platform.Persistence",
  "src/Shared/Ligature.SharedKernel",
];

describe("the build context", () => {
  /**
   * The web client has to enter the build context now that an image builds it,
   * and its certificate directory holds a PRIVATE KEY. Excluding the whole of
   * web/ used to do this job by accident; it has to be said deliberately now.
   */
  it("excludes the development certificate and its key", () => {
    expect(ignorePatterns()).toContain("web/**/.certs/");
  });

  it("excludes the web client's installed packages", () => {
    expect(ignorePatterns()).toContain("web/**/node_modules/");
  });

  it("no longer excludes the whole web client, which an image now builds", () => {
    expect(ignorePatterns()).not.toContain("web/");
  });

  /** The rules that were already there, which this change must not lose. */
  it.each([".env", ".env.*", ".secrets/", "*.token"])("still excludes %s", (pattern) => {
    expect(ignorePatterns()).toContain(pattern);
  });
});

describe("the base Compose file", () => {
  /**
   * Deliberate: `docker compose up` keeps its production-shaped meaning, and
   * the browser-development path is an explicit overlay. A web service here
   * would make the default invocation mean something else.
   */
  it("declares no web service, because development is an overlay", () => {
    expect(Object.keys(read(BASE).services ?? {})).not.toContain("web");
  });
});

describe("the development overlay", () => {
  const overlay = read(OVERLAY);

  const web = overlay.services?.web;

  const host = overlay.services?.host;

  it("adds the web service", () => {
    expect(web).toBeDefined();
  });

  it("publishes the origin the browser and the cookie already expect", () => {
    expect(web?.ports ?? []).toContain("5173:5173");
  });

  /**
   * Read-only, and it is the developer's certificate: nothing in Docker creates,
   * installs or trusts one. Trust lives in the macOS keychain, where mkcert put
   * it.
   */
  it("mounts the developer's certificate read-only", () => {
    const mounts = web?.volumes ?? [];

    expect(mounts.some((mount) => mount.includes(".certs") && mount.endsWith(":ro"))).toBe(true);
  });

  /**
   * The host header must survive both hops, or the API's cross-site check
   * refuses every state-changing request.
   */
  it("points the proxy at the API over the Compose network", () => {
    expect(web?.environment?.LIGATURE_WEB_API_ORIGIN).toBe("http://host:8080");
  });

  it("tells the dev server it is in a container, so it binds every interface", () => {
    expect(web?.environment?.LIGATURE_WEB_IN_CONTAINER).toBe("true");
  });

  /**
   * Never bind-mounted from macOS: the host's node_modules holds native
   * binaries built for the wrong platform.
   */
  it("keeps the web client's packages in a named volume", () => {
    const mounts = web?.volumes ?? [];

    expect(mounts.some((mount) => mount.endsWith(":/app/node_modules"))).toBe(true);
  });

  it("runs the API from its development target rather than the production image", () => {
    expect(host?.build?.target).toBe("host-dev");
  });

  it.each(PROJECTS.flatMap((project) => [`${project}/obj`, `${project}/bin`]))(
    "keeps container build output for %s out of the bind-mounted tree",
    (directory) => {
      const mounts = host?.volumes ?? [];

      expect(mounts.some((mount) => mount.endsWith(`:/source/${directory}`))).toBe(true);
    },
  );

  it("keeps the NuGet cache in a named volume", () => {
    const mounts = host?.volumes ?? [];

    expect(mounts.some((mount) => mount.includes("nuget"))).toBe(true);
  });

  it("declares every named volume it mounts", () => {
    const declared = Object.keys(overlay.volumes ?? {});

    const named = [...(web?.volumes ?? []), ...(host?.volumes ?? [])]
      .map((mount) => mount.split(":")[0] ?? "")
      .filter((source) => source !== "" && !source.startsWith(".") && !source.startsWith("/"));

    expect(named.length).toBeGreaterThan(0);

    for (const volume of new Set(named)) {
      expect(declared).toContain(volume);
    }
  });
});

describe("the developer environment's prerequisite check", () => {
  /**
   * ./up.sh --check reports what is missing and changes nothing. It runs the
   * docker CLI if there is one, but does not require it: the certificate
   * failure below is reported either way, so this test needs no Docker.
   */
  it("refuses when the certificate is absent, and says how to create one", () => {
    const result = spawnSync("./up.sh", ["--check"], {
      cwd: REPO_ROOT,
      env: { ...process.env, LIGATURE_CERTS_DIR: "/nonexistent/certs" },
      encoding: "utf8",
    });

    expect(result.status).not.toBe(0);
    expect(result.stderr).toContain("localhost.pem");
    expect(result.stderr).toContain("mkcert");
  });

  /**
   * It must not create one either. mkcert installs a certificate authority into
   * the system trust store, which needs the developer's password and is not a
   * thing a script does on their behalf.
   */
  it("does not create the certificate it asked for", () => {
    const directory = "/nonexistent/certs";

    spawnSync("./up.sh", ["--check"], {
      cwd: REPO_ROOT,
      env: { ...process.env, LIGATURE_CERTS_DIR: directory },
      encoding: "utf8",
    });

    expect(existsSync(directory)).toBe(false);
  });
});
