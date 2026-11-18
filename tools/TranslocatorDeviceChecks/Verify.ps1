$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $root 'tools/Invoke-ArenaSourceChecks.ps1')
$project = Join-Path $PSScriptRoot 'TranslocatorDeviceChecks.csproj'
$supply = Join-Path $root 'ArenaMode.Client/Combat/Translocator/ArenaTranslocatorSupply.cs'
$inputSource = Join-Path $root 'ArenaMode.Client/Combat/Translocator/ArenaTranslocatorInput.cs'
$model = Join-Path $root 'ArenaMode.Client/Combat/Translocator/ArenaTranslocatorModel.cs'
$pose = Join-Path $root 'ArenaMode.Client/Combat/Translocator/ArenaTranslocatorPose.cs'
# No mod project, build hook, runtime deployment or process launch is reachable here.
$native = (& dotnet build (Join-Path $PSScriptRoot 'NativeApi/NativeApi.csproj') -c Release --nologo -v:q 2>&1 | Out-String)
if ($LASTEXITCODE -ne 0) { throw $native }
Write-Output 'TRANSLOCATOR_DEVICE_NATIVE_API_COMPILED'
$faults = @(
    @{Name='drop-ownership';Path=$supply;Property='SupplySource';Find='&& item.IsChildOf(equipment)';Replace='';Failure='dropped device loses ownership'},
    @{Name='reachability';Path=$supply;Property='SupplySource';Find='&& player.InventoryController.IsAtReachablePlace(item)';Replace='';Failure='unreachable device loses ownership'},
    @{Name='exact-instance';Path=$supply;Property='SupplySource';Find='ReferenceEquals(grant.Item, item)';Replace='grant.Item?.Id == item.Id';Failure='exact item reference required'},
    @{Name='match-ownership';Path=$supply;Property='SupplySource';Find='|| !ReferenceEquals(match, Modes.ArenaMatchService.Current)';Replace='';Failure='match ownership exact'},
    @{Name='ledger-reset';Path=$supply;Property='SupplySource';Find='&& ReferenceEquals(match, previousMatch)) return;';Replace='&& false) return;';Failure='no duplicate across ticks or toggle'},
    @{Name='capability';Path=$supply;Property='SupplySource';Find='HasVerifiedTemplate => Session.ArenaServerClient.TranslocatorAvailableForConfirmedRaid';Replace='HasVerifiedTemplate => true';Failure='confirmed capability required'},
    @{Name='template-invariant';Path=$supply;Property='SupplySource';Find='&& item.Width == 1';Replace='';Failure='changed template rejected'},
    @{Name='supply-live-scope';Path=$supply;Property='SupplySource';Find='!ArenaTranslocatorRuntime.IsLivePlayer(player) || world == null';Replace='world == null';Failure='blocked supply inactive'},
    @{Name='native-simulation';Path=$supply;Property='SupplySource';Find=@'
if (plan.Failed || !inventory.CanExecute(plan.Value)) continue;
                    var added
'@;Replace=@'
if (!inventory.CanExecute(plan.Value)) continue;
                    var added
'@;Failure='native simulation required'},
    @{Name='notification-ownership';Path=$supply;Property='SupplySource';Find=@('grant.Item = item; // Native notification subscribers may throw after physical placement.','added.Value.RaiseEvents(inventory, CommandStatus.Succeed);');Replace=@('// deliberately late assignment','added.Value.RaiseEvents(inventory, CommandStatus.Succeed); grant.Item = item;');Failure='notification failure retains ownership without duplicate'},
    @{Name='late-key-occupant';Path=$supply;Property='SupplySource';Find=@'
if (bound.ContainsKey(slot)) continue;
            foreach (var value in bound.Values) if (ReferenceEquals(value, item)) return;
'@;Replace=@'
foreach (var value in bound.Values) if (ReferenceEquals(value, item)) return;
'@;Failure='late occupant never evicted'},
    @{Name='late-user-binding';Path=$supply;Property='SupplySource';Find=@'
foreach (var value in bound.Values) if (ReferenceEquals(value, item)) return;
            var applied
'@;Replace='var applied';Failure='late user binding preserved'},
    @{Name='release-input';Path=$inputSource;Property='InputSource';Find='if (command == ECommand.EndShooting || command == ECommand.EndAlternativeShooting) return true;';Replace='if (command == ECommand.EndShooting || command == ECommand.EndAlternativeShooting) return false;';Failure='release never fires'},
    @{Name='same-frame-input';Path=$inputSource;Property='InputSource';Find='if ((primary ? frame.Primary : frame.Secondary) == Time.frameCount) return true;';Replace='';Failure='same frame duplicate ignored'},
    @{Name='input-menu';Path=$inputSource;Property='InputSource';Find='|| ArenaInputBlocker.Blocked';Replace='';Failure='blocked owned input menu'},
    @{Name='stale-hands';Path=$inputSource;Property='InputSource';Find='|| !ReferenceEquals(player.HandsController, hands)';Replace='';Failure='blocked owned input stalehands'},
    @{Name='input-shutdown';Path=$inputSource;Property='InputSource';Find='if (!_enabled || (command';Replace='if (false || (command';Failure='input shutdown inert'},
    @{Name='input-old-native-idle';Path=$inputSource;Property='InputSource';Find='!ArenaTranslocatorPose.CanOperate(hands)';Replace='!hands.CanInteract() || hands.IsInInteractionStrictCheck()';Failure='raised native phase admits primary'},
    @{Name='input-phase-bypass';Path=$inputSource;Property='InputSource';Find='|| !ArenaTranslocatorPose.CanOperate(hands)';Replace='';Failure='blocked owned input interaction'},
    @{Name='input-shared-main-phase';Path=$pose;Property='PoseSource';Find='&& ReadyPhase(animator.GetCurrentAnimatorStateInfo(1), firearms)';Replace='';Failure='blocked owned input look'},
    @{Name='input-shared-next-phase';Path=$pose;Property='PoseSource';Find='&& (!animator.IsInTransition(1) || ReadyPhase(animator.GetNextAnimatorStateInfo(1), firearms))';Replace='';Failure='blocked owned input nextlook'},
    @{Name='input-shared-active';Path=$pose;Property='PoseSource';Find='&& AnimationControllerParametersTable.GetBoolActive(animator)';Replace='';Failure='blocked owned input inactive'},
    @{Name='input-shared-operation';Path=$pose;Property='PoseSource';Find='|| current.CurrentHandsOperation?.GetType() != typeof(PortableRangeFinderController.PortableRangeFinderIdling)';Replace='';Failure='blocked owned input draw'},
    @{Name='ordinary-model';Path=$model;Property='ModelSource';Find='&& item?.TemplateId == ArenaTranslocatorItem.TemplateId';Replace='';Failure='model untouched ordinary'},
    @{Name='late-epoch-model';Path=$model;Property='ModelSource';Find='&& ArenaSession.ArmEpoch == epoch';Replace='';Failure='late model rejected epoch'},
    @{Name='late-lifetime-model';Path=$model;Property='ModelSource';Find='&& lifetime == _lifetime';Replace='';Failure='late model rejected shutdown'},
    @{Name='shared-role-model';Path=$model;Property='ModelSource';Find='prefab.IsInPool || ';Replace='';Failure='model untouched pooled'},
    @{Name='restore-foreign-mesh';Path=$model;Property='ModelSource';Find='Filter != null && Filter.sharedMesh == Mesh';Replace='Filter != null';Failure='foreign replacements preserved owned materials released'},
    @{Name='destroy-donor';Path=$model;Property='ModelSource';Find='if (Mesh != null) UnityEngine.Object.Destroy(Mesh);';Replace='if (Mesh != null) UnityEngine.Object.Destroy(Original);';Failure='only owned resources destroyed'},
    @{Name='mesh-construction-leak';Path=$model;Property='ModelSource';Find='catch { UnityEngine.Object.Destroy(mesh); throw; }';Replace='catch { throw; }';Failure='construction failure releases mesh'},
    @{Name='material-construction-leak';Path=$model;Property='ModelSource';Find='catch { UnityEngine.Object.Destroy(material); throw; }';Replace='catch { throw; }';Failure='construction failure releases material'},
    @{Name='hidden-native-lens';Path=$model;Property='ModelSource';Find='lens != null && lens.enabled';Replace='lens != null';Failure='originally hidden lens remains hidden'}
)
Test-ArenaSourceFaults -Project $project -Token 'TRANSLOCATOR_DEVICE_PRODUCTION_PASSED' -Faults $faults -RequireSingleSite
Write-Output 'TRANSLOCATOR_DEVICE_CHECKS_PASSED'
