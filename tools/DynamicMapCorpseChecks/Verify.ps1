#requires -Version 7.0
param()
$ErrorActionPreference = 'Stop'
$mapRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$mapProject = Join-Path $PSScriptRoot 'DynamicMapCorpseChecks.csproj'
$mapAdapterPath = Join-Path $mapRepo 'ArenaMode.Client/Compat/DynamicMapsCorpseMarkers.cs'
$mapCleanupPath = Join-Path $mapRepo 'ArenaMode.Client/Bots/CorpseCleanupService.cs'
$mapAdapter = [IO.File]::ReadAllText($mapAdapterPath)
$mapCleanup = [IO.File]::ReadAllText($mapCleanupPath)
$mapEvidence = Join-Path ([IO.Path]::GetTempPath()) ('arena-dynamicmap-corpse-checks-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($mapEvidence)
function Invoke-Behavior([string[]]$Options = @()) {
    # This isolated project links source only: no mod projects/imports/deployment.
    $mapBuild = @(& dotnet build $mapProject -t:Rebuild -c Release --nologo @Options 2>&1)
    if ($LASTEXITCODE -ne 0) { throw ($mapBuild -join [Environment]::NewLine) }
    $mapRun = @(& dotnet run --project $mapProject -c Release --no-build 2>&1)
    return @{ Exit = $LASTEXITCODE; Text = ($mapRun | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine }
}
function Assert-Production {
    $mapResult = Invoke-Behavior
    if ($mapResult.Exit -ne 0 -or !$mapResult.Text.Contains('ARENA_DYNAMICMAP_CORPSE_BEHAVIOR_PASSED')) { throw $mapResult.Text }
    $mapResult.Text
}
function Reject-Mutant([string]$Name, [string]$Property, [string]$Before, [string]$After, [string]$Assertion) {
    $mapSource = if ($Property -eq 'AdapterSource') { $mapAdapter } else { $mapCleanup }
    if (!$mapSource.Contains($Before)) { throw "Missing compiled mutation anchor: $Name" }
    $mapMutant = Join-Path $mapEvidence ($Name + '.cs')
    [IO.File]::WriteAllText($mapMutant, $mapSource.Replace($Before, $After))
    $mapResult = Invoke-Behavior @("-p:${Property}=$mapMutant")
    [IO.File]::WriteAllText((Join-Path $mapEvidence ($Name + '.log')), $mapResult.Text)
    if ($mapResult.Exit -eq 0 -or !$mapResult.Text.Contains('ASSERT: ' + $Assertion + ';')) {
        throw "Compiled mutation did not fail intended assertion: $Name`n$($mapResult.Text)"
    }
    Write-Output "Rejected compiled control: $Name -> $Assertion"
}
& (Join-Path $PSScriptRoot 'Verify-InstalledContracts.ps1')
try {
    Assert-Production
    Reject-Mutant 'keep-existing-skulls' 'CleanupSource' 'Compat.DynamicMapsCorpseMarkers.RemoveTrimmedMarkers();' '// regression: no map cleanup' 'successful trim removes existing map and minimap markers'
    Reject-Mutant 'allow-later-skulls' 'AdapterSource' 'return !IsTrimmed(__0);' 'return true;' 'later and reopened map markers stay suppressed'
    Reject-Mutant 'suppress-failed-trim' 'CleanupSource' 'new TrimState { Body = new Renderer[0], Sweep = _sweep };' 'new TrimState { Body = new Renderer[0], Sweep = _sweep, Completed = true, World = Singleton<GameWorld>.Instance };' 'excluded or failed trim keeps existing marker failed'
    Reject-Mutant 'destroy-behind-a-held-skull' 'AdapterSource' 'if (!_enabled || player == null) return false;' 'if (true) return false;' 'a body whose map marker is still held is never destroyed'
    Reject-Mutant 'leak-world-evidence' 'CleanupSource' '&& ReferenceEquals(state.World, Singleton<GameWorld>.Instance)' '&& true' 'lifetime boundary stops both suppression paths world'
    Reject-Mutant 'retain-reset-evidence' 'CleanupSource' 'Trimmed.Clear();' '// regression: retain previous trim evidence' 'lifetime boundary stops both suppression paths reset'
    Reject-Mutant 'unreviewed-version' 'AdapterSource' 'info.Metadata.Version != new System.Version(1, 2, 1)' 'false' 'optional bind failure leaves no hook version'
    Reject-Mutant 'suppress-all-bodies' 'AdapterSource' 'return !IsTrimmed(__0);' 'return false;' 'untrimmed bodies retain markers'
}
finally {
    if ([IO.File]::ReadAllText($mapAdapterPath) -ne $mapAdapter -or [IO.File]::ReadAllText($mapCleanupPath) -ne $mapCleanup) { throw 'Production source changed during checks.' }
    Assert-Production
}
& pwsh -NoProfile -File (Join-Path $mapRepo 'tools/CorpseCleanupChecks/Verify-Regression.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Existing corpse cleanup regression checks failed.' }
Write-Output "DynamicMaps corpse check evidence: $mapEvidence"
Write-Output 'ARENA_DYNAMICMAP_CORPSE_CHECKS_PASSED'
