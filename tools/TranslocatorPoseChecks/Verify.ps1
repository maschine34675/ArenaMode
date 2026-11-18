$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $root 'tools/Invoke-ArenaSourceChecks.ps1')
$project = Join-Path $PSScriptRoot 'TranslocatorPoseChecks.csproj'
$pose = Join-Path $root 'ArenaMode.Client/Combat/Translocator/ArenaTranslocatorPose.cs'
# Standalone projects only: no mod ProjectReference, build hook or deployment.
$graph = (& python (Join-Path $PSScriptRoot 'NativeGraph.py') 2>&1 | Out-String)
if ($LASTEXITCODE -ne 0 -or -not $graph.Contains('TRANSLOCATOR_NATIVE_GRAPH_PASSED')) { throw $graph }
Write-Output $graph.Trim()
$native = (& dotnet build (Join-Path $PSScriptRoot 'NativeApi/NativeApi.csproj') -c Release --nologo -v:q 2>&1 | Out-String)
if ($LASTEXITCODE -ne 0) { throw $native }
Write-Output 'TRANSLOCATOR_POSE_NATIVE_API_COMPILED'
$faults = @(
    @{Name='raise-flag';Find='firearms.SetFire(true);';Replace='firearms.SetFire(false);';Failure='native raised pose'},
    @{Name='replace-active-lease';Find='if (_lease != null) return; // No per-frame animation or left-hand reset.';Replace='';Failure='restore exact false flag'},
    @{Name='original-flag';Find='Original = AnimationControllerParametersTable.GetBoolFire(animator)';Replace='Original = false';Failure='original true is untouched'},
    @{Name='live-scope';Find="if (!ArenaTranslocatorRuntime.IsLivePlayer(player) || !ArenaTranslocatorSupply.HasVerifiedTemplate`n            || player == null";Replace="if (!ArenaTranslocatorSupply.HasVerifiedTemplate`n            || player == null";Failure='blocked inactive'},
    @{Name='confirmed-capability';Find="|| !ArenaTranslocatorSupply.HasVerifiedTemplate`n            || player == null";Replace="`n            || player == null";Failure='blocked capability'},
    @{Name='exact-private-owner';Find='|| !ArenaTranslocatorSupply.Owns(player, current.Item)';Replace='';Failure='blocked ordinary item'},
    @{Name='native-ADS-owner';Find='|| current.IsAiming';Replace='';Failure='blocked ADS'},
    @{Name='exact-idle-operation';Find='|| current.CurrentHandsOperation?.GetType() != typeof(PortableRangeFinderController.PortableRangeFinderIdling)';Replace='';Failure='blocked draw operation'},
    @{Name='operation-state';Find='|| current.CurrentHandsOperation.State != Player.EOperationState.Executing';Replace='';Failure='blocked finished operation'},
    @{Name='old-native-idle-gate';Find='return animator != null && firearms.HasParameter';Replace='return animator != null && current.CanInteract() && firearms.HasParameter';Failure='raised phase remains operable despite native idle predicate'},
    @{Name='native-strict-interaction';Find='&& !current.IsInInteractionStrictCheck()';Replace='';Failure='blocked strict interaction'},
    @{Name='native-active';Find='&& AnimationControllerParametersTable.GetBoolActive(animator)';Replace='';Failure='blocked inactive hands'},
    @{Name='native-handsaction';Find='|| current._player?.InventoryController?.HasAnyHandsAction() != false';Replace='';Failure='blocked hands action'},
    @{Name='main-phase';Find='&& ReadyPhase(animator.GetCurrentAnimatorStateInfo(1), firearms)';Replace='';Failure='blocked main phase Hands.LOOK'},
    @{Name='next-phase';Find='&& (!animator.IsInTransition(1) || ReadyPhase(animator.GetNextAnimatorStateInfo(1), firearms))';Replace='';Failure='blocked next inspect phase'},
    @{Name='wrong-raised-name';Find='state.IsName("Hands.AIMING")';Replace='state.IsName("Hands.FIRE")';Failure='raised phase remains operable despite native idle predicate'},
    @{Name='native-can-aim';Find='|| !EFTHardSettings.Instance.CanAimInState(player.CurrentState.Name)';Replace='';Failure='blocked native aim forbidden'},
    @{Name='native-overlap';Find='|| !(current._overlap <= EFTHardSettings.Instance.STOP_AIMING_AT)';Replace='';Failure='blocked overlap'},
    @{Name='overlap-NaN';Find='!(current._overlap <= EFTHardSettings.Instance.STOP_AIMING_AT)';Replace='current._overlap > EFTHardSettings.Instance.STOP_AIMING_AT';Failure='blocked overlap NaN'},
    @{Name='left-hand-owner';Find='&& !firearms.IsInInteraction';Replace='';Failure='blocked left hand interaction'},
    @{Name='pooled-owner';Find='|| prefab.IsInPool';Replace='';Failure='blocked pooled prefab'},
    @{Name='new-item-lease';Find='!ReferenceEquals(_lease.Item, hands.Item) || ';Replace='';Failure='new private instance gets new lease'},
    @{Name='restore-left-hand-sideeffect';Find='AnimationControllerParametersTable.SetFire(lease.Animator, lease.Original);';Replace='lease.Firearms.SetFire(lease.Original);';Failure='restore preserves new left hand interaction'},
    @{Name='restore-foreign-ADS';Find='&& !lease.Hands.IsAiming';Replace='';Failure='external ADS owner preserved'},
    @{Name='restore-rebound-animator';Find='&& ReferenceEquals(lease.Firearms.Animator, lease.Animator)';Replace='';Failure='rebound animator untouched'},
    @{Name='restore-rebound-prefab';Find='&& ReferenceEquals(lease.Prefab.FirearmsAnimator, lease.Firearms)';Replace='';Failure='rebound prefab wrapper untouched'},
    @{Name='pool-foreign-object';Find='&& ReferenceEquals(_lease.Prefab, __instance)';Replace='';Failure='foreign pool event preserves lease'},
    @{Name='pool-no-restoration';Find='if (_lease != null && ReferenceEquals(_lease.Prefab, __instance)) Reset();';Replace='';Failure='pool return restores before native reset'},
    @{Name='pool-late-base-restoration';Find='typeof(WeaponPrefab), nameof(AssetPoolObject.ReturnToPool)';Replace='typeof(AssetPoolObject), nameof(AssetPoolObject.ReturnToPool)';Failure='pool return restores before native reset'},
    @{Name='partial-ownership';Find=@('_lease = lease; // Own a partial native setter before it can throw.','firearms.SetFire(true);');Replace=@('// deliberately late ownership','firearms.SetFire(true); _lease = lease;');Failure='partial native setter restores'},
    @{Name='pool-install-admission';Find='if (!EnsureHooks()) return;';Replace='EnsureHooks();';Failure='failed pool install fails closed'},
    @{Name='shutdown-inert';Find='if (_stopped) return;';Replace='';Failure='shutdown inert'}
)
foreach ($fault in $faults) { $fault.Path=$pose; $fault.Property='PoseSource' }
Test-ArenaSourceFaults -Project $project -Token 'TRANSLOCATOR_POSE_PRODUCTION_PASSED' -Faults $faults -RequireSingleSite
Write-Output 'TRANSLOCATOR_POSE_CHECKS_PASSED'
