import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { createServer as createHttpServer, request, type IncomingHttpHeaders, type Server } from "node:http";
import type { AddressInfo } from "node:net";
import { tmpdir } from "node:os";
import path from "node:path";
import { createServer as createViteServer, type ProxyOptions, type ViteDevServer } from "vite";
import { afterEach, describe, expect, it } from "vitest";
import {
  API_ORIGIN_VARIABLE,
  CERTIFICATE_FILE,
  CERTIFICATE_KEY_FILE,
  CONTAINER_VARIABLE,
  DEFAULT_API_ORIGIN,
  apiProxy,
  devServerHttps,
  resolveApiOrigin,
  resolveDevServerHost,
} from "./dev-server.ts";

const CARRIER_COOKIE = "__Host-ligature=v1.c2Vzc2lvbg.c2lnbmF0dXJl; Path=/; Secure; HttpOnly; SameSite=Strict";

describe("the API proxy", () => {
  const cleanups: (() => Promise<void>)[] = [];

  afterEach(async () => {
    for (const cleanup of cleanups.splice(0).reverse()) {
      await cleanup();
    }
  });

  /**
   * The host's cross-site protection compares Origin with the Host it receives,
   * so the proxy must deliver the browser's Host untouched. Proved through a
   * real Vite server and a stand-in upstream that records what arrived.
   */
  it("forwards /api with the browser's Host header unchanged", async () => {
    const upstream = await startUpstream(cleanups);
    const proxy = await startProxy(cleanups, apiProxy(upstream.origin));

    const response = await send(proxy.port, "/api/auth/sign-in");

    expect(response.status).toBe(204);
    expect(upstream.received()?.host).toBe(`localhost:${String(proxy.port)}`);
  });

  /**
   * The positive control. The same request through a proxy that changes the
   * origin arrives with the upstream's own Host — so the test above would fail
   * if changeOrigin were switched on, rather than passing whatever the setting.
   */
  it("would present the upstream's own Host if the origin were changed", async () => {
    const upstream = await startUpstream(cleanups);
    const changed = apiProxy(upstream.origin)["/api"];

    const proxy = await startProxy(cleanups, { "/api": { ...(changed as ProxyOptions), changeOrigin: true } });

    await send(proxy.port, "/api/auth/sign-in");

    expect(upstream.received()?.host).toBe(new URL(upstream.origin).host);
  });

  it("passes the carrier cookie back exactly as the host set it", async () => {
    const upstream = await startUpstream(cleanups);
    const proxy = await startProxy(cleanups, apiProxy(upstream.origin));

    const response = await send(proxy.port, "/api/auth/sign-in");

    expect(response.headers["set-cookie"]).toEqual([CARRIER_COOKIE]);
  });

  /**
   * Behaviour 11 (B9). The host believes this header only from a proxy it is
   * configured to trust — in the dev stack, exactly this container — so the
   * per-IP limit sees the browser rather than the dev server. Outside that
   * configuration the host ignores it, which is the safe default.
   */
  it("tells the API who the browser is, in X-Forwarded-For", async () => {
    const upstream = await startUpstream(cleanups);
    const proxy = await startProxy(cleanups, apiProxy(upstream.origin));

    await send(proxy.port, "/api/auth/sign-in");

    expect(upstream.received()?.["x-forwarded-for"]).toMatch(/^(127\.0\.0\.1|::1|::ffff:127\.0\.0\.1)$/);
  });

  it("does not proxy paths outside /api", async () => {
    const upstream = await startUpstream(cleanups);
    const proxy = await startProxy(cleanups, apiProxy(upstream.origin));

    await send(proxy.port, "/openapi/v1.json");

    expect(upstream.received()).toBeUndefined();
  });
});

describe("the API origin", () => {
  it("is the ./up.sh host when no override is set", () => {
    expect(resolveApiOrigin({})).toBe(DEFAULT_API_ORIGIN);
    expect(resolveApiOrigin({ [API_ORIGIN_VARIABLE]: "   " })).toBe(DEFAULT_API_ORIGIN);
  });

  it("is the override when one is set", () => {
    expect(resolveApiOrigin({ [API_ORIGIN_VARIABLE]: "http://localhost:5000/" })).toBe("http://localhost:5000");
  });

  it.each([
    // Two different refusals. "localhost:5000" parses — as a URL whose scheme
    // is "localhost:" — so it is refused as another scheme; only a value that
    // does not parse at all reaches the not-a-URL branch.
    ["not a URL at all", "not an origin"],
    ["a host with no scheme", "localhost:5000"],
    ["a path", "http://localhost:5000/api"],
    ["a query", "http://localhost:5000/?x=1"],
    ["another scheme", "ftp://localhost:5000"],
  ])("refuses an override that is %s rather than falling back to the default", (_, value) => {
    expect(() => resolveApiOrigin({ [API_ORIGIN_VARIABLE]: value })).toThrow(API_ORIGIN_VARIABLE);
  });
});

describe("the development server's bind address", () => {
  /**
   * In a container the server must listen on every interface, or the published
   * port reaches nothing. On the developer's own machine it must keep listening
   * on loopback only.
   */
  it("listens on every interface inside a container", () => {
    expect(resolveDevServerHost({ [CONTAINER_VARIABLE]: "true" })).toBe("0.0.0.0");
  });

  it("is not case-sensitive about it", () => {
    expect(resolveDevServerHost({ [CONTAINER_VARIABLE]: "TRUE" })).toBe("0.0.0.0");
  });

  it.each([
    ["the variable is absent", {}],
    ["the variable is blank, which carries no instruction", { [CONTAINER_VARIABLE]: "" }],
    ["the variable says false", { [CONTAINER_VARIABLE]: "false" }],
  ])("listens on loopback when %s", (_, environment) => {
    expect(resolveDevServerHost(environment)).toBe("localhost");
  });

  /**
   * The regression that matters. Binding every interface by accident would put
   * a developer's application, and its proxy to a real API, on whatever network
   * the machine is attached to.
   */
  it("never listens on every interface by default", () => {
    expect(resolveDevServerHost({})).not.toBe("0.0.0.0");
    expect(resolveDevServerHost({})).not.toBe("");
  });

  /**
   * A typo must not silently mean loopback: inside a container that produces an
   * unreachable server, which looks like a broken Docker setup rather than a
   * mistyped value.
   */
  it.each(["1", "yes", "0.0.0.0"])("refuses %s rather than guessing what it meant", (value) => {
    expect(() => resolveDevServerHost({ [CONTAINER_VARIABLE]: value })).toThrow(CONTAINER_VARIABLE);
  });
});


describe("the development certificate", () => {
  const directories: string[] = [];

  afterEach(() => {
    for (const directory of directories.splice(0)) {
      rmSync(directory, { recursive: true, force: true });
    }
  });

  it("refuses to start without a certificate, and says how to create one", () => {
    const directory = temporaryDirectory(directories);

    expect(() => devServerHttps(directory)).toThrow(/mkcert -install/);
    expect(() => devServerHttps(directory)).toThrow(CERTIFICATE_KEY_FILE);
  });

  it("refuses when only one of the certificate and its key is present", () => {
    const directory = temporaryDirectory(directories);

    writeFileSync(path.join(directory, CERTIFICATE_FILE), "certificate");

    expect(() => devServerHttps(directory)).toThrow(CERTIFICATE_KEY_FILE);
  });

  it("serves the certificate and key when both are present", () => {
    const directory = temporaryDirectory(directories);

    writeFileSync(path.join(directory, CERTIFICATE_FILE), "certificate");
    writeFileSync(path.join(directory, CERTIFICATE_KEY_FILE), "key");

    const https = devServerHttps(directory);

    expect(https.cert.toString()).toBe("certificate");
    expect(https.key.toString()).toBe("key");
  });
});

function temporaryDirectory(directories: string[]): string {
  const directory = mkdtempSync(path.join(tmpdir(), "ligature-web-certs-"));

  directories.push(directory);

  return directory;
}

/** A stand-in host that records the headers of the request it received. */
async function startUpstream(cleanups: (() => Promise<void>)[]) {
  let headers: IncomingHttpHeaders | undefined;

  const server: Server = createHttpServer((incoming, outgoing) => {
    headers = incoming.headers;
    outgoing.writeHead(204, { "Set-Cookie": CARRIER_COOKIE });
    outgoing.end();
  });

  await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));

  cleanups.push(() => new Promise<void>((resolve) => server.close(() => { resolve(); })));

  const { port } = server.address() as AddressInfo;

  return {
    origin: `http://127.0.0.1:${String(port)}`,
    received: () => headers,
  };
}

/** A real Vite dev server carrying only the proxy under test, over plain HTTP. */
async function startProxy(cleanups: (() => Promise<void>)[], proxy: Record<string, ProxyOptions>) {
  const server: ViteDevServer = await createViteServer({
    configFile: false,
    root: mkdtempSync(path.join(tmpdir(), "ligature-web-proxy-")),
    logLevel: "silent",
    server: { host: "localhost", port: 0, strictPort: false, proxy, ws: false },
  });

  await server.listen();

  cleanups.push(() => server.close());

  const address = server.httpServer?.address() as AddressInfo;

  return { port: address.port };
}

/** A POST as a browser on the development origin would send it. */
function send(port: number, pathname: string) {
  return new Promise<{ status: number; headers: IncomingHttpHeaders }>((resolve, reject) => {
    const outgoing = request(
      {
        host: "localhost",
        port,
        path: pathname,
        method: "POST",
        headers: { Origin: `https://localhost:${String(port)}`, "Content-Type": "application/json" },
      },
      (incoming) => {
        incoming.resume();
        incoming.on("end", () => { resolve({ status: incoming.statusCode ?? 0, headers: incoming.headers }); });
      },
    );

    outgoing.on("error", reject);
    outgoing.end("{}");
  });
}
