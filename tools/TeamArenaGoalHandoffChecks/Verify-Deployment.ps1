param([string]$SptPath = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
function ExternalHashes {
    $hashes = @{}
    foreach ($relative in @('BepInEx/plugins/SAIN', 'BepInEx/plugins/pitFireTeam', 'SPT_Runtime/user/mods/Solarint-SAIN-ServerMod')) {
        foreach ($file in Get-ChildItem -LiteralPath (Join-Path $SptPath $relative) -File -Recurse) { $hashes[$file.FullName] = (Get-FileHash -LiteralPath $file.FullName).Hash }
    }
    foreach ($relative in @('BepInEx/plugins/DrakiaXYZ-BigBrain.dll', 'BepInEx/config/me.sol.sain.cfg')) {
        $path = Join-Path $SptPath $relative
        if (Test-Path -LiteralPath $path) { $hashes[$path] = (Get-FileHash -LiteralPath $path).Hash }
    }
    return $hashes
}
$before = ExternalHashes
foreach ($project in @('tools/TeamArenaPursuitRuntimeChecks', 'tools/TeamDeathmatchRuntimeChecks', 'tools/TeamGiveUpChecks', 'tools/TeamDeathmatchChecks')) {
    & dotnet run --project (Join-Path $repo $project) -c Release
    if ($LASTEXITCODE -ne 0) { throw "Existing regression failed: $project" }
}
& dotnet build (Join-Path $repo 'ArenaMode.BigBrainCompat/ArenaMode.BigBrainCompat.csproj') -c Release --no-restore "-p:SPTPath=$SptPath"
if ($LASTEXITCODE -ne 0) { throw 'Companion Release build failed' }
# The existing suite builds the client LAST, preserving Release Core deployment.
& pwsh -NoProfile -File (Join-Path $repo 'tools/BotLifecycleSafetyChecks/Verify-Deployment.ps1') -SptPath $SptPath
if ($LASTEXITCODE -ne 0) { throw 'Existing team/PFT/Release checks failed' }
$built = Join-Path $repo 'ArenaMode.BigBrainCompat/bin/Release/maschine-ArenaMode.BigBrainCompat.dll'
$live = Join-Path $SptPath 'BepInEx/plugins/maschine-ArenaMode.BigBrainCompat.dll'
if ((Get-FileHash -LiteralPath $built).Hash -ne (Get-FileHash -LiteralPath $live).Hash) { throw 'Companion deployment mismatch' }
$after = ExternalHashes
if ($before.Count -ne $after.Count) { throw 'External mod file set changed' }
foreach ($file in $before.Keys) { if ($before[$file] -ne $after[$file]) { throw "External file changed: $file" } }
Write-Output "ARENA_PFT_GOAL_DEPLOYMENT_PASSED ($($before.Count) unchanged external files; client/companion/Core Release hashes match)"
