// Violation: a hook imports the API client instead of an API operation.
import { client } from "@/shared/api/client";

export const bypass = client;
