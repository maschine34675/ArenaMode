param([string]$SptRoot = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$arenaSpt = (Resolve-Path -LiteralPath $SptRoot).Path
$arenaEvidence = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Path $arenaEvidence -Force | Out-Null
$arenaParticipant = Join-Path $PSScriptRoot 'ParticipantMenuChecks.csproj'
$arenaNative = Join-Path $arenaRepo 'tools\NativeAreaHookChecks\NativeAreaHookChecks.csproj'
$arenaArea = Join-Path $arenaRepo 'tools\AreaTeamMenuChecks\AreaTeamMenuChecks.csproj'
function Invoke-ParticipantCheck([string]$Project, [string]$Name, [string]$Override = '') {
    $arenaArgs = @('run', '--project', $Project, '-c', 'Release', "-p:SptRoot=$arenaSpt")
    if ($Override) { $arenaArgs += $Override }
    $arenaPrior = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $arenaOutput = (& dotnet @arenaArgs 2>&1 | ForEach-Object { "$_" }) -join "`n"
        $arenaExit = $LASTEXITCODE
    } finally { $ErrorActionPreference = $arenaPrior }
    $arenaOutput | Set-Content -LiteralPath (Join-Path $arenaEvidence ($Name + '.log'))
    return [pscustomobject]@{ Code = $arenaExit; Output = $arenaOutput }
}
$arenaChecks = @(
    @($arenaParticipant, 'participants', 'PARTICIPANT_SOURCE_CHECKS_PASSED'),
    @($arenaNative, 'native', 'NATIVE_AREA_HOOK_CHECKS_PASSED'),
    @($arenaArea, 'area', 'ARENA_TEAM_AREA_VIEW_CHECKS_PASSED'))
foreach ($arenaCheck in $arenaChecks) {
    $arenaResult = Invoke-ParticipantCheck $arenaCheck[0] $arenaCheck[1]
    if ($arenaResult.Code -ne 0 -or -not $arenaResult.Output.Contains($arenaCheck[2])) { throw $arenaResult.Output }
    Write-Output $arenaResult.Output
}

# Only isolated source copies are mutated. These projects contain no mod build
# targets and cannot deploy into the live installation. A fault must fail the
# intended behavioral assertion, never merely fail to compile.
$arenaGenerationSite = @'
if (__instance == null || !ReferenceEquals(state.Generation, generation)
                        || !ReferenceEquals(Controller(__instance), owner) || !IsArena(__instance)
                        || rendered?.Available != true
'@
$arenaGenerationFault = @'
if (__instance == null
                        || !ReferenceEquals(Controller(__instance), owner) || !IsArena(__instance)
                        || rendered?.Available != true
'@
$arenaFaults = @(
    @('missing-disk-save', 'ArenaMode.Client\Config\ArenaParticipantSettings.cs', '_file.Save();', '/* missing save */',
        $arenaParticipant, 'ProductionParticipantSettingsSource', 'PERSISTENCE:'),
    @('armed-write', 'ArenaMode.Client\Config\ArenaParticipantSettings.cs', '|| Session.ArenaSession.ArmedForNextRaid', '',
        $arenaParticipant, 'ProductionParticipantSettingsSource', 'ARMED-GUARD:'),
    @('active-write', 'ArenaMode.Client\Config\ArenaParticipantSettings.cs', 'Session.ArenaSession.IsActive || ', '',
        $arenaParticipant, 'ProductionParticipantSettingsSource', 'ACTIVE-GUARD:'),
    @('retired-native-row', 'ArenaMode.Client\Session\ArenaAreaSelectionView.cs', '|| !_participantRows.Contains(row)', '',
        $arenaParticipant, 'ProductionAreaViewSource', 'STALE-MAP:'),
    @('retired-show', 'ArenaMode.Client\Session\ArenaLocationSelectionPatch.cs', $arenaGenerationSite, $arenaGenerationFault,
        $arenaNative, 'ProductionLocationPatchSource', 'NATIVE-PARTICIPANT-GENERATION:'),
    @('changed-saved-area', 'ArenaMode.Client\Session\ArenaLocationSelectionPatch.cs',
        '&& Settings.TeamDeathmatchArea?.Value != rendered.AreaId', '&& false',
        $arenaNative, 'ProductionLocationPatchSource', 'NATIVE-PARTICIPANT-AREA:'))
try {
    foreach ($arenaFault in $arenaFaults) {
        $arenaSource = (Get-Content -Raw -LiteralPath (Join-Path $arenaRepo $arenaFault[1])).Replace("`r`n", "`n")
        $arenaSite = $arenaFault[2].Replace("`r`n", "`n")
        if (($arenaSource.Split([string[]]@($arenaSite), [StringSplitOptions]::None)).Count -ne 2) {
            throw "Expected exactly one mutation site: $($arenaFault[0])"
        }
        $arenaMutant = Join-Path $arenaEvidence ($arenaFault[0] + '.cs')
        [IO.File]::WriteAllText($arenaMutant, $arenaSource.Replace($arenaSite, $arenaFault[3]))
        $arenaResult = Invoke-ParticipantCheck $arenaFault[4] $arenaFault[0] ("-p:" + $arenaFault[5] + '=' + $arenaMutant)
        if ($arenaResult.Code -eq 0 -or -not $arenaResult.Output.Contains($arenaFault[6])) { throw $arenaResult.Output }
        Write-Output "NEGATIVE CONTROL PASSED: $($arenaFault[0])"
    }
} finally {
    foreach ($arenaProject in @($arenaParticipant, $arenaNative)) {
        & dotnet build $arenaProject -c Release -t:Rebuild "-p:SptRoot=$arenaSpt" --nologo -v:quiet
        if ($LASTEXITCODE -ne 0) { throw 'Could not restore ordinary standalone compiler inputs.' }
    }
}
foreach ($arenaCheck in $arenaChecks) {
    $arenaResult = Invoke-ParticipantCheck $arenaCheck[0] ($arenaCheck[1] + '-restored')
    if ($arenaResult.Code -ne 0 -or -not $arenaResult.Output.Contains($arenaCheck[2])) { throw $arenaResult.Output }
}
Write-Output 'PARTICIPANT MENU CHECKS PASSED'
