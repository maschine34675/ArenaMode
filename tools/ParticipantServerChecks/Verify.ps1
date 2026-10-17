param([string]$SptRoot = 'D:\SPT41')
. (Join-Path $PSScriptRoot '../Read-ArenaInstalledSnapshot.ps1')
$arenaSnapshotRoot = $SptRoot
$arenaLayoutSnapshot = Read-ArenaInstalledSnapshot $arenaSnapshotRoot
try {
$ErrorActionPreference = 'Stop'
$participantRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$participantRun = Join-Path $participantRepo ('artifacts/participant-server/' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($participantRun) | Out-Null
$participantNoDeploy = Join-Path $participantRun 'must-not-exist-no-deploy'
if (Test-Path -LiteralPath $participantNoDeploy) { throw 'No-deploy sentinel must not exist.' }
$participantPreviousDatabase = $env:SPT_DATABASE
$participantPreviousRuntime = $env:SPT_RUNTIME
$env:SPT_DATABASE = Join-Path $SptRoot 'SPT_Runtime/SPT_Data/database'
$env:SPT_RUNTIME = Join-Path $SptRoot 'SPT_Runtime'

function Get-ParticipantHash([string]$Path) {
    if (!(Test-Path -LiteralPath $Path)) { return '<absent>' }
    return (Get-ArenaObservedFileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

$participantInstalled = @(
    (Join-Path $SptRoot 'BepInEx/plugins/maschine-ArenaMode/maschine-ArenaMode.Client.dll'),
    (Join-Path $SptRoot 'BepInEx/plugins/maschine-Redeploy.Core.dll'),
    (Join-Path $SptRoot 'BepInEx/plugins/Redeploy.Core.dll'),
    (Join-Path $SptRoot 'SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll'))
$participantBefore = @{}
foreach ($participantPath in $participantInstalled) { $participantBefore[$participantPath] = Get-ParticipantHash $participantPath }
$participantSources = @('ArenaMode.Server/Services/ArenaSessionService.cs',
    'ArenaMode.Server/Services/ArenaLocationMutator.cs', 'ArenaMode.Server/Patches/GenerateLocationAndLootPatch.cs',
    'ArenaMode.Server/Routers/ArenaModeRouter.cs', 'ArenaMode.Shared/ArenaParticipantPolicy.cs',
    'ArenaMode.Tests/ParticipantCountTests.cs')
$participantSourceHashes = @{}
foreach ($participantPath in $participantSources) {
    $participantSourceHashes[$participantPath] = Get-ParticipantHash (Join-Path $participantRepo $participantPath)
}

function Invoke-ParticipantCase([string]$Name, [string]$Filter, [string]$SourcePath = '',
    [string]$Needle = '', [string]$Replacement = '', [string]$ExpectedFailedTest = '') {
    $participantCase = Join-Path $participantRun $Name
    [IO.Directory]::CreateDirectory($participantCase) | Out-Null
    $participantTargets = Join-Path $participantCase 'ParticipantChecks.targets'
    $participantOverlay = ''
    if ($SourcePath) {
        $participantOriginal = [IO.File]::ReadAllText((Join-Path $participantRepo $SourcePath))
        if (!$participantOriginal.Contains($Needle)) { throw "Negative-control seam changed: $Name" }
        $participantMutant = Join-Path $participantCase ([IO.Path]::GetFileName($SourcePath))
        [IO.File]::WriteAllText($participantMutant, $participantOriginal.Replace($Needle, $Replacement))
        $participantEscaped = [Security.SecurityElement]::Escape($participantMutant)
        $participantFileName = [Security.SecurityElement]::Escape([IO.Path]::GetFileNameWithoutExtension($SourcePath))
        $participantOverlay = @"
  <Target Name="ParticipantNegativeControl" BeforeTargets="CoreCompile" Condition="'`$(MSBuildProjectName)' == 'ArenaMode.Server'">
    <ItemGroup>
      <Compile Remove="@(Compile)" Condition="'%(Compile.Filename)' == '$participantFileName'" />
      <Compile Include="$participantEscaped" />
    </ItemGroup>
  </Target>
"@
    }
    # The nonexistent global SPTUserMods property disables the real deployment
    # target even if import ordering changes. Artifacts isolation avoids sharing
    # intermediate outputs with another integration build or another mutant.
    [IO.File]::WriteAllText($participantTargets, "<Project>`n$participantOverlay`n</Project>")
    $participantArgs = @('test', (Join-Path $participantRepo 'ArenaMode.Tests/ArenaMode.Tests.csproj'),
        '-c', 'Release', '--nologo', '-v:minimal', "-p:SPTUserMods=$participantNoDeploy",
        '-p:UseArtifactsOutput=true', "-p:ArtifactsPath=$participantCase/build", "-p:OutputPath=$participantCase/bin/",
        "-p:CustomAfterMicrosoftCommonTargets=$participantTargets", '--filter', $Filter,
        '--logger', 'trx;LogFileName=results.trx', '--results-directory', $participantCase)
    & dotnet @participantArgs 2>&1 | Tee-Object -FilePath (Join-Path $participantCase 'run.log') | Out-Host
    $participantExit = $LASTEXITCODE
    $participantTrxPath = Join-Path $participantCase 'results.trx'
    if (!(Test-Path -LiteralPath $participantTrxPath)) { throw "$Name failed before producing test evidence (exit $participantExit)." }
    [xml]$participantTrx = Get-Content -Raw -LiteralPath $participantTrxPath
    $participantCounts = $participantTrx.TestRun.ResultSummary.Counters
    $participantTestResults = @($participantTrx.TestRun.Results.UnitTestResult)
    if ([int]$participantCounts.total -le 0) { throw "$Name executed no tests." }
    if ($ExpectedFailedTest) {
        $participantExpected = @($participantTestResults | Where-Object {
            $_.outcome -eq 'Failed' -and $_.testName -like "*$ExpectedFailedTest*"
        })
        if ($participantExit -eq 0 -or $participantExpected.Count -eq 0) {
            throw "$Name did not fail at its intended behavioral assertion: $ExpectedFailedTest"
        }
        if (@($participantTestResults | Where-Object { $_.outcome -notin @('Passed', 'Failed') }).Count) {
            throw "$Name had skipped/inconclusive cases."
        }
        Write-Host "$Name rejected by $($participantExpected.Count) intended test(s)."
    } elseif ($participantExit -ne 0 -or [int]$participantCounts.passed -ne [int]$participantCounts.total) {
        throw "$Name expected executed, unskipped passing tests: $($participantCounts.OuterXml)"
    }
    return [pscustomobject]@{
        Name = $Name; Total = [int]$participantCounts.total; Passed = [int]$participantCounts.passed
        Failed = [int]$participantCounts.failed; Trx = $participantTrxPath
    }
}

Push-Location $participantRepo
try {
    $participantResults = @()
    $participantResults += Invoke-ParticipantCase 'baseline' `
        'FullyQualifiedName~ArenaAreaServerTests.Participant|FullyQualifiedName~LocationMutatorTests|FullyQualifiedName~DeathmatchSelectionTests|FullyQualifiedName~TeamDeathmatchServerTests'
    $participantResults += Invoke-ParticipantCase 'negative-replay-count' `
        'FullyQualifiedName~ParticipantRouteEchoReplayAndStatusKeepTheExactTotal' `
        'ArenaMode.Server/Services/ArenaSessionService.cs' `
        '|| entry.Participants != selectedParticipants' '|| false /* changed counts incorrectly replay */' `
        'ParticipantRouteEchoReplayAndStatusKeepTheExactTotal'
    $participantResults += Invoke-ParticipantCase 'negative-response-clone' `
        'FullyQualifiedName~ParticipantNativeCloneIdentityAndClientConfirmationRequireTheFrozenPopulation' `
        'ArenaMode.Server/Services/ArenaSessionService.cs' `
        '&& !ReferenceEquals(entry.LocationClone, responseLocation)' '&& false /* clone identity incorrectly ignored */' `
        'ParticipantNativeCloneIdentityAndClientConfirmationRequireTheFrozenPopulation'
    $participantResults += Invoke-ParticipantCase 'negative-final-revalidation' `
        'FullyQualifiedName~ParticipantFinalCloneDriftBlocksNativeResponseAndClientActivation' `
        'ArenaMode.Server/Services/ArenaSessionService.cs' `
        'ArenaLocationMutator.ValidateParticipantPopulation(location, entry.Map!, entry.Mode!, entry.Participants.Value);' `
        'System.GC.KeepAlive(location); /* final clone is incorrectly trusted */' `
        'ParticipantFinalCloneDriftBlocksNativeResponseAndClientActivation'
    $participantResults += Invoke-ParticipantCase 'negative-quiet-spawns' `
        'FullyQualifiedName~ParticipantMutatorClosesEverySpawnTrackAndRetainsOnlyRequestedCapacity' `
        'ArenaMode.Server/Services/ArenaLocationMutator.cs' `
        'var quiet = IsQuietMap(mode) || participants.HasValue;' 'var quiet = IsQuietMap(mode); /* selected DM/GG incorrectly loud */' `
        'ParticipantMutatorClosesEverySpawnTrackAndRetainsOnlyRequestedCapacity'

    [ordered]@{
        SourceCommit = (& git rev-parse HEAD); Sources = $participantSourceHashes; Results = $participantResults
        InstalledUnchanged = $participantBefore; Deployed = $false; NativeGpuTested = $false
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $participantRun 'receipt.json')
}
finally {
    Pop-Location
    $env:SPT_DATABASE = $participantPreviousDatabase
    $env:SPT_RUNTIME = $participantPreviousRuntime
    if (Test-Path -LiteralPath $participantNoDeploy) { throw 'Verification created the no-deploy sentinel.' }
    foreach ($participantPath in $participantInstalled) {
        if ((Get-ParticipantHash $participantPath) -ne $participantBefore[$participantPath]) {
            throw "Verification changed installed DLL: $participantPath"
        }
    }
    foreach ($participantPath in $participantSources) {
        if ((Get-ParticipantHash (Join-Path $participantRepo $participantPath)) -ne $participantSourceHashes[$participantPath]) {
            throw "Production source changed during verification: $participantPath"
        }
    }
}
Write-Output "PARTICIPANT SERVER CHECKS PASSED (evidence $participantRun/receipt.json)"

} finally {
    Assert-ArenaInstalledSnapshotUnchanged $arenaLayoutSnapshot $arenaSnapshotRoot
}
