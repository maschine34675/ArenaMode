param([string]$SptPath = 'D:\SPT41', [switch]$CaptureBaselineOnly)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $PSScriptRoot 'ExternalFiles.ps1') -SptPath $SptPath
$baseline = Join-Path $repo '.unlazy/weapons-lasers/external-before.json'
if ($CaptureBaselineOnly) {
    if (Test-Path -LiteralPath $baseline) { throw 'Existing external baseline must not be overwritten during work' }
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($baseline))
    Get-ArenaExternalHashes | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $baseline -Encoding utf8
    Write-Output 'External baseline captured; no build or deployment performed.'
    exit 0
}
$before = if (Test-Path -LiteralPath $baseline) {
    Get-Content -LiteralPath $baseline -Raw | ConvertFrom-Json -AsHashtable
} else { Get-ArenaExternalHashes }
# A user may remove an unrelated mod while checks are running. Preserve the
# original baseline and explicitly record only reviewed, already observed
# removals; never restore that mod or silently accept a fresh blanket baseline.
$removalsPath = Join-Path $repo '.unlazy/weapons-lasers/external-removals.json'
if (Test-Path -LiteralPath $removalsPath) {
    $removals = Get-Content -LiteralPath $removalsPath -Raw | ConvertFrom-Json -AsHashtable
    foreach ($path in $removals.Keys) {
        if (!$before.ContainsKey($path) -or $before[$path] -ne $removals[$path] -or (Test-Path -LiteralPath $path)) {
            throw "Recorded external removal does not match original baseline/current absence: $path"
        }
        $before.Remove($path)
        Write-Output "Previously observed external removal (not restored): $path"
    }
}
$prebuild = Get-ArenaExternalHashes
if ($before.Count -ne $prebuild.Count) { throw 'Unreviewed external file set change before build' }
foreach ($path in $before.Keys) { if ($before[$path] -ne $prebuild[$path]) { throw "External file changed before build: $path" } }
# Build both mod halves and the optional companion, then explicitly build the
# client LAST so dependency test builds cannot leave a Debug Redeploy.Core live.
foreach ($project in @('ArenaMode.Server/ArenaMode.Server.csproj', 'ArenaMode.BigBrainCompat/ArenaMode.BigBrainCompat.csproj', 'ArenaMode.Client/ArenaMode.Client.csproj')) {
    & dotnet build (Join-Path $repo $project) -c Release --no-restore "-p:SPTPath=$SptPath" "-p:SPTUserPath=$(Join-Path $SptPath 'SPT_Runtime')"
    if ($LASTEXITCODE -ne 0) { throw "Release build failed: $project" }
}
foreach ($pair in @(
    @('ArenaMode.Client/bin/Release/maschine-ArenaMode.Client.dll', 'BepInEx/plugins/maschine-ArenaMode/maschine-ArenaMode.Client.dll'),
    @('ArenaMode.BigBrainCompat/bin/Release/maschine-ArenaMode.BigBrainCompat.dll', 'BepInEx/plugins/maschine-ArenaMode/maschine-ArenaMode.BigBrainCompat.dll'),
    @('build/server/Release/maschine-ArenaMode.Server/maschine-ArenaMode.Server.dll', 'SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll'),
    @('../Redeploy/Redeploy.Core/bin/Release/maschine-Redeploy.Core.dll', 'BepInEx/plugins/maschine-Redeploy.Core.dll')
)) {
    $built = (Get-FileHash -LiteralPath (Join-Path $repo $pair[0]) -Algorithm SHA256).Hash
    $installed = (Get-FileHash -LiteralPath (Join-Path $SptPath $pair[1]) -Algorithm SHA256).Hash
    if ($built -ne $installed) { throw "Deployment mismatch: $($pair[1])" }
    Write-Output "$($pair[1]): $installed"
}
$after = Get-ArenaExternalHashes
if ($before.Count -ne $after.Count) { throw 'External or authored catalogue file set changed' }
foreach ($path in $before.Keys) { if ($before[$path] -ne $after[$path]) { throw "External or authored catalogue changed: $path" } }
Write-Output "ARENA_WEAPON_LASER_DEPLOYMENT_PASSED ($($before.Count) unchanged external/config/catalogue files)"
