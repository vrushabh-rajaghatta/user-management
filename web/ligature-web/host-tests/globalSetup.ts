import type { TestProject } from "vitest/node";
import { createEnvironment, destroyEnvironment, HOST_ORIGIN, WEB_ORIGIN } from "./environment.ts";
import { requirePrerequisites } from "./prerequisites.ts";

declare module "vitest" {
  interface ProvidedContext {
    webOrigin: string;
    hostOrigin: string;
    username: string;
    activationToken: string;
  }
}

/**
 * One installation for the whole suite, torn down even when a test fails.
 */
export default async function setup(project: TestProject) {
  await requirePrerequisites();

  const environment = await createEnvironment();

  project.provide("webOrigin", WEB_ORIGIN);
  project.provide("hostOrigin", HOST_ORIGIN);
  project.provide("username", environment.username);
  project.provide("activationToken", environment.activationToken);

  return async () => {
    await destroyEnvironment();
  };
}
