param([string]$SptPath = ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))))
. (Join-Path $PSScriptRoot '../Read-ArenaInstalledSnapshot.ps1')
$arenaSnapshotRoot = $SptPath
$arenaLayoutSnapshot = Read-ArenaInstalledSnapshot $arenaSnapshotRoot
try {
$ErrorActionPreference = 'Stop'
$powerRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$powerSource = Join-Path $powerRepo 'ArenaMode.Client/Pickups'
$indicatorSource = Join-Path $powerRepo 'ArenaMode.Client/Effects'
$powerEvidence = Join-Path $powerRepo ('artifacts/powerup-checks/' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($powerEvidence)
$powerInputs = @{}
$powerInputPaths = @('ArenaMode.Client/Pickups/ArenaPowerups.cs','ArenaMode.Client/Pickups/ArenaPowerupState.cs',
    'ArenaMode.Client/Pickups/ArenaPickupColors.cs','ArenaMode.Client/Pickups/ArenaPickupView.cs','ArenaMode.Client/Pickups/ArenaPickupRuntime.cs',
    'ArenaMode.Client/Effects/ArenaSpawnShieldView.cs','ArenaMode.Client/Effects/ArenaPowerupIndicator.cs','ArenaMode.Client/Effects/ArenaBotEffectLeases.cs','ArenaMode.Client/Effects/ArenaBotEffectRenderer.cs',
    'ArenaMode.Client/Combat/ArenaAdministrativeDeathGate.cs','ArenaMode.Client/Combat/CombatPatches.cs','ArenaMode.Client/Combat/ArenaAssistCapture.cs')
$powerInputPaths += @(Get-ChildItem -LiteralPath $PSScriptRoot -File | ForEach-Object { 'tools/ArenaPowerupChecks/' + $_.Name })
foreach ($path in $powerInputPaths) { $powerInputs[$path] = (Get-ArenaObservedFileHash -LiteralPath (Join-Path $powerRepo $path)).Hash }
$powerInstalled = @{}
foreach ($path in @('BepInEx/plugins/maschine-ArenaMode/maschine-ArenaMode.Client.dll','BepInEx/plugins/maschine-Redeploy.Core.dll',
    'SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll','EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll')) {
    $powerInstalled[$path] = (Get-ArenaObservedFileHash -LiteralPath (Join-Path $SptPath $path)).Hash
}
function Run-Power([string]$name, [string]$source, [string]$failure = '', [string]$indicator = $indicatorSource) {
    $powerDir = Join-Path $powerEvidence $name; [void][IO.Directory]::CreateDirectory($powerDir)
    $powerBuild = & dotnet build (Join-Path $PSScriptRoot 'ArenaPowerupChecks.csproj') -c Release --nologo "-p:PowerupSourceRoot=$source" "-p:IndicatorSourceRoot=$indicator" "-p:BaseIntermediateOutputPath=$powerDir/obj/" "-p:OutputPath=$powerDir/out/" 2>&1 | Out-String
    $code = $LASTEXITCODE; [IO.File]::WriteAllText((Join-Path $powerDir 'build.log'), $powerBuild)
    if ($code -ne 0) { throw "$name must compile before behavioral verification: $powerBuild" }
    $powerRun = & dotnet (Join-Path $powerDir 'out/ArenaPowerupChecks.dll') 2>&1 | Out-String
    $code = $LASTEXITCODE; [IO.File]::WriteAllText((Join-Path $powerDir 'result.log'), $powerRun)
    if ($failure) {
        if ($code -eq 0 -or !$powerRun.Contains('ASSERT: ' + $failure)) { throw "$name did not reject intended fault: $powerRun" }
        Write-Output "Rejected $name at: $failure"
    } elseif ($code -ne 0 -or !$powerRun.Contains('ARENA_POWERUP_BEHAVIOR_PASSED:')) { throw "$name failed: $powerRun" }
    else { Write-Output $powerRun.Trim() }
}
try {
    $native = & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Verify-Native.ps1') -SptPath $SptPath 2>&1 | Out-String
    $code = $LASTEXITCODE; [IO.File]::WriteAllText((Join-Path $powerEvidence 'native.log'), $native)
    if ($code -ne 0 -or !$native.Contains('ARENA_POWERUP_NATIVE_PASSED:')) { throw $native }
    Write-Output $native.Trim()
    Run-Power 'production' $powerSource
    $faults = @(
        @{Name='ammo-stacks';File='ArenaPowerupState.cs';Find='_ammoUntil = deadline;';Replace='_ammoUntil = Math.Max(now, _ammoUntil) + duration;';Failure='ammo duration capped at ten seconds'},
        @{ Name='boots-stack'; File='ArenaPowerupState.cs'; Find='_jumpUntil = deadline;'; Replace='_jumpUntil = Math.Max(now, _jumpUntil) + (deadline - now);'; Failure='boots refresh never adds duration' },
        @{ Name='duration-stacks'; File='ArenaPowerupState.cs'; Find='_damageUntil = deadline;'; Replace='_damageUntil = Math.Max(now, _damageUntil) + (deadline - now);'; Failure='refresh does not stack duration' },
        @{ Name='shield-pool-overfill'; File='ArenaPowerupState.cs'; Find='_shieldPoints = Math.Min(points, MaximumShield);'; Replace='_shieldPoints = points;'; Failure='shield point hard cap' },
        @{ Name='native-head-reduction'; File='ArenaPowerups.cs'; Find='bodyPart == EBodyPart.Head && health.DamageMultiplier <= 1f'; Replace='false'; Failure='shield uses post-multiplier points' },
        @{ Name='spend-during-invulnerability'; File='ArenaPowerups.cs'; Find='|| !ArenaPowerupState.PositiveFinite(health.DamageCoeff)'; Replace='|| false'; Failure='native invulnerability leaves input unchanged' },
        @{ Name='dead-owner-shield-bypass'; File='ArenaPowerups.cs'; Find='|| !ArenaPickupRuntime.IsLiveActor(victim)'; Replace='|| !ArenaPickupRuntime.IsLiveActor(victim) || !ArenaPickupRuntime.IsLiveActor(attacker)'; Failure='dead thrower grenade still meets shield' },
        @{ Name='old-grenade-new-bonus'; File='ArenaPowerups.cs'; Find='info.DamageType == EDamageType.Bullet && info.Weapon is Weapon weapon && !weapon.IsGrenadeLauncher'; Replace='true'; Failure='only direct bullet damage doubled Explosion' },
        @{ Name='physical-grenade-fragment-bonus'; File='ArenaPowerups.cs'; Find='&& info.Weapon is Weapon weapon && !weapon.IsGrenadeLauncher'; Replace=''; Failure='physical grenade fragment cannot inherit outgoing bonus' },
        @{ Name='life-death-retains-buff'; File='ArenaPowerups.cs'; Find='        _lives.Remove(player);'; Replace='        // Fault: retain dead life'; Failure='native death clears physical life immediately' },
        @{ Name='indicator-local-leak'; File='ArenaPowerups.cs'; Find='!player.IsAI || (state.DamageSeconds <= 0f && state.ShieldPoints <= 0f)'; Replace='(state.DamageSeconds <= 0f && state.ShieldPoints <= 0f)'; Failure='local powerups never add a third-person effect lease' },
        @{ Name='indicator-wrong-body'; File='ArenaPowerups.cs'; Find='new ArenaPowerupIndicator(world, player)'; Replace='new ArenaPowerupIndicator(world, world.MainPlayer)'; Failure='shield indicator requests shield color channel' },
        @{ Name='indicator-disabled-body-visible'; File='ArenaPowerups.cs'; Find='player.gameObject.activeInHierarchy ? state : default'; Replace='state'; Failure='disabled native body cannot leave contours visible' },
        @{ Name='indicator-no-immediate-depletion'; File='ArenaPowerups.cs'; Find='if (victimLife != null) RefreshIndicator(victim, victimLife);'; Replace=''; Failure='depletion immediately clears shield without touching spawn protection' },
        @{ Name='indicator-reset-leak'; File='ArenaPowerups.cs'; Find='if (_lives.TryGetValue(player, out var life)) life.Indicator?.Dispose();'; Replace='if (_lives.TryGetValue(player, out var life)) { }'; Failure='local powerups never add a third-person effect lease' },
        @{ Name='indicator-wrong-color'; File='ArenaPickupColors.cs'; Find='internal static Color DoubleDamage => new(1f, .25f, .7f);'; Replace='internal static Color DoubleDamage => new(.25f, .8f, 1f);'; Failure='damage indicator uses pickup pink' },
        @{ Name='indicator-no-damage-channel'; File='ArenaPowerupIndicator.cs'; Find='state.DamageSeconds > 0f'; Replace='false'; Failure='double damage has its own simultaneous color channel'; Indicator=$true }
    )
    foreach ($fault in $faults) {
        $faultRoot = Join-Path $powerEvidence ($fault.Name + '/source'); [void][IO.Directory]::CreateDirectory($faultRoot)
        foreach ($name in @('ArenaPowerups.cs','ArenaPowerupState.cs','ArenaPickupColors.cs','ArenaSpawnShieldView.cs','ArenaPowerupIndicator.cs','ArenaBotEffectLeases.cs')) {
            $sourceDir = if ($name -in @('ArenaSpawnShieldView.cs','ArenaPowerupIndicator.cs','ArenaBotEffectLeases.cs')) { $indicatorSource } else { $powerSource }
            $text = [IO.File]::ReadAllText((Join-Path $sourceDir $name))
            if ($name -eq $fault.File) {
                if ($text.Split(@($fault.Find), [StringSplitOptions]::None).Length -ne 2) { throw "Ambiguous fault anchor: $($fault.Name)" }
                $text = $text.Replace($fault.Find, $fault.Replace)
            }
            [IO.File]::WriteAllText((Join-Path $faultRoot $name), $text)
        }
        Run-Power $fault.Name $faultRoot $fault.Failure $faultRoot
    }
    Run-Power 'production-final' $powerSource
    foreach ($path in $powerInputs.Keys) {
        if ((Get-ArenaObservedFileHash -LiteralPath (Join-Path $powerRepo $path)).Hash -ne $powerInputs[$path]) { throw "Bound source changed; rerun required: $path" }
    }
} finally {
    foreach ($path in $powerInstalled.Keys) {
        if ((Get-ArenaObservedFileHash -LiteralPath (Join-Path $SptPath $path)).Hash -ne $powerInstalled[$path]) { throw "Installed binary changed: $path" }
    }
}
[ordered]@{ sourceHashes=$powerInputs; installedHashes=$powerInstalled; sourcesUnchanged=$true; installedUnchanged=$true;
    compiledFaultCount=$faults.Count; completedUtc=[DateTime]::UtcNow.ToString('o'); evidenceDirectory=$powerEvidence;
    limitation='Actual production effects policy/adapter use explicit native/world/mode boundary doubles. Installed IL/source contracts verify native ordering. No Unity/Harmony execution, full client build or deployment.'
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $powerEvidence 'manifest.json')
Write-Output "ARENA POWERUP CHECKS PASSED; $($faults.Count) compiled faults rejected; evidence: $powerEvidence"

} finally {
    Assert-ArenaInstalledSnapshotUnchanged $arenaLayoutSnapshot $arenaSnapshotRoot
}
