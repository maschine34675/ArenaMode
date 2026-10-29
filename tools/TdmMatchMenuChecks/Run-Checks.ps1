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
    @('domination-companion-lock', 'ArenaMode.Client/Session/ArenaModeSelectionView.cs',
      '&& ArenaMenuSelection.ModeAvailabilityRejection(ArenaModeCatalog.All[index].Id) == null;', ';',
      'ProductionModeView', 'missing objective companion disables Domination even under forced click'),
    @('zombie-state-raid-lock', 'ArenaMode.Client/Session/ArenaZombieRulesState.cs',
      'Visible && !ArenaSession.IsActive && !ArenaSession.ArmedForNextRaid', 'Visible',
      'ProductionZombieState', 'Zombie writes reject armed and active raids'),
    @('zombie-state-range', 'ArenaMode.Client/Session/ArenaZombieRulesState.cs',
      '|| value < range.MinValue || value > range.MaxValue', '',
      'ProductionZombieState', 'Zombie callback rejects invalid value'),
    @('zombie-retired-view', 'ArenaMode.Client/Session/ArenaZombieRulesView.cs',
      "if (_dispatching || !ReferenceEquals(generation, _root) || _root == null`n            || !_root.activeInHierarchy || !_rows.Contains(row)) return;",
      'if (_dispatching || _root == null || !_root.activeInHierarchy) return;',
      'ProductionZombieView', 'retired Zombie slider cannot mutate reopened screen'),
    @('pft-scope', 'ArenaMode.Client\Session\ArenaTdmMatchRulesState.cs',
      'internal bool Visible => !_tacticalOnly', 'internal bool Visible => true',
      'ProductionRulesState', 'PFT cannot expose independent TDM rules'),
    @('unexpected-preset', 'ArenaMode.Client\Session\ArenaTdmMatchRulesState.cs',
      'Editable && Contains(ScoreOptions, value)', 'Editable',
      'ProductionRulesState', 'menu admits only explicit kill presets'),
    @('live-state-lock', 'ArenaMode.Client\Session\ArenaTdmMatchRulesState.cs',
      'Visible && !_raidLocked()', 'Visible',
      'ProductionRulesState', 'state rechecks live raid lock on click'),
    @('config-raid-lock', 'ArenaMode.Client\Config\Settings.cs',
      "Session.ArenaSession.IsActive || Session.ArenaSession.ArmedForNextRaid`n            || entry == null", 'false || entry == null',
      'ProductionSettings', 'settings rejects active and armed raid writes'),
    @('config-clamped-invalid-write', 'ArenaMode.Client\Config\Settings.cs',
      '|| entry == null || value < 0 || value > maximum', '|| entry == null',
      'ProductionSettings', 'settings rejects out-of-range kills without mutating config'),
    @('stale-custom-label', 'ArenaMode.Client\Session\ArenaModeSelectionView.cs',
      "// F12 can change a limit without changing the selected mode.`n            Refresh();",
      '// Fault: no same-mode limit refresh.',
      # Same-mode refresh now first protects the faction-row loading lock.
      'ProductionModeView', 'loading freezes side selection even under forced click'),
    @('retired-row', 'ArenaMode.Client\Session\ArenaTdmMatchRulesView.cs',
      '|| !_choices.Contains(choice)', '|| false',
      'ProductionRulesView', 'retired row cannot modify reopened panel'),
    @('legacy-round-limits', 'ArenaMode.Client\Config\Settings.cs',
      'DeathmatchKills.Value = RoundKillTarget.Value;', 'DeathmatchKills.Value = 0;',
      'ProductionSettings', 'legacy Rounds preserves limits; Endless remains continuous'),
    @('retired-mode-row', 'ArenaMode.Client\Session\ArenaModeSelectionView.cs',
      "!ReferenceEquals(owner, _root) || owner == null || !owner.activeInHierarchy`n                || !_buttons.Contains(button)", 'owner == null',
      'ProductionModeView', 'retired mode row cannot modify reopened panel'),
    @('panel-growth' , 'ArenaMode.Client\Session\ArenaTdmMatchRulesView.cs',
      # Controls must fit below the common facts inside the unchanged panel.
      '20f, 396f, Width, 78f', '20f, 396f, Width, 240f',
      'ProductionRulesView', 'rules remain below heading and inside original mode panel'),
    @('hidden-common-facts', 'ArenaMode.Client/Session/ArenaModeSelectionView.cs',
      '_equipment.gameObject.SetActive(!_equipmentChoice.Visible);',
      '_equipment.gameObject.SetActive(!_equipmentChoice.Visible); _respawn.gameObject.SetActive(!_tdmRules.Visible && !_dmRules.Visible);',
      'ProductionModeView', 'common fact values or their inline choice are visible for every mode'),
    @('description-never-visible', 'ArenaMode.Client/Session/ArenaModeSelectionView.cs',
      '_description.enableWordWrapping = true;',
      '_description.enableWordWrapping = true; _description.gameObject.SetActive(false);',
      'ProductionModeView', 'solo Tactical RULES shows the objective'),
    @('description-does-not-wrap', 'ArenaMode.Client/Session/ArenaModeSelectionView.cs',
      '_description.enableWordWrapping = true;', '_description.enableWordWrapping = false;',
      'ProductionModeView', 'every mode has an actual visible two-line description'),
    @('killcam-tdm-misreported', 'ArenaMode.Client/Session/ArenaModeSelectionView.cs',
      'Settings.GetReplaySeconds(mode.Id) > 0f',
      'mode.Id != ArenaModeCatalog.TeamDeathmatchId && Settings.GetReplaySeconds(mode.Id) > 0f',
      'ProductionModeView', 'Deathmatch and TDM actually show replay-delayed respawn: teamdeathmatch'),
    @('killcam-current-mode', 'ArenaMode.Client/Config/Settings.cs',
      'var entry = ReplayEntry(mode);', 'var entry = ReplayEntry("deathmatch");',
      'ProductionSettings', 'native killcam button persists current mode gungame'),
    @('killcam-retired-callback', 'ArenaMode.Client/Session/ArenaMutatorsView.cs',
      '!Owns(generation) || !_rows.Contains(row) || _dispatching', '_dispatching',
      'ProductionMutatorsView', 'retired killcam callback cannot mutate reopened panel'),
    @('mutator-live-lock', 'ArenaMode.Client/Session/ArenaMutatorSelection.cs',
      'if (Locked || !Visible(id) || UnavailableReason(id) != null)', 'if (!Visible(id) || UnavailableReason(id) != null)',
      'ProductionMutatorSelection', 'PMC voice choice respects active and armed locks'),
    @('mutator-mode-gate', 'ArenaMode.Client/Session/ArenaMutatorSelection.cs',
      'if (Locked || !Visible(id) || UnavailableReason(id) != null)', 'if (Locked || !Visible(id) || (id != ArenaMutator.Pickups && UnavailableReason(id) != null))',
      'ProductionMutatorSelection', 'unavailable pickups preserve preference on direct callback'),
    @('quickslots-wrong-entry', 'ArenaMode.Client/Session/ArenaMutatorSelection.cs',
      'ArenaMutator.AutoQuickslots => Settings.AutoQuickSlots,', 'ArenaMutator.AutoQuickslots => Settings.InstantSearch,',
      'ProductionMutatorSelection', 'mutator persists original F12 entry: AutoQuickslots'),
    @('instant-search-wrong-entry', 'ArenaMode.Client/Session/ArenaMutatorSelection.cs',
      'ArenaMutator.InstantSearch => Settings.InstantSearch,', 'ArenaMutator.InstantSearch => Settings.AutoQuickSlots,',
      'ProductionMutatorSelection', 'mutator persists original F12 entry: InstantSearch'),
    @('combat-hud-writes-global-master', 'ArenaMode.Client/Session/ArenaMutatorSelection.cs',
      'ArenaMutator.ArenaHud => Settings.CombatHudEnabled,', 'ArenaMutator.ArenaHud => Settings.HudEnabled,',
      'ProductionMutatorSelection', 'mutator persists original F12 entry: ArenaHud'),
    @('combat-hud-ignores-global-master', 'ArenaMode.Client/Session/ArenaMutatorSelection.cs',
      'if (id == ArenaMutator.ArenaHud && Settings.HudEnabled?.Value != true)', 'if (false)',
      'ProductionMutatorSelection', 'HUD master disables the combat mutator with an explicit reason'),
    @('combat-hud-overwrites-hitmarker', 'ArenaMode.Client/Session/ArenaMutatorSelection.cs',
      'entry.Value = requested;', 'entry.Value = requested; if (id == ArenaMutator.ArenaHud) Settings.ShowHitmarker.Value = requested;',
      'ProductionMutatorSelection', 'new mutators preserve the HUD master and all individual HUD preferences'),
    # Category headers stay distinct from controls; no legend/footer prose.
    @('mutators-legend-restored', 'ArenaMode.Client/Session/ArenaMutatorsView.cs',
      'Text(_root.transform, typography, "Title", "MUTATORS", 24f, 20f, 10f, 420f, 28f);',
      'Text(_root.transform, typography, "Title", "MUTATORS", 24f, 20f, 10f, 420f, 28f); Text(_root.transform, typography, "Mutator legend", "Green: ON", 13f, 20f, 44f, 420f, 20f);',
      'ProductionMutatorsView', 'mutator panel carries no legend or saved-with-F12 footer'),
    @('mutator-rows-top', 'ArenaMode.Client/Session/ArenaMapPanelLayout.cs',
      'MutatorGroupsTop = 44f, MutatorHeadingHeight = 18f;', 'MutatorGroupsTop = 44f, MutatorHeadingHeight = 0f;',
      'ProductionMapLayout', 'both mutator columns start below their category heading'),
    @('mutator-rows-pitch', 'ArenaMode.Client/Session/ArenaMapPanelLayout.cs',
      'MutatorRowPitch = 40f, MutatorButtonHeight = 22f;', 'MutatorRowPitch = 36f, MutatorButtonHeight = 22f;',
      'ProductionMapLayout', 'mutator rows reflow without moving the panel'),
    @('redundant-objective-restored', 'ArenaMode.Client/Session/ArenaModeSelectionView.cs',
      '_respawn = CreateFact("RESPAWN", typography, 330f);',
      '_respawn = CreateFact("RESPAWN", typography, 330f); CreateFact("OBJECTIVE", typography, 330f);',
      'ProductionModeView', 'objective and protection are not repeated below the description'),
    @('unavailable-reason-hidden', 'ArenaMode.Client/Session/ArenaMutatorsView.cs',
      'row.Description.gameObject.SetActive(_selection.Visible(row.Id) && reason != null);',
      'row.Description.gameObject.SetActive(false);',
      'ProductionMutatorsView', 'N/A reason is visible inline and on hover'),
    # Menu polish 2026-10-08, item 5: colored tracers are team-only like the lasers.
    @('tracer-entry-solo-leak', 'ArenaMode.Client/Config/Settings.cs',
      "ArenaModeCatalog.ZombieId when protectedTeam => TeamTracers,`n            _ => null",
      "ArenaModeCatalog.ZombieId when protectedTeam => TeamTracers,`n            _ => TeamTracers",
      'ProductionSettings', 'colored tracers are N/A outside team modes: deathmatch'),
    @('tracer-team-only-reason', 'ArenaMode.Client/Session/ArenaMutatorSelection.cs',
      "if ((id == ArenaMutator.Tracers || id == ArenaMutator.FriendlyFire)`n            && Entry(id) == null) return TeamModesOnly;",
      'if (false) return TeamModesOnly;',
      'ProductionMutatorSelection', "friendly-fire switch is unavailable in solo mode: deathmatch"),
    @('friendly-zombie-default', 'ArenaMode.Client/Config/Settings.cs',
      'ZombieFriendlyFire = config.Bind("Zombie", "FriendlyFire", false,',
      'ZombieFriendlyFire = config.Bind("Zombie", "FriendlyFire", true,',
      'ProductionSettings', 'friendly fire defaults off only for PFT Zombies'),
    @('friendly-mode-crosstalk', 'ArenaMode.Client/Config/Settings.cs',
      'ArenaModeCatalog.TacticalId when protectedTeam => TacticalFriendlyFire,',
      'ArenaModeCatalog.TacticalId when protectedTeam => ZombieFriendlyFire,',
      'ProductionSettings', 'friendly-fire mode routing keeps three independent team preferences'),
    @('friendly-solo-leak', 'ArenaMode.Client/Config/Settings.cs',
      'ArenaModeCatalog.ZombieId when protectedTeam => ZombieFriendlyFire,',
      'ArenaModeCatalog.ZombieId => ZombieFriendlyFire,',
      'ProductionSettings', 'solo/unknown modes have no friendly-fire immunity preference: zombie')
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
        if ($arenaRun.Code -eq 0 -or $arenaRun.Output.Contains('error CS') -or -not $arenaRun.Output.Contains($arenaFault[5])) { throw $arenaRun.Output }
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
    'ArenaMode.Client\Session\ArenaTdmMatchRulesState.cs', 'ArenaMode.Client\Session\ArenaTdmMatchRulesView.cs',
    'ArenaMode.Client\Session\ArenaMutatorSelection.cs', 'ArenaMode.Client\Session\ArenaMutatorsView.cs') |
    ForEach-Object { [pscustomobject]@{ Path = $_; SHA256 = (Get-FileHash -LiteralPath (Join-Path $arenaRepo $_) -Algorithm SHA256).Hash } }
$arenaHashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $arenaEvidence 'production-sources.json')
Write-Output $arenaRun.Output
Write-Output "Evidence: $arenaEvidence"
