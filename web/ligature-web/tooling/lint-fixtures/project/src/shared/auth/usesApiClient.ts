// Control: shared/auth may import the API client.
import { client } from "../api/client";

export const describeClient = () => String(client);
