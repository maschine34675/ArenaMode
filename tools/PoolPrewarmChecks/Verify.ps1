$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../Invoke-ArenaSourceChecks.ps1')
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$policy=Join-Path $repo 'ArenaMode.Client/Performance/PoolPrewarmPolicy.cs'
$monitor=Join-Path $repo 'ArenaMode.Client/Performance/PoolPrewarmMonitor.cs'
$faults=@(
    # --- Decision logic (PoolPrewarmPolicy, compiled directly into the checks project) ---
    @{Name='arena-gate-removed';Path=$policy;Property='PolicySource';Find='!arenaActive || ';Replace='';Failure='never clamps outside an active arena session'},
    @{Name='reason-gate-removed';Path=$policy;Property='PolicySource';Find='reason != "LoadPlayerResources" || ';Replace='';Failure='reason "initial" never clamps inside an active arena'},
    @{Name='keepidle-zero-ignored';Path=$policy;Property='PolicySource';Find='keepIdle <= 0 || ';Replace='';Failure='a zero keepIdle never clamps'},
    @{Name='negative-allowed-possible';Path=$policy;Property='PolicySource';Find='Math.Max(0, keepIdle - futurePoolSize)';Replace='(keepIdle - futurePoolSize)';Failure='allowed never goes negative once futurePoolSize exceeds keepIdle'},
    @{Name='already-in-budget-still-clamped';Path=$policy;Property='PolicySource';Find='requestedCount <= allowed';Replace='false';Failure='demand at the budget is not clamped'},

    # --- Source guard: this feature may never force a blocking collection ---
    # (PoolPrewarmPolicy is compiled in, so this also proves the guard scan reads
    # the same mutated file the assertions above just ran against.)
    @{Name='policy-forces-a-collection';Path=$policy;Property='PolicySource';Find='return Decision.ClampedTo(allowed);';Replace="GC.Collect();`n        return Decision.ClampedTo(allowed);";Failure='policy source never forces a collection'},
    @{Name='policy-forces-heap-read';Path=$policy;Property='PolicySource';Find='public static bool ShouldLogLedger(int poolsTouched) => poolsTouched > 0;';Replace="public static bool ShouldLogLedger(int poolsTouched) { GC.GetTotalMemory(true); return poolsTouched > 0; }";Failure='policy source never reads the heap with a forced collection'},

    # --- Structural mutants (PoolPrewarmMonitor, text-scanned only — it references
    # UnityEngine/EFT/HarmonyLib types that cannot compile outside the game) ---
    @{Name='originalmethod-reintroduced';Path=$monitor;Property='MonitorSource';Find='ref Task __result)';Replace='ref Task __result, System.Reflection.MethodBase __originalMethod)';Failure='the prefix signature must not gain an __originalMethod parameter'},
    @{Name='objectscount-misspelled';Path=$monitor;Property='MonitorSource';Find='ref int objectsCount,';Replace='ref int objectsCoun,';Failure='the ref parameter is spelled objectsCount, matching the implementation'},
    @{Name='missing-completed-task-on-skip';Path=$monitor;Property='MonitorSource';Find='__result = Task.CompletedTask;';Replace='';Failure='the skip path sets __result before returning false'},
    @{Name='ledger-cleared-before-log-decision';Path=$monitor;Property='MonitorSource';Find='if (PoolPrewarmPolicy.ShouldLogLedger(_poolsTouched.Count))';Replace='_poolsTouched.Clear(); if (PoolPrewarmPolicy.ShouldLogLedger(_poolsTouched.Count))';Failure='Reset() decides whether to log the ledger before clearing it'},
    @{Name='monitor-forces-a-collection';Path=$monitor;Property='MonitorSource';Find='_expansionsClamped++;';Replace="_expansionsClamped++;`n        GC.Collect();";Failure='monitor source never forces a collection'},
    @{Name='monitor-forces-heap-read';Path=$monitor;Property='MonitorSource';Find='_instancesAvoided += Math.Max(0, requested - allowed);';Replace="_instancesAvoided += Math.Max(0, requested - allowed);`n        GC.GetTotalMemory(true);";Failure='monitor source never reads the heap with a forced collection'}
)
Test-ArenaSourceFaults (Join-Path $PSScriptRoot 'PoolPrewarmChecks.csproj') 'ARENA_POOL_PREWARM_BASELINE_PASSED' @() $faults
Write-Output 'ARENA_POOL_PREWARM_CHECKS_PASSED'
