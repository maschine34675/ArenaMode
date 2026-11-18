$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$arenaEvidence = Join-Path $arenaRepo ('artifacts/movement-transitions-20261115/airfire/' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($arenaEvidence) | Out-Null
$arenaFire = Join-Path $arenaRepo 'ArenaMode.Client/Movement/ArenaAirFire.cs'
$arenaScope = Join-Path $arenaRepo 'ArenaMode.Client/Movement/ArenaMovementScope.cs'
$arenaAds = Join-Path $arenaRepo 'ArenaMode.Client/Movement/SprintAdsPatches.cs'
$arenaHashes = @{}
foreach ($path in @($arenaFire,$arenaScope,$arenaAds)) { $arenaHashes[$path] = (Get-FileHash -LiteralPath $path).Hash }
$arenaHashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $arenaEvidence 'production-sources.json')
. (Join-Path $arenaRepo 'tools/Invoke-ArenaSourceChecks.ps1')
$faults = @(
    @{Name='LostFeatureGate';Path=$arenaFire;Property='AirFireSource';Find='if (original || !ArenaSession.IsActive || !_enabled || Settings.AirHipFireEnabled?.Value != true)';Replace='if (original || !ArenaSession.IsActive || !_enabled)';Failure='air rejects feature off'},
    @{Name='LostScope';Path=$arenaFire;Property='AirFireSource';Find="if (!ArenaMovementScope.IsLocal(player, requireInput: true)`n                || !ReferenceEquals(player.HandsController, controller)";Replace='if (false || !ReferenceEquals(player.HandsController, controller)';Failure='fire rejects master off'},
    @{Name='AirAdsFire';Path=$arenaFire;Property='AirFireSource';Find='|| firearm.IsAiming) return false;';Replace=') return false;';Failure='air rejects aiming'},
    @{Name='OtherJumpType';Path=$arenaFire;Property='AirFireSource';Find='&& jump.GetType() == typeof(JumpPlayerState)';Replace='';Failure='air rejects derived state'},
    @{Name='PendingMantle';Path=$arenaFire;Property='AirFireSource';Find='&& !ArenaAirVault.HasPendingRequest(player)';Replace='';Failure='air rejects pending mantle'},
    @{Name='NativeTrueChanged';Path=$arenaFire;Property='AirFireSource';Find='if (original || !ArenaSession.IsActive';Replace='if (!ArenaSession.IsActive';Failure='native true remains unchanged: master off'},
    @{Name='AmbiguousRewrite';Path=$arenaFire;Property='AirFireSource';Find='if (matches.Length != 1)';Replace='if (matches.Length == 0)';Failure='ambiguous or missing native call fails open 2'},
    @{Name='OwnerHandover';Path=$arenaScope;Property='ScopeSource';Find='&& ReferenceEquals(game.LocalPlayer, player)';Replace='';Failure='scope rejects owner handover'},
    @{Name='ForeignBody';Path=$arenaScope;Property='ScopeSource';Find='ReferenceEquals(world.MainPlayer, player) &&';Replace='';Failure='scope rejects foreign current body'},
    @{Name='StoppedGame';Path=$arenaScope;Property='ScopeSource';Find='&& game.Status == GameStatus.Started';Replace='';Failure='scope rejects stopping'},
    @{Name='LostReadyPose';Path=$arenaFire;Property='AirFireSource';Find='return 0f;';Replace='return original;';Failure='sprint jump uses ready first-person weapon authority'},
    @{Name='PoseThirdPerson';Path=$arenaFire;Property='AirFireSource';Find='|| !player.FirstPersonPointOfView';Replace='';Failure='pose rejects third person'},
    @{Name='PoseIdleJump';Path=$arenaFire;Property='AirFireSource';Find='animator.GetCurrentAnimatorStateInfo(0).fullPathHash != SprintJumpLeaf';Replace='false';Failure='pose rejects idle jump leaf'},
    @{Name='PoseIncomingBlend';Path=$arenaFire;Property='AirFireSource';Find='animator.GetNextAnimatorStateInfo(0).fullPathHash != SprintJumpLeaf';Replace='true';Failure='native incoming sprint-jump blend keeps weapon ready'},
    @{Name='PoseStaleNext';Path=$arenaFire;Property='AirFireSource';Find='!animator.IsInTransition(0) || ';Replace='';Failure='stale destination hash does not admit pose'},
    @{Name='PoseOtherCurve';Path=$arenaFire;Property='AirFireSource';Find='|| hash != PlayerAnimator.WEAPON_ROOT_3RD';Replace='';Failure='unrelated visual curve unchanged'},
    @{Name='PoseForeignOwner';Path=$arenaFire;Property='AirFireSource';Find='!ReferenceEquals(Owner(firearm), player) || ';Replace='';Failure='pose rejects foreign firearm owner'},
    @{Name='PoseInteraction';Path=$arenaFire;Property='AirFireSource';Find='|| firearm.IsInInteractionStrictCheck()';Replace='';Failure='pose rejects interaction'},
    @{Name='PoseForeignOperation';Path=$arenaFire;Property='AirFireSource';Find='|| !ReferenceEquals(operation.Controller, firearm)';Replace='';Failure='pose rejects foreign operation'},
    @{Name='PoseReload';Path=$arenaFire;Property='AirFireSource';Find='if (operation is not Player.FirearmController.Idling';Replace='if (false';Failure='pose rejects reload'},
    @{Name='PoseLauncher';Path=$arenaFire;Property='AirFireSource';Find='|| operation is Player.FirearmController.LauncherFire';Replace='';Failure='pose rejects launcher'},
    @{Name='PoseAmbiguousRewrite';Path=$arenaFire;Property='AirFireSource';Find='if (matches.Count != 1)';Replace='if (matches.Count == 0)';Failure='pose rewrite rejects missing or ambiguous target 2'},
    @{Name='AirAdsSetter';Path=$arenaFire;Property='AirFireSource';Find='if (value && PreventAirAim(__instance)) value = false;';Replace='if (value && PreventAirAim(__instance)) value = true;';Failure='air ADS setter rejects Jump'},
    @{Name='ExistingAirAds';Path=$arenaFire;Property='AirFireSource';Find='if (__instance.IsAiming && PreventAirAim(__instance)) __instance.IsAiming = false;';Replace='if (__instance.IsAiming && PreventAirAim(__instance)) __instance.IsAiming = true;';Failure='air entry clears already-held ADS during firing'},
    @{Name='AirAdsStaleHands';Path=$arenaFire;Property='AirFireSource';Find='return ArenaMovementScope.IsLocal(player) && ReferenceEquals(player.HandsController, firearm)';Replace='return ArenaMovementScope.IsLocal(player)';Failure='stale firearm receives no aim override'},
    @{Name='AirAdsContact';Path=$arenaFire;Property='AirFireSource';Find='|| player.CurrentState?.Name == EPlayerState.Jump';Replace='';Failure='jump state during ground-contact edge still denies ADS'},
    @{Name='SprintAdsAirMotion';Path=$arenaAds;Property='SprintAdsSource';Find='&& player.MovementContext.IsGrounded';Replace='';Failure='sprint ADS rejects airborne transition'},
    @{Name='SprintAdsBodyIdentity';Path=$arenaAds;Property='SprintAdsSource';Find='return ArenaMovementScope.IsLocal(player, requireInput: true)';Replace='return player != null';Failure='sprint ADS rejects master off'},
    @{Name='SprintAdsForeignHands';Path=$arenaAds;Property='SprintAdsSource';Find='&& ReferenceEquals(player.HandsController, controller)';Replace='';Failure='sprint ADS rejects foreign hands'},
    @{Name='SprintAdsJumpContact';Path=$arenaAds;Property='SprintAdsSource';Find='&& player.CurrentState?.Name != EPlayerState.Jump';Replace='';Failure='sprint ADS rejects jump contact'},
    @{Name='SprintAdsManagedJump';Path=$arenaAds;Property='SprintAdsSource';Find='&& player.CurrentManagedState is not JumpPlayerState';Replace='';Failure='sprint ADS rejects managed jump contact'},
    @{Name='SprintAdsBypassOwner';Path=$arenaAds;Property='SprintAdsSource';Find='|| !ReferenceEquals(_bypassing, controller)';Replace='';Failure='throw releases bypass synchronously'},
    @{Name='SprintAdsBypassLeak';Path=$arenaAds;Property='SprintAdsSource';Find='_bypassing = __state;';Replace='// broken release';Failure='throw releases bypass synchronously'},
    @{Name='SprintAdsAirRequest';Path=$arenaAds;Property='SprintAdsSource';Find='if (controller != null && ArenaAirFire.PreventAirAim(controller)) return false;';Replace='';Failure='ground-to-air during bypass denies ADS'}
)
try {
    Start-Transcript -LiteralPath (Join-Path $arenaEvidence 'verification.log') | Out-Null
    Test-ArenaSourceFaults -Project (Join-Path $PSScriptRoot 'ArenaAirFireChecks.csproj') `
        -Token 'ARENA_AIR_FIRE_PRODUCTION_PASSED' -Faults $faults -FailurePrefix 'ASSERT: ' -RequireSingleSite
    foreach ($path in $arenaHashes.Keys) { if ((Get-FileHash -LiteralPath $path).Hash -ne $arenaHashes[$path]) { throw "Production changed during checks: $path" } }
    & (Join-Path $PSScriptRoot 'Verify-Installed.ps1')
    Write-Output "Evidence: $arenaEvidence"
    Write-Output 'ARENA_AIR_FIRE_CHECKS_PASSED'
}
finally { Stop-Transcript | Out-Null }
