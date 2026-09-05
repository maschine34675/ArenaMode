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

$tdmView = 'ArenaMode.Client\Session\ArenaModeSelectionView.cs'
$tdmHostility = 'ArenaMode.Server\Patches\AdjustBotHostilityPatch.cs'
$tdmLocation = 'ArenaMode.Server\Services\ArenaLocationMutator.cs'
$tdmOriginals = @{}
foreach ($tdmFile in @($tdmView, $tdmHostility, $tdmLocation)) {
    $tdmOriginals[$tdmFile] = Get-TdmSource $tdmFile
}

$tdmLayoutAnchor = 'var width = Math.Min(190f,'
if (-not $tdmOriginals[$tdmView].Contains($tdmLayoutAnchor)) { throw 'Missing adaptive layout anchor' }
$tdmMutantView = Join-Path $tdmTemp 'fixed-width-view.cs'
# Keeping the original multiline second operand is valid C# and forces 190px.
[IO.File]::WriteAllText($tdmMutantView, $tdmOriginals[$tdmView].Replace($tdmLayoutAnchor, 'var width = Math.Max(190f,'))
$tdmResult = Invoke-TdmCheck @('run', '--project', (Join-Path $PSScriptRoot 'NativeModeSelectionChecks.csproj'),
    '-c', 'TeamDeathmatchMutantLayout', "-p:ProductionViewSource=$tdmMutantView")
if ($tdmResult.Exit -eq 0 -or -not $tdmResult.Output.Contains('each button stays inside the original 820-pixel panel')) {
    throw "Fixed-width mutant did not fail its intended geometry assertion:`n$($tdmResult.Output)"
}
Write-Output 'Rejected mutant: fifth button exceeds original panel'

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
if ($tdmResult.Exit -ne 0) { throw "Production menu checks failed:`n$($tdmResult.Output)" }
Write-Output $tdmResult.Output.Trim()
$tdmResult = Invoke-TdmCheck @('test', (Join-Path $tdmRepo 'ArenaMode.Tests'), '--no-restore', '-c', 'Release',
    "-p:SPTUserMods=$tdmNoDeploy", '--filter', 'FullyQualifiedName~TeamDeathmatchServerTests|FullyQualifiedName~LocationMutatorTests')
if ($tdmResult.Exit -ne 0) { throw "Production server checks failed:`n$($tdmResult.Output)" }
Write-Output 'Production server setup tests passed.'
Write-Output 'ARENA_TEAM_DEATHMATCH_SETUP_REGRESSION_PASSED'
