$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'TeamLaserChecks.csproj'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$temp = Join-Path ([IO.Path]::GetTempPath()) ('arena-team-laser-mutants-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($temp)
$sources = @{
    LaserSource = Join-Path $repo 'ArenaMode.Client/Effects/ArenaTeamLaserColors.cs'
    PolicySource = Join-Path $repo 'ArenaMode.Client/Effects/ArenaTeamLaserPolicy.cs'
}
$snapshots = @{}; $texts = @{}; $hashes = @{}
foreach ($key in $sources.Keys) {
    $texts[$key] = [IO.File]::ReadAllText($sources[$key])
    $hashes[$key] = (Get-FileHash -LiteralPath $sources[$key] -Algorithm SHA256).Hash
    $snapshots[$key] = Join-Path $temp ($key + '.cs')
    [IO.File]::WriteAllText($snapshots[$key], $texts[$key])
}
$mutants = @(
    @{ Name='NormalGate'; Key='LaserSource'; From='ArenaSession.IsActive && Settings.TeamLasers?.Value == true'; To='Settings.TeamLasers?.Value == true'; Failure='invalid scope remains native: normal' },
    @{ Name='IRVisibility'; Key='PolicySource'; From='Finite(factor) && factor > 0f'; To='Finite(factor)'; Failure='invalid scope remains native: irbeam' },
    @{ Name='PooledOwner'; Key='LaserSource'; From='!ReferenceEquals(hands.ControllerGameObject.GetComponent<WeaponPrefab>(), weapon)'; To='false'; Failure='invalid scope remains native: pooled' },
    @{ Name='LeakedBlock'; Key='LaserSource'; From='BeamBlock.SetValue(state.Laser, state.OriginalBeam);'; To='BeamBlock.SetValue(state.Laser, state.Beam);'; Failure='original block identity and absence of color override are restored' },
    @{ Name='IncreasedBrightness'; Key='PolicySource'; From='originalPeak / palettePeak'; To='1f'; Failure='native HDR magnitude and alpha are preserved' },
    @{ Name='LostLightRestore'; Key='PolicySource'; From='_applied && current.Equals(_last) ? _original : current'; To='current'; Failure='reset restores native light color' }
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
