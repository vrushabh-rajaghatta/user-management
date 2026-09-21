// Violation: web storage reached through globalThis.
export const remember = () => { globalThis.sessionStorage.setItem("key", "value"); };
