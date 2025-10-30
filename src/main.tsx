import React from "react";
import ReactDOM from "react-dom/client";
import { WorldProvider } from "koota/react";
import { world } from "./ecs";
import App from "./react-components/App";
import "./style.css";

ReactDOM.createRoot(document.getElementById("root") as HTMLElement).render(
  <React.StrictMode>
    <WorldProvider world={world}>
      <App />
    </WorldProvider>
  </React.StrictMode>
);
