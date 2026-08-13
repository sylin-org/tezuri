$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$composeFile = Join-Path $repoRoot 'compose.yaml'
$workspace = (Resolve-Path (Join-Path $repoRoot 'samples/folder-native-workspace')).Path
$previousWorkspace = [Environment]::GetEnvironmentVariable('TEZURI_WORKSPACE')
$previousPort = [Environment]::GetEnvironmentVariable('TEZURI_PORT')

try {
  $env:TEZURI_WORKSPACE = $workspace
  $env:TEZURI_PORT = '8080'

  & docker compose --project-name tezuri --file $composeFile up --build --detach --wait
  if ($LASTEXITCODE -ne 0) {
    Write-Host "`nTezuri container status:"
    & docker compose --project-name tezuri --file $composeFile ps --all
    Write-Host "`nTezuri app log:"
    & docker compose --project-name tezuri --file $composeFile logs --no-color --tail 100 app
    throw 'Tezuri could not start. The Docker error, container status, and app log are shown above.'
  }

  $logs = (& docker compose --project-name tezuri --file $composeFile logs --no-color app 2>&1) -join "`n"
  if ($LASTEXITCODE -ne 0) {
    throw "docker compose logs failed with exit code $LASTEXITCODE."
  }

  $nonceUrls = [regex]::Matches(
    $logs,
    'http://127\.0\.0\.1:8080/\?nonce=([^\s]+)')
  if ($nonceUrls.Count -eq 0) {
    throw 'Tezuri started, but its launch URL was not found in the container log.'
  }

  $nonce = $nonceUrls[$nonceUrls.Count - 1].Groups[1].Value
  $url = "http://127.0.0.1:8080/?nonce=$nonce"

  Write-Host "Tezuri is ready: $url"
  Start-Process $url
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
