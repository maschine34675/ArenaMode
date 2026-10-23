param([string]$EvidenceRoot)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!$EvidenceRoot) { $EvidenceRoot = Join-Path $repo 'artifacts/bowl-team-boxes-20261023/native' }
$manifest = Get-Content -Raw -LiteralPath (Join-Path $EvidenceRoot 'delivery.json') | ConvertFrom-Json
& git -C $repo cat-file -e "$($manifest.commit)^{commit}"
if ($LASTEXITCODE -ne 0) { throw 'Delivery source is not a committed revision.' }
$changes = @(& git -C $repo diff --name-only $manifest.commit -- ArenaMode.Client ArenaMode.Shared ArenaMode.Server ArenaMode.BigBrainCompat)
if ($LASTEXITCODE -ne 0 -or $changes.Count) { throw 'Production code changed since the tested delivery.' }
foreach ($entry in $manifest.files) {
    foreach ($path in @($entry.source, $entry.local, $entry.host)) {
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne $entry.sha256) { throw "Delivery drift: $path" }
    }
}
foreach ($entry in $manifest.preserved) {
    if ((Get-FileHash -LiteralPath $entry.path -Algorithm SHA256).Hash -cne $entry.sha256) { throw "Preserved file changed: $($entry.path)" }
}
$status = Get-Content -Raw -LiteralPath (Join-Path $EvidenceRoot 'final-status.json') | ConvertFrom-Json
$entries = @($status.structuredContent.plugins) + @($status.structuredContent.libraries)
foreach ($name in @('maschine-ArenaMode.Client.dll','maschine-ArenaMode.BigBrainCompat.dll')) {
    $loaded = @($entries | Where-Object { (Split-Path $_.file -Leaf) -ceq $name })
    if ($loaded.Count -ne 1 -or $loaded[0].compareStatus -cne 'identical'
        -or !$loaded[0].loadedInformationalVersion.EndsWith($manifest.commit, [StringComparison]::Ordinal)) {
        throw "Missing exact loaded module evidence: $name"
    }
}
Write-Output "Verified $($manifest.files.Count) deployed artifacts and $($manifest.preserved.Count) preserved files."
Write-Output 'BOWL_TEAM_BOX_DELIVERY_PASSED'
