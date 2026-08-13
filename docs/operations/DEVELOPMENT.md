# Development

## Toolchain

- Git 2.44 or newer
- .NET SDK exactly 10.0.302 (`global.json` disables roll-forward)
- Node.js 24 recommended; client engine range is 22.12 through 26
- npm from the selected Node distribution
- Docker Desktop/current Docker Engine for container checks
- PowerShell 7 for `eng/*.ps1`, or a POSIX shell for `eng/*.sh`

Dependency versions are centralized and exact. NuGet `packages.lock.json`, Koan `koan.lock.json`, and
the npm lockfile are committed. CI restores in locked mode. Do not edit generated lock content by
hand; make the manifest change, restore deliberately, and review the diff.

## Local loop

```sh
dotnet restore Tezuri.sln --locked-mode
(cd src/Tezuri.App/ClientApp && npm ci && npm run check && npm run build)
dotnet build Tezuri.sln --configuration Release --no-restore
dotnet test Tezuri.sln --configuration Release --no-build --no-restore
```

For a running source host, set `TEZURI_WORKSPACE` to an absolute repository containing `tezuri.yaml`
and run `dotnet run --project src/Tezuri.App/Tezuri.App.csproj`. Open only the nonce URL emitted by
the process. Client development may use Vite, but API mutations still require the server-supplied
nonce and matching Origin; document any proxy configuration rather than weakening the boundary.

## Code placement

- Permanent records/protocol strings: Domain.
- Path, bytes, process, Git, import, or target mechanics: Infrastructure.
- DI, Koan pipeline contribution, HTTP mapping, and browser transport: App.
- Editor/library state: ClientApp only and ephemeral.

Search for an existing type/constant before adding one. Keep interfaces at a real substitution or
test boundary, not one per class. Avoid packages when the platform offers a smaller auditable
solution; do not hand-roll a broad parser/crypto primitive when correctness requires a maintained
library.

## Generated and owner-controlled files

Never hand-edit or commit `bin`, `obj`, `node_modules`, `dist`, generated `wwwroot`, coverage, or
browser reports. `PROJECT-STARTUP-PROMPT.md` is the owner's founding brief and must not be rewritten
as implementation output. The Koan and website checkouts are separate repositories; Tezuri work must
not stage or clean them.

See [CONTRIBUTING.md](../../CONTRIBUTING.md) for review evidence and
[AGENTS.md](../../AGENTS.md) for the durable implementation guardrails.

