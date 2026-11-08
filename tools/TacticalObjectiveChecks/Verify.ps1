$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../Invoke-ArenaSourceChecks.ps1')
$policy = (Resolve-Path (Join-Path $PSScriptRoot '../../ArenaMode.Client/Modes/TacticalObjectivePolicy.cs')).Path
$lanes = (Resolve-Path (Join-Path $PSScriptRoot '../../ArenaMode.Client/Modes/TacticalObjectiveLanes.cs')).Path
$faults = @(
    @{ Name='hold-unclamped'; Path=$policy; Property='PolicySource'; Find='Math.Min(MaximumHoldSeconds, Math.Max(MinimumHoldSeconds, holdSeconds))'; Replace='holdSeconds'; Failure='appear and hold seconds clamp at their floors' },
    @{ Name='appears-at-once'; Path=$policy; Property='PolicySource'; Find='if (now - _startedAt >= AppearAfterSeconds) Active = true;'; Replace='if (true) Active = true;'; Failure='standing on the point before it appears earns nothing' },
    @{ Name='capture-repeats'; Path=$policy; Property='PolicySource'; Find='if (!Started || Captured != TacticalObjectiveSide.None) return TacticalObjectiveSide.None;'; Replace='if (!Started) return TacticalObjectiveSide.None;'; Failure='a hold completes exactly once' },
    @{ Name='contested-advances'; Path=$policy; Property='PolicySource'; Find='if (Contested) return TacticalObjectiveSide.None;'; Replace='if (false) return TacticalObjectiveSide.None;'; Failure='contested points pause the hold without resetting it' },
    @{ Name='empty-keeps-progress'; Path=$policy; Property='PolicySource'; Find="            Holder = TacticalObjectiveSide.None;`n            Progress = 0f;`n            return TacticalObjectiveSide.None;"; Replace="            return TacticalObjectiveSide.None;"; Failure='an empty point resets the hold' },
    @{ Name='switch-keeps-progress'; Path=$policy; Property='PolicySource'; Find='if (Holder != present) { Holder = present; Progress = 0f; }'; Replace='Holder = present;'; Failure='a change of holder restarts the hold for the new holder' },
    @{ Name='time-runs-backwards'; Path=$policy; Property='PolicySource'; Find='|| now < _lastAdvance) return TacticalObjectiveSide.None;'; Replace=') return TacticalObjectiveSide.None;'; Failure='time running backwards changes nothing' },
    @{ Name='stall-awards'; Path=$policy; Property='PolicySource'; Find='if (dt > MaxStepSeconds) dt = 0f;'; Replace=''; Failure='a step over thirty seconds awards nothing' },
    @{ Name='lanes-off-centre'; Path=$lanes; Property='LanesSource'; Find='float offset = (i - (lanes - 1) * .5f) * spacing;'; Replace='float offset = i * spacing;'; Failure='lanes are centred on the axis' },
    @{ Name='lanes-parallel'; Path=$lanes; Property='LanesSource'; Find='float lateralX = axisZ, lateralZ = -axisX;'; Replace='float lateralX = axisX, lateralZ = axisZ;'; Failure='lanes lie across the middle between the groups' },
    @{ Name='spacing-unclamped'; Path=$lanes; Property='LanesSource'; Find='float spacing = Math.Min(MaxSpacing, Math.Max(MinSpacing, length * SpacingFraction));'; Replace='float spacing = length * SpacingFraction;'; Failure='lane spacing floors at six metres' },
    @{ Name='missing-centroid-lanes'; Path=$lanes; Property='LanesSource'; Find='if (!squad.HasValue || !players.HasValue || lanes < 1) return Array.Empty<TacticalLanePoint>();'; Replace='if (lanes < 1) return Array.Empty<TacticalLanePoint>(); squad ??= new TacticalLanePoint(); players ??= new TacticalLanePoint();'; Failure='a missing centroid yields no lanes' }
)
Test-ArenaSourceFaults -Project (Join-Path $PSScriptRoot 'TacticalObjectiveChecks.csproj') -Token 'TACTICAL_OBJECTIVE_CHECKS_PASSED' -Properties @() -Faults $faults
