import React from "react";
import ReactDOM from "react-dom/client";
import App from "./App";
import "./styles.css";

// The desk never becomes a file-drop target: a stray drop on the chrome
// must not navigate the whole application to a raw file. Imports happen
// only inside the Write plane's editor.
window.addEventListener("dragover", (e) => e.preventDefault());
window.addEventListener("drop", (e) => e.preventDefault());

ReactDOM.createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>
);
