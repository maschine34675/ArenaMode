#nullable disable
using System;
using ArenaMode.Combat;
using Comfort.Common;
using EFT;
using EFT.AssetsManager;
using EFT.InventoryLogic;
using HarmonyLib;

namespace ArenaMode.Combat.Translocator;

/// <summary>Lease only the native rangefinder's hand-pose flag, never its ADS state or optics.</summary>
internal static class ArenaTranslocatorPose
{
    private sealed class Lease
    {
        internal Player Player;
        internal PortableRangeFinderController Hands;
        internal Item Item;
        internal WeaponPrefab Prefab;
        internal FirearmsAnimator Firearms;
        internal IAnimator Animator;
        internal bool Original, Changed;
    }

    private static readonly Harmony Patches = new(Plugin.PluginGuid + ".TranslocatorPose");
    private static Lease _lease;
    private static bool _attempted, _ready, _stopped, _warned;

    internal static void Tick()
    {
        if (_stopped) return;
        try
        {
            var player = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance?.MainPlayer : null;
            if (!TryOwner(player, out var hands, out var prefab, out var firearms, out var animator))
            { Reset(); return; }
            if (_lease != null && (!ReferenceEquals(_lease.Player, player) || !ReferenceEquals(_lease.Hands, hands)
                || !ReferenceEquals(_lease.Item, hands.Item) || !ReferenceEquals(_lease.Prefab, prefab)
                || !ReferenceEquals(_lease.Firearms, firearms) || !ReferenceEquals(_lease.Animator, animator))) Reset();
            if (_lease != null) return; // No per-frame animation or left-hand reset.
            if (!EnsureHooks()) return; // Pool restoration is a prerequisite, not optional cleanup.

            var lease = new Lease { Player = player, Hands = hands, Item = hands.Item, Prefab = prefab,
                Firearms = firearms, Animator = animator, Original = AnimationControllerParametersTable.GetBoolFire(animator) };
            _lease = lease; // Own a partial native setter before it can throw.
            if (lease.Original) return;
            lease.Changed = true;
            // Native UsableItemController.Idling.SetAiming splits this animation
            // from IsAiming. The latter owns PWA/slowdown/stamina; rangefinder's
            // override separately enables its optics. Neither path is invoked.
            firearms.SetFire(true);
        }
        catch (Exception error) { Reset(); Warn(error); }
    }

    private static bool TryOwner(Player player, out PortableRangeFinderController hands, out WeaponPrefab prefab,
        out FirearmsAnimator firearms, out IAnimator animator)
    {
        hands = null; prefab = null; firearms = null; animator = null;
        if (!ArenaTranslocatorRuntime.IsLivePlayer(player) || !ArenaTranslocatorSupply.HasVerifiedTemplate
            || player == null || !(player.HandsController is PortableRangeFinderController current)
            || !ArenaTranslocatorSupply.Owns(player, current.Item)
            || WeaponSwapService.IsSwapping || player.InventoryController?.IsChangingWeapon != false
            || EFTHardSettings.Instance == null || player.CurrentState == null
            || !EFTHardSettings.Instance.CanAimInState(player.CurrentState.Name)
            || !(current._overlap <= EFTHardSettings.Instance.STOP_AIMING_AT)) return false;
        firearms = current.FirearmsAnimator;
        if (firearms == null || !ReferenceEquals(player.HandsAnimator, firearms)
            || !firearms.HasParameter(AnimationControllerParametersTable.BOOL_FIRE)) return false;
        animator = firearms.Animator;
        if (animator == null) return false;
        if (!CanOperate(current)) return false;
        prefab = Player.UsableItemController.GetWeaponPrefab(current);
        if (prefab == null || prefab.IsInPool || prefab.IsStub || !prefab.gameObject.activeInHierarchy
            || !ReferenceEquals(prefab.FirearmsAnimator, firearms)) return false;
        hands = current;
        return true;
    }

    // CanInteract additionally requires an animator state named IDLE. Native
    // rangefinder Fire raises the hand out of that state while the controller
    // remains in its idle operation. Gate the real operation/interaction instead.
    internal static bool CanOperate(Player.UsableItemController hands)
    {
        if (!(hands is PortableRangeFinderController current) || current.IsAiming
            || current.CurrentHandsOperation?.GetType() != typeof(PortableRangeFinderController.PortableRangeFinderIdling)
            || current.CurrentHandsOperation.State != Player.EOperationState.Executing
            || current._player?.InventoryController?.HasAnyHandsAction() != false) return false;
        var firearms = current.FirearmsAnimator;
        var animator = firearms?.Animator;
        return animator != null && firearms.HasParameter(AnimationControllerParametersTable.BOOL_ACTIVE)
            && AnimationControllerParametersTable.GetBoolActive(animator)
            && ReadyPhase(animator.GetCurrentAnimatorStateInfo(1), firearms)
            && (!animator.IsInTransition(1) || ReadyPhase(animator.GetNextAnimatorStateInfo(1), firearms))
            && !firearms.IsInInventory && !firearms.IsInInteraction && !current.IsInInteractionStrictCheck();
    }

    // Installed rangefinder graph: Fire selects Hands.AIMING, not FIRE. LOOK,
    // CLOSE INVENTORY and draw/hide tails can retain the same controller operation.
    private static bool ReadyPhase(AnimatorStateInfoWrapper state, FirearmsAnimator firearms)
        => state.IsName(firearms.FullIdleStateName) || state.IsName("Hands.AIMING");

    // On-demand inspection only; no frame logging and no mutation. Normal raids
    // and ordinary rangefinders expose no device/body diagnostic information.
    internal static string Diagnostic
    {
        get
        {
            try
            {
                var player = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance?.MainPlayer : null;
                if (!ArenaTranslocatorRuntime.IsLivePlayer(player) || !ArenaTranslocatorSupply.HasVerifiedTemplate
                    || !(player?.HandsController is PortableRangeFinderController hands)
                    || !ArenaTranslocatorSupply.Owns(player, hands.Item)) return "inactive";
                var firearms = hands.FirearmsAnimator;
                var animator = firearms?.Animator;
                if (animator == null) return "private; animator=unavailable";
                var phase = animator.GetCurrentAnimatorStateInfo(1);
                var next = animator.GetNextAnimatorStateInfo(1);
                return $"private; ready={CanOperate(hands)}; lease={_lease != null}; operation={hands.CurrentHandsOperation?.GetType().Name}/{hands.CurrentHandsOperation?.State}; "
                    + $"active={AnimationControllerParametersTable.GetBoolActive(animator)}; fire={AnimationControllerParametersTable.GetBoolFire(animator)}; ads={hands.IsAiming}; "
                    + $"canInteract={hands.CanInteract()}; strict={hands.IsInInteractionStrictCheck()}; inventory={firearms.IsInInventory}; leftHand={firearms.IsInInteraction}; handsAction={player.InventoryController?.HasAnyHandsAction()}; "
                    + $"nativeIdle={firearms.IsIdling()}; phase={phase.fullPathHash}/{phase.shortNameHash}; next={next.fullPathHash}/{next.shortNameHash}; transition={animator.IsInTransition(1)}; "
                    + $"idleName={phase.IsName(firearms.FullIdleStateName)}; aimingName={phase.IsName("Hands.AIMING")}; canAim={EFTHardSettings.Instance?.CanAimInState(player.CurrentState.Name)}; overlap={hands._overlap}";
            }
            catch (Exception error) { return "private diagnostic unavailable: " + error.GetType().Name; }
        }
    }

    internal static void Reset()
    {
        var lease = _lease;
        _lease = null;
        if (lease == null || !lease.Changed) return;
        try
        {
            // Never touch a rebound animator, a foreign ADS owner, or a native
            // flag that has already changed. Direct parameter restoration avoids
            // SetFire's ResetLeftHand side effect during a new interaction.
            if (lease.Prefab != null && ReferenceEquals(lease.Prefab.FirearmsAnimator, lease.Firearms)
                && ReferenceEquals(lease.Firearms.Animator, lease.Animator) && !lease.Hands.IsAiming
                && AnimationControllerParametersTable.GetBoolFire(lease.Animator))
                AnimationControllerParametersTable.SetFire(lease.Animator, lease.Original);
        }
        catch (Exception error) { Warn(error); }
    }

    internal static void Shutdown()
    {
        _stopped = true;
        Reset();
        if (_attempted) Patches.UnpatchSelf();
        _ready = false;
    }

    private static bool EnsureHooks()
    {
        if (_attempted) return _ready;
        _attempted = true;
        try
        {
            // WeaponPrefab clears its animator before calling the base pool method.
            // Restore at the override entry while the exact instance is still bound.
            var put = AccessTools.Method(typeof(WeaponPrefab), nameof(AssetPoolObject.ReturnToPool), Type.EmptyTypes);
            var get = AccessTools.Method(typeof(AssetPoolObject), nameof(AssetPoolObject.OnGetFromPool), Type.EmptyTypes);
            if (put == null || get == null) throw new MissingMethodException("native device pool lifecycle unavailable");
            var prefix = new HarmonyMethod(typeof(ArenaTranslocatorPose), nameof(BeforePoolTransition));
            Patches.Patch(put, prefix: prefix); Patches.Patch(get, prefix: prefix);
            return _ready = true;
        }
        catch (Exception error) { Patches.UnpatchSelf(); Warn(error); return false; }
    }

    private static void BeforePoolTransition(AssetPoolObject __instance)
    {
        if (_lease != null && ReferenceEquals(_lease.Prefab, __instance)) Reset();
    }

    private static void Warn(Exception error)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning("Arena Translocator hand pose unavailable: " + error.GetBaseException().Message);
    }
}
