// Violation: web storage outside the session hint source.
export const remember = () => { localStorage.setItem("key", "value"); };
