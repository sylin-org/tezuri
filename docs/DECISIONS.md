# Decisions

Every decision still in force, one entry each, newest first. An entry says what was decided and —
more usefully — what it cost or replaced, so nobody re-argues it from scratch.

Fifteen separate ADR files preceded this page. They were written before the product could create an
article, and six of the fifteen described machinery that no longer exists. The history is in git;
what survives is here.

---

## Tezuri is a desktop application, not a container

One executable, a native window around the system webview (Photino), a folder picker at launch.

The container was a tax paid on every single use: install Docker, mount a volume, find a URL in a
log. None of that is writing. The desktop shell also unlocks multiple repositories, which the
container could not do without re-mounting.

**One session edits one repository.** Opening another launches another Tezuri. That costs a process
and saves invalidating a data store that was configured from the first choice — the store's
directory is resolved once, on first use, and never has to change underneath a live session.

`--server` still runs headless with no window, for test hosts and machines without a desktop. The
port is always whatever the operating system hands out; a fixed port would collide the moment a
second window opened.

Superseded: the OCI release boundary, and the container security boundary as *a container* — the
origin check and launch nonce below survive it.

## The article entity is canonical; Markdown is generated

An article is a Koan entity stored as `article.json` in its own folder. `index.md` is rendered from
it on every save, one way, and never read back.

This is the decision that paid for itself most. Treating the Markdown file as authoritative had
forced a byte-patch protocol, a frontmatter byte-range reader, a round-trip fidelity gate, and an
external-edit conflict experience — roughly 2,000 lines. Reversing it deleted all of them in an
afternoon, because a generator owns its output and external-edit reconciliation stopped being a
problem that could arise.

Unknown metadata rides `[JsonExtensionData]`, so an imported corpus carrying fields Tezuri has never
heard of survives a read/write cycle untouched and reaches the generated frontmatter intact.

Koan's JSON connector uses the `IndividualFiles` layout so each article is one file that a commit
can select on its own.

Superseded: repository files are authoritative; the lossless Markdown/frontmatter protocol; the
rich-editor document boundary.

## Optimistic concurrency is Tezuri's own job

Koan's JSON connector does not implement `IConditionalWriteRepository`, and `EntityController` does
not enforce `If-Match`. The write path compares a `Revision` the client read and returns 409 when it
has moved on.

The only writer contention left is a second Tezuri session, because Markdown is an output. That
narrowness is why a revision compare is enough.

## Layout is convention; almost nothing is configuration

Articles live in `src/writing/<slug>/`, holding `article.json`, `index.md`, and `media/`. The
constants are in one place, `WorkspaceLayout`.

A committed `tezuri.yaml` used to make all of it configurable, which bought nothing — every
workspace used the same values — and cost a hand-rolled YAML subset parser with indent and
line-count guards, a validator, a loader, a JSON Schema, and a layout contract threaded through five
services.

What is left is a genuine choice and has a working default: the media policy, the Proof command, and
the paths Git publication may touch.

Superseded: versioned restricted workspace configuration.

## The local boundary is an origin check and a launch nonce

The server binds loopback only. Mutating requests need `X-Tezuri-Nonce`, minted per launch and put
in the window's URL. Cross-origin mutation and unexpected `Host` headers are refused, and responses
carry restrictive headers including `frame-ancestors 'none'`.

The nonce survives a page refresh via tab-scoped session storage. Holding it only in a module
variable meant an ordinary refresh silently downgraded the editor to read-only with no way back —
the most common way to lose access to your own work. Session storage is origin-scoped and dies with
the tab, and any script able to read it could already have issued requests with the in-memory value.

## Media is article-owned and named by content

An asset lands in `media/` beside the article that displays it, named by its own SHA-256. Identical
bytes uploaded twice are one file. Extension and signature must agree; SVG and anything else
scriptable is refused; truncated images are refused before anything is written.

An article is its folder: media can only be written into one that already exists, so an upload
naming an article nobody created cannot bring a directory into being.

## Publication is explicit Git with delegated credentials

Tezuri stages exactly the paths a person selected, commits with its own identity, hooks and signing
disabled, and pushes only after proving the remote tip is still the reviewed one. It never stores a
credential; it uses whatever the machine's Git already has.

Containment does not depend on the allow-list: a path that traverses, escapes, or names `.git` is
refused before the allow-list is consulted, so the list only ever narrows an already-safe set.

## The target site's own build is the proof

Proof copies the workspace to an isolated directory and runs the repository's declared build there,
under a timeout, with bounded and redacted output. The mounted repository is never written to.

A proof executable may not be a shell interpreter. Executable and arguments stay separate and
nothing is passed through a shell, so no configured string can become shell syntax; naming `sh` or
`pwsh` would hand that separation straight back. This is checked before a byte is copied.

## Import is idempotent instead of transactional

Substack import creates one article per importable post and **skips any article that already
exists**, never overwriting.

That single rule replaced plan digests, an `If-Match` preview/apply handshake, a staging tree,
atomic directory moves, and a committed manifest recording what happened — about 700 lines. When a
mechanism exists to make an operation safe to repeat, ask first whether the operation can simply be
repeatable. Git already records what changed.

Superseded: manifested and complete Substack import.

## The visual language is Sylin's workstation dialect

Amber on violet, not the moss green that had drifted in. Underlined tabs with an amber glow, amber
inset-bar active rows, 9px uppercase mono microlabels, blue focus rings.

Green in the chrome was not a taste problem: it made the passing-evidence dot identical to the
primary button, so success became invisible. Tokens are transcribed from `sylin.org` and pinned in
`docs/design/SYLIN-VISUAL-CONTRACT.md`.

Superseded: inheriting Sylin semantics without runtime coupling.

## No wire-protocol envelopes

Responses carry data. They do not carry `protocol` and `version` discriminators, and the client does
not check them at runtime.

Those existed because the client and server were treated as independently versioned parties. They
are one executable, built and shipped together; a guard that cannot fail is not a guard.

Superseded: public schemas own serialized contracts and evolve explicitly.

## Ceremony follows a working product

This repository front-loaded a full open-source establishment contract — fifteen ADRs, thirteen JSON
Schemas, twenty-seven golden samples, eight root policy files, eight test projects — around a
prototype that could not yet create an article.

None of it was wrong in isolation. All of it was early. Governance is a response to having users,
contributors, and a compatibility surface, not a substitute for them.
