#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$armRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$armScratch = Join-Path ([IO.Path]::GetTempPath()) ('arena-armband-regression-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($armScratch)
$armNoDeploy = Join-Path $armScratch 'no-server-deploy'
if (Test-Path -LiteralPath $armNoDeploy) { throw 'No-deploy path must not exist.' }
$armProject = Join-Path $armRepo 'ArenaMode.Tests'
$armSourcePaths = @('ArenaMode.Server/Services/TeamDeathmatchArmbandPolicy.cs',
    'ArenaMode.Server/Patches/ArenaTeamDeathmatchArmbandPatch.cs', 'ArenaMode.Server/Services/ArenaSessionService.cs')
$armOriginals = @{}
foreach ($path in $armSourcePaths) { $armOriginals[$path] = [IO.File]::ReadAllText((Join-Path $armRepo $path)) }

function Invoke-ArmCheck([string[]] $Arguments) {
    $result = & dotnet @Arguments 2>&1 | Out-String
    return @{ Exit = $LASTEXITCODE; Output = $result }
}

$armMutants = @(
    @{ Name = 'WrongColour'; Source = $armSourcePaths[0]; Compile = 'Services\TeamDeathmatchArmbandPolicy.cs';
       From = 'Equal(details.Side, "Usec") ? UsecBlue : BearRed'; To = 'Equal(details.Side, "Usec") ? BearRed : UsecBlue';
       Test = 'FinalGeneratedBandOverridesApbsWithoutChangingSharedGear' },
    @{ Name = 'ApbsBandWins'; Source = $armSourcePaths[0]; Compile = 'Services\TeamDeathmatchArmbandPolicy.cs';
       From = 'if (bands.Count == 1 && removed.Count == 1 && bands[0].Template == template)'; To = 'if (bands.Count == 1)';
       Test = 'FinalGeneratedBandOverridesApbsWithoutChangingSharedGear' },
    @{ Name = 'OrdinaryModeLeak'; Source = $armSourcePaths[2]; Compile = 'Services\ArenaSessionService.cs';
       From = '|| !string.Equals(entry.Mode, "teamdeathmatch", StringComparison.OrdinalIgnoreCase)'; To = '|| false';
       Test = 'OtherModesAndProtectedPftLeaveEquipmentUntouched' },
    @{ Name = 'NewRaidAdoption'; Source = $armSourcePaths[1]; Compile = 'Patches\ArenaTeamDeathmatchArmbandPatch.cs';
       From = '|| !string.Equals(__state, identity, StringComparison.Ordinal)'; To = '|| false';
       Test = 'OldGenerationCannotAdoptNewRaidIdentity' }
)

foreach ($mutant in $armMutants) {
    $source = $armOriginals[$mutant.Source]
    if (!$source.Contains($mutant.From)) { throw "Missing mutation anchor: $($mutant.Name)" }
    $replacement = Join-Path $armScratch ($mutant.Name + '.cs')
    [IO.File]::WriteAllText($replacement, $source.Replace($mutant.From, $mutant.To))
    $targetsPath = Join-Path $armScratch ($mutant.Name + '.targets')
    $escapedReplacement = [Security.SecurityElement]::Escape($replacement)
    $targets = @"
<Project>
  <Target Name="UseArmBandMutation" BeforeTargets="CoreCompile" Condition="'`$(MSBuildProjectName)' == 'ArenaMode.Server'">
    <ItemGroup>
      <Compile Remove="$($mutant.Compile)" />
      <Compile Include="$escapedReplacement" />
    </ItemGroup>
  </Target>
</Project>
"@
    [IO.File]::WriteAllText($targetsPath, $targets)
    $result = Invoke-ArmCheck @('test', $armProject, '--no-restore', '-c', ('TeamArmbandMutant' + $mutant.Name),
        "-p:SPTUserMods=$armNoDeploy", "-p:CustomAfterMicrosoftCommonTargets=$targetsPath",
        '--filter', ('FullyQualifiedName~TeamDeathmatchArmbandTests.' + $mutant.Test))
    if ($result.Exit -eq 0 -or !$result.Output.Contains('[FAIL]') -or !$result.Output.Contains($mutant.Test)) {
        throw "Mutant did not fail the intended assertion: $($mutant.Name)`n$($result.Output)"
    }
    Write-Output ('Rejected mutant: ' + $mutant.Name)
}

foreach ($path in $armSourcePaths) {
    if ([IO.File]::ReadAllText((Join-Path $armRepo $path)) -cne $armOriginals[$path]) { throw "Production source changed: $path" }
}
$result = Invoke-ArmCheck @('test', $armProject, '--no-restore', '-c', 'TeamArmbandChecks', "-p:SPTUserMods=$armNoDeploy",
    '--filter', 'FullyQualifiedName~TeamDeathmatchArmbandTests|FullyQualifiedName~TeamDeathmatchServerTests|FullyQualifiedName~BotTierScalePatchTests')
if ($result.Exit -ne 0 -or !$result.Output.Contains('ArenaMode.Tests.dll')) { throw "Production baseline failed:`n$($result.Output)" }
Write-Output $result.Output.Trim()
Write-Output 'ARENA_TEAM_ARMBAND_REGRESSION_PASSED'
