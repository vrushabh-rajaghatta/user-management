import { existsSync, readFileSync } from "node:fs";
import path from "node:path";
import type { ProxyOptions } from "vite";

/**
 * The development server's transport: HTTPS with a locally trusted
 * certificate, and a same-origin proxy for /api. Kept out of vite.config.ts so
 * both halves can be exercised by tests rather than trusted as configuration.
 */

/** The ./up.sh host. */
export const DEFAULT_API_ORIGIN = "http://localhost:8080";

/** Set this to point the proxy at a host started with `dotnet run`. */
export const API_ORIGIN_VARIABLE = "LIGATURE_WEB_API_ORIGIN";

export const CERTIFICATE_FILE = "localhost.pem";

export const CERTIFICATE_KEY_FILE = "localhost-key.pem";

/**
 * The origin the proxy forwards to: the override when one is set, the ./up.sh
 * host otherwise.
 *
 * A value that is present but is not a bare http(s) origin throws rather than
 * falling back to the default. A typo would otherwise send requests to a host
 * nobody chose, and fail as something that looks like a backend fault.
 */
export function resolveApiOrigin(environment: Record<string, string | undefined>): string {
  const configured = environment[API_ORIGIN_VARIABLE];

  if (configured === undefined || configured.trim() === "") {
    return DEFAULT_API_ORIGIN;
  }

  let url: URL;

  try {
    url = new URL(configured.trim());
  } catch {
    throw new Error(
      `${API_ORIGIN_VARIABLE} is '${configured}', which is not a URL. `
        + `Set it to an origin such as http://localhost:5000, or unset it to use ${DEFAULT_API_ORIGIN}.`,
    );
  }

  const bare = url.pathname === "/" && url.search === "" && url.hash === "" && url.username === "" && url.password === "";

  if ((url.protocol !== "http:" && url.protocol !== "https:") || !bare) {
    throw new Error(
      `${API_ORIGIN_VARIABLE} is '${configured}', which is not a bare http(s) origin. `
        + "Give the scheme, host and port only, with no path.",
    );
  }

  return url.origin;
}

/**
 * /api goes to the host with the browser's Host header left as it is.
 *
 * changeOrigin stays false, and that is load-bearing. The host's cross-site
 * protection (docs/architecture.md §17, Amendment: cookie transport) compares a
 * request's Origin with the Host it receives. With changeOrigin the proxy would
 * rewrite Host to the backend's own address, the browser's Origin would no
 * longer match it, and every state-changing request that reaches the Origin
 * rule would be refused.
 *
 * Nothing else is rewritten either: no forwarded headers, which the host does
 * not read, and no cookie rewriting, so the carrier cookie reaches the browser
 * exactly as the host set it.
 */
export function apiProxy(apiOrigin: string): Record<string, ProxyOptions> {
  return {
    "/api": {
      target: apiOrigin,
      changeOrigin: false,
    },
  };
}

/**
 * The certificate and key for https://localhost, or a refusal naming how to
 * create them.
 *
 * There is deliberately no fallback to HTTP. The carrier cookie is Secure and
 * __Host- prefixed, and whether a browser accepts that over plain
 * http://localhost differs between browsers — a development server that
 * silently downgraded would make sign-in work in one browser and fail without
 * explanation in another.
 */
export function devServerHttps(certificateDirectory: string): { cert: Buffer; key: Buffer } {
  const certificate = path.join(certificateDirectory, CERTIFICATE_FILE);
  const key = path.join(certificateDirectory, CERTIFICATE_KEY_FILE);

  const missing = [certificate, key].filter((file) => !existsSync(file));

  if (missing.length > 0) {
    throw new Error(
      [
        "The development server runs over HTTPS only, and its certificate is missing:",
        ...missing.map((file) => `  ${file}`),
        "",
        "Create a locally trusted certificate once with mkcert (see AGENTS.md, Web client):",
        "  brew install mkcert nss",
        "  mkcert -install",
        `  mkcert -cert-file ${path.join(certificateDirectory, CERTIFICATE_FILE)} \\`,
        `         -key-file ${path.join(certificateDirectory, CERTIFICATE_KEY_FILE)} localhost`,
      ].join("\n"),
    );
  }

  return { cert: readFileSync(certificate), key: readFileSync(key) };
}
