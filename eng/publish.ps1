<#
  Builds the client, then publishes Tezuri as one executable that needs no runtime installed.
  Defaults to this machine; pass a runtime identifier to cross-publish.
#>
[CmdletBinding()]
param(
  [string] $Runtime = $(if ($IsWindows) { 'win-x64' } elseif ($IsMacOS) { 'osx-arm64' } else { 'linux-x64' }),
  [string] $Output = 'artifacts/publish'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
  Push-Location src/Tezuri.App/ClientApp
  try {
    npm ci --no-audit --no-fund
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    npm run build
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
  }
  finally { Pop-Location }

  dotnet publish src/Tezuri.App/Tezuri.App.csproj `
    --configuration Release `
    --runtime $Runtime `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    --output $Output
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

  Write-Host "Tezuri published to $Output"
}
finally { Pop-Location }
