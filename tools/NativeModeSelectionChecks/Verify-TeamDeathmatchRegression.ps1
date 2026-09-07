$ErrorActionPreference = 'Stop'
$tdmRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$tdmTemp = Join-Path ([IO.Path]::GetTempPath()) ('arena-tdm-setup-mutants-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($tdmTemp)
$tdmNoDeploy = Join-Path $tdmTemp 'no-server-deploy'
if (Test-Path -LiteralPath $tdmNoDeploy) { throw 'The no-deploy path must not exist' }

function Invoke-TdmCheck([string[]] $Arguments) {
    $tdmPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $tdmOutput = & dotnet @Arguments 2>&1 | Out-String
        return @{ Exit = $LASTEXITCODE; Output = $tdmOutput }
    }
    finally { $ErrorActionPreference = $tdmPreference }
}

function Get-TdmSource([string] $RelativePath) {
    return [IO.File]::ReadAllText((Join-Path $tdmRepo $RelativePath))
}

$tdmState = 'ArenaMode.Client\Session\ArenaModeSelectionState.cs'
$tdmHostility = 'ArenaMode.Server\Patches\AdjustBotHostilityPatch.cs'
$tdmLocation = 'ArenaMode.Server\Services\ArenaLocationMutator.cs'
$tdmOriginals = @{}
foreach ($tdmFile in @($tdmState, $tdmHostility, $tdmLocation)) {
    $tdmOriginals[$tdmFile] = Get-TdmSource $tdmFile
}

$tdmStateAnchor = 'if (!CanSelectIndex(index))'
if (-not $tdmOriginals[$tdmState].Contains($tdmStateAnchor)) { throw 'Missing state selection guard anchor' }
$tdmMutantState = Join-Path $tdmTemp 'unguarded-selection-state.cs'
[IO.File]::WriteAllText($tdmMutantState, $tdmOriginals[$tdmState].Replace($tdmStateAnchor, 'if (false)'))
$tdmResult = Invoke-TdmCheck @('run', '--project', (Join-Path $PSScriptRoot 'NativeModeSelectionChecks.csproj'),
    '-c', 'TeamDeathmatchMutantStateGuard', "-p:ProductionStateSource=$tdmMutantState")
if ($tdmResult.Exit -eq 0 -or -not $tdmResult.Output.Contains('protected team rejects a non-Tactical selection without calling the writer')) {
    throw "Unguarded state mutant did not fail its intended selection assertion:`n$($tdmResult.Output)"
}
Write-Output 'Rejected mutant: protected-team selection bypasses state guard'

$tdmMutants = @(
    @{ Name = 'FfaTeams'; Source = $tdmHostility; Compile = 'Patches\AdjustBotHostilityPatch.cs';
       From = 'if (string.Equals(mode, "teamdeathmatch", StringComparison.OrdinalIgnoreCase))'; To = 'if (false)';
       Test = 'BothFactionsOverrideConflictingHostilityWithoutChangingSharedRecords' },
    @{ Name = 'SharedChance'; Source = $tdmHostility; Compile = 'Patches\AdjustBotHostilityPatch.cs';
       From = '.Select(chanced => chanced with { })'; To = '.Select(chanced => chanced)';
       Test = 'BothFactionsOverrideConflictingHostilityWithoutChangingSharedRecords' },
    @{ Name = 'ChancedPmc'; Source = $tdmHostility; Compile = 'Patches\AdjustBotHostilityPatch.cs';
       From = '.Where(chanced => !IsPmcRole(chanced.Role))'; To = '.Where(chanced => true)';
       Test = 'BothFactionsOverrideConflictingHostilityWithoutChangingSharedRecords' },
    @{ Name = 'AmbientTeams'; Source = $tdmLocation; Compile = 'Services\ArenaLocationMutator.cs';
       From = '|| string.Equals(mode, "teamdeathmatch", StringComparison.OrdinalIgnoreCase)'; To = '|| false';
       Test = 'TeamDeathmatchSuppressesEveryAmbientSpawnTrack' }
)

foreach ($tdmMutant in $tdmMutants) {
    $tdmOriginal = $tdmOriginals[$tdmMutant.Source]
    if (-not $tdmOriginal.Contains($tdmMutant.From)) { throw "Missing mutation anchor: $($tdmMutant.Name)" }
    $tdmSourcePath = Join-Path $tdmTemp ($tdmMutant.Name + '.cs')
    [IO.File]::WriteAllText($tdmSourcePath, $tdmOriginal.Replace($tdmMutant.From, $tdmMutant.To))
    $tdmTargetsPath = Join-Path $tdmTemp ($tdmMutant.Name + '.targets')
    $tdmEscapedSource = [Security.SecurityElement]::Escape($tdmSourcePath)
    # Source replacement happens only inside this separate server build. No
    # project files are patched and the live deployment target cannot run.
    $tdmTargets = @"
<Project>
  <Target Name="UseTeamDeathmatchMutation" BeforeTargets="CoreCompile" Condition="'`$(MSBuildProjectName)' == 'ArenaMode.Server'">
    <ItemGroup>
      <Compile Remove="$($tdmMutant.Compile)" />
      <Compile Include="$tdmEscapedSource" />
    </ItemGroup>
  </Target>
</Project>
"@
    [IO.File]::WriteAllText($tdmTargetsPath, $tdmTargets)
    $tdmResult = Invoke-TdmCheck @('test', (Join-Path $tdmRepo 'ArenaMode.Tests'), '--no-restore',
        '-c', ('TeamDeathmatchMutant' + $tdmMutant.Name), "-p:SPTUserMods=$tdmNoDeploy",
        "-p:CustomAfterMicrosoftCommonTargets=$tdmTargetsPath", '--filter', ('FullyQualifiedName~' + $tdmMutant.Test))
    if ($tdmResult.Exit -eq 0 -or -not $tdmResult.Output.Contains('[FAIL]') -or
        -not $tdmResult.Output.Contains($tdmMutant.Test)) {
        throw "Mutant did not fail its intended test: $($tdmMutant.Name)`n$($tdmResult.Output)"
    }
    Write-Output "Rejected mutant: $($tdmMutant.Name)"
}

foreach ($tdmFile in $tdmOriginals.Keys) {
    if ((Get-TdmSource $tdmFile) -cne $tdmOriginals[$tdmFile]) { throw "Production source changed: $tdmFile" }
}
$tdmResult = Invoke-TdmCheck @('run', '--project', (Join-Path $PSScriptRoot 'NativeModeSelectionChecks.csproj'), '-c', 'Release')
if ($tdmResult.Exit -ne 0) { throw "Production state checks failed:`n$($tdmResult.Output)" }
Write-Output $tdmResult.Output.Trim()
$tdmResult = Invoke-TdmCheck @('test', (Join-Path $tdmRepo 'ArenaMode.Tests'), '--no-restore', '-c', 'Release',
    "-p:SPTUserMods=$tdmNoDeploy", '--filter', 'FullyQualifiedName~TeamDeathmatchServerTests|FullyQualifiedName~LocationMutatorTests')
if ($tdmResult.Exit -ne 0) { throw "Production server checks failed:`n$($tdmResult.Output)" }
Write-Output 'Production server setup tests passed.'
Write-Output 'ARENA_TEAM_DEATHMATCH_SETUP_REGRESSION_PASSED'
