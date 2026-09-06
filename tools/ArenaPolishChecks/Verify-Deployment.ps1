param([string]$SptPath = 'D:\SPT41', [switch]$CaptureBaselineOnly)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $repo 'tools/WeaponLaserIntegrationChecks/ExternalFiles.ps1') -SptPath $SptPath
$baseline = Join-Path $repo '.unlazy/polish-nre/external-before.json'
if ($CaptureBaselineOnly) {
    if (Test-Path -LiteralPath $baseline) { throw 'Do not overwrite the task baseline' }
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($baseline))
    Get-ArenaExternalHashes | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $baseline -Encoding utf8
    Write-Output 'Read-only external baseline captured; no deployment.'
    exit 0
}
$before = if (Test-Path -LiteralPath $baseline) {
    Get-Content -LiteralPath $baseline -Raw | ConvertFrom-Json -AsHashtable
} else { Get-ArenaExternalHashes }
foreach ($project in @('ArenaMode.Server/ArenaMode.Server.csproj', 'ArenaMode.BigBrainCompat/ArenaMode.BigBrainCompat.csproj', 'ArenaMode.Client/ArenaMode.Client.csproj')) {
    & dotnet build (Join-Path $repo $project) -c Release --no-restore "-p:SPTPath=$SptPath" "-p:SPTUserPath=$(Join-Path $SptPath 'SPT_Runtime')"
    if ($LASTEXITCODE -ne 0) { throw "Release build failed: $project" }
}
foreach ($pair in @(
    @('ArenaMode.Client/bin/Release/maschine-ArenaMode.Client.dll', 'BepInEx/plugins/maschine-ArenaMode.Client.dll'),
    @('ArenaMode.BigBrainCompat/bin/Release/maschine-ArenaMode.BigBrainCompat.dll', 'BepInEx/plugins/maschine-ArenaMode.BigBrainCompat.dll'),
    @('build/server/Release/maschine-ArenaMode.Server/maschine-ArenaMode.Server.dll', 'SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll'),
    @('../Redeploy/Redeploy.Core/bin/Release/maschine-Redeploy.Core.dll', 'BepInEx/plugins/maschine-Redeploy.Core.dll')
)) {
    $built = (Get-FileHash -LiteralPath (Join-Path $repo $pair[0]) -Algorithm SHA256).Hash
    $live = (Get-FileHash -LiteralPath (Join-Path $SptPath $pair[1]) -Algorithm SHA256).Hash
    if ($built -ne $live) { throw "Deployment mismatch: $($pair[1])" }
    Write-Output "$($pair[1]): $live"
}
$after = Get-ArenaExternalHashes
if ($before.Count -ne $after.Count) { throw 'External mod/config/catalogue file set changed' }
foreach ($path in $before.Keys) { if ($before[$path] -ne $after[$path]) { throw "External file changed: $path" } }
Write-Output "ARENA_POLISH_DEPLOYMENT_PASSED ($($before.Count) unchanged external/config/catalogue files)"
