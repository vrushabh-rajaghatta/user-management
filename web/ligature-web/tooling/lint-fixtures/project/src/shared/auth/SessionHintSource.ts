// Control: shared/auth may import the API client, and this one file may use web storage.
import { client } from "../api/client";

export const readHint = () => sessionStorage.getItem("hint") ?? String(client);
