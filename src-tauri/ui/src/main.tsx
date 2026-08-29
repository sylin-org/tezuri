import React from "react";
import ReactDOM from "react-dom/client";
import App from "./App";
import "./styles.css";

// The desk never becomes a file-drop target: a stray drop on the chrome
// must not navigate the whole application to a raw file. Imports happen
// only inside the Write plane's editor. While a file drag is over the
// window, the desk fades and the Write frame's ancestor chain is lit —
// the visual grammar of where a drop can land.
let dragDepth = 0;
const hasFiles = (e: DragEvent) =>
  Array.from(e.dataTransfer?.types ?? []).includes("Files");
const setLanding = (on: boolean) => {
  document.body.classList.toggle("tz-file-drag", on);
  document.querySelectorAll(".tz-lit").forEach((el) => el.classList.remove("tz-lit"));
  if (on) {
    let el: Element | null = document.querySelector(".write-frame-host");
    while (el && el !== document.body) {
      el.classList.add("tz-lit");
      el = el.parentElement;
    }
  }
};
window.addEventListener("dragenter", (e) => {
  if (!hasFiles(e)) return;
  dragDepth += 1;
  setLanding(true);
});
window.addEventListener("dragleave", () => {
  if (dragDepth > 0) dragDepth -= 1;
  if (dragDepth === 0) setLanding(false);
});
window.addEventListener("dragover", (e) => e.preventDefault());
window.addEventListener("drop", (e) => {
  dragDepth = 0;
  setLanding(false);
  e.preventDefault();
});

ReactDOM.createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>
);
