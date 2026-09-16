// Violation: app/ imports the API client instead of composing a module.
import { client } from "@/shared/api/client";

export const leak = client;
