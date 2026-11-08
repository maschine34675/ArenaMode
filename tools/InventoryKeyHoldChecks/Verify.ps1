$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../Invoke-ArenaSourceChecks.ps1')
$source = (Resolve-Path (Join-Path $PSScriptRoot '../../ArenaMode.Client/Hud/InventoryKeyHold.cs')).Path
$faults = @(
    @{ Name='threshold-changed'; Find='internal const float HoldSeconds = 0.25f;'; Replace='internal const float HoldSeconds = 0.5f;'; Failure='hold threshold is a quarter second' },
    @{ Name='table-never-shows'; Find='Held = elapsed >= HoldSeconds;'; Replace='Held = false;'; Failure='held once the threshold passes' },
    @{ Name='table-shows-at-once'; Find='Held = elapsed >= HoldSeconds;'; Replace='Held = true;'; Failure='early frames of a press show no table' },
    @{ Name='hold-release-forwards'; Find='bool tap = elapsed < HoldSeconds;'; Replace='bool tap = true;'; Failure='releasing a hold forwards no tap' },
    @{ Name='tap-lost'; Find='bool tap = elapsed < HoldSeconds;'; Replace='bool tap = false;'; Failure='short release is a tap' },
    @{ Name='threshold-release-is-tap'; Find='bool tap = elapsed < HoldSeconds;'; Replace='bool tap = elapsed <= HoldSeconds;'; Failure='release at the threshold is a hold, not a tap' },
    @{ Name='release-keeps-pending'; Find="        bool tap = elapsed < HoldSeconds;`n        _pressedAt = -1f;`n        Held = false;`n        return tap;"; Replace="        bool tap = elapsed < HoldSeconds;`n        Held = false;`n        return tap;"; Failure='release clears the pending press and hides the table' },
    @{ Name='cancel-keeps-table'; Find="    internal void Cancel()`n    {`n        _pressedAt = -1f;`n        Held = false;`n    }"; Replace="    internal void Cancel()`n    {`n        _pressedAt = -1f;`n    }"; Failure='cancel hides the table' },
    @{ Name='repress-keeps-clock'; Find="    internal void Press(float now)`n    {`n        _pressedAt = now;`n        Held = false;`n    }"; Replace="    internal void Press(float now)`n    {`n        if (_pressedAt < 0f) _pressedAt = now;`n        Held = false;`n    }"; Failure='a re-press restarts the hold clock' }
)
foreach ($fault in $faults) { $fault.Path = $source; $fault.Property = 'HoldSource' }
Test-ArenaSourceFaults -Project (Join-Path $PSScriptRoot 'InventoryKeyHoldChecks.csproj') -Token 'INVENTORY_KEY_HOLD_CHECKS_PASSED' -Properties @() -Faults $faults
