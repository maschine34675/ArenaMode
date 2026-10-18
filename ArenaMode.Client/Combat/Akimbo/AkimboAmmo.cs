using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using CylinderOp = EFT.Player.FirearmController.FireCylinderMagOperation;

namespace ArenaMode.Combat.Akimbo;

/// <summary>Magazine pistols use ArenaInfiniteAmmo's existing single-shot data seam.
/// Cylinder pistols have a separate native preparation/visual completion path.</summary>
internal static class AkimboAmmo
{
    private sealed class Receipt
    {
        internal Ammo Shot;
        internal Firearms Firearms;
        internal int Camora;
        internal Weapon.EFireMode Mode;
    }
    private static readonly ConditionalWeakTable<CylinderOp, Receipt> Prepared = new();
    private static Receipt _firing;

    internal static void Enable()
    {
        new ArenaAkimboAmmoPreparePatch().Enable(); new ArenaAkimboAmmoFirePatch().Enable(); new ArenaAkimboAmmoShellPatch().Enable(); new ArenaAkimboAmmoResetPatch().Enable();
        new ArenaAkimboAmmoChamberPatch().Enable();
    }

    private sealed class ArenaAkimboAmmoPreparePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.DeclaredMethod(typeof(CylinderOp), nameof(CylinderOp.PrepareShot));
        [PatchPrefix, HarmonyPriority(Priority.Last)]
        private static bool Prefix(CylinderOp __instance, bool __runOriginal)
        {
            try { return Prepare(__instance, __runOriginal); }
            catch (Exception ex)
            {
                // No native cartridge has been changed. Native PrepareShot rebuilds
                // SingleShotData from scratch. Retain any reserved credit on failure.
                Prepared.Remove(__instance);
                ArenaAkimbo.AmmoFailed(ex);
                return true;
            }
        }

        private static bool Prepare(CylinderOp op, bool runOriginal)
        {
            Prepared.Remove(op);
            if (!runOriginal || !ArenaAkimbo.FreeShot(op) || !Singleton<ItemFactory>.Instantiated) return true;
            var cylinder = op._cylinderMagazine;
            var weapon = op.Weapon;
            var source = cylinder?.GetFirstAmmo(!weapon.CylinderHammerClosed);
            if (source == null || source.IsUsed || source.StackObjectsCount != 1
                || op._camoraIndex < 0 || op._camoraIndex >= cylinder.Camoras.Length
                || !ReferenceEquals(cylinder.Camoras[op._camoraIndex].ContainedItem, source)) return true;
            var clone = source.Clone(Singleton<ItemFactory>.Instance.NextId);
            if (clone == null || ReferenceEquals(clone, source) || clone.CurrentAddress != null || clone.Id == source.Id
                || !ArenaAkimbo.FreeShot(op) || !ReferenceEquals(op.Weapon, weapon)
                || !ReferenceEquals(weapon.GetCurrentMagazine(), cylinder)
                || !ReferenceEquals(cylinder.GetFirstAmmo(!weapon.CylinderHammerClosed), source)) return true;
            clone.StackObjectsCount = 1; clone.IsUsed = true;
            if (!ArenaAkimbo.ReserveFreeShot(op, clone)) return true;
            op._singleShotData = new Player.FirearmController.FireOperationBase.SingleShotData { AmmoToFire = clone };
            Prepared.Add(op, new Receipt { Shot = clone, Firearms = op.Controller.Firearms,
                Camora = op._camoraIndex, Mode = weapon.SelectedFireMode });
            // Native firing completion stays intact. Do not consume, mark spent,
            // advance the cylinder or add a shell for this free half of the pair.
            op.FirearmsAnimator.SetCamoraIndex(cylinder.CurrentCamoraIndex);
            op.FirearmsAnimator.SetFire(op.Controller.IsTriggerPressed);
            op.ShowIncompatibleNotification();
            op.Player.MouseLook();
            return false;
        }
    }
    private sealed class ArenaAkimboAmmoFirePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.DeclaredMethod(typeof(CylinderOp), nameof(CylinderOp.OnFireEvent));
        [PatchPrefix] private static void Prefix(CylinderOp __instance, out Receipt __state)
        {
            __state = _firing;
            _firing = Prepared.TryGetValue(__instance, out var receipt)
                && ReferenceEquals(receipt.Shot, __instance._singleShotData.AmmoToFire)
                && ReferenceEquals(receipt.Firearms, __instance.Controller.Firearms)
                && receipt.Camora == __instance._camoraIndex ? receipt : null;
        }
        [PatchFinalizer] private static Exception Finalizer(CylinderOp __instance, Receipt __state, Exception __exception)
        { _firing = __state; if (__exception != null) Prepared.Remove(__instance); return __exception; }
    }
    private sealed class ArenaAkimboAmmoShellPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(Firearms), nameof(Firearms.MoveAmmoFromChamberToShellPort));
        [PatchPrefix] private static bool Prefix(Firearms __instance, int chamberIndex)
            => _firing == null || !ReferenceEquals(_firing.Firearms, __instance) || _firing.Camora != chamberIndex;
    }
    private sealed class ArenaAkimboAmmoResetPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.DeclaredMethod(typeof(CylinderOp), nameof(CylinderOp.Reset));
        [PatchPrefix] private static void Prefix(CylinderOp __instance) => Prepared.Remove(__instance);
    }
    private sealed class ArenaAkimboAmmoChamberPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.DeclaredMethod(typeof(CylinderOp), nameof(CylinderOp.OnAddAmmoInChamber));
        [PatchPrefix] private static bool Prefix(CylinderOp __instance)
        {
            try
            {
                var op = __instance;
                if (!Prepared.TryGetValue(op, out var receipt) || !ReferenceEquals(receipt.Shot, op._singleShotData.AmmoToFire)
                    || !ReferenceEquals(receipt.Firearms, op.Controller.Firearms) || receipt.Mode != op.Weapon.SelectedFireMode) return true;
                // Free half must not cock the hammer into a DIFFERENT source chamber
                // on the first DA shot after SA. Keep native UI values, defer that
                // hammer transition to the paid half; a deliberate mode change wins.
                op.FirearmsAnimator.SetAmmoOnMag(op._cylinderMagazine.Count);
                op.FirearmsAnimator.SetDoubleAction(Convert.ToSingle(op.Weapon.CylinderHammerClosed));
                return false;
            }
            catch (Exception ex) { ArenaAkimbo.AmmoFailed(ex); return true; }
        }
    }
}
