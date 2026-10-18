using ArenaMode.Areas;
using ArenaMode.Config;
using ArenaMode.Effects;
using ArenaMode.Hud;
using ArenaMode.Modes;
using ArenaMode.Respawn;
using ArenaMode.Session;
using ArenaMode.Spectator;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using EFT.InventoryLogic;
using HarmonyLib;
using RootMotion.FinalIK;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ArenaMode.Combat.Akimbo;

/// <summary>Local pistol mutator; the native firearm remains the sole hands/inventory owner.</summary>
internal sealed class ArenaAkimbo : MonoBehaviour
{
    private static readonly AkimboBindings Bindings = new();
    private static AkimboLease _lease => Bindings.Lease;
    private static AkimboView _view;
    private static Player _body;
    private static Player.FirearmController _hands;
    private static Weapon _weapon;
    private static bool _available;
    private static int _visualFrame = -10;
    private static AccessTools.FieldRef<Player, LimbIK[]> Limbs;
    internal static int RightShots, LeftShots, BlockedShots;
    private sealed class ShotScope
    {
        internal AkimboLease Lease;
        internal Player.FirearmController Hands;
        internal Firearms Firearms;
        internal bool Left;
        internal bool Free;
        internal ShotScope Previous;
    }
    private static ShotScope _shot;
    private static readonly ConditionalWeakTable<Weapon, AkimboPairCredit> Credits = new();
    private static readonly ConditionalWeakTable<Ammo, AkimboPairCredit> FreeCartridges = new();

    internal static void Enable()
    {
        try
        {
            Limbs = AccessTools.FieldRefAccess<Player, LimbIK[]>("_limbs");
            new ArenaAkimboPosePatch().Enable(); new ArenaAkimboVisualPatch().Enable(); new ArenaAkimboShotPatch().Enable(); new ArenaAkimboAimPatch().Enable();
            new ArenaAkimboMuzzlePatch().Enable(); new ArenaAkimboAfterFireEventsPatch().Enable();
            AkimboAmmo.Enable();
            AkimboCadence.Enable();
            _available = true;
        }
        catch (Exception ex) { Plugin.ModLogger.LogWarning("Arena akimbo unavailable: " + ex.GetBaseException().Message); }
    }

    private static bool Context(out Player body, out ArenaMatch match, out GameWorld world)
    {
        body = null; match = ArenaMatchService.Current;
        world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        if (!ArenaSession.IsActive || !_available || match?.Mode == null || match.Mode.IsComplete
            || world == null || !Singleton<AbstractGame>.Instantiated || ArenaMatchService.TeamStartAbortPending
            || ArenaAreaRuntime.IsPending || ArenaRespawnDriver.StandDown || ArenaRespawnDriver.IsRespawnInProgress
            || ArenaSpectatorService.IsActive) return false;
        var game = Singleton<AbstractGame>.Instance as LocalGame;
        body = world.MainPlayer;
        return game != null && game.Status == GameStatus.Started && ArenaSession.IsExactActiveRaid(game, world)
            && body != null && !body.IsAI && body.IsYourPlayer && ReferenceEquals(game.LocalPlayer, body)
            && ReferenceEquals(world.GetAlivePlayerByProfileID(body.ProfileId), body)
            && body.HealthController?.IsAlive == true && (match.Mode switch
            {
                RoundsMode dm => dm.AssistScoringOpen,
                GunGameMode gg => gg.AssistScoringOpen,
                TacticalMode tactical => tactical.AssistScoringOpen,
                TeamDeathmatchMode tdm => tdm.AssistScoringOpen,
                _ => false
            });
    }
    private static int Round(ArenaMatch match) => match.Mode switch
    { RoundsMode dm => dm.Round.Number, TacticalMode tactical => tactical.RoundNumber, _ => 1 };

    private static AkimboOwner Owner(Player body, ArenaMatch match, GameWorld world)
    {
        var hands = body.HandsController as Player.FirearmController;
        return new AkimboOwner(body, hands, hands?.Item, body.HealthController, world, match,
            hands?.HandsHierarchy?.GetTransform(ECharacterWeaponBones.Weapon_root), hands?.Item?.GetCurrentMagazine(),
            ArenaSession.ArmEpoch, Round(match));
    }
    private static bool Supported(Weapon weapon) => weapon != null && AkimboEligibility.Supports(
        weapon is Pistol || weapon is Revolver, weapon.GetTemplate<WeaponTemplate>().weapClass,
        weapon.IsFlareGun, weapon.IsGrenadeLauncher);

    private static bool Current()
        => Settings.AkimboEnabled?.Value == true && _lease != null && Context(out var body, out var match, out var world)
            && _lease.Current(Owner(body, match, world), true)
            && _view?.Matches(_hands) == true;

    private static bool Ready()
        => Current() && _body.PointOfView == EPointOfView.FirstPerson && !_body.IsInventoryOpened
            && !ArenaInputBlocker.Blocked && !_hands.IsAiming && !_hands.Blindfire && !_hands.InventoryOpened
            // The controller's Malfunction flag also ends ordinary empty/chamber cycles
            // (including revolvers) and can remain true in Idle. Only the actual weapon
            // fault state is authoritative; native operation guards still hide repairs.
            && _weapon.MalfState.State == Weapon.EMalfunctionState.None
            && !_hands.IsInInteractionStrictCheck()
            && !_hands.IsInReloadOperation() && !_hands.IsInSpawnOperation() && !_hands.IsInRemoveOperation()
            && (_hands.CurrentOperation is Player.FirearmController.Idling
                || _hands.CurrentOperation is Player.FirearmController.FireOperationBase)
            && !_body.IsSprintEnabled && !_body.IsInPronePose
            && !_body.MovementContext.PlayerAnimatorGetIsVaulting() && !_body.MovementContext.IsInMountedState
            && !_body.MovementContext.PlayerAnimator.AnimatedInteractions.IsInteractionPlaying
            && _body.LeftHandController?.IsUsing != true && CameraManager.Exist && CameraManager.Instance.Camera != null;

    internal static bool FreeShot(Player.FirearmController.FireOperationBase op)
    {
        try { return op != null && Ready() && !_lease.NextLeft && _view.Visible && _visualFrame >= Time.frameCount - 1
            && ReferenceEquals(op.Controller, _hands) && ReferenceEquals(op.Player, _body)
            && ReferenceEquals(op.Weapon, _weapon) && ReferenceEquals(_hands.CurrentOperation, op); }
        catch (Exception ex) { Fail(ex); return false; }
    }

    internal static void AmmoFailed(Exception ex) => Fail(ex);
    internal static bool OwnsHands(Player.FirearmController hands) => hands != null && ReferenceEquals(hands, _hands);
    internal static IAnimator OwnedAnimator => _hands?.FirearmsAnimator?.Animator;

    internal static bool TryCadence(out Player body, out Player.FirearmController.FireOperationBase operation)
    {
        body = _body; operation = _hands?.CurrentOperation as Player.FirearmController.FireOperationBase;
        return operation != null && Ready() && _view.Visible
            && operation.State == Player.EOperationState.Executing
            && ReferenceEquals(operation.Player, body) && ReferenceEquals(operation.Controller, _hands)
            && ReferenceEquals(operation.Weapon, _weapon)
            && (operation.GetType() == typeof(Player.FirearmController.FireOperation)
                || operation.GetType() == typeof(Player.FirearmController.FireCylinderMagOperation)
                || operation.GetType() == typeof(Player.FirearmController.AutomaticFireOperation));
    }

    internal static bool ReserveFreeShot(Player.FirearmController.FireOperationBase op, Ammo shot)
    {
        if (shot == null || !FreeShot(op) || !_lease.Credit.ReserveFree()) return false;
        FreeCartridges.Add(shot, _lease.Credit);
        return true;
    }

    private void Update()
    {
        AkimboOwner candidate = default;
        AkimboView pendingView = null;
        try
        {
            AkimboCadence.Refresh();
            if (Settings.AkimboEnabled?.Value != true)
            { Reset("disabled"); return; }
            if (!Context(out var body, out var match, out var world))
            {
                Release("outside live round");
                // Preserve a failed owner through temporary busy/respawn transitions,
                // but never retain scene bodies after the Arena session has ended.
                if (!ArenaSession.IsActive) Bindings.Clear();
                return;
            }
            candidate = Owner(body, match, world);
            if (_lease != null && !_lease.Current(candidate, true)) Release("owner changed");
            if (_lease != null)
            {
                if (_view?.Matches(_hands) != true) throw new InvalidOperationException("Owned pistol view changed");
                if (!Ready()) _view.Hide();
                return;
            }
            if (!Bindings.MayAttach(candidate)
                || body.HandsController is not Player.FirearmController hands || hands.GetType() != typeof(Player.FirearmController)
                || !Supported(hands.Item) || body.PointOfView != EPointOfView.FirstPerson
                || hands.IsInReloadOperation() || hands.IsInSpawnOperation() || hands.IsInRemoveOperation()
                || hands.IsInInteractionStrictCheck() || ArenaInputBlocker.Blocked
                || hands.CurrentOperation is not Player.FirearmController.Idling
                || body.IsSprintEnabled || body.IsInventoryOpened || body.LeftHandController?.IsUsing == true) return;
            var limbs = Limbs(body);
            if (limbs == null || limbs.Length < 2 || limbs[0]?.solver == null || limbs[1]?.solver == null)
                throw new InvalidOperationException("Arm solver unavailable");
            pendingView = new AkimboView(hands);
            // Publish only a complete view whose exact native owner is still current.
            if (!Context(out var currentBody, out var currentMatch, out var currentWorld)
                || !candidate.Matches(Owner(currentBody, currentMatch, currentWorld))
                || Bindings.Attach(candidate, Credits.GetValue(hands.Item, _ => new AkimboPairCredit())) == null)
                return;
            _view = pendingView; pendingView = null;
            _body = body; _hands = hands; _weapon = hands.Item;
            RightShots = LeftShots = BlockedShots = 0;
            hands.IsAiming = false;
            Plugin.ModLogger.LogDebug($"Arena akimbo bound: {_weapon.TemplateId}, {_view.RendererCount} owned mesh renderers; independent pistol visuals, 2x firing cadence; alternating single shots, two shots per loaded cartridge.");
        }
        catch (Exception ex) { Fail(ex, candidate); }
        finally { pendingView?.Dispose(); }
    }

    private static Vector3 Offset() => -CameraManager.Instance.Camera.transform.right * .30f;

    private static void Pose(Player body)
    {
        if (!ReferenceEquals(body, _body) || !Ready()) return;
        _view.BeginIk();
        Visual(body);
        var limbs = Limbs(body);
        for (int i = 0; i < 2; i++)
        {
            var solver = limbs[i].solver;
            var position = solver.GetIKPosition(); var rotation = solver.GetIKRotation();
            _view.Grip(i, ref position, ref rotation, _hands.CurrentOperation is Player.FirearmController.FireOperationBase);
            // Native IkProcess overwrites these transient targets every frame.
            solver.SetIKPosition(position); solver.SetIKRotation(rotation);
        }
    }
    private static void Visual(Player body, bool followSolvedHands = false)
    {
        if (!ReferenceEquals(body, _body) || _view == null) return;
        bool ready = Ready();
        _view.Pose(ready ? CameraManager.Instance.Camera.transform : null, ready ? Offset() : Vector3.zero,
            ready, _hands?.CurrentOperation is Player.FirearmController.FireOperationBase);
        if (ready && followSolvedHands && _view.Visible)
        {
            var limbs = Limbs(body);
            for (int i = 0; i < 2; i++) _view.FollowHand(i, limbs[i].solver.bone3.transform);
            // Native WeaponDirection includes the pistol's own shot-axis/zeroing;
            // transform.forward would be wrong on some pistol/revolver models.
            // Match Shot's FOV/back-offset correction, without random spread.
            _view.UseFireport(_hands.CurrentFireport.Original);
            var aim = AkimboNativeAim.Read(body, _hands);
            bool teamColor = Settings.TeamLasers?.Value == true && (ArenaMatchService.Current.Mode is TeamDeathmatchMode
                || ArenaMatchService.Current.Mode is TacticalMode tactical && tactical.IsTeamArena);
            Color color = teamColor
                ? ArenaTracerPalette.Resolve(Settings.FriendlyTracerColor?.Value ?? ArenaTracerColor.Cyan)
                : new Color(.2f, 1f, .16f, 1f);
            _view.Aim(CameraManager.Instance.Camera.transform.position, aim.Origin, aim.Direction, aim.Muzzle, aim.Body, color);
        }
        if (ready && _view.Visible) _visualFrame = Time.frameCount;
    }

    private static ShotScope BeforeShot(Player.FirearmController hands, IWeapon weapon, Ammo ammo,
        ref Vector3 shotPosition, ref Vector3 shotDirection, ref Vector3 fireportPosition, bool runOriginal)
    {
        if (_lease == null || !ReferenceEquals(hands, _hands) || !ReferenceEquals(weapon, _weapon) || !Ready()
            || _shot != null || !_view.Visible || _visualFrame < Time.frameCount - 1) return null;
        var op = hands.CurrentOperation as Player.FirearmController.FireOperationBase;
        bool sourceValid = op != null && ReferenceEquals(op.Controller, hands) && ReferenceEquals(op.Player, _body)
            && ReferenceEquals(op.Weapon, _weapon) && ammo != null && ammo.ProjectileCount == 1
            && ammo.InitialSpeed > 0f && !float.IsInfinity(ammo.InitialSpeed) && !float.IsNaN(ammo.InitialSpeed)
            && !ammo.AmmoTemplate.HasGrenaderComponent && !ammo.AmmoTemplate.IsLightAndSoundShot
            && ammo.GetItemComponent<ExplosiveAmmoComponent>() == null
            && hands.Firearms != null;
        if (!sourceValid) return null;
        var lease = _lease;
        bool free = FreeCartridges.TryGetValue(ammo, out var credit) && ReferenceEquals(credit, lease.Credit);
        bool left = !free && lease.NextLeft;
        Vector3 origin = shotPosition, direction = shotDirection, visualOrigin = fireportPosition;
        if (left)
        {
            // Do not sample the new native fire-event pose before its admitted
            // side is confirmed: that would animate the preceding gun as well.
            // The frame freshness guard above already validates the visual.
            _view.UseFireport(hands.CurrentFireport.Original);
            // Preserve native AdjustShotVectors' FOV/back-offset correction. Shift
            // that corrected origin by the difference between the two visual muzzles.
            visualOrigin = _view.Fireport.position;
            bool clear = AkimboShotGeometry.TryLeft(CameraManager.Instance.Camera.transform.position,
                shotPosition, shotDirection, fireportPosition, visualOrigin, out origin, out direction);
            if (!clear) { BlockedShots++; left = false; } // Native right shot remains safe/unchanged.
        }
        if (!lease.BeginShot(Current(), runOriginal, Ready(), sourceValid, true)) return null;
        var scope = new ShotScope { Lease = lease, Hands = hands, Firearms = hands.Firearms, Left = left, Free = free };
        _shot = scope;
        if (left) { shotPosition = origin; shotDirection = direction; fireportPosition = visualOrigin; }
        return scope;
    }

    private static void AfterShot(ShotScope scope, bool runOriginal)
    {
        if (scope == null || !ReferenceEquals(_shot, scope) || !runOriginal) return;
        scope.Lease.CompleteShot(true, scope.Free);
        if (scope.Left) LeftShots++; else RightShots++;
        if (ReferenceEquals(scope.Lease, _lease) && Ready())
        {
            _view.Shot(scope.Left);
        }
        if (ArenaMode.Diagnostics.ArenaLog.DeveloperEnabled && LeftShots + RightShots <= 8)
            ArenaMode.Diagnostics.ArenaLog.Developer($"Arena akimbo single shot: {(scope.Left ? "left" : "right")}; right={RightShots}, left={LeftShots}; one projectile, two shots per loaded cartridge.");
    }

    private static void EndShot(ShotScope scope)
    {
        if (scope == null) return;
        scope.Lease?.EndShot();
        if (ReferenceEquals(_shot, scope)) _shot = scope.Previous;
    }

    private static void Release(string reason)
    {
        AkimboCadence.Reset();
        bool had = _lease != null || _view != null;
        _shot = null;
        Bindings.Release();
        _view?.Dispose(); _view = null;
        _body = null; _hands = null; _weapon = null; _visualFrame = -10;
        if (had) Plugin.ModLogger.LogDebug($"Arena akimbo stopped: {reason}; right={RightShots}, left={LeftShots}, obstructed={BlockedShots}.");
    }
    internal static void Reset(string reason = "shutdown")
    {
        Release(reason);
        Bindings.Clear();
    }
    private static void Fail(Exception ex, AkimboOwner candidate = default)
    {
        if (!candidate.Valid && _lease != null) candidate = _lease.Owner;
        if (candidate.Valid) Bindings.Reject(candidate);
        else _available = false; // Unknown/global failure: one warning, never a per-frame retry loop.
        Plugin.ModLogger.LogWarning("Arena akimbo suspended for this owner: " + ex.GetBaseException().Message);
        Release("failed closed"); // Saved mutator preference is never changed by runtime cleanup.
    }
    private void OnDestroy() => Reset();

    private sealed class ArenaAkimboPosePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(Player), nameof(Player.IkProcess));
        [PatchPostfix] private static void Postfix(Player __instance)
        { try { Pose(__instance); } catch (Exception ex) { Fail(ex); } }
    }
    private sealed class ArenaAkimboVisualPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(Player), nameof(Player.VisualPass));
        [PatchPostfix] private static void Postfix(Player __instance)
        { try { Visual(__instance, true); } catch (Exception ex) { Fail(ex); } }
    }
    private sealed class ArenaAkimboAfterFireEventsPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(Player), nameof(Player.ArmsLateUpdate));
        [PatchPostfix] private static void Postfix(Player __instance)
        {
            try
            {
                if (!ReferenceEquals(__instance, _body) || !Ready()) return;
                _view.CaptureAfterFireEvents(_hands.CurrentOperation is Player.FirearmController.FireOperationBase);
                Visual(__instance, true);
            }
            catch (Exception ex) { Fail(ex); }
        }
    }
    private sealed class ArenaAkimboShotPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(Player.FirearmController),
            nameof(Player.FirearmController.InitiateShot), new[] { typeof(IWeapon), typeof(Ammo), typeof(Vector3), typeof(Vector3), typeof(Vector3), typeof(int), typeof(float) });
        [PatchPrefix, HarmonyPriority(Priority.Last)]
        private static void Prefix(Player.FirearmController __instance, IWeapon weapon, Ammo ammo,
            ref Vector3 shotPosition, ref Vector3 shotDirection, ref Vector3 fireportPosition,
            bool __runOriginal, out ShotScope __state)
        {
            var previous = _shot;
            if (previous == null && !ReferenceEquals(__instance, _hands)) { __state = null; return; }
            __state = new ShotScope { Previous = previous };
            // Mask an outer left effect scope even when this invocation is rejected.
            // The lease independently rejects nested shots of the same controller.
            _shot = null;
            try
            {
                var admitted = BeforeShot(__instance, weapon, ammo, ref shotPosition, ref shotDirection, ref fireportPosition, __runOriginal);
                if (admitted != null) { admitted.Previous = previous; __state = admitted; }
                _shot = __state;
            }
            catch (Exception ex) { Fail(ex); }
        }
        [PatchPostfix]
        private static void Postfix(ShotScope __state, bool __runOriginal)
        { try { if (__state?.Lease != null) AfterShot(__state, __runOriginal); } catch (Exception ex) { Fail(ex); } }
        [PatchFinalizer]
        private static Exception Finalizer(Exception __exception, ShotScope __state)
        { EndShot(__state); if (__exception != null && __state?.Lease != null) Fail(__exception, __state.Lease.Owner); return __exception; }
    }
    private sealed class ArenaAkimboMuzzlePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(Firearms), nameof(Firearms.PlayShotEffects));
        [PatchPrefix] private static bool Prefix(Firearms __instance)
        {
            try { return _shot == null || _shot.Lease == null || !ReferenceEquals(_shot.Firearms, __instance)
                || !ReferenceEquals(_shot.Hands, _hands) || !ReferenceEquals(_shot.Lease, _lease) || !Current(); }
            catch (Exception ex) { Fail(ex); return true; }
        }
    }
    private sealed class ArenaAkimboAimPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.PropertySetter(typeof(Player.FirearmController), nameof(Player.FirearmController.IsAiming));
        [PatchPrefix] private static void Prefix(Player.FirearmController __instance, ref bool value)
        {
            try { if (value && ReferenceEquals(__instance, _hands) && Current()) value = false; }
            catch (Exception ex) { Fail(ex); }
        }
    }
}
