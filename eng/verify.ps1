[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    dotnet restore Tezuri.sln --locked-mode
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Push-Location src/Tezuri.App/ClientApp
    try {
        npm ci --no-audit --no-fund
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        npm test
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        npm run check
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        npx vite build --outDir=.verification-dist
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    finally {
        $verificationOutput = Join-Path (Get-Location) '.verification-dist'
        if (Test-Path -LiteralPath $verificationOutput) {
            Remove-Item -LiteralPath $verificationOutput -Recurse -Force
        }
        Pop-Location
    }

    dotnet format Tezuri.sln --verify-no-changes --no-restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet build Tezuri.sln --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet test Tezuri.sln --configuration Release --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    node eng/verify-repository.mjs
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    git diff --check
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    Pop-Location
}

