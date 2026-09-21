using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using ShotOperation = EFT.Player.FirearmController.FireOperationBase;

namespace ArenaMode.Pickups;

/// <summary>Skip only cartridge consumption; never refill or insert inventory items.</summary>
internal static class ArenaInfiniteAmmo
{
    private static bool _attempted;
    private static int? _warnedEpoch;
    internal static bool Enabled { get; private set; }

    internal static void Enable()
    {
        if (_attempted) return;
        _attempted = true;
        try { new ShotPatch().Enable(); Enabled = true; }
        catch (Exception ex) { Warn(ex); }
    }

    private static void Warn(Exception ex)
    {
        if (_warnedEpoch == Session.ArenaSession.ArmEpoch) return;
        _warnedEpoch = Session.ArenaSession.ArmEpoch;
        Plugin.ModLogger.LogWarning("Arena infinite ammo unavailable; native ammunition use retained: " + ex.GetBaseException().Message);
    }

    private static bool Eligible(ShotOperation op)
    {
        var player = op?.Player;
        return Enabled && player != null && !player.IsAI && player.IsYourPlayer
            && ArenaPowerups.Read(player).AmmoSeconds > 0f
            && ReferenceEquals(player.HandsController, op.Controller)
            && ReferenceEquals(op.Controller?.CurrentOperation, op)
            && ReferenceEquals(op.Weapon, op.Controller.Item);
    }

    private static bool Source(ShotOperation op, out Magazine magazine, out Ammo ammo)
    {
        magazine = null; ammo = null;
        var weapon = op?.Weapon;
        if (weapon == null || weapon.IsStationaryWeapon || weapon.IsGrenadeLauncher
            || weapon.IsFlareGun || weapon.IsOneOff || weapon is RocketLauncher || weapon is Revolver
            || weapon.ReloadMode == Weapon.EReloadMode.OnlyBarrel || weapon.Chambers == null
            || weapon.Chambers.Length > 1) return false;
        magazine = weapon.GetCurrentMagazine();
        if (magazine == null || magazine is CylinderMagazine || !magazine.IsAmmoCompatible(weapon.Chambers)) return false;
        ammo = weapon.HasChambers ? weapon.Chambers[0].ContainedItem as Ammo : magazine.Cartridges.Last as Ammo;
        return ammo != null && !ammo.IsUsed && ammo.StackObjectsCount > 0
            && !ammo.GetTemplate<AmmoTemplate>().HasGrenaderComponent;
    }

    private sealed class ShotPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(ShotOperation), nameof(ShotOperation.ProcessInventoryOperationsForSingleShot));

        [PatchPrefix, HarmonyPriority(Priority.Last)]
        private static bool Prefix(ShotOperation __instance, Weapon.EMalfunctionState malfState,
            bool __runOriginal, ref ShotOperation.SingleShotData __result)
        {
            if (!__runOriginal) return true;
            try
            {
                if (malfState != Weapon.EMalfunctionState.None || !Eligible(__instance)
                    || !Source(__instance, out var magazine, out var source)
                    || !Singleton<ItemFactory>.Instantiated) return true;
                var weapon = __instance.Weapon;
                int chambers = weapon.ChamberAmmoCount, rounds = magazine.Count;
                // Native Ammo.Clone makes a detached cartridge with the SAME loaded
                // template. Only that disposable shot item is marked spent. The real
                // chamber/magazine, mixed-ammo order and item identities stay intact.
                var shot = source.Clone(Singleton<ItemFactory>.Instance.NextId);
                if (shot == null || ReferenceEquals(shot, source) || shot.CurrentAddress != null) return true;
                shot.StackObjectsCount = 1;
                shot.IsUsed = true;
                if (!Eligible(__instance) || !ReferenceEquals(__instance.Weapon, weapon)
                    || !Source(__instance, out var currentMagazine, out var currentAmmo)
                    || !ReferenceEquals(currentMagazine, magazine) || !ReferenceEquals(currentAmmo, source)
                    || weapon.ChamberAmmoCount != chambers || magazine.Count != rounds) return true;
                __result = new ShotOperation.SingleShotData
                {
                    AmmoToFire = shot,
                    AmmoWillBeLoadedToChamber = weapon.HasChambers ? source : null,
                    AmmoCountInChamberBeforeShot = weapon.HasChambers ? chambers : 1,
                    AmmoCountInChamberAfterShot = chambers,
                    AmmoCountInMagBeforeShot = rounds,
                    AmmoCountInMagAfterShot = rounds,
                    IsAmmoCompatible = true
                };
                return false;
            }
            catch (Exception ex) { Warn(ex); return true; }
        }
    }
}
