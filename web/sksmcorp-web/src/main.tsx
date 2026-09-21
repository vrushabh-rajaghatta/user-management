import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import "./index.css";
import { App } from "@/app/App";

const root = document.getElementById("root");

// index.html owns this element. Its absence is a broken build, not a state
// to render around.
if (root === null) {
  throw new Error("index.html has no #root element.");
}

createRoot(root).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
