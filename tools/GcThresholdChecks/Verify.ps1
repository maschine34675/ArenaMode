$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../Invoke-ArenaSourceChecks.ps1')
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$policy=Join-Path $repo 'ArenaMode.Client/Performance/GcThresholdPolicy.cs'
$monitor=Join-Path $repo 'ArenaMode.Client/Performance/GcThresholdMonitor.cs'
$faults=@(
    # --- Decision logic (GcThresholdPolicy, compiled directly into the checks project) ---
    @{Name='zero-threshold-ignored';Path=$policy;Property='PolicySource';Find='if (thresholdBytes <= 0)';Replace='if (false)';Failure='a zero threshold never arms'},
    @{Name='not-gated-on-arena';Path=$policy;Property='PolicySource';Find='if (!arenaActive || !gcCurrentlyDisabled)';Replace='if (!gcCurrentlyDisabled)';Failure='never arms outside an active arena session'},
    @{Name='arms-with-collector-enabled';Path=$policy;Property='PolicySource';Find='if (!arenaActive || !gcCurrentlyDisabled)';Replace='if (!arenaActive)';Failure='never arms while the collector is already enabled'},
    @{Name='rearms-after-armed-or-switched';Path=$policy;Property='PolicySource';Find='if (state.Armed || state.Switched)';Replace='if (false)';Failure='an already-armed raid does not arm twice'},
    @{Name='moment-ignores-switched';Path=$policy;Property='PolicySource';Find='state.Armed && !state.Switched;';Replace='state.Armed;';Failure='an already-switched raid does not switch again at a moment'},
    @{Name='timeout-ignores-switched';Path=$policy;Property='PolicySource';Find='state.Armed && !state.Switched && now - state.ArmedAt >= ArmedTimeoutSeconds;';Replace='state.Armed && now - state.ArmedAt >= ArmedTimeoutSeconds;';Failure='an already-switched raid does not switch again on timeout'},
    @{Name='timeout-never-elapses';Path=$policy;Property='PolicySource';Find='now - state.ArmedAt >= ArmedTimeoutSeconds;';Replace='false;';Failure='the bounded wait switches without a moment'},
    @{Name='reset-does-not-clear';Path=$policy;Property='PolicySource';Find='state = State.Fresh;';Replace='state = state;';Failure='reset clears the armed flag for the next raid'},
    # Caught in OncePerRaidAndReset (which runs first and also exercises
    # ShouldReassert on a just-reset state) rather than in Reassert's own
    # first assertion of the same rule — both cover the same guard clause.
    @{Name='reassert-before-switch';Path=$policy;Property='PolicySource';Find='if (!arenaActive || !state.Switched || !gcCurrentlyDisabled)';Replace='if (!arenaActive || !gcCurrentlyDisabled)';Failure='a reset raid has nothing to reassert'},
    @{Name='reassert-ignores-collector-enabled';Path=$policy;Property='PolicySource';Find='if (!arenaActive || !state.Switched || !gcCurrentlyDisabled)';Replace='if (!arenaActive || !state.Switched)';Failure='reassert does not fire while the collector reads enabled'},
    @{Name='reassert-cooldown-ignored';Path=$policy;Property='PolicySource';Find='return float.IsNaN(state.LastReassertAt) || now - state.LastReassertAt >= ReassertCooldownSeconds;';Replace='return true;';Failure='reassert is rate limited'},

    # --- Source guard: this feature may never force a blocking collection ---
    # (GcThresholdPolicy is compiled in, so this also proves the guard scan reads
    # the same mutated file the assertions above just ran against.)
    # Injected into a block-bodied method (ShouldArm/ShouldReassert), not the
    # one-line expression-bodied setters, so the mutant still compiles.
    @{Name='policy-forces-a-collection';Path=$policy;Property='PolicySource';Find='return heapBytes >= thresholdBytes;';Replace="GC.Collect();`n        return heapBytes >= thresholdBytes;";Failure='policy source never forces a collection'},
    @{Name='policy-forces-heap-read';Path=$policy;Property='PolicySource';Find='return float.IsNaN(state.LastReassertAt) || now - state.LastReassertAt >= ReassertCooldownSeconds;';Replace="GC.GetTotalMemory(true);`n        return float.IsNaN(state.LastReassertAt) || now - state.LastReassertAt >= ReassertCooldownSeconds;";Failure='policy source never reads the heap with a forced collection'},
    # GcThresholdMonitor references UnityEngine/EFT types that do not exist outside
    # the game, so it is never compiled here — only text-scanned, via MonitorSource.
    @{Name='monitor-forces-a-collection';Path=$monitor;Property='MonitorSource';Find='InGameMemoryManagement.GCEnabled = true;';Replace='InGameMemoryManagement.GCEnabled = true; GC.Collect();';Failure='monitor source never forces a collection'},
    @{Name='monitor-forces-heap-read';Path=$monitor;Property='MonitorSource';Find='GC.GetTotalMemory(false)';Replace='GC.GetTotalMemory(true)';Failure='monitor source never reads the heap with a forced collection'}
)
Test-ArenaSourceFaults (Join-Path $PSScriptRoot 'GcThresholdChecks.csproj') 'ARENA_GC_THRESHOLD_BASELINE_PASSED' @() $faults
Write-Output 'ARENA_GC_THRESHOLD_CHECKS_PASSED'
