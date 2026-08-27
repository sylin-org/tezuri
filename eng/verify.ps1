# The canonical repository check. Runs everything CI would: frontend bundle,
# rust format check, lints, the whole test suite, a desktop executable build,
# and a whitespace audit of the working patch. Nothing here mutates the tree.
#
#   pwsh ./eng/verify.ps1

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    Write-Host '== frontend: install + type-check bundle =='
    Push-Location (Join-Path $root 'src-tauri/ui')
    try {
        if (-not (Test-Path 'node_modules')) {
            npm ci --no-fund --no-audit
            if ($LASTEXITCODE -ne 0) { throw 'npm ci failed' }
        }
        npx tsc --noEmit
        if ($LASTEXITCODE -ne 0) { throw 'frontend typecheck failed' }
        npm run build
        if ($LASTEXITCODE -ne 0) { throw 'frontend build failed' }
        if (-not (Test-Path 'dist/index.html')) {
            throw 'frontend build reported success but dist/index.html is missing'
        }
    }
    finally {
        Pop-Location
    }

    Write-Host '== rustfmt =='
    cargo fmt --all -- --check
    if ($LASTEXITCODE -ne 0) { throw 'rustfmt check failed' }

    Write-Host '== clippy =='
    cargo clippy --workspace --all-targets -- -D warnings
    if ($LASTEXITCODE -ne 0) { throw 'clippy failed' }

    Write-Host '== tests =='
    cargo test --workspace
    if ($LASTEXITCODE -ne 0) { throw 'tests failed' }

    Write-Host '== desktop executable builds =='
    cargo build -p tezuri-desktop
    if ($LASTEXITCODE -ne 0) { throw 'desktop build failed' }

    Write-Host '== patch whitespace =='
    git diff --check
    if ($LASTEXITCODE -ne 0) { throw 'whitespace errors in unstaged changes' }
    git diff --cached --check
    if ($LASTEXITCODE -ne 0) { throw 'whitespace errors in staged changes' }

    Write-Host ''
    Write-Host 'verify: ok'
}
finally {
    Pop-Location
}
