$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../Invoke-ArenaSourceChecks.ps1')
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$faults = @(
    @{ Name='legacy-overlap'; File='ArenaHudStackLayout.cs'; Property='LayoutSource'; Find='ScoreHeight(match) + PowerupGap'; Replace='ScorePanel.PanelHeight + PowerupGap'; Failure='powerups are below actual objective bottom with a visible gap' },
    @{ Name='missing-objective-height'; File='ArenaHudStackLayout.cs'; Property='LayoutSource'; Find='? ObjectiveHeight : 0f'; Replace='? 0f : 0f'; Failure='shared score background includes the complete objective row' },
    @{ Name='ordinary-mode-gap'; File='ArenaHudStackLayout.cs'; Property='LayoutSource'; Find='? ObjectiveHeight : 0f'; Replace='? ObjectiveHeight : ObjectiveHeight'; Failure='hidden objective releases reserved row immediately' },
    @{ Name='countdown-uses-objective-slot'; File='ArenaHudStackLayout.cs'; Property='LayoutSource'; Find='&& mode.IsSearchActive'; Replace=''; Failure='objective visibility requires current context and live phase' },
    @{ Name='lost-context-visibility'; File='ArenaHudStackLayout.cs'; Property='LayoutSource'; Find='&& mode.ObjectiveHudVisible'; Replace=''; Failure='objective visibility requires current context and live phase' },
    @{ Name='foreign-match-row'; File='ArenaHudStackLayout.cs'; Property='LayoutSource'; Find='&& ReferenceEquals(ArenaMatchService.Current, match)'; Replace=''; Failure='foreign match cannot publish another match objective row' },
    @{ Name='view-height-drift'; File='DominationPanel.cs'; Property='ObjectiveSource'; Find='new Vector2(760f, ArenaHudStackLayout.ObjectiveHeight)'; Replace='new Vector2(760f, ArenaHudStackLayout.ObjectiveHeight + 12f)'; Failure='objective reserves the same height that its actual view draws' },
    @{ Name='independent-objective-card'; File='DominationPanel.cs'; Property='ObjectiveSource'; Find='_rect.SetParent(parent, false);'; Replace='_rect.SetParent(parent, false); _root.AddComponent<UnityEngine.UI.Image>();'; Failure='control points have no independent panel surface' },
    @{ Name='detached-objective-row'; File='ScorePanel.cs'; Property='ScoreSource'; Find='_objectives.Create(_root);'; Replace='_objectives.Create(hudRoot);'; Failure='control points belong to the main score panel' },
    @{ Name='unexpanded-score-surface'; File='ScorePanel.cs'; Property='ScoreSource'; Find='ArenaHudStackLayout.ScoreHeight(match)'; Replace='PanelHeight'; Failure='shared score background includes the complete objective row' },
    @{ Name='throttled-score-size'; File='ScorePanel.cs'; Property='ScoreSource'; Find='if (_root.sizeDelta != size) _root.sizeDelta = size;'; Replace='if (Time.unscaledTime >= _nextRefreshAt && _root.sizeDelta != size) _root.sizeDelta = size;'; Failure='shared background changes before throttled score text' },
    @{ Name='powerup-offset-lost'; File='ArenaPowerupPanel.cs'; Property='PowerupSource'; Find='ArenaHudStackLayout.PowerupPosition(Modes.ArenaMatchService.Current)'; Replace='new Vector2(0f, -200f)'; Failure='powerups track score X offset' },
    @{ Name='throttled-position'; File='ArenaPowerupPanel.cs'; Property='PowerupSource'; Find='_rect.anchoredPosition = ArenaHudStackLayout.PowerupPosition(Modes.ArenaMatchService.Current);'; Replace='if (Time.unscaledTime >= _next) _rect.anchoredPosition = ArenaHudStackLayout.PowerupPosition(Modes.ArenaMatchService.Current);'; Failure='offset refresh precedes throttled powerup text' }
)
foreach ($fault in $faults) { $fault.Path = Join-Path $root ('ArenaMode.Client/Hud/' + $fault.File) }
Test-ArenaSourceFaults -Project (Join-Path $PSScriptRoot 'DominationHudChecks.csproj') -Token 'DOMINATION_HUD_CHECKS_PASSED' -Properties @() -Faults $faults
