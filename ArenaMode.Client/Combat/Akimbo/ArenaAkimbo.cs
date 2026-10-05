using ArenaMode.Areas;
using ArenaMode.Config;
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
    internal static int BonusShots, BlockedShots;

    internal static void Enable()
    {
        try
        {
            Limbs = AccessTools.FieldRefAccess<Player, LimbIK[]>("_limbs");
            new PosePatch().Enable(); new VisualPatch().Enable(); new ShotPatch().Enable(); new AimPatch().Enable();
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
            && !_hands.Malfunction && _weapon.MalfState.State == Weapon.EMalfunctionState.None
            && !_hands.IsInInteractionStrictCheck()
            && !_hands.IsInReloadOperation() && !_hands.IsInSpawnOperation() && !_hands.IsInRemoveOperation()
            && (_hands.CurrentOperation is Player.FirearmController.Idling
                || _hands.CurrentOperation is Player.FirearmController.FireOperationBase)
            && !_body.IsSprintEnabled && !_body.IsInPronePose
            && !_body.MovementContext.PlayerAnimatorGetIsVaulting() && !_body.MovementContext.IsInMountedState
            && !_body.MovementContext.PlayerAnimator.AnimatedInteractions.IsInteractionPlaying
            && _body.LeftHandController?.IsUsing != true && CameraManager.Exist && CameraManager.Instance.Camera != null;

    private void Update()
    {
        AkimboOwner candidate = default;
        AkimboView pendingView = null;
        try
        {
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
                if (!Ready()) _view.Pose(Vector3.zero, false);
                return;
            }
            if (!Bindings.MayAttach(candidate)
                || body.HandsController is not Player.FirearmController hands || hands.GetType() != typeof(Player.FirearmController)
                || !Supported(hands.Item) || body.PointOfView != EPointOfView.FirstPerson
                || hands.IsInReloadOperation() || hands.IsInSpawnOperation() || hands.IsInRemoveOperation()
                || hands.IsInInteractionStrictCheck() || ArenaInputBlocker.Blocked
                || body.IsSprintEnabled || body.IsInventoryOpened || body.LeftHandController?.IsUsing == true) return;
            var limbs = Limbs(body);
            if (limbs == null || limbs.Length < 2 || limbs[0]?.solver == null)
                throw new InvalidOperationException("Left arm solver unavailable");
            pendingView = new AkimboView(hands);
            // Publish only a complete view whose exact native owner is still current.
            if (!Context(out var currentBody, out var currentMatch, out var currentWorld)
                || !candidate.Matches(Owner(currentBody, currentMatch, currentWorld)) || Bindings.Attach(candidate) == null)
                return;
            _view = pendingView; pendingView = null;
            _body = body; _hands = hands; _weapon = hands.Item;
            BonusShots = BlockedShots = 0;
            hands.IsAiming = false;
            Plugin.ModLogger.LogInfo($"Arena akimbo bound: {_weapon.TemplateId}, {_view.RendererCount} owned mesh renderers; simultaneous shots, shared native magazine/reload.");
        }
        catch (Exception ex) { Fail(ex, candidate); }
        finally { pendingView?.Dispose(); }
    }

    private static Vector3 Offset() => -CameraManager.Instance.Camera.transform.right * .30f;

    private static void Pose(Player body)
    {
        if (!ReferenceEquals(body, _body) || !Ready()) return;
        var solver = Limbs(body)[0].solver;
        // Native IkProcess overwrites these transient targets every frame. No marker,
        // animator, finger mapping or shared configuration is replaced.
        solver.SetIKPosition(solver.GetIKPosition() + Offset());
    }
    private static void Visual(Player body)
    {
        if (!ReferenceEquals(body, _body) || _view == null) return;
        bool ready = Ready();
        _view.Pose(ready ? Offset() : Vector3.zero, ready);
        if (ready) _visualFrame = Time.frameCount;
    }

    private static void Bonus(Player.FirearmController hands, IWeapon weapon, Ammo ammo,
        Vector3 shotPosition, Vector3 shotDirection, bool runOriginal)
    {
        if (_lease == null || !ReferenceEquals(hands, _hands) || !ReferenceEquals(weapon, _weapon) || !Ready()
            || !_view.Visible || _visualFrame < Time.frameCount - 1) return;
        var op = hands.CurrentOperation as Player.FirearmController.FireOperationBase;
        bool sourceValid = op != null && ReferenceEquals(op.Controller, hands) && ReferenceEquals(op.Player, _body)
            && ReferenceEquals(op.Weapon, _weapon) && ammo != null && ammo.ProjectileCount == 1
            && ammo.InitialSpeed > 0f && !float.IsInfinity(ammo.InitialSpeed) && !float.IsNaN(ammo.InitialSpeed)
            && !ammo.AmmoTemplate.HasGrenaderComponent && !ammo.AmmoTemplate.IsLightAndSoundShot
            && ammo.GetItemComponent<ExplosiveAmmoComponent>() == null
            && Singleton<ItemFactory>.Instantiated;
        if (!sourceValid) return;
        // Recompute the replica from the current pose, not an old frame's muzzle.
        _view.Pose(Offset(), true);
        _view.UseFireport(hands.CurrentFireport.Original);
        Vector3 origin = _view.Fireport.position;
        int mask = (int)LayersMaskController.HighPolyWithTerrainMask;
        bool clear = Finite(origin) && Finite(shotPosition) && Finite(shotDirection) && shotDirection.sqrMagnitude > .9f
            && Vector3.Distance(origin, shotPosition) <= .65f
            && !Physics.Linecast(shotPosition, origin, mask, QueryTriggerInteraction.Ignore)
            && !Physics.CheckSphere(origin, .025f, mask, QueryTriggerInteraction.Ignore);
        if (!clear) { BlockedShots++; return; }
        var lease = _lease;
        if (!lease.BeginShot(Current(), runOriginal, Ready(), sourceValid, clear)) return;
        try
        {
            var extra = ammo.Clone(Singleton<ItemFactory>.Instance.NextId);
            if (extra == null || ReferenceEquals(extra, ammo) || extra.CurrentAddress != null || extra.Id == ammo.Id) return;
            extra.StackObjectsCount = 1; extra.IsUsed = true;
            if (!Current() || !Ready()) return;
            // Converge toward the original ballistic ray at 50m; keep its native spread.
            Vector3 direction = (shotPosition + shotDirection.normalized * 50f - origin).normalized;
            using (ArenaShotAwardHooks.BeginFire(hands, weapon))
                hands.BallisticsCalculator.Shoot(extra, origin, direction, _body.ProfileId, weapon.Item, weapon.SpeedFactor, 0);
            BonusShots++;
            _view?.Flash();
            if (BonusShots <= 3) Plugin.ModLogger.LogInfo($"Arena akimbo bonus shot {BonusShots}: {origin}; native ballistic projectile, separate cartridge ID.");
        }
        finally { lease.EndShot(); }
    }

    private static bool Finite(Vector3 value) => !(float.IsNaN(value.x) || float.IsInfinity(value.x)
        || float.IsNaN(value.y) || float.IsInfinity(value.y) || float.IsNaN(value.z) || float.IsInfinity(value.z));

    private static void Release(string reason)
    {
        bool had = _lease != null || _view != null;
        Bindings.Release();
        _view?.Dispose(); _view = null;
        _body = null; _hands = null; _weapon = null; _visualFrame = -10;
        if (had) Plugin.ModLogger.LogInfo($"Arena akimbo stopped: {reason}; bonus shots={BonusShots}, obstructed={BlockedShots}.");
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

    private sealed class PosePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(Player), nameof(Player.IkProcess));
        [PatchPostfix] private static void Postfix(Player __instance)
        { try { Pose(__instance); } catch (Exception ex) { Fail(ex); } }
    }
    private sealed class VisualPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(Player), nameof(Player.VisualPass));
        [PatchPostfix] private static void Postfix(Player __instance)
        { try { Visual(__instance); } catch (Exception ex) { Fail(ex); } }
    }
    private sealed class ShotPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(Player.FirearmController),
            nameof(Player.FirearmController.InitiateShot), new[] { typeof(IWeapon), typeof(Ammo), typeof(Vector3), typeof(Vector3), typeof(Vector3), typeof(int), typeof(float) });
        [PatchPostfix, HarmonyPriority(Priority.Last)]
        private static void Postfix(Player.FirearmController __instance, IWeapon weapon, Ammo ammo,
            Vector3 shotPosition, Vector3 shotDirection, bool __runOriginal)
        { try { Bonus(__instance, weapon, ammo, shotPosition, shotDirection, __runOriginal); } catch (Exception ex) { Fail(ex); } }
    }
    private sealed class AimPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.PropertySetter(typeof(Player.FirearmController), nameof(Player.FirearmController.IsAiming));
        [PatchPrefix] private static void Prefix(Player.FirearmController __instance, ref bool value)
        {
            try { if (value && ReferenceEquals(__instance, _hands) && Current()) value = false; }
            catch (Exception ex) { Fail(ex); }
        }
    }
}
