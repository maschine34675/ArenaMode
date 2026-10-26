$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../Invoke-ArenaSourceChecks.ps1')
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$faults = @(
    @{ Name='legacy-overlap'; File='ArenaHudStackLayout.cs'; Property='LayoutSource'; Find='? ObjectivePosition - new Vector2(0f, ObjectiveHeight + PowerupGap)'; Replace='? ScorePanel.Position - new Vector2(0f, ScorePanel.PanelHeight + PowerupGap)'; Failure='powerups are below actual objective bottom with a visible gap' },
    @{ Name='missing-objective-height'; File='ArenaHudStackLayout.cs'; Property='LayoutSource'; Find='ObjectiveHeight + PowerupGap'; Replace='PowerupGap'; Failure='powerups are below actual objective bottom with a visible gap' },
    @{ Name='ordinary-mode-gap'; File='ArenaHudStackLayout.cs'; Property='LayoutSource'; Find=': ScorePanel.Position - new Vector2(0f, ScorePanel.PanelHeight + PowerupGap)'; Replace=': ObjectivePosition - new Vector2(0f, ObjectiveHeight + PowerupGap)'; Failure='hidden objective releases reserved row immediately' },
    @{ Name='countdown-uses-objective-slot'; File='ArenaHudStackLayout.cs'; Property='LayoutSource'; Find='&& mode.IsSearchActive'; Replace=''; Failure='objective visibility requires current context and live phase' },
    @{ Name='lost-context-visibility'; File='ArenaHudStackLayout.cs'; Property='LayoutSource'; Find='&& mode.ObjectiveHudVisible'; Replace=''; Failure='objective visibility requires current context and live phase' },
    @{ Name='foreign-match-row'; File='ArenaHudStackLayout.cs'; Property='LayoutSource'; Find='&& ReferenceEquals(ArenaMatchService.Current, match)'; Replace=''; Failure='foreign match cannot publish another match objective row' },
    @{ Name='view-height-drift'; File='DominationPanel.cs'; Property='ObjectiveSource'; Find='new Vector2(760f, ArenaHudStackLayout.ObjectiveHeight)'; Replace='new Vector2(760f, ArenaHudStackLayout.ObjectiveHeight + 12f)'; Failure='objective reserves the same height that its actual view draws' },
    @{ Name='powerup-offset-lost'; File='ArenaPowerupPanel.cs'; Property='PowerupSource'; Find='ArenaHudStackLayout.PowerupPosition(Modes.ArenaMatchService.Current)'; Replace='new Vector2(0f, -200f)'; Failure='powerups track score X offset' },
    @{ Name='throttled-position'; File='ArenaPowerupPanel.cs'; Property='PowerupSource'; Find='_rect.anchoredPosition = ArenaHudStackLayout.PowerupPosition(Modes.ArenaMatchService.Current);'; Replace='if (Time.unscaledTime >= _next) _rect.anchoredPosition = ArenaHudStackLayout.PowerupPosition(Modes.ArenaMatchService.Current);'; Failure='offset refresh precedes throttled powerup text' }
)
foreach ($fault in $faults) { $fault.Path = Join-Path $root ('ArenaMode.Client/Hud/' + $fault.File) }
Test-ArenaSourceFaults -Project (Join-Path $PSScriptRoot 'DominationHudChecks.csproj') -Token 'DOMINATION_HUD_CHECKS_PASSED' -Properties @() -Faults $faults
