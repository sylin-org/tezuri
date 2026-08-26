// Dev-only stand-in for the Tauri bridge so the interface can be styled and
// checked in a plain browser. Loaded by main.tsx only when vite is running in
// development AND the real bridge is absent; the packaged application never
// imports it. The data here is synthetic — a fake publication, fake articles.
import React from "react";
import ReactDOM from "react-dom/client";
import App from "./App";
import "./styles.css";

async function boot() {
  if (import.meta.env.DEV && !(window as any).__TAURI__) {
    const { installMock } = await import("./mock");
    installMock();
  }
  ReactDOM.createRoot(document.getElementById("root")!).render(
    <React.StrictMode>
      <App />
    </React.StrictMode>
  );
}

void boot();
