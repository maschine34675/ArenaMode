param([string]$SptPath = ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))))
. (Join-Path $PSScriptRoot '../Read-ArenaInstalledSnapshot.ps1')
$arenaSnapshotRoot = $SptPath
$arenaLayoutSnapshot = Read-ArenaInstalledSnapshot $arenaSnapshotRoot
try {
$ErrorActionPreference = 'Stop'
$shotRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$shotSource = Join-Path $shotRoot 'ArenaMode.Client/Combat'
$shotProject = Join-Path $PSScriptRoot 'ShotAwardIntegrationChecks.csproj'
$shotScratch = Join-Path $shotRoot ('artifacts/shot-award-integration/' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($shotScratch)
$shotFiles = @('ArenaShotAwardHooks.cs','ArenaShotAwardHistory.cs','ArenaHitHistory.cs')
$shotBound = @($shotFiles | ForEach-Object { 'ArenaMode.Client/Combat/' + $_ }) + @(
    'ArenaMode.Client/Combat/CombatPatches.cs','ArenaMode.Client/Modes/ArenaMatch.cs'
) + @(Get-ChildItem -LiteralPath $PSScriptRoot -File | ForEach-Object { 'tools/ShotAwardIntegrationChecks/' + $_.Name })
$shotHashes = @{}
foreach ($path in $shotBound) { $shotHashes[$path] = (Get-ArenaObservedFileHash -LiteralPath (Join-Path $shotRoot $path)).Hash }
$shotInstalledPaths = @(
    'BepInEx/plugins/maschine-ArenaMode/maschine-ArenaMode.Client.dll',
    'SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll',
    'BepInEx/plugins/maschine-Redeploy.Core.dll',
    'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'
)
$shotInstalledHashes = @{}
foreach ($path in $shotInstalledPaths) { $shotInstalledHashes[$path] = (Get-ArenaObservedFileHash -LiteralPath (Join-Path $SptPath $path)).Hash }

function Run-Probe([string]$name, [string]$sourceRoot, [string]$suite = 'all', [string]$failure = '') {
    $dir = Join-Path $shotScratch $name
    [void][IO.Directory]::CreateDirectory($dir)
    $build = & dotnet build $shotProject -c Release --nologo "-p:ShotSourceRoot=$sourceRoot" "-p:BaseIntermediateOutputPath=$dir/obj/" "-p:OutputPath=$dir/out/" 2>&1 | Out-String
    $code = $LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $dir 'build.log'), $build)
    if ($code -ne 0) { throw "$name must compile before it can demonstrate a behavioral failure: $build" }
    $run = & dotnet (Join-Path $dir 'out/ShotAwardIntegrationChecks.dll') $suite 2>&1 | Out-String
    $code = $LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $dir 'result.log'), $run)
    if ($failure) {
        if ($code -eq 0 -or !$run.Contains('FAILED: ' + $failure)) { throw "$name did not reject the intended fault: $run" }
        Write-Output "Rejected $name at: $failure"
    } elseif ($code -ne 0 -or !$run.Contains('SHOT_AWARD_NATIVE_ADAPTER_PASSED:')) { throw "$name failed: $run" }
    else { Write-Output $run.Trim() }
}

try {
    $native = & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Verify-Native.ps1') -SptPath $SptPath 2>&1 | Out-String
    $nativeCode = $LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $shotScratch 'native-contract.log'), $native)
    if ($nativeCode -ne 0 -or !$native.Contains('SHOT_AWARD_NATIVE_SOURCE_CONTRACT_PASSED:')) { throw $native }
    Write-Output $native.Trim()
    Run-Probe 'production' $shotSource
    $faults = @(
        @{ Name='instant-airtime'; File='ArenaShotAwardHooks.cs'; Suite='airtime';
           Find='MinimumAirborneSeconds = 0.5f'; Replace='MinimumAirborneSeconds = 0f'; Failure='airtime threshold is inclusive and rejects short drops' },
        @{ Name='allow-aimed-airborne'; File='ArenaShotAwardHooks.cs'; Suite='conditions';
           Find='!firearm.IsAiming && HasQualifiedFlight'; Replace='HasQualifiedFlight'; Failure='airborne requires both native ground checks and no ADS' },
        @{ Name='foreign-active-world'; File='ArenaShotAwardHooks.cs'; Suite='admission';
           Find='&& ArenaSession.IsExactActiveRaid(game, world)'; Replace='&& true'; Failure='foreign active raid cannot create award facts' },
        @{ Name='wrong-ammunition-source'; File='ArenaShotAwardHooks.cs'; Suite='correlation';
           Find='|| !string.Equals(info.SourceId, scope.Shot.Ammo.TemplateId, StringComparison.Ordinal)'; Replace=''; Failure='wrong ammunition source rejected' },
        @{ Name='pooled-shot-retains-old-facts'; File='ArenaShotAwardHistory.cs'; Suite='lifetime';
           Find='_shots.Remove(shot);'; Replace='if (_shots.TryGetValue(shot, out _)) return;'; Failure='unknown pooled create erases former firing facts' },
        @{ Name='missing-native-finalizer-cleanup'; File='ArenaShotAwardHooks.cs'; Suite='finalizers';
           Find='__state?.Dispose(); return __exception;'; Replace='return __exception;'; Occurrences=3; Failure='fire exception finalizer clears capture' },
        @{ Name='pellets-count-as-bullet'; File='ArenaShotAwardHooks.cs'; Suite='collateral';
           Find='ammo.ProjectileCount == 1'; Replace='ammo.ProjectileCount >= 1'; Failure='independent fragments pellets rounds and invalid deaths do not combine: pellets' },
        @{ Name='generic-parent-is-continuation'; File='ArenaShotAwardHistory.cs'; Suite='collateral';
           Find='singleBullet && continuedBullet && parentToken == inherited.ProjectileToken'; Replace='singleBullet'; Failure='independent fragments pellets rounds and invalid deaths do not combine: fragment' },
        @{ Name='copied-lineage-loses-first-victim'; File='ArenaShotAwardHistory.cs'; Suite='collateral';
           Find='collateral = source.Collateral;'; Replace='collateral = new CollateralLineage();'; Failure='native penetration preserves first victim across precreated child' },
        @{ Name='duplicate-victim-counted'; File='ArenaShotAwardHistory.cs'; Suite='collateral';
           Find=' || lineage.FirstVictim == victimBodyId'; Replace=''; Failure='duplicate victim callback cannot earn collateral' },
        @{ Name='collateral-repeats'; File='ArenaShotAwardHistory.cs'; Suite='collateral';
           Find='lineage.Awarded = true;'; Replace='lineage.Awarded = false;'; Failure='third victim cannot repeat collateral' },
        @{ Name='world-identity-ignored'; File='ArenaShotAwardHistory.cs'; Suite='collateral';
           Find='|| entry.WorldId != worldId || entry.GameId != gameId'; Replace=''; Failure='stale bullet lineage cannot award: world' }
    )
    foreach ($fault in $faults) {
        $faultRoot = Join-Path $shotScratch ($fault.Name + '/source')
        [void][IO.Directory]::CreateDirectory($faultRoot)
        foreach ($file in $shotFiles) { Copy-Item -LiteralPath (Join-Path $shotSource $file) -Destination (Join-Path $faultRoot $file) }
        $path = Join-Path $faultRoot $fault.File
        $source = [IO.File]::ReadAllText($path)
        $occurrences = if ($fault.Occurrences) { $fault.Occurrences } else { 1 }
        if ($source.Split(@($fault.Find), [StringSplitOptions]::None).Length -ne $occurrences + 1) { throw "Ambiguous compiled fault anchor: $($fault.Name)" }
        [IO.File]::WriteAllText($path, $source.Replace($fault.Find, $fault.Replace))
        Run-Probe $fault.Name $faultRoot $fault.Suite $fault.Failure
    }
    Run-Probe 'production-final' $shotSource
    foreach ($path in $shotBound) {
        if ((Get-ArenaObservedFileHash -LiteralPath (Join-Path $shotRoot $path)).Hash -ne $shotHashes[$path]) { throw "Bound source changed; rerun required: $path" }
    }
} finally {
    foreach ($path in $shotInstalledPaths) {
        if ((Get-ArenaObservedFileHash -LiteralPath (Join-Path $SptPath $path)).Hash -ne $shotInstalledHashes[$path]) { throw "Installed binary changed during verification: $path" }
    }
}
[ordered]@{
    sourceHashes=$shotHashes; installedHashes=$shotInstalledHashes; sourcesUnchanged=$true; installedUnchanged=$true;
    compiledFaultCount=$faults.Count; completedUtc=[DateTime]::UtcNow.ToString('o'); evidenceDirectory=$shotScratch;
    limitation='Adapter behavior uses explicit native boundary doubles. Native metadata and source wiring checks are read-only, not in-game runtime proof.'
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $shotScratch 'manifest.json')
Write-Output "SHOT AWARD INTEGRATION PASSED; $($faults.Count) compiled faults rejected; installed Arena client/server/Core unchanged; evidence: $shotScratch"

} finally {
    Assert-ArenaInstalledSnapshotUnchanged $arenaLayoutSnapshot $arenaSnapshotRoot
}
