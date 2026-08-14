# Tezuri

A local press. You open the repository you write in, you write, and what comes out is committed
Markdown your site already knows how to build.

Tezuri is one executable with a real window. No runtime to install, no container, no service.

> **Status: early.** This is a working prototype, not a release. It edits one repository at a time,
> it has been exercised by hand and by its own tests, and the interfaces below can still change.

## Run it

Download the executable for your platform and open it. It asks which repository you write in,
remembers it, and opens there next time.

To open a specific repository directly:

```
Tezuri.App path/to/repository
```

To run it headless with no window — for a test host, a remote box, or CI:

```
Tezuri.App --server path/to/repository
```

Server mode prints a URL carrying a single-use launch nonce. Open that URL and nothing else; the
nonce is what separates you from any other page on the machine.

## What it does

**Write.** A document-first editor — Milkdown, with a slash menu, a selection bubble, and drag-and-drop
images. Dropping an image puts it in the article's own `media/` folder, named by its content hash,
and links it. The same bytes twice are one file.

**Publish.** Select exactly the changed paths you mean, review them, commit, push. Tezuri stages only
what you selected, uses its own commit identity, disables hooks and signing, and refuses to push if
the remote moved after you reviewed it. It never stores a credential — it uses the Git your machine
already has.

**Proof.** Run your repository's own declared build in an isolated copy of the workspace, under a
timeout, with output bounded and secrets redacted. Your working tree is never touched.

**Import.** Point it at a Substack export. It creates articles, converts the HTML, and brings the
images the export actually contains. It never fetches from the network, and it never overwrites an
article that already exists — so re-running it is safe and boring.

## How a repository is laid out

Convention, not configuration. There is no config file to write.

```
src/writing/<slug>/
  article.json   the article — this is the canonical copy
  index.md       generated from it on every save, for your site build
  media/         images this article owns, named by content hash
```

`article.json` is what Tezuri edits. `index.md` is an output and is never read back, which is why
Tezuri cannot disagree with itself about what you wrote. Metadata Tezuri has no control for is
preserved verbatim and written back into the frontmatter untouched.

The few real choices — media policy, the Proof command, which paths publication may touch — have
working defaults and can be set through ordinary configuration under a `Tezuri` section.

## Boundary

Tezuri binds loopback only and does not listen on your network. Every mutating request needs the
launch nonce. Cross-origin mutation and unexpected `Host` headers are refused. Responses carry
restrictive security headers.

It writes inside the repository you chose and nowhere else. Paths are canonicalised and containment
checked, symlinks and junctions that leave the workspace are refused, and file replacement is atomic.

To report a vulnerability, see [SECURITY.md](SECURITY.md).

## Build from source

You need .NET SDK 10 and Node.js 24.

```
pwsh ./eng/verify.ps1     # the whole gate: client, format, build, tests, repository checks
pwsh ./eng/publish.ps1    # one executable in artifacts/publish
```

`verify.ps1` is what CI runs. If it is green, the branch is green.

## Layout

```
src/Tezuri.App/     one project, one file per concept
  Program.cs        host, composition, the desktop window
  Desktop.cs        native window, folder picker, remembered repositories
  Article.cs        the entity
  Articles.cs       CRUD and the revision guard
  Markdown.cs       entity to index.md
  Media.cs          ingest, verify, deduplicate, serve
  Git.cs            inspect, plan, commit, push
  Proof.cs          isolated build
  Import.cs         Substack export to articles
  Html.cs           HTML to Markdown
  Workspace.cs      layout, settings, path containment, atomic writes
  Security.cs       nonce, origin checks, headers
  ClientApp/        the editor
tests/Tezuri.Tests/ one suite
```

## Documents

- [docs/DECISIONS.md](docs/DECISIONS.md) — every decision in force, and what it replaced
- [docs/MEMORY.md](docs/MEMORY.md) — standing preferences and durable learnings
- [docs/design/SYLIN-VISUAL-CONTRACT.md](docs/design/SYLIN-VISUAL-CONTRACT.md) — the visual tokens
- [AGENTS.md](AGENTS.md) — where an agent should start

## Contributing

Open an issue before a large change. Run `pwsh ./eng/verify.ps1` before opening a pull request, and
write commit messages that say why. That is the whole process.

## Licence

Apache-2.0. See [LICENSE](LICENSE).
