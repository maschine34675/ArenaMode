param()
$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$arenaEvidence = Join-Path $arenaRepo ('artifacts\tdm-match-menu-checks\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $arenaEvidence -Force | Out-Null
$arenaProject = Join-Path $PSScriptRoot 'TdmMatchMenuChecks.csproj'
function Invoke-MenuCheck([string]$Name, [string]$Override = '') {
    $arenaArguments = @('run', '--project', $arenaProject, '-c', 'Release')
    if ($Override) { $arenaArguments += $Override }
    $arenaOld = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $arenaOutput = (& dotnet @arenaArguments 2>&1 | ForEach-Object { "$_" }) -join "`n"
        $arenaCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $arenaOld }
    [IO.File]::WriteAllText((Join-Path $arenaEvidence ($Name + '.log')), $arenaOutput)
    return [pscustomobject]@{ Code = $arenaCode; Output = $arenaOutput }
}
$arenaRun = Invoke-MenuCheck 'production'
if ($arenaRun.Code -ne 0 -or -not $arenaRun.Output.Contains('TDM_MATCH_MENU_CHECKS_PASSED')) { throw $arenaRun.Output }
Write-Output $arenaRun.Output
# Mutants change isolated compiler inputs only. The test project imports no
# deployment targets and links actual production Settings/state/view files.
$arenaFaults = @(
    @('pft-scope', 'ArenaMode.Client\Session\ArenaTdmMatchRulesState.cs',
      'internal bool Visible => !_tacticalOnly', 'internal bool Visible => true',
      'ProductionRulesState', 'PFT cannot expose independent TDM rules'),
    @('unexpected-preset', 'ArenaMode.Client\Session\ArenaTdmMatchRulesState.cs',
      'Editable && Contains(KillOptions, value)', 'Editable',
      'ProductionRulesState', 'menu admits only explicit kill presets'),
    @('live-state-lock', 'ArenaMode.Client\Session\ArenaTdmMatchRulesState.cs',
      'Visible && !_raidLocked()', 'Visible',
      'ProductionRulesState', 'state rechecks live raid lock on click'),
    @('config-raid-lock', 'ArenaMode.Client\Config\Settings.cs',
      'Session.ArenaSession.IsActive || Session.ArenaSession.ArmedForNextRaid', 'false',
      'ProductionSettings', 'settings rejects active and armed raid writes'),
    @('config-clamped-invalid-write', 'ArenaMode.Client\Config\Settings.cs',
      '|| entry == null || value < 0 || value > maximum', '|| entry == null',
      'ProductionSettings', 'settings rejects out-of-range kills without mutating config'),
    @('stale-custom-label', 'ArenaMode.Client\Session\ArenaModeSelectionView.cs',
      "// F12 can change a limit without changing the selected mode.`n            RefreshTdmRules();",
      '// Fault: no same-mode limit refresh.',
      'ProductionModeView', 'unchanged-mode refresh displays new F12 values'),
    @('retired-row', 'ArenaMode.Client\Session\ArenaTdmMatchRulesView.cs',
      '|| !_choices.Contains(choice)', '|| false',
      'ProductionRulesView', 'retired row cannot modify reopened panel'),
    @('panel-growth', 'ArenaMode.Client\Session\ArenaTdmMatchRulesView.cs',
      '20f, 349f, Width, 124f', '20f, 349f, Width, 240f',
      'ProductionRulesView', 'rules remain below heading and inside original mode panel')
)
try {
    foreach ($arenaFault in $arenaFaults) {
        $arenaSource = [IO.File]::ReadAllText((Join-Path $arenaRepo $arenaFault[1])).Replace("`r`n", "`n")
        if (($arenaSource.Split([string[]]@($arenaFault[2]), [StringSplitOptions]::None)).Count -ne 2) {
            throw "Expected exactly one mutation site: $($arenaFault[0])"
        }
        $arenaMutant = Join-Path $arenaEvidence ($arenaFault[0] + '.cs')
        [IO.File]::WriteAllText($arenaMutant, $arenaSource.Replace($arenaFault[2], $arenaFault[3]))
        $arenaRun = Invoke-MenuCheck $arenaFault[0] ('-p:' + $arenaFault[4] + '=' + $arenaMutant)
        if ($arenaRun.Code -eq 0 -or -not $arenaRun.Output.Contains($arenaFault[5])) { throw $arenaRun.Output }
        Write-Output "NEGATIVE CONTROL PASSED: $($arenaFault[0])"
    }
}
finally {
    # Also restore after an unexpected failure; never leave a mutant executable
    # available for a later --no-build check.
    $arenaRestore = (& dotnet build $arenaProject -c Release -t:Rebuild --nologo -v:quiet 2>&1 | ForEach-Object { "$_" }) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw $arenaRestore }
    [IO.File]::WriteAllText((Join-Path $arenaEvidence 'restore.log'), $arenaRestore)
}
$arenaRun = Invoke-MenuCheck 'production-final'
if ($arenaRun.Code -ne 0 -or -not $arenaRun.Output.Contains('TDM_MATCH_MENU_CHECKS_PASSED')) { throw $arenaRun.Output }
$arenaHashes = @('ArenaMode.Client\Config\Settings.cs', 'ArenaMode.Client\Session\ArenaModeSelectionView.cs',
    'ArenaMode.Client\Session\ArenaTdmMatchRulesState.cs', 'ArenaMode.Client\Session\ArenaTdmMatchRulesView.cs') |
    ForEach-Object { [pscustomobject]@{ Path = $_; SHA256 = (Get-FileHash -LiteralPath (Join-Path $arenaRepo $_) -Algorithm SHA256).Hash } }
$arenaHashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $arenaEvidence 'production-sources.json')
Write-Output $arenaRun.Output
Write-Output "Evidence: $arenaEvidence"
