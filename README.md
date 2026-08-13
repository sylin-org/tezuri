# Tezuri

Tezuri is a local writing room for a repository you already own. Mount a site repository, open its
folder-native Markdown articles in a browser, make reviewable source changes, and let the site's own
build remain the final authority. There is no Tezuri account, article database, hosted control
plane, or telemetry.

> **Maturity:** active pre-release development. The file-native article/configuration contracts,
> local request boundary, API tests, client shell, and container foundation exist. Rich-editor
> fidelity, full media/Proof/publish flows, the Kintsugi migration, public GHCR image, and sylin.org
> dogfood publication are not yet release-proven. Do not treat `main` as a stable content editor.

## Five-minute run with Docker Desktop

The sample workspace is safe to use while evaluating the current build. Tezuri binds only to the
host loopback interface and prints a per-launch URL containing a nonce.

PowerShell (builds the image, starts the bundled sample, and opens Tezuri):

```powershell
.\start.ps1
```

Stop it without deleting the sample content or cached image:

```powershell
.\stop.ps1
```

POSIX shell:

```sh
TEZURI_WORKSPACE="$(pwd)/samples/folder-native-workspace" docker compose up --build
```

For the manual POSIX flow, open the exact `http://127.0.0.1:8080/?nonce=...` URL printed in the
container log. The client removes the nonce from browser history and keeps it only in memory. Stop
with `Ctrl+C`; remove the disposable container and locally built image with:

```sh
docker compose down --remove-orphans
docker image rm ghcr.io/sylin-org/tezuri:local
```

Saved content remains in the mounted repository. Tezuri never requires the Docker socket, a home
directory mount, or a credential mount.

## Run from source

Prerequisites are .NET SDK 10.0.302 and Node.js 24 (the client accepts Node 22.12 through 26). Restore
uses committed NuGet and npm lock files.

PowerShell:

```powershell
$env:TEZURI_WORKSPACE = (Resolve-Path .\samples\folder-native-workspace).Path
Push-Location .\src\Tezuri.App\ClientApp
npm ci
npm run build
Pop-Location
dotnet run --project .\src\Tezuri.App\Tezuri.App.csproj
```

POSIX shell:

```sh
export TEZURI_WORKSPACE="$(pwd)/samples/folder-native-workspace"
(cd src/Tezuri.App/ClientApp && npm ci && npm run build)
dotnet run --project src/Tezuri.App/Tezuri.App.csproj
```

## Workspace contract

Tezuri reads a committed `tezuri.yaml` at the repository root. The versioned schema is
[`schemas/tezuri-workspace-v1.schema.json`](schemas/tezuri-workspace-v1.schema.json), and a complete
minimal repository is in [`samples/folder-native-workspace`](samples/folder-native-workspace).

The V1 article layout is configurable but folder-native:

```text
tezuri.yaml
schemas/article-v1.schema.json
schemas/editor-hints-v1.json
src/writing/
  <slug>/
    index.md
    media/
    media-manifest.json  # optional intrinsic asset provenance
```

Markdown plus YAML frontmatter is canonical. The HTTP source envelope carries exact UTF-8 bytes,
SHA-256, byte ranges, and expected-byte patch preconditions. A no-op save returns the original bytes;
an external change causes a conflict instead of an overwrite. Unknown syntax must remain source,
never silently disappear.

Proof commands are structured executable/argument arrays from the trusted mounted configuration.
Tezuri never accepts arbitrary shell text from the browser. The mounted repository's own build—
Eleventy for sylin.org—owns publishable HTML, feeds, discovery files, and social metadata.

## Architecture

- `src/Tezuri.Domain` contains versioned source/media/proof/publication contracts.
- `src/Tezuri.Infrastructure` owns guarded filesystem and process adapters.
- `src/Tezuri.App` is the .NET 10 Koan host and bundled TypeScript/Milkdown client.
- `tests` contains contract, filesystem, configuration, and host integration suites.
- `schemas` and `samples` are committed interoperability artifacts.
- [`docs`](docs/README.md) separates product truth, architecture, decisions, operations, and evidence.
- [`docs/contracts`](docs/contracts/README.md) owns serialized authority, semantic vocabularies, and
  compatible-evolution rules.

The host deliberately uses only `builder.Services.AddKoan()`. App-owned security joins Koan through
its supported pre-routing pipeline contributor; Tezuri does not depend on `.AsWebApi()` or a Koan
recipe bundle. The Koi AI recipes describe optional outer-host networking only and are not a Tezuri
runtime dependency.

## Development and tests

Run the normal repository gate from the root:

```powershell
pwsh .\eng\verify.ps1
```

or:

```sh
./eng/verify.sh
```

The gate performs locked restores, client type/tests/build, repository checks, formatting, a
zero-warning Release build, and all .NET tests. The slower Docker Desktop smoke is explicit:

```powershell
pwsh .\eng\container-smoke.ps1
```

See [development](docs/operations/DEVELOPMENT.md), [testing](docs/operations/TESTING.md), and
[the architecture map](docs/architecture/README.md).

## Images and upgrades

No public image or version exists yet. `ghcr.io/sylin-org/tezuri` examples in release automation are
the intended destination, not a claim that an anonymous pull works today. Before a first release,
the project must publish a multi-platform digest, attach SBOM/provenance, make the GHCR package
public, and repeat the mounted-workspace smoke by digest.

For a future immutable release, stop the old container, pull the documented digest, and recreate the
container against the same repository mount. Back up and recover with ordinary Git and filesystem
tools. Removal means deleting the container/image and optional caches; never delete the mounted
repository.

## Project policies

Contributions are welcome under [CONTRIBUTING.md](CONTRIBUTING.md) and the
[Code of Conduct](CODE_OF_CONDUCT.md). Use [SUPPORT.md](SUPPORT.md) for the right public channel and
[SECURITY.md](SECURITY.md) for private reports. Tezuri is licensed under the
[Apache License 2.0](LICENSE).
