# ADR 0014: Adopt the Sylin workstation design language

- Status: Accepted
- Date: 2026-08-13
- Supersedes: [ADR 0011](0011-sylin-semantics-without-runtime-coupling.md)

## Context

ADR 0011 established that Tezuri translates Sylin semantics with locally owned CSS and no runtime
dependency on the website. That boundary was correct and is retained. What failed was the
translation.

ADR 0011 described the target only in prose — "warm night-garden surfaces," "restrained gold/green
light" — and named `docs/design/SYLIN-VISUAL-CONTRACT.md` as the durable contract. That document
records no token values and no reconciliation date. With nothing pinned, the implementation drifted
to a palette that is not Sylin's, and the design contract was subsequently reworded to describe the
drift. The guardrail against drift became its record.

Measured against `src/assets/site.css` and `src/assets/image-tools.css` in the website repository,
the shipped client diverges on every load-bearing value:

| Role | sylin.org | Shipped Tezuri |
| --- | --- | --- |
| Ground | `#0f0e12` near-black violet | `#08110d` dark green |
| Primary light | `#fbbf24` amber | `#86b987` / `#b7dbac` moss |
| Accent | `#fcd34d` | `#d4b979` tan |
| Focus | `#60a5fa` blue | `#d8ef91` yellow-green |
| Destructive | `#fb7185` rose | `#f0b27d` orange |
| Type | system sans and mono, no serif | Georgia serif for brand, headings, titles |

Two consequences are functional, not cosmetic. Because the entire chrome is green, the reserved
"proved ready" green no longer signals anything — `.evidence-dot--passed` resolves to the same value
as the primary button, so success is invisible. And the cream `--paper: #f3efe2` editor card styles
Tezuri's editing chrome to impersonate a published article, which the product brief prohibits.

The prose target was also the wrong one. Sylin already has an application-shaped surface: the Image
Tools workspace. It is a denser, warmer, more instrumented dialect of the public site, and it is the
correct precedent for a writing workstation. ADR 0011 pointed at the public site's semantics and got
a public-site pastiche instead of a working room.

## Decision

Tezuri implements the **Sylin workstation dialect**: the public site's color and voice discipline in
the application grammar established by Image Tools. The no-runtime-coupling boundary from ADR 0011
is retained unchanged — these values are snapshotted locally, never fetched or built from the
website.

### Pinned tokens

Reconciled against `sylin-org/website` `src/assets/site.css` and `src/assets/image-tools.css` on
**2026-08-13**. The ink scale is Image Tools' warmer application scale, not the public site's neutral
scale. Any change to this table is a deliberate edit to this ADR, never an incidental CSS change.

```css
/* Surfaces */
--tz-bg: #0f0e12;             /* application ground */
--tz-bg-2: #131217;           /* recessed bands, option bars */
--tz-panel: #17161c;          /* panels, editor surface */
--tz-panel-2: #1c1a21;        /* nested panel */
--tz-rail: #111015;           /* navigation rail */
--tz-foot: #0d0c10;           /* footer, status bar */
--tz-line: rgb(255 255 255 / 10%);
--tz-line-soft: rgb(255 255 255 / 6%);
--tz-line-amber: rgb(251 191 36 / 30%);

/* Ink */
--tz-ink: #f5f2ea;            /* principal, warm off-white */
--tz-ink-2: #cec9bf;          /* secondary */
--tz-muted: #8e8994;          /* quiet, microlabels */
--tz-faint: #706b75;          /* furthest back */

/* Light and state */
--tz-amber: #fbbf24;          /* primary action, active context */
--tz-amber-soft: #fcd34d;     /* hover on amber */
--tz-on-amber: #18130a;       /* text on an amber fill */
--tz-blue: #60a5fa;           /* focus only */
--tz-green: #4ade80;          /* local-running, passed, ready only */
--tz-danger: #fb7185;         /* destructive, failed only */

/* Type */
--tz-sans: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
--tz-mono: "SFMono-Regular", Consolas, "Liberation Mono", monospace;
```

No serif. No webfont, font account, or decorative type dependency.

### Color semantics

Amber is primary action and active context. It is not a warning color and does not flood the editor.
The ink scale carries hierarchy before color does. Blue is focus and nothing else. **Green is
reserved** for local-running, proof-passed, and ready; rose is reserved for destructive and failed.
No state is communicated by color alone — every state carries text, and shape or icon where it
reinforces.

### Component grammar

| Element | Rule |
| --- | --- |
| Microlabel / eyebrow | 8–9px mono, `letter-spacing: .09–.12em`, uppercase; muted, or amber for a section eyebrow |
| Tab | Muted → ink on select, 2px amber underline via `::after` with a soft glow. Never a filled pill. |
| Active list row | `border-color: rgb(251 191 36 / 48%)`, `background: rgb(251 191 36 / 6.5%)`, `box-shadow: inset 2px 0 var(--tz-amber)` |
| Button | min-height 39px, radius 8px, `rgb(255 255 255 / 2.5%)`, 11px/620. Primary is an amber fill with `--tz-on-amber`. Quiet is transparent. |
| Field | height 35px, radius 7px, `--tz-panel` ground, 1px line |
| Panel | 1px line, radius 12px, `--tz-panel`; head row ≥43px with a 10px/620 name and an 8px mono fact |
| Local indicator | 6px `--tz-green` dot with glow beside uppercase mono text |
| Focus | `outline: 2px solid var(--tz-blue)`, `outline-offset: 2px`, on every interactive element |
| Radii | 5px inline, 7px fields, 8px buttons and tiles, 12px panels, 14–16px large surfaces |
| Motion | 120–180ms ease; 1px press translate. Fully disabled under `prefers-reduced-motion`. |

Chrome is sized in px on the Image Tools scale. Article prose keeps relative units so it reflows with
user zoom.

### Shape

The application adopts Image Tools' workstation behavior: at `min-width: 861px` and
`min-height: 720px` the shell becomes a `100dvh` grid with `overflow: hidden` and independently
scrolling regions. Below that it degrades to an ordinary scrolling document. Machinery collapses when
it has no work — the Image Tools intro shrinks and drops its description once content exists, and
Tezuri's equivalents follow that precedent rather than holding empty panels open.

The right rail stops being a mosaic of four equal always-open sections. **Write, Proof, and Publish
become named modes over one canvas** — views of the same file, as the product brief frames them —
with only the current mode's supporting machinery present. The article text is the visual center of
gravity in Write mode.

### Capability honesty

No control ships visible and permanently inert. Every affordance is either wired or absent, and
what is present is driven by the capability the API actually reports for the opened article. Where a
capability is genuinely unavailable, the interface says so once, in plain language, with the next
useful action — it does not render a dead button and explain the disappointment beside it.

### Voice

`VOICE.md` governs every label, empty state, and validation message. Protocol vocabulary — byte
ranges, SHA prefixes, envelope and projection language, patch mechanics — moves behind an evidence
disclosure. Front-of-house states a property and its evidence: `Saved to src/writing/craft/index.md`,
not `Ready to replace canonical byte range 1482–1620`. Preservation guarantees are demonstrated by
behavior; they are not narrated at the writer.

## Necessary changes

### Visual system

1. Replace the token block in `ClientApp/src/styles.css` with the pinned table. Delete `--night*`,
   `--moss*`, `--gold`, `--paper*`, the orange `--danger`, and the yellow-green `--focus`.
2. Remove Georgia from the brand mark, rail headings, document heading, article titles, and editor
   prose.
3. Remove the cream `--paper` editor card and its hand-rolled greys (`#e8e2d4`, `#d9d3c5`, `#c3bdaf`,
   `#d2cdbf`, `#d0cabc`). The writing surface is `--tz-panel` on the application ground.
4. Move chrome sizing to the px scale; keep prose relative.
5. Focus becomes `--tz-blue` everywhere.
6. Rebuild `.mode-tabs` as underlined tabs; remove the filled pill group.
7. Rebuild `.article-item.is-active` on the amber inset-bar pattern.
8. Give the status pill success and danger tones so passed, committed, and changed stop rendering
   identically.
9. Reserve green for local-running, passed, and ready; move failure and destructive states to rose.
10. Add the workstation `100dvh` layout above 861×720 with internal scroll regions.
11. Replace the 760px horizontally scrolling article strip with vertical navigation.

### Shape and capability

12. Restructure the shell into Write / Proof / Publish modes; publication machinery leaves the
    writing surface.
13. Drive rendering from reported capability; remove every permanently disabled control.
14. Wire the bold, italic, and inline-code toolbar. `MarkdownEditor.run` already implements these
    commands; they are dead only because the editor is constructed read-only.
15. Enable article creation. There is currently no way to start a new article.
16. Make rich editing writable, gated per article on a byte-clean `serialize(parse(body))` round
    trip. Articles that fail the check get source editing and a plain reason. This keeps the editor
    from becoming the serializer and preserves the no-op-identical invariant.
17. Make the metadata fields real controls that patch frontmatter, and remove
    `Not projected by the V1 source contract` from the interface.
18. Accept dragged and pasted media, replacing the file-input-and-alt-field rail flow.
19. Add autosave, `Ctrl+S`, and a visible last-saved state.
20. Preserve drafts across article switching. Remove the `window.confirm` dialog that offers to
    discard unsaved work.
21. Render Proof as the real built page at desktop and true 390px, plus feed and social previews.
    Serve it in a sandboxed frame **without** `allow-same-origin`, with headers scoped to the proof
    route — the global policy in `SecurityHeadersMiddleware` sets `frame-ancestors 'none'` and would
    otherwise let target-site JavaScript run same-origin with the Tezuri API.

### Voice

22. Move byte, hash, and envelope language behind evidence disclosures.
23. Rewrite status text to state a property and its evidence.
24. Reduce live-region announcements to state changes a person needs, in plain sentences.

### Setup

25. Accept a workspace path in `start.ps1` and add a POSIX `start.sh`. The launcher currently
    hardcodes the bundled sample, so the advertised one-command path can only ever open the demo.
26. Show the real mounted host path with copy and open-as-files affordances, replacing the hardcoded
    `/workspace` literal.
27. Give a nonce-less session an in-app recovery path instead of a disabled interface.
28. Offer to write a reviewed `tezuri.yaml` when configuration is absent, instead of surfacing a
    loader exception as a line of grey text.

### Documentation

29. Rewrite `docs/design/SYLIN-VISUAL-CONTRACT.md` around the pinned tokens, the component grammar,
    and the reconciliation date, so it can detect drift instead of absorbing it.

## Consequences

- Green regains meaning, which restores the passed/ready signal the current build cannot express.
- The visual contract becomes falsifiable: a token change is now a visible edit to a pinned table.
- Tezuri gains Image Tools' density. Comfortable public-site spacing does not survive the move, and
  the writing surface must be protected from that density deliberately.
- Items 16 and 21 are substantial slices with their own risk, not styling work. Item 16 supplies the
  fidelity evidence [ADR 0003](0003-rich-editor-boundary-and-milkdown-spike.md) requires before
  Milkdown can be accepted; the round-trip gate is that evidence expressed as a runtime capability.
- The offline and independent-release properties from ADR 0011 are unchanged.

## Evidence

`site.css` and `image-tools.css` were read in the website checkout on 2026-08-13; every value in the
pinned table is transcribed from one of them. The divergence table above was produced by comparing
them against `ClientApp/src/styles.css`. The dead-control findings are from the shipped client:
`commandButtons` is disabled at `main.ts:127` and never re-enabled, the create-article button is
disabled in `index.html:51` and untouched by script, all four metadata inputs are `readonly`, and the
editor is constructed with `readonly: true` at `main.ts:369`.

Browser evidence at desktop, 390px, 200% zoom, keyboard, and reduced motion is required per change
and remains a release gate.

## Rejected alternatives

- **Keep the moss palette and supersede the color rule instead.** Defensible if the green were a
  deliberate identity, but it is undocumented, breaks the reserved success signal, and reads as a
  different product beside the live site.
- **Adopt the public site's surface directly.** Public-site spacing and hierarchy do not carry a
  dense authoring workspace; this is the mistake ADR 0011's prose invited.
- **Fix the palette without the shape and capability work.** Recolors an interface whose dominant
  defect is that a large share of it does nothing.
- **Consume website CSS at runtime.** Still rejected, for ADR 0011's original reasons.

## Revalidation triggers

Revisit when sylin.org's design language materially changes, when the Image Tools dialect and the
public site diverge enough that "same house" needs restating, when shared versioned tokens become a
real maintained artifact, or when accessibility review shows the translation harms the authoring
surface. Reconcile the pinned table against the website on every dogfood pass and update the date.
