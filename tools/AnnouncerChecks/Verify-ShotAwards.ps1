param([switch]$ShotOnly)
$ErrorActionPreference = 'Stop'
$shotRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$shotScratch = Join-Path $shotRoot ('artifacts/shot-award-audio/' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($shotScratch)
$shotSources = @{
    AwardsSource = 'ArenaMode.Client/Combat/ArenaCombatAwards.cs'
    AnnouncerServiceSource = 'ArenaMode.Client/Audio/AnnouncerService.cs'
}
$shotBound = @($shotSources.Values) + @('ArenaMode.Client/Combat/ArenaKill.cs')
$shotBound += @('tools/AnnouncerChecks', 'tools/AnnouncerCombatChecks') | ForEach-Object {
    $directory = $_
    Get-ChildItem -LiteralPath (Join-Path $shotRoot $directory) -File | ForEach-Object { $directory + '/' + $_.Name }
}
$shotHashes = @{}
foreach ($relative in $shotBound) { $shotHashes[$relative] = (Get-FileHash -LiteralPath (Join-Path $shotRoot $relative)).Hash }
$shotTexts = @{}
foreach ($property in $shotSources.Keys) { $shotTexts[$property] = [IO.File]::ReadAllText((Join-Path $shotRoot $shotSources[$property])).Replace("`r`n", "`n") }

# Retain the prior full regressions: these already exercise production audio
# output/recovery/queue/result/Clutch and complete match/scoreboard admission.
foreach ($check in $(if ($ShotOnly) { @() } else { @('tools/AnnouncerChecks/Verify-Regression.ps1', 'tools/AnnouncerCombatChecks/Verify.ps1') })) {
    & pwsh -NoProfile -File (Join-Path $shotRoot $check) 2>&1 | Tee-Object -FilePath (Join-Path $shotScratch ((Split-Path (Split-Path $check) -Leaf) + '.log'))
    if ($LASTEXITCODE -ne 0) { throw "Historical regression failed: $check" }
}

$shotFaults = @(
    @{ Name='collateral-label-suppressed'; Property='AwardsSource';
       Find='&& ArenaShotAwardHistory.ReportCollateralDeath(kill.CollateralProof, kill.VictimBodyId)'; Replace='&& false';
       Failure='collateral takes presentation precedence over DOUBLE KILL' },
    @{ Name='text-only-attempts-audio'; Property='AnnouncerServiceSource';
       Find='if (key == "collateral") return;'; Replace='';
       Failure='Text-only collateral neither plays nor diagnoses missing audio' },
    @{ Name='spree-before-shot'; Property='AwardsSource';
       Find=': kill.ScoreEligible && kill.AirborneHipFire ? "AIRBORNE"';
       Replace=': existingLabel != null ? existingLabel : kill.ScoreEligible && kill.AirborneHipFire ? "AIRBORNE"';
       Failure='shot award precedes simultaneous spree: AIRBORNE' },
    @{ Name='noscope-before-airborne'; Property='AwardsSource';
       Find=": kill.ScoreEligible && kill.AirborneHipFire ? `"AIRBORNE`"`n            : kill.ScoreEligible && kill.BoltActionHipFire ? `"NO SCOPE`"";
       Replace=": kill.ScoreEligible && kill.BoltActionHipFire ? `"NO SCOPE`"`n            : kill.ScoreEligible && kill.AirborneHipFire ? `"AIRBORNE`"";
       Failure='verified shot selects exactly one logical award: AIRBORNE' },
    @{ Name='godlike-priority-collapse'; Property='AnnouncerServiceSource';
       Find='"no_scope" => 65,'; Replace='"no_scope" => 60,';
       Failure='Shot queue priority chooses the expected recording: NO SCOPE/KILLING SPREE' },
    @{ Name='missing-fallback'; Property='AnnouncerServiceSource';
       Find='=> key is "airborne" or "no_scope" ? "godlike" : null;'; Replace='=> (string)null;';
       Failure='Missing shot recordings use Godlike exactly once while preserving logical awards: AIRBORNE' },
    @{ Name='failed-custom-runtime'; Property='AnnouncerServiceSource';
       Find="Skip(key, `"clip_rebuild_failed`", clipProblem);`n                return PlayShotAwardFallback(key, out clipLengthSeconds);";
       Replace="Skip(key, `"clip_rebuild_failed`", clipProblem);`n                return false;";
       Failure='Failed custom runtime reconstruction uses one provisional recording' },
    @{ Name='custom-recording-ignored'; Property='AnnouncerServiceSource';
       Find='var found = Clips.TryGetValue(key, out var cached);';
       Replace='var found = Clips.TryGetValue(key, out var cached); if (found && ShotAwardFallback(key) != null) { key = ShotAwardFallback(key); found = Clips.TryGetValue(key, out cached); }';
       Failure='Healthy custom root audio wins; invalid optional decode still falls back: AIRBORNE' }
)
foreach ($fault in $shotFaults) {
    $source = $shotTexts[$fault.Property]
    if ($source.Split(@($fault.Find), [StringSplitOptions]::None).Length -ne 2) { throw "Ambiguous shot fault anchor: $($fault.Name)" }
    $directory = Join-Path $shotScratch $fault.Name
    [void][IO.Directory]::CreateDirectory($directory)
    $copy = Join-Path $directory 'Source.cs'
    [IO.File]::WriteAllText($copy, $source.Replace($fault.Find, $fault.Replace))
    $combat = $fault.Property -eq 'AwardsSource'
    $assembly = if ($combat) { 'AnnouncerCombatChecks' } else { 'AnnouncerChecks' }
    $project = Join-Path $shotRoot ('tools/' + $assembly + '/' + $assembly + '.csproj')
    $build = & dotnet build $project -c Release "-p:$($fault.Property)=$copy" "-p:IntermediateOutputPath=$directory/obj/" "-p:OutputPath=$directory/out/" --nologo 2>&1 | Out-String
    [IO.File]::WriteAllText((Join-Path $directory 'build.log'), $build)
    if ($LASTEXITCODE -ne 0) { throw "$($fault.Name) failed to compile: $build" }
    $arguments = @(Join-Path $directory ('out/' + $assembly + '.dll'))
    if ($combat) { $arguments += 'shot' }
    $run = & dotnet @arguments 2>&1 | Out-String
    $exitCode = $LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $directory 'result.log'), $run)
    if ($exitCode -eq 0 -or !$run.Contains($fault.Failure)) { throw "$($fault.Name) did not reject expected behavior: $run" }
    Write-Host "Rejected $($fault.Name): $($fault.Failure)"
}
foreach ($relative in $shotBound) {
    if ((Get-FileHash -LiteralPath (Join-Path $shotRoot $relative)).Hash -ne $shotHashes[$relative]) { throw "Shot award source changed during checks; rerun required: $relative" }
}
foreach ($project in @('tools/AnnouncerChecks', 'tools/AnnouncerCombatChecks')) {
    & dotnet run --project (Join-Path $shotRoot $project) -c Release
    if ($LASTEXITCODE -ne 0) { throw "Final unchanged production regression failed: $project" }
}
[ordered]@{ sourceHashes=$shotHashes; controls=$shotFaults.Count;
    limitation='Offline real awards and playback service; external native/audio capture boundary doubled. No GPU or audible-output proof.' } |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $shotScratch 'source-binding.json') -Encoding utf8
if ($ShotOnly) { Write-Host "SHOT AWARD FOCUSED CHECKS PASSED; $($shotFaults.Count) controls; historical verification not run; evidence: $shotScratch" }
else { Write-Host "SHOT AWARD AUDIO CHECKS PASSED; $($shotFaults.Count) new compiled fault controls plus historical regressions; evidence: $shotScratch" }
