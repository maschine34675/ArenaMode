param([switch]$ProductionOnly)
$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$arenaNative = [IO.Path]::GetFullPath((Join-Path $arenaRepo '..\_decompiled'))
$arenaWork = Join-Path $arenaRepo ('artifacts\air-vault-checks\' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($arenaWork) | Out-Null
$arenaProject = Join-Path $PSScriptRoot 'ArenaAirVaultChecks.csproj'
$arenaSource = [IO.File]::ReadAllText((Join-Path $arenaRepo 'ArenaMode.Client\Movement\ArenaAirVault.cs')).Replace("`r`n","`n")
$arenaUnitySource = [IO.File]::ReadAllText((Join-Path $arenaRepo 'ArenaMode.Client\Movement\ArenaAirVault.Unity.cs')).Replace("`r`n","`n")
$arenaAnimatorSource = [IO.File]::ReadAllText((Join-Path $arenaRepo 'ArenaMode.Client\Movement\ArenaAirVault.Animator.cs')).Replace("`r`n","`n")

function Read-Method([string]$File, [string]$Signature) {
    $arenaText = [IO.File]::ReadAllText((Join-Path $arenaNative $File))
    $arenaStart = $arenaText.IndexOf($Signature, [StringComparison]::Ordinal)
    if ($arenaStart -lt 0) { throw "Missing native method: $Signature" }
    $arenaBrace = $arenaText.IndexOf('{', $arenaStart)
    $arenaDepth = 0
    for ($arenaIndex = $arenaBrace; $arenaIndex -lt $arenaText.Length; $arenaIndex++) {
        if ($arenaText[$arenaIndex] -eq '{') { $arenaDepth++ }
        if ($arenaText[$arenaIndex] -eq '}') { $arenaDepth--; if ($arenaDepth -eq 0) { return $arenaText.Substring($arenaStart,$arenaIndex-$arenaStart+1) } }
    }
    throw "Unclosed native method: $Signature"
}
$arenaMethods = @(
    (Read-Method 'EFT.Vaulting\VaultingComponent.cs' 'public bool CanVaulting()'),
    (Read-Method 'EFT.Vaulting\VaultingComponent.cs' 'public EVaultingStrategy GetVaultingStrategy()').Replace('public EVaultingStrategy GetVaultingStrategy()', 'public EVaultingStrategy NativeGetVaultingStrategy()'),
    (Read-Method 'EFT.Vaulting\VaultingComponent.cs' 'public EVaultingStrategy GetAutoVaultingStrategy()'),
    (Read-Method 'EFT.Vaulting\VaultingComponent.cs' 'public bool TryVaulting()'),
    (Read-Method 'EFT.Vaulting\VaultingComponent.cs' 'public bool DoVaultingByStrategy(EVaultingStrategy strategy)').Replace('public bool DoVaultingByStrategy(', 'public bool NativeDoVaultingByStrategy('))
$arenaNativeFile = Join-Path $arenaWork 'Native.cs'
[IO.File]::WriteAllText($arenaNativeFile, 'namespace EFT.Vaulting { public partial class VaultingComponent {' + ($arenaMethods -join [Environment]::NewLine) + '} }' + [Environment]::NewLine + 'namespace EFT { public partial class MovementContext {' + (Read-Method 'EFT\MovementContext.cs' 'public bool TryVaulting()') + '} }')

$arenaExtraNative = [Environment]::NewLine + 'namespace FastAnimatorSystem { public partial class FastAnimatorProcessor {' +
    (Read-Method 'FastAnimatorSystem\FastAnimatorProcessor.cs' 'public void ProcessState(FastLayerInfo layerInfo, float deltaTime, int layerIndex, int recursionLevel = 0)').Replace('public void ProcessState(', 'public void NativeProcessState(') +
    (Read-Method 'FastAnimatorSystem\FastAnimatorProcessor.cs' 'public void ProcessImmediateTransition(') +
    (Read-Method 'FastAnimatorSystem\FastAnimatorProcessor.cs' 'public AnimatorControllerTransition GetInterruptedTriggeredTransition(') + '} }' +
    'namespace EFT { public partial class MovementState {' +
    (Read-Method 'EFT\MovementState.cs' 'public override void OnStateEnter(float normalizedTime)') +
    (Read-Method 'EFT\MovementState.cs' 'public override void OnStateMove(float normalizedTime)') +
    (Read-Method 'EFT\MovementState.cs' 'public override void OnStateExit(float normalizedTime)') +
    '} public partial class MovementContext {' +
    (Read-Method 'EFT\MovementContext.cs' 'public void ProcessStateEnter(BaseMovementState enteredState)').Replace('_playerAnimator.Animator.Play(', '((FastAnimatorSystem.FastAnimatorProcessor)_playerAnimator.Animator).Play(') + '} }'
$arenaExtraNative += 'namespace EFT { public partial class Player {' +
    (Read-Method 'EFT\Player.cs' 'public void ToggleVaulting()').Replace('public void ToggleVaulting()', 'public void NativeToggleVaulting()').Replace('Time.deltaTime','UnityEngine.Time.deltaTime') + '} }'
$arenaExtraNative += 'public partial class PlayerStateContainer {' +
    (Read-Method 'PlayerStateContainer.cs' 'public sealed override void OnStateEnter(').Replace('public sealed override void OnStateEnter(', 'public void NativeOnStateEnter(').Replace('Animator animator, AnimatorStateInfo stateInfo', 'UnityEngine.Animator animator, UnityEngine.AnimatorStateInfo stateInfo') + '}' +
    'namespace EFT { public partial class Player {' +
    (Read-Method 'EFT\Player.cs' 'public virtual void BodyUpdate(float deltaTime, int loop = 1)').Replace('public virtual void BodyUpdate(', 'public void NativeBodyUpdate(') + '} }'
[IO.File]::AppendAllText($arenaNativeFile, $arenaExtraNative)

function Invoke-Checks([string]$Source = '', [string]$AnimatorSource = '', [string]$UnitySource = '') {
    $arenaExtra = @("-p:NativeSource=$arenaNativeFile")
    if ($Source) { $arenaExtra += "-p:AirVaultSource=$Source" }
    if ($UnitySource) { $arenaExtra += "-p:AirVaultUnitySource=$UnitySource" }
    if ($AnimatorSource) { $arenaExtra += "-p:AirVaultAnimatorSource=$AnimatorSource" }
    $arenaLog = @(& dotnet build $arenaProject -t:Rebuild -c Release --nologo @arenaExtra 2>&1)
    if ($LASTEXITCODE -ne 0) { throw ($arenaLog -join [Environment]::NewLine) }
    $arenaOutput = @(& dotnet run --project $arenaProject -c Release --no-build 2>&1)
    return @{ Exit=$LASTEXITCODE; Text=($arenaOutput -join [Environment]::NewLine) }
}
function Assert-Production {
    $arenaResult = Invoke-Checks
    [IO.File]::WriteAllText((Join-Path $arenaWork 'production.log'),$arenaResult.Text)
    if ($arenaResult.Exit -ne 0 -or !$arenaResult.Text.Contains('ARENA_AIR_VAULT_CONTRACTS_PASSED')) { throw $arenaResult.Text }
    Write-Output $arenaResult.Text
}
function Test-Fault([string]$Name,[string]$Before,[string]$After,[string]$Assertion,[switch]$Animator,[switch]$Unity) {
    $arenaCandidate = if ($Unity) { $arenaUnitySource } elseif ($Animator) { $arenaAnimatorSource } else { $arenaSource }
    if (!$arenaCandidate.Contains($Before) -or ($arenaCandidate.Length-$arenaCandidate.Replace($Before,'').Length)/$Before.Length -ne 1) { throw "Missing/ambiguous fault anchor: $Name" }
    $arenaFile=Join-Path $arenaWork ($Name+'.cs')
    [IO.File]::WriteAllText($arenaFile,$arenaCandidate.Replace($Before,$After))
    $arenaResult=if ($Unity) { Invoke-Checks -UnitySource $arenaFile } elseif ($Animator) { Invoke-Checks -AnimatorSource $arenaFile } else { Invoke-Checks -Source $arenaFile }
    [IO.File]::WriteAllText((Join-Path $arenaWork ($Name+'.log')),$arenaResult.Text)
    if ($arenaResult.Exit -eq 0 -or !$arenaResult.Text.Contains('ASSERT: '+$Assertion)) { throw "Fault $Name did not fail its intended assertion: $($arenaResult.Text)" }
    Write-Output "Air vault negative control rejected: $Name"
}
try {
    Assert-Production
    if ($ProductionOnly) { return }
    Test-Fault 'scope-removed' '|| !ArenaMovementScope.IsLocal(request.Player, requireInput: true)' '' 'ordinary raid never receives airborne strategy'
    Test-Fault 'held-input-removed' 'return request.Player.IsVaultingPressed' 'return true' 'normal jump without held vault is untouched'
    Test-Fault 'geometry-removed' '&& model.VaultingStatesModel.ClimbState.CanMove() && !context.IsSprintEnabled' '&& !context.IsSprintEnabled' 'no native geometry means no mantle or jump consumption'
    Test-Fault 'stamina-removed' 'component._canClimbByStaminaGetter?.Invoke() == true' 'true' 'native climb stamina remains required'
    Test-Fault 'final-hands-check-removed' '&& request.Player.VaultingGameplayRestrictions?.CanVaulting() == true' '' 'final native geometry rechecks hands restrictions before commit'
    Test-Fault 'lease-cleanup-removed' "catch (Exception ex) { Warn(ex); }`n            finally { if (ReferenceEquals(_request, request)) _request = null; }" "catch (Exception ex) { Warn(ex); }`n            finally { }" 'strategy exception does not survive synchronous request'
    Test-Fault 'pending-parameter-refresh-removed' 'request.Component.SetParametersInAnimator();' '' 'changed valid geometry refreshes animator parameters without repeating request effects'
    Test-Fault 'world-binding-removed' '|| !ReferenceEquals(Singleton<GameWorld>.Instance, request.World)' '' 'world drift during geometry refresh rejects commit'
    Test-Fault 'game-binding-removed' '|| !ReferenceEquals(Singleton<AbstractGame>.Instance, request.Game)' '' 'game drift during geometry refresh rejects commit'
    Test-Fault 'finite-geometry-removed' 'if (request.CommittedGeometry.IsFinite) return true;' 'return true;' 'nonfinite native parameters cannot enter animator'
    Test-Fault 'animator-binding-removed' '&& ReferenceEquals(request.Context.PlayerAnimator, request.Animator)' '' 'replacement animator flag is not owned by pending request'
    Test-Fault 'animator-handle-binding-removed' '&& ReferenceEquals(request.Animator.Animator, request.AnimatorHandle)' '' 'replacement underlying animator flag is not owned by pending request'
    Test-Fault 'component-binding-removed' '&& ReferenceEquals(request.Player.VaultingComponent, request.Component)' '' 'replacement vault component cannot inherit old pending request'
    Test-Fault 'missing-native-edge' '__result = transition;' '' 'missing graph edge enters native mantle through actual ProcessState' -Animator
    Test-Fault 'native-interruption-removed' 'entry.InterruptionSource, entry.IsOrderedInterruption' 'ETransitionInterruptionSource.None, true' 'native destination interruption remains available during vault blend' -Animator
    Test-Fault 'jump-flag-not-consumed' 'request.Context.PlayerAnimatorEnableJump(false);' '' 'successful mantle consumes jump flag to prevent graph reentry' -Animator
    Test-Fault 'native-transition-overwritten' '__result != null || frame == null' 'frame == null' 'existing native transition always wins unchanged' -Animator
    Test-Fault 'unbounded-normalized-duration' 'if (!(normalizedDuration > 0f) || !Finite(normalizedDuration)) return;' '' 'overflowed normalized blend cannot enter animator' -Animator
    Test-Fault 'preentry-edge-left-queued' 'frame.Layer.Transition = null;' '' 'pre-entry native failure retires only own transition and pending flag' -Animator
    Test-Fault 'foreign-destination-context' '|| !ReferenceEquals(movement.MovementContext, frame.Pending.Request.Context)' '' 'foreign native destination context cannot receive mantle' -Animator
    Test-Fault 'native-entry-conditions-removed' 'entry == null || !entry.InternalCheckConditions()' 'entry == null' 'native entry conditions remain authoritative' -Animator
    Test-Fault 'loop-owner-support-removed' '&& SourceOwnsJump(frame)' '&& ReferenceEquals(frame.Source.GetBehavior<FastAnimationPlayerStateContainerBehaviour>()?.EncapsulatedState, frame.Pending.Request.Jump)' 'real behaviorless jump loop can mantle through verified incoming owner' -Animator
    Test-Fault 'post-parameter-hands-removed' 'return valid && ReferenceEquals(_pending, pending)' 'return ReferenceEquals(_pending, pending)' 'parameter callbacks cannot bypass final hands restriction'
    Test-Fault 'post-parameter-strategy-removed' '&& SelectGeometryStrategy(request) == pending.Strategy;' ';' 'parameter callbacks cannot bypass final stamina strategy'
    Test-Fault 'deferred-admission-cancel' '{ if (ReferenceEquals(_pending, frame.Pending)) CancelPending(); return; }' '{ return; }' 'failed animator admission cancels pending flag immediately' -Animator
    Test-Fault 'unity-managed-only-proof' '&& UnityDestinationIsActive(pending)' '' 'Unity polling cannot prove entry from managed state alone' -Unity
    Test-Fault 'unity-dispatch-missing' 'animator.CrossFade(destination.StateFullNameHash, climb ? 0.15f : 0.1f, 0, climb ? 0f : 0.1f);' '' 'Unity Auto and Manual body updates both issue one transition' -Unity
    Test-Fault 'unity-jump-cleanup-missing' 'request.Context.PlayerAnimatorEnableJump(false);' '' 'Unity committed entry consumes jump even after settings disable or release' -Unity
    Test-Fault 'unity-native-landing-overwritten' '!animator.GetBool(PlayerAnimator.LANDING_PARAM_HASH)' 'true' 'Unity native landing always wins' -Unity
    Test-Fault 'unity-foreign-destination' '|| !ReferenceEquals(movement.MovementContext, request.Context)' '' 'Unity foreign destination context cannot receive mantle' -Unity
    Test-Fault 'unity-missing-state' 'if (!request.UnityAnimator.HasState(0, hash)) return null;' '' 'Unity missing destination state cannot receive mantle' -Unity
    Test-Fault 'unity-controller-drift' '&& ReferenceEquals(request.UnityAnimator.runtimeAnimatorController, request.UnityController)' '' 'Unity controller replacement revokes pending ownership'
    Test-Fault 'unity-observer-lost-on-error' 'if (pending.Unity == null) CancelPending();' 'CancelPending();' 'Unity queue acceptance followed by exception retains observer' -Unity
    Test-Fault 'unity-rebound-movement' 'var target = pending.Unity.Movement;' 'var target = pending.Unity.Destination.EncapsulatedState;' 'Unity reinitialized container cannot impersonate captured destination movement' -Unity
    Test-Fault 'unity-timeout-retires-observer' 'pending.Unity.TimeoutReported = true;' 'pending.Unity.TimeoutReported = true; _pending = null;' 'Unity missing entry retains observer without rewriting native committed move' -Unity
}
finally { Assert-Production }
Write-Output "Air vault evidence: $arenaWork"
Write-Output 'ARENA_AIR_VAULT_CHECKS_PASSED'
