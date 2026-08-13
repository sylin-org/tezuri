[CmdletBinding()]
param(
    [string]$Image = 'tezuri-local-smoke:dev',
    [int]$Port = 18080,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$containerName = "tezuri-local-smoke-$PID"
$temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$workspace = Join-Path $temporaryParent ("tezuri-container-smoke-" + [Guid]::NewGuid().ToString('N'))

function Invoke-DockerChecked {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
    & docker @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "docker $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

try {
    Copy-Item -LiteralPath (Join-Path $repoRoot 'samples/folder-native-workspace') `
        -Destination $workspace -Recurse

    if (-not $SkipBuild) {
        Push-Location $repoRoot
        try {
            Invoke-DockerChecked build `
                --build-arg 'VERSION=0.0.0-dev' `
                --build-arg 'REVISION=local-smoke' `
                --tag $Image .
        }
        finally {
            Pop-Location
        }
    }

    Invoke-DockerChecked run --detach `
        --name $containerName `
        --platform linux/amd64 `
        --read-only `
        --cap-drop ALL `
        --security-opt no-new-privileges `
        --tmpfs '/tmp:rw,nosuid,nodev,size=256m,mode=1777' `
        --tmpfs '/app/data:rw,nosuid,nodev,size=64m,mode=1777' `
        --mount "type=bind,src=$workspace,dst=/workspace" `
        --publish "127.0.0.1:$Port`:8080" `
        $Image | Out-Null

    $healthy = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        $state = & docker inspect --format '{{.State.Status}}|{{.State.Health.Status}}' $containerName
        if ($state -eq 'running|healthy') {
            $healthy = $true
            break
        }
        if ($state -match '^(exited|dead)\|') {
            & docker logs $containerName
            throw "Container stopped before becoming healthy: $state"
        }
        Start-Sleep -Seconds 1
    }
    if (-not $healthy) {
        & docker logs $containerName
        throw 'Container did not become healthy within 60 seconds.'
    }

    $userId = (& docker exec $containerName id -u).Trim()
    if ($userId -eq '0') { throw 'Container unexpectedly runs as root.' }

    $baseUri = "http://127.0.0.1:$Port"
    Invoke-RestMethod "$baseUri/health/live" | Out-Null
    Invoke-RestMethod "$baseUri/health/ready" | Out-Null

    $response = Invoke-WebRequest "$baseUri/"
    if ($response.Headers['X-Content-Type-Options'] -ne 'nosniff') {
        throw 'SPA response is missing the restrictive security headers.'
    }

    $logs = (& docker logs $containerName 2>&1) -join "`n"
    $match = [regex]::Match($logs, 'http://127\.0\.0\.1:8080/\?nonce=([^\s]+)')
    if (-not $match.Success) { throw 'Launch nonce URL was not emitted.' }
    $nonce = $match.Groups[1].Value

    $articles = Invoke-RestMethod "$baseUri/api/v1/articles"
    $article = @($articles.articles) | Select-Object -First 1
    if ($null -eq $article) { throw 'The sample workspace article was not discovered.' }
    $source = Invoke-RestMethod "$baseUri/api/v1/articles/$($article.id)/source"
    $patch = @{
        protocol = 'tezuri.source-patch-set'
        version = 1
        articleId = $article.id
        relativePath = $source.article.relativePath
        baseSha256 = $source.base.sha256
        operations = @()
    } | ConvertTo-Json -Depth 8
    $applied = Invoke-RestMethod `
        "$baseUri/api/v1/articles/$($article.id)/source-patches" `
        -Method Post `
        -Headers @{ 'X-Tezuri-Nonce' = $nonce; Origin = $baseUri } `
        -ContentType 'application/json' `
        -Body $patch
    if ($applied.current.base.sha256 -ne $source.base.sha256) {
        throw 'A no-op source patch changed the sample article.'
    }

    Write-Host "Container smoke passed for $Image as uid $userId on 127.0.0.1:$Port."
}
finally {
    & docker rm --force $containerName *> $null
    if (Test-Path -LiteralPath $workspace) {
        $resolvedWorkspace = [IO.Path]::GetFullPath($workspace)
        if (-not $resolvedWorkspace.StartsWith($temporaryParent, [StringComparison]::OrdinalIgnoreCase) -or
            -not ([IO.Path]::GetFileName($resolvedWorkspace)).StartsWith('tezuri-container-smoke-', [StringComparison]::Ordinal)) {
            throw "Refusing to remove unexpected smoke workspace: $resolvedWorkspace"
        }
        Remove-Item -LiteralPath $resolvedWorkspace -Recurse -Force
    }
}

