import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import "./index.css";
import PullRequestForRevitApp from "./components/PullRequestForRevitApp";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <PullRequestForRevitApp />
  </StrictMode>
);

