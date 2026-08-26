# Sylin visual contract

Tezuri is a working room in the same house as sylin.org. This document is the local snapshot of that
language and the check against drift. It is normative: if the implementation and this file disagree,
one of them is a bug.

- Product authority: [docs/PRODUCT-BRIEF.md](../PRODUCT-BRIEF.md)
- Source of truth: `sylin-org/website` `src/assets/site.css` and `src/assets/image-tools.css`
- **Last reconciled: 2026-08-13**

There is no runtime or build dependency on the website. These values are transcribed by hand and
revalidated on each dogfood pass. Changing a value here is a deliberate edit with application
evidence, never a side effect of styling work.

## Which dialect

sylin.org has two registers. The public site is discovery: generous spacing, large light headings,
ambient motion, trading cards. **Image Tools is the application register** — dense, instrumented,
warm-inked, still. Tezuri is an application, so Image Tools is the precedent.

The public-site register would produce a cream paper card and serif headings: a public-article
pastiche in a workspace where they have no job.

Carry the grammar; do not carry the landing page. No twinkling stars, card tilt, foil, mascot halo,
or ambient animation. Those belong to identity, not concentration.

## Tokens

Use these semantic `--tz-*` custom properties in the application frontend.

### Surfaces

| Token | Value | Job |
| --- | --- | --- |
| `--tz-bg` | `#0f0e12` | Application ground |
| `--tz-bg-2` | `#131217` | Recessed bands, command bars |
| `--tz-panel` | `#17161c` | Panels, editor surface, fields |
| `--tz-panel-2` | `#1c1a21` | Nested panel |
| `--tz-rail` | `#111015` | Navigation rail, inspector |
| `--tz-foot` | `#0d0c10` | Footer, status bar |
| `--tz-line` | `rgb(255 255 255 / 10%)` | Ordinary divider and border |
| `--tz-line-soft` | `rgb(255 255 255 / 6%)` | Quiet divider inside a panel |
| `--tz-line-amber` | `rgb(251 191 36 / 30%)` | Focused or active container edge |

### Ink

The warm application scale from Image Tools, not the public site's neutral scale. The ink scale
carries hierarchy **before** color does.

| Token | Value | Job |
| --- | --- | --- |
| `--tz-ink` | `#f5f2ea` | Principal text, active labels |
| `--tz-ink-2` | `#cec9bf` | Body and secondary text |
| `--tz-muted` | `#8e8994` | Microlabels, quiet explanation |
| `--tz-faint` | `#706b75` | Placeholders, blocked rows |

### Light and state

| Token | Value | Job |
| --- | --- | --- |
| `--tz-amber` | `#fbbf24` | Primary action, active context, earned attention |
| `--tz-amber-soft` | `#fcd34d` | Hover on an amber fill, unsaved-change ink |
| `--tz-on-amber` | `#18130a` | Text on an amber fill |
| `--tz-blue` | `#60a5fa` | Focus, and nothing else |
| `--tz-green` | `#4ade80` | Local-running, passed, proved, clean |
| `--tz-danger` | `#fb7185` | Destructive or failed, and nothing else |

**Green is reserved.** This is a functional rule, not a preference. When green was the chrome color,
`evidence-dot--passed` resolved to the same value as the primary button and proof success became
invisible. Amber must never flood the editor, and no state is ever carried by color alone — every
state carries text, and shape or icon where it reinforces.

### Type

| Token | Value |
| --- | --- |
| `--tz-sans` | `Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif` |
| `--tz-mono` | `"SFMono-Regular", Consolas, "Liberation Mono", monospace` |

No serif. No webfont, font account, tracking request, or decorative type dependency. Mono is for
things that reveal structure: paths, branches, commits, commands, checksums, counts, and microlabels.

## Component grammar

| Element | Rule |
| --- | --- |
| Microlabel / eyebrow | 9px mono, `letter-spacing: .09–.12em`, uppercase. Muted, or amber when it names a section. |
| Tab | Muted → ink on select with a 2px amber `::after` underline and a soft glow. Never a filled pill. |
| Active list row | `border-color: rgb(251 191 36 / 48%)`, `background: rgb(251 191 36 / 6.5%)`, `box-shadow: inset 2px 0 var(--tz-amber)` |
| Button | min-height 39px, radius 8px, `rgb(255 255 255 / 2.5%)`, 11px/620. Primary is an amber fill. Quiet is transparent. |
| Field | min-height 35px, radius 7px, `--tz-panel` ground, amber edge on focus |
| Panel | 1px line, radius 12px, `--tz-panel`; head row ≥43px with a 10px/620 name and a 9px mono fact |
| Local indicator | 6px `--tz-green` dot with glow beside uppercase mono text |
| Focus | `outline: 2px solid var(--tz-blue)`, `outline-offset: 2px` |
| Radii | 5px inline · 7px fields · 8px buttons and tiles · 12px panels · 14–16px large surfaces |
| Motion | 120–180ms ease, 1px press translate. Fully disabled under `prefers-reduced-motion`. |

Chrome is sized in px on the Image Tools scale. **Article prose keeps relative units** so it reflows
with user zoom. Tezuri runs slightly more generously than Image Tools — 9px microlabels rather than
8px — because a writing session is long and a pixel utility session is short. That is a deliberate
adaptation, not drift.

## Shape

Above `861px × 720px` the shell is a `100dvh` grid with `overflow: hidden` and independently
scrolling rail, canvas, and inspector — a workstation, not a long page. Below that it degrades to an
ordinary scrolling document with vertical navigation. No horizontal scrolling as primary navigation,
and no document overflow at 390px.

The article text is the visual center of gravity. Rails are subordinate and collapse when they have
no work, following the Image Tools precedent where the intro shrinks and drops its description once
content exists. Prefer one strong canvas with supporting rails over a mosaic of equal cards.

Proof reports the target build's verdict and evidence; Tezuri does not render or restyle the target
site. Do not make the editing chrome impersonate a published article. The writing plane is a calm
dark surface that is honestly an editor.

## Behavior

- The editor works on the Markdown article itself, which is canonical. Derived views — desk
  entries, previews, renditions, journal receipts — are never presented as an editable source or
  read back into the article.
- Metadata is progressive disclosure, not a wall of form controls standing between a person and
  their first sentence.
- Content the editor cannot safely represent is never invisibly removed. Refuse it or preserve it
  with a visible explanation and an actionable way forward.
- Proof shows the named program and argument list before first execution. Its bounded captured output
  stays inspectable, and any truncation is stated plainly.
- Publish shows selected paths, diff, branch and remote state, the intended commit, and the
  credential boundary before any mutation.
- Success is quiet and durable. A commit or push receipt stays in the interface; it never
  depends on a disappearing toast.
- No control ships visible and permanently inert.

## Voice

Every label, empty state, and validation message states what the system does, what happened, and what
a person can do next.

A status names a property and its evidence — `Saved in this project`, `Proof passed at 14:32`,
`Remote moved by 2 commits` — never a bare `Healthy` or `Synced`. Implementation vocabulary lives
behind an evidence disclosure, not in the writer's line of sight. Preservation is demonstrated by
behavior; it is not narrated.

Name an unfinished boundary candidly and once. No warning theater, apology banners, or copy that
makes a working pre-1.0 path sound defective.

## Accessibility

Semantic landmarks, programmatic labels, logical heading order, visible focus, keyboard-complete
tabs, toolbars and dialogs, live announcements for async state, and at least WCAG 2.2 AA contrast.
Usable at 200% zoom and a true 390px viewport with no horizontal page overflow. Touch targets reach
44px on narrow and touch layouts even where the visual treatment stays compact. Motion respects
`prefers-reduced-motion`; decoration never carries meaning.

## Review gate

Every material UI change is reviewed in the real Tauri application at 1440×900, 1280×720, a
height-constrained desktop, and a 390×844 narrow window — with keyboard, 200% zoom, reduced motion,
and a no-console-error check.

The test is not pixel identity with sylin.org. It is whether typography, hierarchy, color meaning,
interaction restraint, evidence, and voice unmistakably belong to the same system.

Reconcile the token tables against the website on each dogfood pass and update the date at the top.
