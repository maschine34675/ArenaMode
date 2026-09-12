param([string]$SptRoot = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$arenaStage = Join-Path $arenaRepo ('artifacts/deathmatch-hostility/' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($arenaStage) | Out-Null
$arenaInstalled = Join-Path $SptRoot 'SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll'
$arenaBefore = if (Test-Path -LiteralPath $arenaInstalled) { (Get-FileHash -LiteralPath $arenaInstalled -Algorithm SHA256).Hash } else { $null }

function Invoke-DeathmatchTest([string]$Name, [string]$Filter, [string]$ReplacementSource, [bool]$ExpectFailure) {
    $arenaRun = Join-Path $arenaStage $Name
    [IO.Directory]::CreateDirectory($arenaRun) | Out-Null
    $arenaTargets = Join-Path $arenaRun 'NoDeploy.targets'
    $arenaOverlay = ''
    if ($ReplacementSource) {
        $arenaMutantPath = Join-Path $arenaRun 'ArenaSessionService.cs'
        [IO.File]::WriteAllText($arenaMutantPath, $ReplacementSource)
        $arenaEscaped = [Security.SecurityElement]::Escape($arenaMutantPath)
        $arenaOverlay = @"
  <Target Name="ArenaDeathmatchNegativeControl" BeforeTargets="CoreCompile" Condition="'`$(MSBuildProjectName)' == 'ArenaMode.Server'">
    <ItemGroup>
      <Compile Remove="Services\ArenaSessionService.cs" />
      <Compile Include="$arenaEscaped" />
    </ItemGroup>
  </Target>
"@
    }
    # The global SPTUserPath also points to a fresh nonexistent install. Even if
    # SDK target ordering changed, the project's own deploy condition is false.
    [IO.File]::WriteAllText($arenaTargets, @"
<Project>
  <Target Name="DeployToSpt" />
$arenaOverlay
</Project>
"@)
    $arenaArgs = @('test', (Join-Path $arenaRepo 'ArenaMode.Tests/ArenaMode.Tests.csproj'), '-c', 'Release',
        '--nologo', '-v:minimal', "-p:SPTUserPath=$arenaRun/install", "-p:OutputPath=$arenaRun/bin/",
        "-p:CustomAfterMicrosoftCommonTargets=$arenaTargets", '--filter', $Filter,
        '--logger', 'trx;LogFileName=results.trx', '--results-directory', $arenaRun)
    & dotnet @arenaArgs 2>&1 | Tee-Object -FilePath (Join-Path $arenaRun 'run.log') | Out-Host
    $arenaExit = $LASTEXITCODE
    $arenaTrxPath = Join-Path $arenaRun 'results.trx'
    if (!(Test-Path -LiteralPath $arenaTrxPath)) { throw "$Name failed before test results (exit $arenaExit)." }
    [xml]$arenaTrx = Get-Content -LiteralPath $arenaTrxPath -Raw
    $arenaCounts = $arenaTrx.TestRun.ResultSummary.Counters
    if ([int]$arenaCounts.total -le 0) { throw "$Name ran no tests." }
    if ($ExpectFailure) {
        if ($arenaExit -eq 0 -or [int]$arenaCounts.failed -le 0) { throw "$Name failed to detect the deliberate production defect." }
        Write-Host "$Name detected its deliberate defect: $($arenaCounts.failed) failed tests."
    } elseif ($arenaExit -ne 0 -or [int]$arenaCounts.passed -ne [int]$arenaCounts.total) {
        throw "$Name expected every test passed and unskipped: $($arenaCounts.OuterXml)"
    }
    return [pscustomobject]@{ Name = $Name; Total = [int]$arenaCounts.total; Passed = [int]$arenaCounts.passed; Failed = [int]$arenaCounts.failed; Trx = $arenaTrxPath }
}

Push-Location $arenaRepo
try {
    $arenaBaseline = Invoke-DeathmatchTest 'baseline' 'FullyQualifiedName~DeathmatchSelectionTests|FullyQualifiedName~ArenaAreaServerTests.Deathmatch|FullyQualifiedName~TeamDeathmatchServerTests' '' $false
    $arenaOriginal = [IO.File]::ReadAllText((Join-Path $arenaRepo 'ArenaMode.Server/Services/ArenaSessionService.cs'))
    $arenaReplayNeedle = '|| !string.Equals(entry.DeathmatchHostility, frozenHostility, StringComparison.Ordinal)'
    if (!$arenaOriginal.Contains($arenaReplayNeedle)) { throw 'Replay negative-control seam changed; update the test explicitly.' }
    $arenaReplayMutant = $arenaOriginal.Replace($arenaReplayNeedle, '|| false /* negative control: changed hostility replays */')
    $arenaReplay = Invoke-DeathmatchTest 'negative-replay-identity' 'FullyQualifiedName~ArenaAreaServerTests.DeathmatchRouteEchoAndRetry' $arenaReplayMutant $true
    $arenaCloneNeedle = 'if (IsLiveRaid(entry) && ReferenceEquals(entry.LocationClone, candidate))'
    if (!$arenaOriginal.Contains($arenaCloneNeedle)) { throw 'Clone negative-control seam changed; update the test explicitly.' }
    $arenaCloneMutant = $arenaOriginal.Replace($arenaCloneNeedle, 'if (IsLiveRaid(entry)) /* negative control: any map clone */')
    $arenaClone = Invoke-DeathmatchTest 'negative-clone-identity' 'FullyQualifiedName~ArenaAreaServerTests.DeathmatchOnlyTheExactLiveStashedClone' $arenaCloneMutant $true
    [ordered]@{ SourceCommit = (& git rev-parse HEAD); Baseline = $arenaBaseline; NegativeReplay = $arenaReplay; NegativeClone = $arenaClone; Deployed = $false; LiveGpuTested = $false } |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $arenaStage 'receipt.json')
    Write-Output "Deathmatch hostility evidence: $arenaStage/receipt.json"
    Write-Output 'DEATHMATCH_HOSTILITY_CHECKS_PASSED'
} finally {
    Pop-Location
    $arenaAfter = if (Test-Path -LiteralPath $arenaInstalled) { (Get-FileHash -LiteralPath $arenaInstalled -Algorithm SHA256).Hash } else { $null }
    if ($arenaAfter -ne $arenaBefore) { throw 'Installed server DLL changed during the staged checks.' }
}
