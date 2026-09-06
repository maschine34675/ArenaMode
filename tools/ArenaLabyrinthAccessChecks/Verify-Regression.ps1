#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$labRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$labScratch = Join-Path ([IO.Path]::GetTempPath()) ('arena-labyrinth-access-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($labScratch)
$labNoDeploy = Join-Path $labScratch 'no-server-deploy'
if (Test-Path -LiteralPath $labNoDeploy) { throw 'No-deploy path must not exist.' }
$labPaths = @('ArenaMode.Client/Session/ArenaLabyrinthAccess.cs', 'ArenaMode.Client/Session/MapAccessPatch.cs',
    'ArenaMode.Client/Session/ArenaLocationSelectionPatch.cs', 'ArenaMode.Server/Services/ArenaLocationMutator.cs')
$labOriginals = @{}
foreach ($path in $labPaths) { $labOriginals[$path] = [IO.File]::ReadAllText((Join-Path $labRepo $path)) }

function Invoke-Check([string] $Exe, [string[]] $Arguments) {
    $output = & $Exe @Arguments 2>&1 | Out-String
    return @{ Exit = $LASTEXITCODE; Output = $output }
}

foreach ($mutant in @(
    @{ Name = 'SharedMutation'; Source = $labPaths[0]; Property = 'ProductionAccessSource';
       From = 'var view = (LocationSettings.Location)CopyLocation.Invoke(location, null);'; To = 'var view = location;';
       Failure = 'Standalone metadata must be copied before unlocking the Arena view' },
    @{ Name = 'ForcedOnline'; Source = $labPaths[0]; Property = 'ProductionAccessSource';
       From = 'view.ForceOnlineRaidInPVE = false;'; To = 'view.ForceOnlineRaidInPVE = true;';
       Failure = 'Standalone Arena projection must expose local-PVE Labyrinth' },
    @{ Name = 'SelectionLeak'; Source = $labPaths[0]; Property = 'ProductionAccessSource';
       From = '&& ArenaMenuSelection.IsSelectedFor(settings)'; To = '&& true';
       Failure = 'The same settings object becomes ordinary after ending Arena' },
    @{ Name = 'NoKeyWaiver'; Source = $labPaths[1]; Property = 'ProductionKeycardSource';
       From = 'if (!ArenaClaim.WouldTakeOver(settings))'; To = 'if (true)';
       Failure = 'Existing exact Arena key waiver must cover standalone Labyrinth' }
)) {
    $original = $labOriginals[$mutant.Source]
    if (!$original.Contains($mutant.From)) { throw "Missing mutation anchor: $($mutant.Name)" }
    $replacement = Join-Path $labScratch ($mutant.Name + '.cs')
    [IO.File]::WriteAllText($replacement, $original.Replace($mutant.From, $mutant.To))
    $result = Invoke-Check 'dotnet' @('run', '--project', $PSScriptRoot, '-c', ('LabyrinthMutant' + $mutant.Name),
        "-p:$($mutant.Property)=$replacement")
    if ($result.Exit -eq 0 -or !$result.Output.Contains($mutant.Failure)) {
        throw "Client mutant did not fail its intended assertion: $($mutant.Name)`n$($result.Output)"
    }
    Write-Output ('Rejected mutant: ' + $mutant.Name)
}

$hookAnchor = 'value = ArenaLabyrinthAccess.Project(Controller(__instance)?.RaidSettings, value);'
if (!$labOriginals[$labPaths[2]].Contains($hookAnchor)) { throw 'Missing selection projection hook anchor' }
$hookPath = Join-Path $labScratch 'DisconnectedSelection.cs'
[IO.File]::WriteAllText($hookPath, $labOriginals[$labPaths[2]].Replace($hookAnchor, 'value = value;'))
$result = Invoke-Check 'pwsh' @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'Verify-Contracts.ps1'), '-PatchSource', $hookPath)
if ($result.Exit -eq 0 -or !$result.Output.Contains('Selected-location projection hook is missing.')) {
    throw "Disconnected hook mutant did not fail intended integration check:`n$($result.Output)"
}
Write-Output 'Rejected mutant: DisconnectedSelection'

$serverAnchor = 'location.ForceOnlineRaidInPVE = false;'
if (!$labOriginals[$labPaths[3]].Contains($serverAnchor)) { throw 'Missing server force-online anchor' }
$serverPath = Join-Path $labScratch 'ServerForcedOnline.cs'
[IO.File]::WriteAllText($serverPath, $labOriginals[$labPaths[3]].Replace($serverAnchor, 'location.ForceOnlineRaidInPVE = true;'))
$targetsPath = Join-Path $labScratch 'ServerForcedOnline.targets'
$escapedSource = [Security.SecurityElement]::Escape($serverPath)
$targets = @"
<Project>
  <Target Name="UseLabyrinthAccessMutant" BeforeTargets="CoreCompile" Condition="'`$(MSBuildProjectName)' == 'ArenaMode.Server'">
    <ItemGroup>
      <Compile Remove="Services\ArenaLocationMutator.cs" />
      <Compile Include="$escapedSource" />
    </ItemGroup>
  </Target>
</Project>
"@
[IO.File]::WriteAllText($targetsPath, $targets)
$result = Invoke-Check 'dotnet' @('test', (Join-Path $labRepo 'ArenaMode.Tests'), '--no-restore', '-c', 'LabyrinthMutantServer',
    "-p:SPTUserMods=$labNoDeploy", "-p:CustomAfterMicrosoftCommonTargets=$targetsPath",
    '--filter', 'FullyQualifiedName~ArenaLabyrinthAccessTests.RaidCloneSupportsStandaloneAndCoInstalledMetadataWithoutTouchingSource')
if ($result.Exit -eq 0 -or !$result.Output.Contains('[FAIL]') -or
    !$result.Output.Contains('RaidCloneSupportsStandaloneAndCoInstalledMetadataWithoutTouchingSource')) {
    throw "Server mutant did not fail intended test:`n$($result.Output)"
}
Write-Output 'Rejected mutant: ServerForcedOnline'

foreach ($path in $labPaths) {
    if ([IO.File]::ReadAllText((Join-Path $labRepo $path)) -cne $labOriginals[$path]) { throw "Production source changed: $path" }
}
foreach ($check in @(
    @{ Project = $PSScriptRoot; Token = 'ARENA_LABYRINTH_ACCESS_CHECKS_PASSED' },
    @{ Project = (Join-Path $labRepo 'tools/NativeMapViewChecks'); Token = 'native map-view regression checks' },
    @{ Project = (Join-Path $labRepo 'tools/NativeMenuChecks'); Token = 'ARENA_MENU_CHECKS_PASSED' }
)) {
    $result = Invoke-Check 'dotnet' @('run', '--project', $check.Project, '-c', 'Release')
    if ($result.Exit -ne 0 -or !$result.Output.Contains($check.Token)) { throw "Production client baseline failed:`n$($result.Output)" }
    Write-Output $result.Output.Trim()
}
$result = Invoke-Check 'dotnet' @('test', (Join-Path $labRepo 'ArenaMode.Tests'), '--no-restore', '-c', 'LabyrinthAccessChecks',
    "-p:SPTUserMods=$labNoDeploy", '--filter', 'FullyQualifiedName~ArenaLabyrinthAccessTests|FullyQualifiedName~LocationMutatorTests')
if ($result.Exit -ne 0 -or !$result.Output.Contains('ArenaMode.Tests.dll')) { throw "Production server baseline failed:`n$($result.Output)" }
Write-Output $result.Output.Trim()
Write-Output 'ARENA_LABYRINTH_ACCESS_REGRESSION_PASSED'
