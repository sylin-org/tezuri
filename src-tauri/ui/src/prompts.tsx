// The one dialog primitive: a promise-based form/confirm modal in the family
// dialect. Tauri's webview does not implement window.prompt/confirm/alert,
// so every ask in the product goes through here and gets a real answer.

import { useEffect, useState } from "react";

export interface Field {
  key: string;
  label: string;
  placeholder?: string;
  initial?: string;
}

type Request =
  | {
      kind: "form";
      title: string;
      hint?: string;
      fields: Field[];
      confirmLabel: string;
      resolve: (values: Record<string, string> | null) => void;
    }
  | {
      kind: "confirm";
      title: string;
      body: string;
      confirmLabel: string;
      danger?: boolean;
      resolve: (ok: boolean) => void;
    };

// Single-window app: one module-level handoff from ask*() to the mounted host.
let push: ((r: Request) => void) | null = null;

export function askForm(opts: {
  title: string;
  hint?: string;
  fields: Field[];
  confirmLabel?: string;
}): Promise<Record<string, string> | null> {
  return new Promise((resolve) => {
    push?.({
      kind: "form",
      confirmLabel: opts.confirmLabel ?? "Save",
      ...opts,
      resolve,
    });
  });
}

export function askConfirm(opts: {
  title: string;
  body: string;
  confirmLabel?: string;
  danger?: boolean;
}): Promise<boolean> {
  return new Promise((resolve) => {
    push?.({
      kind: "confirm",
      confirmLabel: opts.confirmLabel ?? "Confirm",
      ...opts,
      resolve,
    });
  });
}

export function ModalHost() {
  const [req, setReq] = useState<Request | null>(null);
  const [values, setValues] = useState<Record<string, string>>({});

  useEffect(() => {
    push = (r: Request) => {
      if (r.kind === "form") {
        setValues(Object.fromEntries(r.fields.map((f) => [f.key, f.initial ?? ""])));
      }
      setReq(r);
    };
    return () => { push = null; };
  }, []);

  useEffect(() => {
    if (!req) return;
    const h = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        if (req.kind === "confirm") req.resolve(false);
        else req.resolve(null);
        setReq(null);
      }
    };
    window.addEventListener("keydown", h);
    return () => window.removeEventListener("keydown", h);
  }, [req]);

  if (!req) return null;

  const cancel = () => {
    if (req.kind === "confirm") req.resolve(false);
    else req.resolve(null);
    setReq(null);
  };

  return (
    <div className="modal-backdrop" onMouseDown={(e) => { if (e.target === e.currentTarget) cancel(); }}>
      <div className="modal" role="dialog" aria-modal="true" aria-label={req.title}>
        <h3>{req.title}</h3>
        {req.kind === "form" ? (
          <form onSubmit={(e) => { e.preventDefault(); req.resolve(values); setReq(null); }}>
            {req.hint && <p className="modal-hint">{req.hint}</p>}
            {req.fields.map((f, i) => (
              <label key={f.key}>
                {f.label}
                <input
                  autoFocus={i === 0}
                  value={values[f.key] ?? ""}
                  placeholder={f.placeholder}
                  onChange={(e2) => setValues({ ...values, [f.key]: e2.target.value })}
                />
              </label>
            ))}
            <div className="modal-actions">
              <button type="button" onClick={cancel}>Cancel</button>
              <button type="submit" className="primary">{req.confirmLabel}</button>
            </div>
          </form>
        ) : (
          <>
            <p className="modal-body">{req.body}</p>
            <div className="modal-actions">
              <button onClick={cancel}>Cancel</button>
              <button
                autoFocus
                className={req.danger ? "danger" : "primary"}
                onClick={() => { req.resolve(true); setReq(null); }}
              >{req.confirmLabel}</button>
            </div>
          </>
        )}
      </div>
    </div>
  );
}
