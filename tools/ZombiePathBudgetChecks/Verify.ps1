$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $repo 'tools/Invoke-ArenaSourceChecks.ps1')
& (Join-Path $PSScriptRoot 'Verify-NativeContract.ps1')
$policy=Join-Path $repo 'ArenaMode.Client/Bots/ZombiePathBudgetPolicy.cs'
$runtime=Join-Path $repo 'ArenaMode.Client/Bots/ZombiePathBudgetRuntime.cs'
$melee=Join-Path $repo 'ArenaMode.Client/Bots/ZombieMeleePatch.cs'
$transpiler=Join-Path $repo 'ArenaMode.Client/Bots/ZombiePathBudgetTranspiler.cs'
$faults=@(
    @{Name='third-job';Path=$policy;Property='PolicySource';Find='internal const int JobsPerFrame = 2;';Replace='internal const int JobsPerFrame = 3;';Failure='mixed callers never exceed two planning jobs per frame'},
    @{Name='reset-every-call';Path=$policy;Property='PolicySource';Find='if (_frame == frame) return;';Replace='if (_frame == frame) { FrameJobs = 0; return; }';Failure='mixed callers never exceed two planning jobs per frame'},
    @{Name='first-callers-starve-tail';Path=$policy;Property='PolicySource';Find=' || !ReferenceEquals(_pending.First.Value, entry)';Replace='';Failure='FIFO preserves fair bounded waits in stable native order'},
    @{Name='same-frame-permit-reused';Path=$policy;Property='PolicySource';Find='if (entry.GrantedFrame == frame) return false;';Replace='';Failure='same body cannot replay a consumed permit in one frame'},
    @{Name='abandoned-head-never-expires';Path=$policy;Property='PolicySource';Find='internal const int MissedFrames = 2;';Replace='internal const int MissedFrames = int.MaxValue;';Failure='abandoned pending heads expire after two missed frames'},
    @{Name='unbounded-identity-retention';Path=$policy;Property='PolicySource';Find='internal const int MaximumEntries = 128;';Replace='internal const int MaximumEntries = 1024;';Failure='queue and retained identities have an explicit memory bound'},
    @{Name='stale-ticket-poisons-queue';Path=$policy;Property='PolicySource';Find='catch { current = false; }';Replace='catch { throw; }';Failure='throwing stale ticket is evicted without poisoning other callers'},
    @{Name='approach-bypasses-budget';Path=$runtime;Property='RuntimeSource';Find='private static bool Prefix(MoveToEnemyZigZag __instance) => AllowApproach(__instance);';Replace='private static bool Prefix(MoveToEnemyZigZag __instance) => true;';Failure='actual approach prefix shares the two job budget'},
    @{Name='foreign-owner-metered';Path=$runtime;Property='RuntimeSource';Find='if (owner == null || !ZombieRecoveryRuntime.OwnsCurrentBody(owner)) return true;';Replace='if (owner == null) return true;';Failure='unowned and foreign bodies remain native without consuming or resetting budget'},
    @{Name='denied-approach-clock-advanced';Path=$runtime;Property='RuntimeSource';Find='return TryTake(node._owner, melee: false);';Replace='bool allowed = TryTake(node._owner, melee: false); if (!allowed) node._nextCheck = Time.time + 1f; return allowed;';Failure='denied approach leaves native timers target and path untouched'},
    @{Name='melee-bypasses-budget';Path=$runtime;Property='RuntimeSource';Find='return !TryTake(owner, melee: true);';Replace='return false;';Failure='deferred melee is no path failure and preserves retry clock existing path'},
    @{Name='skip-attack-on-deferral';Path=$melee;Property='MeleeSource';Find=@('if (ZombiePathBudgetRuntime.ShouldDeferMelee(melee)) return true;','if (melee._nextTryHitTime < Time.time)');Replace=@('','if (ZombiePathBudgetRuntime.ShouldDeferMelee(melee)) return true; if (melee._nextTryHitTime < Time.time)');Failure='deferred melee still attempts attack steering and sprint'},
    @{Name='denied-melee-clock-advanced';Path=$melee;Property='MeleeSource';Find='if (ZombiePathBudgetRuntime.ShouldDeferMelee(melee)) return true;';Replace='if (ZombiePathBudgetRuntime.ShouldDeferMelee(melee)) { melee._runPathCheck = Time.time + 1f; return true; }';Failure='deferred melee is no path failure and preserves retry clock existing path'},
    @{Name='reverse-labels-bypass-guard';Path=$transpiler;Property='TranspilerSource';Find='first.labels.AddRange(code[anchor].labels);';Replace='';Failure='emitted reverse guard preserves attack and covers hit-not-due branch labels'},
    @{Name='inherited-field-identity-drift';Path=$transpiler;Property='TranspilerSource';Find='&& actual.Module == expected.Module && actual.MetadataToken == expected.MetadataToken;';Replace='&& Equals(actual, expected);';Failure='emitted reverse guard preserves attack and covers hit-not-due branch labels'},
    @{Name='reverse-guard-inverted';Path=$transpiler;Property='TranspilerSource';Find='new CodeInstruction(OpCodes.Brfalse, resume)';Replace='new CodeInstruction(OpCodes.Brtrue, resume)';Failure='emitted reverse guard preserves attack and covers hit-not-due branch labels'}
)
Test-ArenaSourceFaults -Project (Join-Path $PSScriptRoot 'ZombiePathBudgetChecks.csproj') -Token 'ZOMBIE_PATH_BUDGET_CHECKS_PASSED' -Faults $faults -RequireSingleSite
Test-ArenaSourceFaults -Project (Join-Path $repo 'tools/ZombieRecoveryChecks/ZombieRecoveryChecks.csproj') -Token 'ZOMBIE_RECOVERY_CHECKS_PASSED' -RequireSingleSite -Faults @(
    @{Name='raid-reset-keeps-old-budget';Path=(Join-Path $repo 'ArenaMode.Client/Bots/ZombieSpawnService.cs');Property='ServiceSource';Find='ZombiePathBudgetRuntime.Reset();';Replace='';Failure='spawn lifecycle resets path planning budget'}
)
'ZOMBIE_PATH_BUDGET_VERIFIED'
