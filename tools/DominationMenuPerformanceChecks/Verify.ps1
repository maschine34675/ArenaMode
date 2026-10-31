$ErrorActionPreference = 'Stop'
if (Test-Path variable:PSNativeCommandUseErrorActionPreference) { $PSNativeCommandUseErrorActionPreference = $false }
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project = Join-Path $PSScriptRoot 'DominationMenuPerformanceChecks.csproj'
$evidence = Join-Path $repo ('artifacts/domination-followup-20261026/menu-mutants/' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($evidence) | Out-Null
$sources = @{
    ProductionLocationPatchSource = Join-Path $repo 'ArenaMode.Client/Session/ArenaLocationSelectionPatch.cs'
    ProductionMenuSelectionSource = Join-Path $repo 'ArenaMode.Client/Session/ArenaMenuSelection.cs'
}
$hashes = @{}
foreach ($key in $sources.Keys) { $hashes[$key] = (Get-FileHash -LiteralPath $sources[$key] -Algorithm SHA256).Hash }
function Invoke-Case([string]$name, [string]$property, [string]$copy) {
    $arguments = @('run', '--project', $project, '-c', 'Release',
        "-p:CustomAfterMicrosoftCommonTargets=$(Join-Path $repo 'tools/AirPitChecks/NoDeploy.targets')",
        "-p:SPTUserMods=$(Join-Path $repo 'artifacts/nodeploy/must-not-exist')",
        "-p:OutputPath=$(Join-Path $evidence ($name + '-bin/'))",
        "-p:IntermediateOutputPath=$(Join-Path $evidence ($name + '-obj/'))")
    if ($property) { $arguments += "-p:$property=$copy" }
    $output = & dotnet @arguments 2>&1
    $code = $LASTEXITCODE
    $text = ($output | ForEach-Object { [string]$_ }) -join "`n"
    [IO.File]::WriteAllText((Join-Path $evidence ($name + '.log')), $text)
    return @{ Exit = $code; Text = $text }
}
$baseline = Invoke-Case 'baseline' '' ''
if ($baseline.Exit -ne 0 -or $baseline.Text -notmatch 'DOMINATION_MENU_PERFORMANCE_PASSED') { throw $baseline.Text }
$cases = @(
    @{ Name='tile-rechecks-admission'; Property='ProductionLocationPatchSource';
       From='row.View.Refresh(ArenaMapCatalog.CanSelectNativeLocation(row.MapId, availability.IsMapDiscoverable),';
       To='row.View.Refresh(ArenaMapCatalog.CanSelectNativeLocation(row.MapId, ArenaMenuSelection.IsMapDiscoverable),';
       Assertion='one display pass evaluates each eligibility source once, not once per map' },
    @{ Name='selected-map-rechecks-admission'; Property='ProductionLocationPatchSource';
       From='!availability.TryPrepareMapArea(Controller(screen)?.RaidSettings, map)';
       To='!ArenaMenuSelection.TryPrepareMapArea(Controller(screen)?.RaidSettings, map)';
       Assertion='selected-map repair and rendering share display eligibility' },
    @{ Name='snapshot-survives-refresh'; Property='ProductionLocationPatchSource';
       From='var availability = Availability = new ArenaMapAvailabilitySnapshot(ArenaMenuSelection.CurrentEligibility);';
       To='var availability = Availability ??= new ArenaMapAvailabilitySnapshot(ArenaMenuSelection.CurrentEligibility);';
       # Since 2026-10-31 the earlier refusal-between-refreshes check catches this before the mode switch.
       Assertion='a refusal that arrives between two refreshes reaches the next display pass' },
    @{ Name='final-start-trusts-old-display'; Property='ProductionMenuSelectionSource';
       From='var eligibility = CurrentEligibility;';
       To='var eligibility = new Eligibility(true, false, string.Empty);';
       Assertion='final start rechecks compatibility immediately instead of trusting the display snapshot' },
    @{ Name='display-pass-never-ends'; Property='ProductionLocationPatchSource';
       From='using (ArenaMenuSelection.BeginDisplayPass())';
       To='ArenaMenuSelection.BeginDisplayPass();';
       Assertion='a native click after a display pass evaluates admission fresh' },
    @{ Name='verdict-survives-pass'; Property='ProductionMenuSelectionSource';
       From='if (--_displayPassDepth == 0) ForgetDisplayPassVerdicts();';
       To='--_displayPassDepth;';
       Assertion='a refusal that arrives between two refreshes reaches the next display pass' },
    @{ Name='refresh-without-display-pass'; Property='ProductionLocationPatchSource';
       From='using (ArenaMenuSelection.BeginDisplayPass())';
       To='';
       Assertion='one display pass evaluates optional-mod admission exactly once' },
    # A pass that ends only when the refresh returns (no finally): a throwing refresh leaves it open.
    @{ Name='display-pass-without-finally'; Property='ProductionLocationPatchSource';
       From='using (ArenaMenuSelection.BeginDisplayPass())';
       To='for (var pass = ArenaMenuSelection.BeginDisplayPass(); pass != null; pass.Dispose(), pass = null)';
       Assertion='a native click after a refresh that threw evaluates admission fresh' },
    @{ Name='inner-pass-end-drops-verdicts'; Property='ProductionMenuSelectionSource';
       From='if (--_displayPassDepth == 0) ForgetDisplayPassVerdicts();';
       To='--_displayPassDepth; ForgetDisplayPassVerdicts();';
       Assertion='a nested display pass keeps the outer verdict and only the outermost end drops it' }
)
foreach ($case in $cases) {
    $source = [IO.File]::ReadAllText($sources[$case.Property])
    if ($source.Split([string[]]@($case.From), [StringSplitOptions]::None).Count -ne 2) {
        throw "Mutation anchor is not unique: $($case.Name)"
    }
    $copy = Join-Path $evidence ($case.Name + '.cs')
    [IO.File]::WriteAllText($copy, $source.Replace($case.From, $case.To))
    $result = Invoke-Case $case.Name $case.Property $copy
    if ($result.Exit -eq 0 -or $result.Text -match 'error CS' -or
        $result.Text -notmatch [regex]::Escape('ASSERT: ' + $case.Assertion)) { throw $result.Text }
    Write-Output "MENU_PERFORMANCE_MUTANT_REJECTED: $($case.Name)"
}
foreach ($key in $sources.Keys) {
    if ((Get-FileHash -LiteralPath $sources[$key] -Algorithm SHA256).Hash -ne $hashes[$key]) {
        throw "A production source changed during the check: $($sources[$key])"
    }
}
Write-Output "DOMINATION_MENU_PERFORMANCE_VERIFIED: $($cases.Count) behavioral mutants; evidence $evidence"
