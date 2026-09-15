import { setupServer } from "msw/node";
import { handlers } from "./handlers";

/**
 * The one MSW server for the web test project. Tests declare the responses
 * they depend on with server.use(...), and those are reset after every test.
 */
export const server = setupServer(...handlers);
