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

const DOCKERFILE = path.join(REPO_ROOT, "docker", "Dockerfile");

interface ComposeHealthcheck {
  readonly test?: readonly string[] | string;
  readonly interval?: string;
  readonly retries?: number;
  readonly start_period?: string;
}

interface ComposeDependency {
  readonly condition?: string;
}

interface ComposeService {
  readonly build?: { readonly target?: string };
  readonly depends_on?: Readonly<Record<string, ComposeDependency>>;
  readonly ports?: readonly string[];
  readonly volumes?: readonly string[];
  readonly environment?: Readonly<Record<string, string>>;
  readonly healthcheck?: ComposeHealthcheck;
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

/**
 * PRV-C2's deployment step (docs/requirements.md). It lives in the BASE file,
 * not the overlay: reconciling a release's catalogue with the database it is
 * deployed against is what a deployment does, not a convenience for developers.
 *
 * The chain it belongs to, and the reason the order is not arbitrary:
 *
 *     roles -> migrator -> audit-schema -> catalogue-sync -> host
 *
 * audit-schema first, because it deploys both the corrected
 * PermissionCatalogUpdated definition the synchronisation records itself with
 * and the 005 privileges without which it could not record anything. The host
 * last, because a refused synchronisation must stop the deployment.
 */
describe("catalogue synchronisation in the deployment chain", () => {
  const base = read(BASE);

  const sync = base.services?.["catalogue-sync"];

  const dockerfile = readFileSync(DOCKERFILE, "utf8");

  it("is a step in the base file, because it is part of deploying", () => {
    expect(sync).toBeDefined();
    expect(sync?.build?.target).toBe("catalogue-sync");
  });

  /**
   * PE2 puts catalogue writes under a migration role. provisioning_role's
   * catalogue privileges were deliberately removed by audit script 004, so
   * running this as that role would both break and undo a decision.
   */
  it("connects as the migration role, and as nothing else", () => {
    // The anchor is resolved by the parser, so this reads the credential the
    // container is actually given rather than the alias it was written as.
    const connection = sync?.environment?.LIGATURE_CONNECTION ?? "";

    expect(connection).toContain("Username=migration_role");
    expect(connection).not.toContain("provisioning_role");
    expect(connection).not.toContain("app_role");
  });

  it("runs after the audit schema has completed", () => {
    expect(sync?.depends_on?.["audit-schema"]?.condition).toBe("service_completed_successfully");
  });

  /**
   * FAIL CLOSED, and this is the deployment-level expression of it. A refusal
   * exits 2 and an operational failure exits 3; Compose treats both as "did not
   * complete successfully", so neither starts the API. Serving against a
   * catalogue state the release has determined is unsafe is the outcome this
   * prevents.
   */
  it("gates the host, which no longer depends on the audit schema directly", () => {
    const host = base.services?.host;

    expect(host?.depends_on?.["catalogue-sync"]?.condition).toBe("service_completed_successfully");
    expect(host?.depends_on?.["audit-schema"]).toBeUndefined();
  });

  /**
   * The image has to contain the executable the entry point names. A target
   * that copies from a publish output nobody wrote produces a container that
   * starts and immediately cannot find its DLL — which looks like a runtime
   * fault rather than a build one.
   */
  it("publishes the tool into the output its target copies from", () => {
    expect(dockerfile).toContain("--output /out/catalogue-sync");
    expect(dockerfile).toContain("COPY --from=build /out/catalogue-sync ./");
    expect(dockerfile).toContain('ENTRYPOINT ["dotnet", "Ligature.CatalogueSync.dll"]');
  });

  it("restores the tool's project, so the publish is not resolving it by accident", () => {
    expect(dockerfile).toContain("dotnet restore src/Tools/Ligature.CatalogueSync/Ligature.CatalogueSync.csproj");
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

  /**
   * The environment reports whether it is still serving what it promised.
   *
   * ./up.sh proves five criteria at STARTUP and says nothing about the minutes
   * after. A dev server that comes back on plain HTTP — which happened, and
   * cost an afternoon — still reads `Up` in `docker compose ps` while being
   * unreachable through the published port. These checks make that state
   * visible.
   *
   * They do NOT cure it. Compose's `restart:` policy acts on process EXIT, not
   * on health, so nothing restarts an unhealthy container here; the cure stays
   * a developer restarting it, and the note in AGENTS.md says so.
   */
  describe("the health of a running environment", () => {
    /** Compose accepts a string or an argv array; both are read the same way here. */
    const probe = (service: ComposeService | undefined): string => {
      const test = service?.healthcheck?.test ?? [];

      return typeof test === "string" ? test : test.join(" ");
    };

    it("checks the web client's health", () => {
      expect(web?.healthcheck).toBeDefined();
    });

    /**
     * THE POINT OF THE CHECK. Probing http://localhost:5173 would pass in
     * exactly the broken state this exists to catch, because a dev server that
     * lost its TLS configuration answers plain HTTP perfectly well.
     */
    it("probes the web client over HTTPS, since plain HTTP is the failure it looks for", () => {
      expect(probe(web)).toContain("https");
      expect(probe(web)).toContain("5173");
    });

    /**
     * node:24-bookworm-slim carries neither curl nor wget. A probe written with
     * either is not a failing check — it is a check that can never pass, and
     * would report a healthy server as broken forever.
     */
    it("probes the web client with node, the only client its image has", () => {
      expect(probe(web)).toContain("node");
      expect(probe(web)).not.toContain("curl");
      expect(probe(web)).not.toContain("wget");
    });

    it("checks the API's health", () => {
      expect(host?.healthcheck).toBeDefined();
    });

    it("gives each check an interval and a grace period, so a slow start is not a failure", () => {
      for (const service of [web, host]) {
        expect(service?.healthcheck?.interval).toBeDefined();
        expect(service?.healthcheck?.start_period).toBeDefined();
      }
    });
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
