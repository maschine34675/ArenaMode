using System.Collections;
using ArenaMode.Kits;
using ArenaMode.Pickups;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

internal static class Program
{
    private static int _checks;
    private static bool _current;
    private static GameWorld _world;
    private static Player P => _world.MainPlayer;
    private static InventoryController Inv => P.InventoryController;
    private static Slot Slot(EquipmentSlot slot) => Inv.Inventory.Equipment.GetSlot(slot);
    private static void Check(bool value, string message)
    {
        _checks++;
        if (!value) throw new Exception("ASSERT: " + message);
    }
    private static void Reset()
    {
        _current = true;
        _world = new();
        Singleton<GameWorld>.Instance = _world;
        Singleton<ObjectsFactory>.Instance = new();
        ArenaPickupRuntime.Generation++;
        KitBundlePreloader.Done = false;
        KitBuilder.Next = new(); KitBuilder.Built = 0; KitBuilder.Registered = 0;
        ItemManipulator.Simulations = 0; ItemManipulator.DestroyOnMove = false;
        P.ActiveSlot = Slot(EquipmentSlot.FirstPrimaryWeapon);
        P.ActiveSlot.ContainedItem = new Weapon();
        Slot(EquipmentSlot.Holster).Compatible = false;
    }
    private static void Drain(IEnumerator routine)
    {
        while (routine.MoveNext()) if (routine.Current is IEnumerator nested) Drain(nested);
    }
    private static ArenaWeaponPickup.Handle Prepare()
    {
        ArenaWeaponPickup.Handle result = null;
        Drain(ArenaWeaponPickup.Prepare(_world, new(), new(), new(), () => _current, handle => result = handle));
        return result;
    }
    public static int Main()
    {
        try
        {
            Reset();
            var preparation = ArenaWeaponPickup.Prepare(_world, new(), new(), new(), () => _current, _ => { });
            Check(preparation.MoveNext() && preparation.Current is IEnumerator, "prepare waits for bundles");
            Check(KitBuilder.Built == 0 && Singleton<ObjectsFactory>.Instance.Prefabs == 0, "nothing fabricated before preload");
            Drain((IEnumerator)preparation.Current);
            _current = false; Drain(preparation);
            Check(_world.Created == 0, "scope lost during preload never creates loot");

            Reset(); KitBuilder.Next.Contents.Clear();
            Check(Prepare() == null && _world.Created == 0, "empty weapon is refused");
            Reset(); KitBuilder.Next.MissingVitalParts.Add(new Item());
            Check(Prepare() == null && _world.Created == 0, "incomplete weapon is refused");
            Reset(); Singleton<ObjectsFactory>.Instance.NullPrefab = true;
            Check(Prepare() == null, "missing pool prefab fails without a handle");
            Reset(); _world.ThrowAfterRegistration = true;
            Check(Prepare() == null && _world.Destroyed == 1, "partial native creation unregisters its exact source loot");

            Reset(); var h = Prepare(); var visual = h.Visual;
            Check(h != null && visual != null && h.Collider != null && _world.Loot.PhysicsStopped, "prepared weapon keeps a fixed native visual");
            var old = P.ActiveSlot.ContainedItem; var hands = P.HandsController;
            bool? result = null;
            Check(h.TryTake(P, () => _current, ok => result = ok), "free compatible secondary starts");
            Check(result == null && h.IsPending && Inv.Moves == 0, "queue admission is not success");
            Check(!h.TryTake(P, () => true, _ => throw new Exception()), "pending pickup admits one consumer only");
            Check(Inv.BeginMove(), "current exact actor is admitted by native source owner");
            Check(result == null, "native ownership before final callback is not success");
            Check(ReferenceEquals(h.Visual, visual) && h.Collider != null, "pending native source visual stays identifiable until removal");
            Inv.Finish();
            Check(result == true && !h.IsPending && !h.IsAvailable, "final native ownership consumes pickup");
            Check(ReferenceEquals(P.ActiveSlot.ContainedItem, old) && ReferenceEquals(P.HandsController, hands), "current weapon and hands remain unchanged");
            Check(ReferenceEquals(Slot(EquipmentSlot.SecondPrimaryWeapon).ContainedItem, KitBuilder.Next), "exact complete weapon goes to secondary slot");
            Check(KitBuilder.Registered == 1 && _world.Destroyed == 1 && h.Visual == null, "native remove releases visual and registers only transferred ammo");
            h.Dispose(); h.Dispose();
            Check(_world.Destroyed == 1 && ReferenceEquals(KitBuilder.Next.Parent.GetOwner(), Inv), "dispose never destroys transferred equipment or reused visual");
            Inv.Finish(); Check(KitBuilder.Registered == 1, "duplicate native callback cannot grant twice");

            foreach (var scenario in new[] { "full", "active-empty", "incompatible", "changing", "unsupported-hands", "ai", "foreign-body", "dead", "stale", "network-controller", "can-execute", "destruction" })
            {
                Reset(); h = Prepare(); var actor = P;
                switch (scenario)
                {
                    case "full": Slot(EquipmentSlot.SecondPrimaryWeapon).ContainedItem = new Weapon(); break;
                    case "active-empty": P.ActiveSlot = Slot(EquipmentSlot.SecondPrimaryWeapon); break;
                    case "incompatible": Slot(EquipmentSlot.SecondPrimaryWeapon).Compatible = false; break;
                    case "changing": Inv.IsChangingWeapon = true; break;
                    case "unsupported-hands": P.HandsController.Supported = false; break;
                    case "ai": actor.IsAI = true; break;
                    case "foreign-body": actor = new Player(); break;
                    case "dead": actor.Live = false; break;
                    case "stale": _current = false; break;
                    case "network-controller": actor.InventoryController = new InventoryController(); break;
                    case "can-execute": Inv.Allowed = false; break;
                    case "destruction": ItemManipulator.DestroyOnMove = true; break;
                }
                Check(!h.TryTake(actor, () => true, _ => throw new Exception()), scenario + " is refused before submission");
                Check(Inv.Submitted == 0 && h.IsAvailable, scenario + " leaves pickup unconsumed");
                h.Dispose(); Check(_world.Destroyed == 1, "untransferred visual is cleaned once");
            }

            foreach (var scenario in new[] { "round", "raid", "body", "dead", "caller", "slot-filled", "active-slot", "hands", "inventory", "dispose" })
            {
                Reset(); h = Prepare(); var actor = P; var inventory = Inv; result = null;
                Check(h.TryTake(actor, () => _current, ok => result = ok), "queued before " + scenario);
                switch (scenario)
                {
                    case "round": ArenaPickupRuntime.Generation++; break;
                    case "raid": Singleton<GameWorld>.Instance = new(); break;
                    case "body": _world.MainPlayer = new(); break;
                    case "dead": actor.Live = false; break;
                    case "caller": _current = false; break;
                    case "slot-filled": Slot(EquipmentSlot.SecondPrimaryWeapon).ContainedItem = new Weapon(); break;
                    case "active-slot": actor.ActiveSlot = Slot(EquipmentSlot.SecondPrimaryWeapon); break;
                    case "hands": actor.HandsController = new(); break;
                    case "inventory": actor.InventoryController = new Player.SinglePlayerInventoryController(); break;
                    case "dispose": h.Dispose(); break;
                }
                Check(!inventory.BeginMove() && inventory.Moves == 0, "native boundary rejects delayed " + scenario);
                inventory.Finish(false);
                Check(result == false && !h.IsPending, "refused delayed transfer settles once: " + scenario);
            }

            Reset(); h = Prepare(); result = null;
            Check(h.TryTake(P, () => true, ok => result = ok), "ownership proof test starts");
            Inv.Finish(true, false);
            Check(result == false && h.IsAvailable && KitBuilder.Registered == 0, "success callback without ownership is rejected");
            Check(h.TryTake(P, () => true, ok => result = ok), "final refusal permits retry");
            Check(Inv.BeginMove(), "retry moves the same weapon");
            h.Dispose(); Check(_world.Destroyed == 0, "dispose defers visual cleanup while native transfer may roll back");
            Check(!_world.Loot.gameObject.Active && h.Visual == null, "retired pending collider is inert before native completion");
            Inv.RollBack(); Inv.Finish(false);
            Check(result == false && _world.Destroyed == 1, "rollback after dispose cleans restored source visual");

            Reset(); h = Prepare(); result = null;
            h.TryTake(P, () => true, ok => result = ok); Inv.BeginMove();
            ArenaPickupRuntime.Generation++; Inv.Finish();
            Check(result == false && !h.IsAvailable && KitBuilder.Registered == 0, "late round completion cannot report success or respawn a clone");
            h.Dispose(); Check(_world.Destroyed == 1, "late transferred item survives dispose");

            Reset(); h = Prepare(); result = null;
            h.TryTake(P, () => true, ok => result = ok); Inv.BeginMove(); Inv.Finish(false, false);
            Check(result == false && !h.IsAvailable, "final failure with carried ownership cannot rearm");
            h.Dispose();
            Check(_world.Destroyed == 1 && ReferenceEquals(KitBuilder.Next.Parent.GetOwner(), Inv), "unfinalized source visual cleans without destroying carried equipment");

            Reset(); h = Prepare(); result = null; Inv.ThrowSubmission = true;
            Check(h.TryTake(P, () => true, ok => result = ok), "uncertain submission stays reserved");
            Check(!h.TryTake(P, () => true, _ => { }) && !h.IsAvailable, "submission exception cannot duplicate on retry");
            Check(!Inv.BeginMove(), "quarantined submission cannot transfer later");
            Inv.Finish(false); h.Dispose();
            Check(_world.Destroyed == 1, "uncertain failure eventually cleans the source visual");

            Console.WriteLine($"ARENA WEAPON ADAPTER PASSED: {_checks} assertions");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
