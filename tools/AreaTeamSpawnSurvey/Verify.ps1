param([string]$SptRoot = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$arenaRoot = (Resolve-Path -LiteralPath $SptRoot).Path
$arenaProject = Join-Path $PSScriptRoot 'AreaTeamSpawnSurvey.csproj'
$arenaProposal = Join-Path $PSScriptRoot 'Proposal.json'
$arenaOutput = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Path $arenaOutput -Force | Out-Null

# This standalone executable links shared source only, never a mod project,
# SPT dependency or deployment target. The source database is read-only.
& dotnet build $arenaProject -c Release --nologo -v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Spawn survey compilation failed.' }
$arenaDll = Join-Path $PSScriptRoot 'bin\Release\net10.0\AreaTeamSpawnSurvey.dll'
& dotnet $arenaDll $arenaRoot $arenaProposal | Tee-Object -FilePath (Join-Path $arenaOutput 'survey.log')
if ($LASTEXITCODE -ne 0) { throw 'Installed spawn corpus or proposed region validation failed.' }

$arenaFaults = [ordered]@{
    'missing-west-bot' = 'start proposal is missing an allowed marker'
    'invalid-pmc-mask' = 'initial PMC pool must permit either faction'
    'collapsed-west-bots' = 'start pool has duplicate physical positions'
    'excluded-sniper' = 'start proposal includes excluded sniper'
}
foreach ($arenaFault in $arenaFaults.Keys) {
    $arenaPreviousPreference = $ErrorActionPreference
    try {
        # Expected native stderr is data for these deliberate negative runs.
        $ErrorActionPreference = 'Continue'
        $arenaText = (& dotnet $arenaDll $arenaRoot $arenaProposal $arenaFault 2>&1 | ForEach-Object { "$_" }) -join "`n"
        $arenaExit = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $arenaPreviousPreference }
    $arenaText | Set-Content -LiteralPath (Join-Path $arenaOutput ($arenaFault + '.log'))
    if ($arenaExit -eq 0 -or -not $arenaText.Contains('SURVEY_REJECTED: ' + $arenaFaults[$arenaFault])) {
        throw "Deliberate fault '$arenaFault' did not fail for its expected reason. $arenaText"
    }
    Write-Output "NEGATIVE CONTROL PASSED: $arenaFault"
}
Write-Output 'ARENA_TEAM_SPAWN_SURVEY_PASSED'
