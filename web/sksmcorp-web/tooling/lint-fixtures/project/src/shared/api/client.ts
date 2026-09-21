// Control: the one file that may use the network.
export const client = { post: (url: string) => fetch(url, { method: "POST" }) };
