$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$composeFile = Join-Path $repoRoot 'compose.yaml'
$previousWorkspace = [Environment]::GetEnvironmentVariable('TEZURI_WORKSPACE')
$previousPort = [Environment]::GetEnvironmentVariable('TEZURI_PORT')

try {
  # Compose requires this value even though `down` never mounts the path.
  $env:TEZURI_WORKSPACE = $repoRoot
  $env:TEZURI_PORT = '8080'

  & docker compose --project-name tezuri --file $composeFile down --remove-orphans
  if ($LASTEXITCODE -ne 0) {
    throw "docker compose down failed with exit code $LASTEXITCODE."
  }

  Write-Host 'Tezuri is stopped. Saved workspace files and the cached image were left in place.'
}
finally {
  if ($null -eq $previousWorkspace) {
    Remove-Item Env:TEZURI_WORKSPACE -ErrorAction SilentlyContinue
  }
  else {
    $env:TEZURI_WORKSPACE = $previousWorkspace
  }

  if ($null -eq $previousPort) {
    Remove-Item Env:TEZURI_PORT -ErrorAction SilentlyContinue
  }
  else {
    $env:TEZURI_PORT = $previousPort
  }
}
