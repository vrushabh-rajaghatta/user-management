// Violation: web storage anywhere (docs/frontend-architecture.md §8, B6).
//
// THE NAME IS THE POINT. The lint exemption that made web storage legal was
// addressed to this exact path, so a fixture at any other path would pass even
// if the exemption were still there. It was a control until B6; it is a
// violation now, and that flip is what proves the exception is gone.
export const remember = () => { sessionStorage.setItem("key", "value"); };
