# Sylin visual contract

Tezuri should feel like a quiet, tactile writing room in the Sylin world without copying a deployed
site stylesheet or creating a cross-repository runtime dependency.

## Semantic inheritance

- Lead with the human task and current state; technology is supporting detail.
- Use warm night-garden surfaces, paper-like writing planes, restrained gold/green light, generous
  breathing room, and a small amount of purposeful pixel character.
- Typography and hierarchy favor long-form reading. Article prose remains calmer than surrounding
  application chrome.
- Controls use ordinary words: Write, Source, Proof, Review changes, Prepare commit, Publish.
- Status colors always have text/icon/shape reinforcement. “Saved,” “Proof passed,” and “Published”
  are distinct claims.

## Component behavior

- The article rail stays subordinate to the writing surface and can collapse at narrow widths.
- Metadata is progressive disclosure, not a wall of form controls before writing.
- Rich and Source are explicit modes with persistent focus and unsaved/conflict state.
- Unsupported content appears as a visible protected source block with an explanation and escape
  hatch; it is never invisibly removed.
- Proof shows the exact trusted commands before first execution and keeps build output inspectable.
- Publish shows selected paths, diff, branch/remote state, intended commit, and credential boundary
  before any mutation.

## Accessibility and resilience

The client uses semantic landmarks, programmatic labels, logical heading order, visible focus,
keyboard-complete tabs/toolbars/dialogs, live announcements for async state, and at least WCAG AA
text contrast. It must remain usable at 200% zoom and a true 390px viewport with no horizontal page
overflow. Motion respects `prefers-reduced-motion`; decoration never carries meaning. Reading and
navigation must degrade sensibly without JavaScript on the public target site.

Every material UI change is reviewed in a real browser at desktop and 390px, with keyboard and
reduced-motion states. Tezuri-owned CSS may translate current Sylin semantics, but `sylin.org` remains
the authority for its public layouts and the adapter must be revalidated when that design changes.

