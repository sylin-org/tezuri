# The canonical release check. verify.ps1 is the fast gate; this exercises
# the artifact a person actually downloads: the release binary with the
# current bundle embedded, launched as a real process.
#
#   pwsh ./eng/release-check.ps1

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    Write-Host '== frontend: type-check + bundle =='
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

    Write-Host '== release binary =='
    cargo build --release -p tezuri-desktop
    if ($LASTEXITCODE -ne 0) { throw 'release build failed' }
    $exe = Join-Path $root 'target/release/tezuri-desktop.exe'
    if (-not (Test-Path $exe)) { throw 'release executable missing' }

    Write-Host '== smoke: the downloaded artifact launches and stays alive =='
    $proc = Start-Process -FilePath $exe -PassThru
    Start-Sleep -Seconds 6
    if ($proc.HasExited) {
        throw "release executable exited immediately (code $($proc.ExitCode))"
    }
    Stop-Process -Id $proc.Id -Force
    Start-Sleep -Milliseconds 500
    Write-Host "alive after 6s, stopped cleanly."

    Write-Host ''
    Write-Host 'release-check: ok'
}
finally {
    Pop-Location
}
