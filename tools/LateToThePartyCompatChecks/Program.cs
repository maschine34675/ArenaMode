using System.Collections;
using System.Reflection;
using ArenaMode.Compat;
using ArenaMode.Kits;
using ArenaMode.Pickups;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using HarmonyLib;

public static class Program
{
    private static int _checks;
    private static Fixture _fixture;
    private static GameWorld _world;
    private static LocalGame _game;
    public static Item QueueItem;
    public static LootItem QueueLoot;
    private static readonly Dictionary<string, int> Counts = new();
    public static void Record(string name) => Counts[name] = Counts.GetValueOrDefault(name) + 1;
    public static void Physical(Item item) => Record("physical");
    private static int Count(string name) => Counts.GetValueOrDefault(name);
    private static void Need(bool okay, string name) { _checks++; if (!okay) throw new InvalidOperationException("ASSERT: " + name); }

    public static bool Dispatch(string method, object item)
    {
        MethodInfo target = method == "ProcessFoundLooseLootItem" ? _fixture.Found : _fixture.Helpers.GetMethod(method);
        bool run = true;
        foreach (var patch in Harmony.GetPatchInfo(target)?.Prefixes.ToArray() ?? Array.Empty<Patch>())
        {
            object[] arguments = patch.PatchMethod.GetParameters().Length == 2 ? new[] { item, (object)true } : new[] { item };
            bool allowed = (bool)patch.PatchMethod.Invoke(null, arguments);
            if (!allowed)
            {
                if (arguments.Length == 2) Need(arguments[1] is false, "suppressed eligibility returns false");
                run = false;
            }
        }
        return run;
    }

    private static void Reset(Fixture fixture = null)
    {
        Harmony.FailUnpatch = false; ArenaLateToThePartyCompat.Shutdown();
        Harmony.Bound.Clear(); Chainloader.PluginInfos.Clear(); Harmony.DropReceipt = Harmony.FailPatch = false;
        ArenaMode.Plugin.ModLogger.Messages.Clear(); ArenaMode.Plugin.ModLogger.Warnings.Clear(); Counts.Clear();
        _world = new(); _game = new() { GameWorld = _world };
        Singleton<GameWorld>.Instance = _world; Singleton<AbstractGame>.Instance = _game; Singleton<IBotGame>.Instance = _game;
        Singleton<ObjectsFactory>.Instance = new(); ArenaPickupRuntime.Generation++; ArenaPickupRuntime.ScopeLive = true;
        KitBuilder.Next = new(); KitBundlePreloader.Done = false; ItemManipulator.Rejection = null; ItemManipulator.DestroyOnMove = false;
        var player = _world.MainPlayer; player.ActiveSlot = player.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.FirstPrimaryWeapon);
        player.ActiveSlot.ContainedItem = new Weapon();
        _fixture = fixture;
        if (fixture != null) Install(fixture);
    }
    private static void Install(Fixture f)
    {
        _fixture = f;
        Chainloader.PluginInfos[ArenaLateToThePartyContract.PluginGuid] = new() { Instance = f.Instance, Metadata = f.Metadata };
    }
    private static void Drain(IEnumerator enumerator) { while (enumerator.MoveNext()) if (enumerator.Current is IEnumerator child) Drain(child); }
    private static ArenaWeaponPickup.Handle Prepare()
    {
        ArenaWeaponPickup.Handle handle = null;
        Drain(ArenaWeaponPickup.Prepare(_world, new(), new(), new(), () => true, value => handle = value));
        foreach (var item in KitBuilder.Next.Contents) item.Parent = new ItemAddress(_world.Loot.ItemOwner);
        return handle;
    }
    private static Item Ordinary() { var item = new Item(); _ = new ItemController(item, item.Id, item.ShortName); return item; }
    private static void Admitted() => Need(ArenaLateToThePartyCompat.GetRejectionReason() == null, "reviewed fixture admission: " + ArenaLateToThePartyCompat.GetRejectionReason());
    private static void Paths(Item item, bool allowed, string name, LootItem source = null)
    {
        int before = Count("physical"), door = Count("door"), notify = Count("event"), confirm = Count("confirmed");
        _fixture.Start.Invoke(null, new object[] { item });
        Need(Count("physical") == before + (allowed ? 1 : 0), name + " outer deletion");
        Need(Count("door") == door + (allowed ? 1 : 0) && Count("event") == notify + (allowed ? 1 : 0)
            && Count("confirmed") == confirm + (allowed ? 1 : 0), name + " no false door event accounting");
        before = Count("physical"); _fixture.Destroy.Invoke(null, new object[] { item });
        Need(Count("physical") == before + (allowed ? 1 : 0), name + " direct deletion");
        bool eligible = (bool)_fixture.Eligible.Invoke(null, new object[] { item, null, 20d });
        Need(eligible == allowed, name + " eligibility");
        if (source != null)
        {
            before = Count("discovered"); _fixture.Found.Invoke(null, new object[] { source, 20d });
            Need(Count("discovered") == before + (allowed ? 1 : 0), name + " discovery");
            QueueItem = item; QueueLoot = source; before = Count("physical");
            var queue = (IEnumerator)_fixture.Manager.GetMethod("FindAndDestroyLoot").Invoke(Activator.CreateInstance(_fixture.Manager), new object[] { null, 0.5f, 20d });
            queue.MoveNext(); Need(Count("physical") == before + (allowed ? 1 : 0), name + " queued cleanup");
        }
    }

    private static void Ownership()
    {
        Reset();
        var registry = (IList)typeof(ArenaWeaponPickup.Handle).GetField("Sources", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        int originalCount = registry.Count;
        var retained = new List<ArenaWeaponPickup.Handle>();
        for (int i = 0; i < 25; i++) { KitBuilder.Next = new(); var owned = Prepare(); retained.Add(owned); owned.Dispose(); }
        Need(registry.Count <= originalCount, "source registry retires without optional plugin");
        var fixture = FixtureFactory.Make(); Reset(fixture); Admitted();
        Paths(Ordinary(), true, "ordinary PMC loot");
        var handle = Prepare(); var weapon = KitBuilder.Next; var child = weapon.Contents[0]; var source = _world.Loot;
        Paths(weapon, false, "owned weapon", source); Paths(child, false, "owned generated attachment");
        Paths(Ordinary(), true, "ordinary Arena loot");
        var sameId = Ordinary(); sameId.Id = weapon.Id; weapon.Contents.Add(sameId); Paths(sameId, true, "same id different instance"); weapon.Contents.Remove(sameId);
        var added = Ordinary(); weapon.Contents.Add(added); Paths(added, true, "later attached ordinary item"); weapon.Contents.Remove(added);
        weapon.Contents.Remove(child); Paths(child, true, "detached generated attachment"); weapon.Contents.Add(child);
        var foreignLoot = new LootItem { Item = weapon, ItemOwner = source.ItemOwner, gameObject = new() };
        Need(Dispatch("ProcessFoundLooseLootItem", foreignLoot), "different native source object remains ordinary");
        var savedItem = source.Item; source.Item = Ordinary(); Paths(weapon, true, "reused native loot root"); source.Item = savedItem;
        var savedOwner = source.ItemOwner; source.ItemOwner = new ItemController(null, "other", "other"); Paths(weapon, true, "foreign native source owner"); source.ItemOwner = savedOwner;
        var parent = weapon.Parent; weapon.CurrentAddress = null;
        Need(Dispatch("DestroyViaLTTP", weapon), "detached root is not protected"); weapon.Parent = parent;
        Singleton<AbstractGame>.Instance = null; Paths(weapon, true, "menu ignores stale sources"); Singleton<AbstractGame>.Instance = _game;
        Singleton<AbstractGame>.Instance = new HideoutGame(); Paths(weapon, true, "hideout ignores sources"); Singleton<AbstractGame>.Instance = _game;
        Singleton<IBotGame>.Instance = new LocalGame(); Paths(weapon, true, "foreign bot game"); Singleton<IBotGame>.Instance = _game;
        _game.GameWorld = new(); Paths(weapon, true, "foreign local world"); _game.GameWorld = _world;
        ArenaPickupRuntime.Generation++; Paths(weapon, true, "retired nonpending generation");
        handle.Dispose(); Need(_world.Destroyed == 1, "Arena cleanup still destroys its source");

        Reset(fixture); Admitted(); handle = Prepare(); weapon = KitBuilder.Next; source = _world.Loot;
        var player = _world.MainPlayer; var inventory = player.InventoryController;
        Need(handle.TryTake(player, () => true, _ => { }), "valid native pickup submitted");
        Paths(weapon, false, "queued source", source);
        Need(inventory.BeginMove(), "exact native transfer admitted"); Paths(weapon, false, "native InProcess owner", source);
        handle.Dispose(); ArenaPickupRuntime.Generation++;
        Paths(weapon, false, "retiring pending source", source);
        inventory.Finish(); Need(!handle.IsPending && handle.Visual == null, "native success retires source");
        Paths(weapon, true, "successfully carried weapon");
        _ = new ItemController(weapon, weapon.Id, weapon.ShortName); Paths(weapon, true, "later ordinary drop");

        Reset(fixture); Admitted(); handle = Prepare(); weapon = KitBuilder.Next; inventory = _world.MainPlayer.InventoryController;
        Need(handle.TryTake(_world.MainPlayer, () => true, _ => { }), "queued retiring pickup submitted"); handle.Dispose(); ArenaPickupRuntime.Generation++;
        Paths(weapon, false, "retired before native move"); Need(!inventory.BeginMove(), "retirement cannot authorize transfer");
        inventory.Finish(false); Paths(weapon, true, "retired refusal releases source"); Need(_world.Destroyed == 1, "retired refusal cleans source exactly once");

        Reset(fixture); Admitted(); handle = Prepare(); weapon = KitBuilder.Next; inventory = _world.MainPlayer.InventoryController;
        inventory.ThrowSubmission = true; Need(handle.TryTake(_world.MainPlayer, () => true, _ => { }), "ambiguous submission quarantined");
        Paths(weapon, false, "uncertain source"); handle.Dispose(); ArenaPickupRuntime.Generation++; Paths(weapon, false, "uncertain retirement");
        Need(!inventory.BeginMove(), "uncertain request cannot later authorize transfer"); inventory.Finish(false);
        Paths(weapon, true, "uncertain final refusal releases source");

        Reset(fixture); Admitted(); handle = Prepare(); weapon = KitBuilder.Next;
        Singleton<GameWorld>.Instance = new(); _game.GameWorld = Singleton<GameWorld>.Instance;
        Paths(weapon, true, "next ordinary world cannot inherit source"); handle.Dispose();
    }
    private static void Contracts()
    {
        Reset(); ArenaLateToThePartyCompat.Initialize(); Need(ArenaLateToThePartyCompat.GetRejectionReason() == null, "optional mod absent");
        var fixture = FixtureFactory.Make(); Install(fixture); Admitted();
        Need(Harmony.Bound.Values.Sum(i => i.Prefixes.Count) == 4, "four required receipts"); Admitted();
        Need(Harmony.Bound.Values.Sum(i => i.Prefixes.Count) == 4, "initialization is idempotent");
        foreach (var version in new[] { "3.1.2", "4.0.0" }) { Install(FixtureFactory.Make(version)); Admitted(); }
        Need(ArenaMode.Plugin.ModLogger.Warnings.Count == 1, "newer versions warn once");
        foreach (var defect in new[] { "assembly", "version", "attribute", "signature", "eligibility-edge", "destroy-edge", "discovery-edge", "queue-edge", "bypass", "concrete-bypass", "delayed-bypass" })
        {
            Reset(FixtureFactory.Make(defect: defect));
            string rejection = ArenaLateToThePartyCompat.GetRejectionReason();
            Need(rejection?.Contains("Ordinary raids remain available") == true, "unsafe contract rejected " + defect);
            if (defect.EndsWith("bypass")) Need(rejection.Contains("item deletion bypasses"), "bypass rejected for real IL path " + defect + ": " + rejection);
            Need(Harmony.Bound.Values.All(i => i.Prefixes.Count == 0), "unsafe contract unpatched " + defect);
        }
        Reset(FixtureFactory.Make("3.1.0")); Need(ArenaLateToThePartyCompat.GetRejectionReason() != null, "older version rejected");
        foreach (string mode in new[] { "receipt", "partial" })
        {
            Reset(FixtureFactory.Make()); Harmony.DropReceipt = mode == "receipt"; Harmony.FailPatch = mode == "partial";
            Need(ArenaLateToThePartyCompat.GetRejectionReason() != null, "patch failure rejects Arena " + mode);
            Paths(Ordinary(), true, "ordinary after patch failure " + mode);
            Need(Harmony.Bound.Values.All(i => i.Prefixes.Count == 0), "partial install rolled back " + mode);
        }
        Reset(FixtureFactory.Make()); Admitted(); Harmony.Bound[_fixture.Destroy].Prefixes.Clear();
        Need(ArenaLateToThePartyCompat.GetRejectionReason() != null, "missing direct receipt rejects Arena");
        Reset(FixtureFactory.Make()); Admitted(); var handle = Prepare(); Harmony.FailUnpatch = true; ArenaLateToThePartyCompat.Shutdown();
        Paths(KitBuilder.Next, true, "shutdown revokes protection before unpatch failure"); handle.Dispose();
        Harmony.FailUnpatch = false;
    }
    public static int Main(string[] args)
    {
        try
        {
            Contracts(); Ownership();
            if (args.Length != 1) throw new InvalidOperationException("Supply published 3.1.1 DLL for metadata-only verification.");
            PublishedMetadata.Verify(args[0], Need);
            Console.WriteLine($"LATE_TO_THE_PARTY_CHECKS_PASSED ({_checks})"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("LATE_TO_THE_PARTY_CHECKS_FAILED: " + ex); return 1; }
    }
}
