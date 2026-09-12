$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'TeamLaserChecks.csproj'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$temp = Join-Path ([IO.Path]::GetTempPath()) ('arena-team-laser-mutants-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($temp)
$sources = @{
    LaserSource = Join-Path $repo 'ArenaMode.Client/Effects/ArenaTeamLaserColors.cs'
    PolicySource = Join-Path $repo 'ArenaMode.Client/Effects/ArenaTeamLaserPolicy.cs'
    BrightnessSource = Join-Path $repo 'ArenaMode.Client/Effects/ArenaLaserBrightness.cs'
    TacticalSource = Join-Path $repo 'ArenaMode.Client/Modes/TacticalMode.Lasers.cs'
}
$snapshots = @{}; $texts = @{}; $hashes = @{}
foreach ($key in $sources.Keys) {
    $texts[$key] = [IO.File]::ReadAllText($sources[$key])
    $hashes[$key] = (Get-FileHash -LiteralPath $sources[$key] -Algorithm SHA256).Hash
    $snapshots[$key] = Join-Path $temp ($key + '.cs')
    [IO.File]::WriteAllText($snapshots[$key], $texts[$key])
}
$mutants = @(
    @{ Name='DeathmatchAdmission'; Key='LaserSource'; From='mode.Id != ArenaMode.Shared.DeathmatchSelection.ModeId && '; To=''; Failure='solo Arena brightness without invented teams: deathmatch' },
    @{ Name='NormalGate'; Key='LaserSource'; From='ArenaSession.IsActive && (Settings.TeamLasers?.Value'; To='(Settings.TeamLasers?.Value'; Failure='invalid scope remains native: normal' },
    @{ Name='IRVisibility'; Key='PolicySource'; From='Finite(factor) && factor > 0f'; To='Finite(factor)'; Failure='invalid scope remains native: irbeam' },
    @{ Name='PooledOwner'; Key='LaserSource'; From='!ReferenceEquals(hands.ControllerGameObject.GetComponent<WeaponPrefab>(), weapon)'; To='false'; Failure='invalid scope remains native: pooled' },
    @{ Name='LeakedBlock'; Key='LaserSource'; From='BeamBlock.SetValue(state.Laser, state.OriginalBeam);'; To='BeamBlock.SetValue(state.Laser, state.Beam);'; Failure='original block identity and absence of color override are restored' },
    @{ Name='IncreasedBrightness'; Key='PolicySource'; From='originalPeak / palettePeak'; To='1f'; Failure='native HDR magnitude and alpha are preserved' },
    @{ Name='LostLightRestore'; Key='PolicySource'; From='_applied && current.Equals(_last) ? _original : current'; To='current'; Failure='reset restores native light color' },
    @{ Name='DoubleBrightBoost'; Key='BrightnessSource'; From='Math.Max(current, minimum)'; To='current * Math.Min(setting, 5f)'; Failure='BrightLasers 2 plus Arena 2 remains 2' },
    @{ Name='ScalarLeak'; Key='LaserSource'; From='state.Laser.IntensityFactor = state.OriginalIntensity;'; To='state.Laser.IntensityFactor = state.AppliedIntensity;'; Failure='Arena 4 raises Bright 2 only to prefab 4' },
    @{ Name='BrightnessLightLeak'; Key='BrightnessSource'; From='_applied && current.Equals(_last) ? _original : current'; To='current'; Failure='boosted deferred light restores to BrightLasers baseline' },
    @{ Name='AliveBodyAlias'; Key='LaserSource'; From='!ReferenceEquals(world.GetAlivePlayerByProfileID(owner.ProfileId), owner)'; To='false'; Failure='brightness invalid scope remains native: alive-alias' },
    @{ Name='TacticalScopeOmission'; Key='LaserSource'; From='if (!tacticalMode.TryClassifyLaserBody(owner, out bool side)) return false;'; To='if (!tacticalMode.TryClassifyLaserBody(owner, out bool side) || mode.Id == "tactical") return false;'; Failure='protected Tactical exact roster team color: 0' },
    @{ Name='TacticalRebind'; Key='TacticalSource'; From='_phase != Phase.Live || IsComplete'; To='IsComplete'; Failure='Tactical rejects stale round or body: rebind' },
    @{ Name='TacticalPhysicalAlias'; Key='TacticalSource'; From='ReferenceEquals(owner, _boundTeamFollowers[i])'; To='true'; Failure='Tactical rejects stale round or body: body-alias' }
)
foreach ($m in $mutants) {
    if (-not $texts[$m.Key].Contains($m.From)) { throw "Missing mutant anchor $($m.Name)" }
    $path = Join-Path $temp ($m.Name + '.cs')
    [IO.File]::WriteAllText($path, $texts[$m.Key].Replace($m.From, $m.To))
    $argsForDotnet = @('run', '--project', $project, '-c', ('LaserMutant' + $m.Name))
    foreach ($key in $sources.Keys) {
        $source = if ($key -eq $m.Key) { $path } else { $snapshots[$key] }
        $argsForDotnet += "-p:${key}=$source"
    }
    $output = & dotnet @argsForDotnet 2>&1 | Out-String
    if ($LASTEXITCODE -eq 0 -or -not $output.Contains($m.Failure)) { throw "Mutant did not fail its intended assertion: $($m.Name)`n$output" }
    Write-Output "Rejected mutant: $($m.Name)"
}
foreach ($key in $sources.Keys) {
    if ((Get-FileHash -LiteralPath $sources[$key] -Algorithm SHA256).Hash -ne $hashes[$key]) { throw "Source changed during regression: $key" }
}
& dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) { throw 'Final production-linked baseline failed.' }
Write-Output 'ARENA_TEAM_LASER_REGRESSION_PASSED'
