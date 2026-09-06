#requires -Version 7.0
param([string]$SptPath = 'D:\SPT41')
$ErrorActionPreference = 'Stop'

# Native assemblies/materials plus the complete production-linked brightness,
# team/body/raid/pool replay and its existing thirteen negative controls.
& (Join-Path $PSScriptRoot 'Verify-Brightness.ps1') -SptPath $SptPath
& python (Join-Path $PSScriptRoot 'Verify-BeamShader.py') $SptPath
if ($LASTEXITCODE -ne 0) { throw 'Installed beam/point shader instructions no longer match the reviewed rendering boundary.' }

# Beam-specific mutations compile only scratch copies. A compiler error or an
# unrelated failure is not an accepted negative control; the intended runtime
# assertion must reject each mutation. Production source is never rewritten.
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project = Join-Path $PSScriptRoot 'TeamLaserChecks.csproj'
$temp = Join-Path ([IO.Path]::GetTempPath()) ('arena-beam-mutants-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($temp)
$sources = @{
    LaserSource = Join-Path $repo 'ArenaMode.Client/Effects/ArenaTeamLaserColors.cs'
    PolicySource = Join-Path $repo 'ArenaMode.Client/Effects/ArenaTeamLaserPolicy.cs'
    BrightnessSource = Join-Path $repo 'ArenaMode.Client/Effects/ArenaLaserBrightness.cs'
    TacticalSource = Join-Path $repo 'ArenaMode.Client/Modes/TacticalMode.Lasers.cs'
}
$texts = @{}; $snapshots = @{}; $hashes = @{}
foreach ($key in $sources.Keys) {
    $texts[$key] = [IO.File]::ReadAllText($sources[$key])
    $hashes[$key] = (Get-FileHash -LiteralPath $sources[$key] -Algorithm SHA256).Hash
    $snapshots[$key] = Join-Path $temp ($key + '.cs')
    [IO.File]::WriteAllText($snapshots[$key], $texts[$key])
}
$mutants = @(
    @{ Name='NoBeamOutput'; Key='LaserSource'; From='state.Beam.SetFloat(FactorId, beamFactor);'; To='state.Beam.SetFloat(FactorId, laser.BeamMaterial.GetFloat(FactorId));'; Failure='beam factor boost reaches the full native taper: 0' },
    @{ Name='BeamStacksBright'; Key='BrightnessSource'; From='float minimum = baseline.BeamFactor * Math.Min(setting, 5f);'; To='float minimum = current * Math.Min(setting, 5f);'; Failure='Bright factor two plus Arena beam two stays two' },
    @{ Name='BeamChangesPoint'; Key='LaserSource'; From='state.Beam.SetFloat(FactorId, beamFactor);'; To='{ state.Beam.SetFloat(FactorId, beamFactor); state.Point.SetFloat(FactorId, beamFactor); }'; Failure='beam setting does not further brighten point or spot light' },
    @{ Name='BeamWritesMaterial'; Key='LaserSource'; From='state.Beam.SetFloat(FactorId, beamFactor);'; To='laser.BeamMaterial.SetFloat(FactorId, beamFactor);'; Failure='beam and point materials remain unchanged after beam boost' },
    @{ Name='ForeignBeamBaseline'; Key='BrightnessSource'; From='!ReferenceEquals(laser.BeamMaterial, baseline.InitializedBeamMaterial?.Target)'; To='false'; Failure='unrelated later material cannot inherit or recapture a foreign beam baseline' },
    @{ Name='DisabledBeamBoost'; Key='BrightnessSource'; From='!Positive(current) || !Positive(setting) || setting <= 1f'; To='!Positive(current) || !Positive(setting)'; Failure='beam setting one adds nothing even when another mod dims the initialized material' }
)
foreach ($m in $mutants) {
    if (-not $texts[$m.Key].Contains($m.From)) { throw "Missing beam mutant anchor $($m.Name)" }
    $path = Join-Path $temp ($m.Name + '.cs')
    [IO.File]::WriteAllText($path, $texts[$m.Key].Replace($m.From, $m.To))
    $argsForDotnet = @('run', '--project', $project, '-c', ('BeamMutant' + $m.Name))
    foreach ($key in $sources.Keys) {
        $source = if ($key -eq $m.Key) { $path } else { $snapshots[$key] }
        $argsForDotnet += "-p:${key}=$source"
    }
    $output = & dotnet @argsForDotnet 2>&1 | Out-String
    if ($LASTEXITCODE -eq 0 -or -not $output.Contains($m.Failure)) { throw "Beam mutant did not fail its intended assertion: $($m.Name)`n$output" }
    Write-Output "Rejected beam mutant: $($m.Name)"
}
foreach ($key in $sources.Keys) {
    if ((Get-FileHash -LiteralPath $sources[$key] -Algorithm SHA256).Hash -ne $hashes[$key]) { throw "Source changed during beam regression: $key" }
}
& dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) { throw 'Final production-linked beam baseline failed.' }
Write-Output 'ARENA_LASER_BEAM_PASSED'
