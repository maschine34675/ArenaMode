using ArenaMode;
using ArenaMode.Config;
using ArenaMode.Kits;
using ArenaMode.Session;
using EFT;
using EFT.InventoryLogic;
using EFT.UI.DragAndDrop;
using System.Reflection;
using System.Text.RegularExpressions;

internal static class Program
{
    private static int _assertions;

    private static readonly Type AfterRemoval = PatchType("PickAfterRemovalPatch");
    private static readonly Type BeforeTap = PatchType("PickBeforeTapThrowPatch");

    private static int Main()
    {
        try
        {
            CheckRegistration();
            CheckEnableFailureIsContained();
            CheckModelReproducesTheReport();
            CheckThrownBackpackGrenadeMovesThePick();
            CheckNestedRigGrenade();
            CheckUseItemsAnywhereInstall();
            CheckMovedButReachableKeepsThePick();
            CheckDroppedGrenadeMovesThePick();
            CheckContainerLeavingWithThePick();
            CheckReachDecisionFollowsTryProceed();
            CheckEventFilters();
            CheckRigGrenadeStaysNative();
            CheckLastGrenadeClearsThePick();
            CheckKeyBackstopAfterSettingOff();
            CheckKeyLeavesADrawablePickAlone();
            CheckOrdinaryRaidIsUntouched();
            CheckFailuresStayContained();
            CheckSourceContract();
            Console.WriteLine($"ARENA_GRENADE_PICK_CHECKS_PASSED ({_assertions} assertions)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ARENA_GRENADE_PICK_CHECKS_FAILED after {_assertions} assertions: {ex}");
            return 1;
        }
    }

    private sealed class World
    {
        public InventoryController Controller;
        public Player Player;
        public PlayerInputTranslator Keys;
        public FastAccessGrenadeItemView View;
        public Weapon Rifle;
        public CompoundItem BackpackPouch;
        public Grid VestGrid, BackpackGrid, BackpackPouchGrid, VestPouchGrid, GroundGrid;
        public ThrowWeap VestFrag, VestSmoke, BackpackFrag, BackpackPouchFrag, VestPouchFrag;
        public Meds BackpackMeds;
        public ThrowWeap Pick => Controller.Inventory.Equipment.TopPriorityGrenade;
    }

    /// <summary>
    /// A fresh arena raid (new arming epoch): a PMC with a rifle; a rig holding a
    /// frag, a smoke and a pouch with a frag; a backpack holding a frag, a
    /// Grizzly and a pouch with a frag; empty pockets and secure container; a
    /// loot container on the ground. The battle screen's quick-throw view is
    /// shown, which picks the rig frag.
    /// </summary>
    private static World Build(bool arena = true, bool reach = true, bool mod = false, bool modFirst = false,
        bool vestGrenades = true)
    {
        PatchHost.Reset();
        Install.UseItemsAnywhere = mod;
        ArenaSession.IsActive = arena;
        ArenaSession.ArmEpoch++;
        Settings.UseItemsAnywhere.Value = reach;
        Plugin.ModLogger.Warnings.Clear();
        ArenaReachPatches.Enable();
        if (mod)
            PatchHost.RegisterExternal(typeof(UseItemsAnywhereModel), modFirst);
        PlayerInputTranslator.RandomIndex = _ => 0;

        var equipment = new InventoryEquipment("equipment");
        var w = new World { Rifle = new Weapon("rifle") };
        InventoryController.Equip(w.Rifle, equipment.GetSlot(EquipmentSlot.FirstPrimaryWeapon));

        var vest = new CompoundItem("rig");
        w.VestGrid = vest.AddGrid();
        InventoryController.Equip(vest, equipment.GetSlot(EquipmentSlot.TacticalVest));
        var pockets = new CompoundItem("pockets");
        pockets.AddGrid();
        InventoryController.Equip(pockets, equipment.GetSlot(EquipmentSlot.Pockets));
        var backpack = new CompoundItem("backpack");
        w.BackpackGrid = backpack.AddGrid();
        InventoryController.Equip(backpack, equipment.GetSlot(EquipmentSlot.Backpack));
        var secure = new CompoundItem("secure");
        secure.AddGrid();
        InventoryController.Equip(secure, equipment.GetSlot(EquipmentSlot.SecuredContainer));

        w.BackpackPouch = new CompoundItem("backpack-pouch");
        w.BackpackPouchGrid = w.BackpackPouch.AddGrid();
        InventoryController.Place(w.BackpackPouch, w.BackpackGrid);
        var vestPouch = new CompoundItem("rig-pouch");
        w.VestPouchGrid = vestPouch.AddGrid();
        InventoryController.Place(vestPouch, w.VestGrid);

        w.VestFrag = new ThrowWeap("rig-frag", ThrowWeapType.frag_grenade);
        w.VestSmoke = new ThrowWeap("rig-smoke", ThrowWeapType.smoke_grenade);
        if (vestGrenades)
        {
            InventoryController.Place(w.VestFrag, w.VestGrid);
            InventoryController.Place(w.VestSmoke, w.VestGrid);
        }
        w.BackpackFrag = new ThrowWeap("backpack-frag", ThrowWeapType.frag_grenade);
        InventoryController.Place(w.BackpackFrag, w.BackpackGrid);
        w.BackpackMeds = new Meds("backpack-grizzly");
        InventoryController.Place(w.BackpackMeds, w.BackpackGrid);
        w.BackpackPouchFrag = new ThrowWeap("backpack-pouch-frag", ThrowWeapType.frag_grenade);
        InventoryController.Place(w.BackpackPouchFrag, w.BackpackPouchGrid);
        w.VestPouchFrag = new ThrowWeap("rig-pouch-frag", ThrowWeapType.frag_grenade);
        InventoryController.Place(w.VestPouchFrag, w.VestPouchGrid);

        var ground = new CompoundItem("ground-loot");
        w.GroundGrid = ground.AddGrid();

        w.Controller = new InventoryController("pmc", new Inventory(equipment));
        w.Player = new Player(w.Controller, w.Rifle);
        w.Keys = new PlayerInputTranslator(w.Player);
        w.View = new FastAccessGrenadeItemView();
        w.View.Show(w.Controller);
        return w;
    }

    private static void CheckRegistration()
    {
        Build();
        Assert(PatchHost.Registered.Count == 4, "reach registers two place patches and two pick patches");
        var byPatch = PatchHost.Registered.ToDictionary(r => r.Patch, r => r.Target);
        MethodBase removal = byPatch[AfterRemoval];
        Assert(removal.DeclaringType == typeof(FastAccessGrenadeItemView)
               && removal.Name == nameof(FastAccessGrenadeItemView.OnItemRemoved)
               && removal.GetParameters().Single().ParameterType == typeof(RemoveItemEventArgs),
            "the removal cleanup targets the view's own OnItemRemoved(RemoveItemEventArgs)");
        MethodBase tap = byPatch[BeforeTap];
        Assert(tap.DeclaringType == typeof(PlayerInputTranslator)
               && tap.Name == nameof(PlayerInputTranslator.vmethod_1) && tap.GetParameters().Length == 0,
            "the key backstop targets the tap-G method itself");
        MethodInfo postfix = AfterRemoval.GetMethods(BindingFlags.Static | BindingFlags.NonPublic).Single();
        Assert(postfix.GetParameters().Any(p => p.Name == "___InventoryController"),
            "the view's protected controller is injected by name (checked at registration like Harmony)");
        Assert(BeforeTap.GetMethods(BindingFlags.Static | BindingFlags.NonPublic).Single().ReturnType == typeof(void),
            "the key backstop is a void prefix: the native method always runs");
    }

    private static void CheckEnableFailureIsContained()
    {
        PatchHost.Reset();
        Plugin.ModLogger.Warnings.Clear();
        PatchHost.FailRegistration = t => t == AfterRemoval;
        bool escaped = false;
        try
        {
            ArenaReachPatches.Enable();
        }
        catch (Exception)
        {
            escaped = true;
        }
        PatchHost.FailRegistration = null;
        Assert(!escaped, "a pick patch that cannot be enabled never stops the rest of the plugin");
        Assert(PatchHost.Registered.Count == 3 && PatchHost.Registered.Any(r => r.Patch == BeforeTap),
            "the place patches and the other pick patch are still enabled");
        Assert(Plugin.ModLogger.Warnings.Count == 1 && Plugin.ModLogger.Warnings[0].Contains("pick patch not enabled"),
            "the missing pick patch is reported");
    }

    /// <summary>Without the two pick patches the model shows the reported defect.</summary>
    private static void CheckModelReproducesTheReport()
    {
        var w = Build();
        PatchHost.Suppressed.Add(AfterRemoval);
        PatchHost.Suppressed.Add(BeforeTap);

        Assert(w.Pick == w.VestFrag, "model: showing the view picks the first rig grenade");
        DrawByKey(w, w.BackpackFrag);
        w.Player.ThrowGrenadeInHands();
        Assert(w.BackpackFrag.CurrentAddress == null, "model: a thrown grenade has no address");
        Assert(w.Pick == w.BackpackFrag && w.View.Shown == w.BackpackFrag,
            "control: the native view leaves the pick (and its icon) on the thrown backpack grenade");
        Assert(w.View.SetNewTopPriorityCalls == 0, "control: the native handler never reacted");

        w.Keys.vmethod_1();
        Assert(w.Player.FallbackCount == 1 && w.Player.InHands == w.Rifle,
            "control: tap-G falls back to SetFirstAvailableItem instead of throwing");
        w.Keys.vmethod_1();
        Assert(w.Player.FallbackCount == 2, "control: and keeps failing with two grenades in the rig");
    }

    private static void CheckThrownBackpackGrenadeMovesThePick()
    {
        foreach (bool keyBackstop in new[] { true, false })
        {
            var w = Build();
            if (!keyBackstop)
                PatchHost.Suppressed.Add(BeforeTap);
            string label = keyBackstop ? "" : " (removal cleanup alone)";

            DrawByKey(w, w.BackpackFrag);
            w.Player.ThrowGrenadeInHands();
            Assert(w.Pick == w.VestFrag,
                "throwing the picked backpack grenade moves the pick to the next rig grenade" + label);
            Assert(w.View.Shown == w.VestFrag, "the quick slot shows the new pick, as natively" + label);
            Assert(w.View.SetNewTopPriorityCalls == 1, "through the view's own SetNewTopPriorityGrenade, once" + label);

            w.Keys.vmethod_1();
            Assert(w.Player.InHands == w.VestFrag && w.Player.FallbackCount == 0,
                "tap-G throws again after a backpack grenade was thrown by number key" + label);
            AssertQuiet("the repaired throw logs nothing" + label);
        }
    }

    private static void CheckNestedRigGrenade()
    {
        var w = Build();
        ArenaSession.IsActive = false;
        Assert(!w.Controller.IsAtBindablePlace(w.VestPouchFrag) && w.Controller.IsAtReachablePlace(w.VestPouchFrag),
            "model: natively a grenade in a rig pouch is reachable but not bindable");
        ArenaSession.IsActive = true;

        DrawByKey(w, w.VestPouchFrag);
        w.Player.ThrowGrenadeInHands();
        Assert(w.Pick == w.VestFrag && w.View.SetNewTopPriorityCalls == 1,
            "a thrown rig-pouch grenade (bindable only through Arena) moves the pick");
        w.Keys.vmethod_1();
        Assert(w.Player.InHands == w.VestFrag && w.Player.FallbackCount == 0, "and tap-G throws the rig frag");
    }

    /// <summary>
    /// The user's install carries UseItemsAnywhere 1.4.0, which tracks the
    /// backpack itself. Its postfix order against Arena's is load-order
    /// dependent, so both orders are run.
    /// </summary>
    private static void CheckUseItemsAnywhereInstall()
    {
        foreach (bool modFirst in new[] { true, false })
        {
            var w = Build(mod: true, modFirst: modFirst);
            string label = modFirst ? " (mod postfix first)" : " (Arena postfix first)";

            DrawByKey(w, w.BackpackFrag);
            w.Player.ThrowGrenadeInHands();
            Assert(w.Pick == w.VestFrag && w.View.SetNewTopPriorityCalls == 1,
                "with the mod the backpack's top level is native bookkeeping and gets no second call" + label);

            DrawByKey(w, w.BackpackPouchFrag);
            w.Player.ThrowGrenadeInHands();
            Assert(w.Pick == w.VestFrag && w.View.SetNewTopPriorityCalls == 2,
                "a grenade in a backpack pouch still needs the cleanup and gets it" + label);
            w.Keys.vmethod_1();
            Assert(w.Player.InHands == w.VestFrag && w.Player.FallbackCount == 0, "tap-G throws" + label);
        }
    }

    private static void CheckMovedButReachableKeepsThePick()
    {
        var w = Build();
        DrawByKey(w, w.BackpackFrag);
        w.Player.PutAwayToRifle();
        w.Controller.Move(w.BackpackFrag, w.BackpackPouchGrid);
        Assert(w.Pick == w.BackpackFrag && w.View.SetNewTopPriorityCalls == 0,
            "a grenade that only moved and is still reachable keeps the pick");
        w.Keys.vmethod_1();
        Assert(w.Player.InHands == w.BackpackFrag && w.Player.FallbackCount == 0,
            "and tap-G still draws it from its new place");
    }

    private static void CheckDroppedGrenadeMovesThePick()
    {
        var w = Build();
        DrawByKey(w, w.BackpackFrag);
        w.Player.PutAwayToRifle();
        w.Controller.Move(w.BackpackFrag, w.GroundGrid);
        Assert(w.BackpackFrag.CurrentAddress != null, "model: a dropped grenade keeps an address, in the loot");
        Assert(w.Pick == w.VestFrag, "a picked grenade dropped out of the inventory moves the pick");
        w.Keys.vmethod_1();
        Assert(w.Player.InHands == w.VestFrag && w.Player.FallbackCount == 0, "and tap-G throws the rig frag");
    }

    /// <summary>
    /// A dropped backpack or a pouch dragged into loot raises its event for the
    /// container, never for the grenade inside (native review finding).
    /// </summary>
    private static void CheckContainerLeavingWithThePick()
    {
        var w = Build();
        DrawByKey(w, w.BackpackFrag);
        w.Player.PutAwayToRifle();
        w.Controller.Move(w.BackpackPouch, w.GroundGrid);
        Assert(w.Pick == w.BackpackFrag && w.View.SetNewTopPriorityCalls == 0,
            "a container that does not hold the pick leaving leaves the pick alone");

        var v = Build();
        DrawByKey(v, v.BackpackPouchFrag);
        v.Player.PutAwayToRifle();
        v.Controller.Move(v.BackpackPouch, v.GroundGrid);
        Assert(v.Pick == v.VestFrag && v.View.Shown == v.VestFrag,
            "a pouch dropped with the pick inside moves the pick");
        v.Keys.vmethod_1();
        Assert(v.Player.InHands == v.VestFrag && v.Player.FallbackCount == 0, "and tap-G throws the rig frag");
    }

    /// <summary>
    /// The cleanup's decision must be TryProceed's own question, not "still
    /// carried" or "still bindable" (harness review finding: those substitutes
    /// kept every earlier check green).
    /// </summary>
    private static void CheckReachDecisionFollowsTryProceed()
    {
        var w = Build();
        DrawByKey(w, w.BackpackFrag);
        w.Player.PutAwayToRifle();
        Settings.UseItemsAnywhere.Value = false;
        w.Controller.Move(w.BackpackFrag, w.BackpackGrid);
        Assert(w.Pick == w.VestFrag,
            "with reach off a picked grenade moved inside the backpack (carried, not drawable) moves the pick");

        var v = Build();
        DrawByKey(v, v.VestPouchFrag);
        v.Player.PutAwayToRifle();
        Settings.UseItemsAnywhere.Value = false;
        v.Keys.vmethod_1();
        Assert(v.Pick == v.VestPouchFrag && v.Player.InHands == v.VestPouchFrag,
            "with reach off a natively drawable rig-pouch pick (not bindable) is still drawn");
    }

    private static void CheckEventFilters()
    {
        var w = Build();
        DrawByKey(w, w.BackpackFrag);
        w.Player.PutAwayToRifle();
        ItemAddress from = w.BackpackFrag.CurrentAddress;
        InventoryController.Detach(w.BackpackFrag);

        // Spec checks: they mirror the native handler's own filters.
        w.Controller.RaiseRemove(new RemoveItemEventArgs(w.BackpackFrag, from, CommandStatus.Begin, w.Controller.ID));
        Assert(w.Pick == w.BackpackFrag, "a Begin-only removal leaves the pick alone");
        w.Controller.RaiseRemove(new RemoveItemEventArgs(w.BackpackFrag, from, CommandStatus.Failed, w.Controller.ID));
        Assert(w.Pick == w.BackpackFrag, "a Failed removal leaves the pick alone");
        w.Controller.RaiseRemove(new RemoveItemEventArgs(w.BackpackFrag, from, CommandStatus.Succeed, "bot-7"));
        Assert(w.Pick == w.BackpackFrag, "an event for another owner leaves the pick alone");

        ItemAddress medsFrom = w.BackpackMeds.CurrentAddress;
        InventoryController.Detach(w.BackpackMeds);
        w.Controller.RaiseRemove(new RemoveItemEventArgs(w.BackpackMeds, medsFrom, CommandStatus.Succeed, w.Controller.ID));
        Assert(w.Pick == w.BackpackFrag, "a non-grenade leaving leaves the pick alone");

        w.Controller.Discard(w.BackpackPouchFrag);
        Assert(w.Pick == w.BackpackFrag && w.View.SetNewTopPriorityCalls == 0,
            "a grenade that is not the pick leaving leaves the pick alone");

        w.Controller.RaiseRemove(new RemoveItemEventArgs(w.BackpackFrag, from, CommandStatus.Succeed, w.Controller.ID));
        Assert(w.Pick == w.VestFrag, "the same removal, completed and own, does move it (the setup was live)");
    }

    /// <summary>Runs after the event filters so a missing Succeed gate is named there.</summary>
    private static void CheckRigGrenadeStaysNative()
    {
        var w = Build();
        w.Keys.vmethod_1();
        Assert(w.Player.InHands == w.VestFrag, "tap-G draws the view's initial pick");
        w.Player.ThrowGrenadeInHands();
        Assert(w.Pick == w.VestSmoke && w.View.Shown == w.VestSmoke && w.View.SetNewTopPriorityCalls == 1,
            "a rig grenade is still moved on by the native handler alone, without a second call");
    }

    private static void CheckLastGrenadeClearsThePick()
    {
        var w = Build(vestGrenades: false);
        Assert(w.Pick == null, "model: no rig or pocket grenade, nothing is picked when the view shows");
        DrawByKey(w, w.BackpackFrag);
        w.Player.ThrowGrenadeInHands();
        Assert(w.Pick == null && w.View.Shown == null,
            "throwing the pick with nothing left in the view's list clears the pick and the slot, as natively");
        w.Keys.vmethod_1();
        Assert(w.Player.InHands == w.Rifle && w.Player.FallbackCount == 0,
            "with no candidate tap-G does nothing, exactly as natively");
        AssertQuiet("a tap of G with nothing picked logs nothing");
    }

    private static void CheckKeyBackstopAfterSettingOff()
    {
        foreach (bool backstop in new[] { false, true })
        {
            var w = Build();
            if (!backstop)
                PatchHost.Suppressed.Add(BeforeTap);
            DrawByKey(w, w.BackpackFrag);
            w.Player.PutAwayToRifle();
            Settings.UseItemsAnywhere.Value = false;
            Assert(w.Pick == w.BackpackFrag && !w.Controller.IsAtReachablePlace(w.BackpackFrag),
                "turning reach off strands the backpack pick");

            w.Keys.vmethod_1();
            if (!backstop)
            {
                Assert(w.Player.FallbackCount == 1, "control: without the backstop the stranded pick blocks G");
                continue;
            }
            Assert(w.Pick != w.BackpackFrag, "the key clears a pick the setting stranded");
            Assert(w.Player.InHands == w.VestFrag && w.Player.FallbackCount == 0,
                "and the native no-pick branch throws from G's own candidate list");
            AssertQuiet("the backstop logs nothing");
        }
    }

    private static void CheckKeyLeavesADrawablePickAlone()
    {
        var w = Build();
        DrawByKey(w, w.VestSmoke);
        w.Player.PutAwayToRifle();
        w.Keys.vmethod_1();
        Assert(w.Pick == w.VestSmoke && w.Player.InHands == w.VestSmoke,
            "a drawable rig pick is left alone and drawn");

        w.Player.PutAwayToRifle();
        DrawByKey(w, w.BackpackFrag);
        w.Player.PutAwayToRifle();
        w.Keys.vmethod_1();
        Assert(w.Pick == w.BackpackFrag && w.Player.InHands == w.BackpackFrag,
            "a backpack pick that reach can still draw is left alone (the documented tap-G exception)");
    }

    /// <summary>
    /// Nothing may change outside the arena. The user's install makes this a
    /// real ordinary-raid path: UseItemsAnywhere binds a rig-pouch grenade, and
    /// the native view leaves the same stale pick. It must stay native.
    /// </summary>
    private static void CheckOrdinaryRaidIsUntouched()
    {
        var w = Build(arena: false, mod: true);
        DrawByKey(w, w.VestPouchFrag);
        w.Player.ThrowGrenadeInHands();
        w.Keys.vmethod_1();
        Assert(w.Pick == w.VestPouchFrag && w.View.Shown == w.VestPouchFrag && w.View.SetNewTopPriorityCalls == 0
               && w.Player.FallbackCount == 1,
            "ordinary raid keeps the native stale pick, icon and fallback exactly");

        var plain = Build(arena: false);
        var ghost = new ThrowWeap("ghost", ThrowWeapType.frag_grenade);
        plain.Controller.Inventory.Equipment.TopPriorityGrenade = ghost;
        plain.Keys.vmethod_1();
        plain.Controller.RaiseRemove(new RemoveItemEventArgs(ghost, new GridItemAddress(plain.BackpackGrid),
            CommandStatus.Succeed, plain.Controller.ID));
        plain.Controller.Move(plain.BackpackPouch, plain.GroundGrid);
        Assert(plain.Pick == ghost && plain.Player.FallbackCount == 1 && plain.View.SetNewTopPriorityCalls == 0,
            "ordinary raid: neither pick patch touches an unreachable pick");
        AssertQuiet("ordinary raid: nothing is logged");
    }

    private static void CheckFailuresStayContained()
    {
        var w = Build();
        int probeEvents = 0;
        w.Controller.RemoveItemEvent += _ => probeEvents++;
        DrawByKey(w, w.BackpackFrag);
        w.Controller.FaultWhilePickPatchAsks = true;
        w.Player.ThrowGrenadeInHands();
        Assert(w.Controller.SwallowedEventExceptions.Count == 0 && probeEvents == 2,
            "a failing pick cleanup never escapes into the event multicast");
        Assert(w.Pick == w.BackpackFrag, "a failed cleanup leaves the native state as it was");
        Assert(Plugin.ModLogger.Warnings.Count == 1
               && Plugin.ModLogger.Warnings[0].Contains("Arena grenade pick cleanup failed")
               && Plugin.ModLogger.Warnings[0].Contains(nameof(InvalidOperationException)),
            "the failure is reported with its exception");

        bool escaped = false;
        try
        {
            w.Keys.vmethod_1();
        }
        catch (Exception)
        {
            escaped = true;
        }
        Assert(!escaped, "a failing key backstop never escapes into the input translator");
        Assert(w.Player.FallbackCount == 1, "and the native method still runs");
        Assert(Plugin.ModLogger.Warnings.Count == 1, "a second failure in the same raid is not reported again");

        w.Controller.FaultWhilePickPatchAsks = false;
        w.Keys.vmethod_1();
        Assert(w.Player.InHands == w.VestFrag, "once the fault clears the backstop recovers G");

        ArenaSession.ArmEpoch++;
        w.Controller.FaultWhilePickPatchAsks = true;
        w.Controller.Inventory.Equipment.TopPriorityGrenade = new ThrowWeap("stranded", ThrowWeapType.frag_grenade);
        w.Keys.vmethod_1();
        Assert(Plugin.ModLogger.Warnings.Count == 2, "a failure in a later raid is reported again");
    }

    private static void CheckSourceContract()
    {
        string path = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "ProductionReachSource").Value!;
        string source = File.ReadAllText(path);
        Assert(Regex.Matches(source, @"private static bool PickGuardWanted => ArenaSession\.IsActive;").Count == 1,
            "source: the pick patches gate on the arena itself");
        Assert(Regex.Matches(source, @"if \(!PickGuardWanted\)").Count == 2,
            "source: both pick patches open with that gate");
        Assert(Regex.Matches(source, @"TopPriorityGrenade\s*=(?!=)").Count == 1
               && source.Contains("equipment.TopPriorityGrenade = null;"),
            "source: Arena's only own write to the pick is clearing it");
        Assert(!source.Contains("_sortedGrenadeList") && !source.Contains("GrenadeThrowingSlots ="),
            "source: the view's private list and the slot list are never touched");
        Assert(source.Contains("EnablePickPatch(() => new PickAfterRemovalPatch());")
               && source.Contains("EnablePickPatch(() => new PickBeforeTapThrowPatch());"),
            "source: both pick patches are enabled, contained, with reach");
    }

    private static void DrawByKey(World w, ThrowWeap grenade)
    {
        Assert(w.Controller.IsAtBindablePlace(grenade), $"{grenade} can be bound to a number key");
        w.Player.PressBoundKey(grenade);
        Assert(w.Player.InHands == grenade && w.Pick == grenade, $"drawing {grenade} makes it the pick");
    }

    private static void AssertQuiet(string message)
        => Assert(Plugin.ModLogger.Warnings.Count == 0, message);

    private static Type PatchType(string name)
        => typeof(ArenaReachPatches).GetNestedType(name, BindingFlags.NonPublic)
           ?? throw new InvalidOperationException($"production patch {name} not found");

    private static void Assert(bool condition, string message)
    {
        _assertions++;
        if (!condition)
            throw new InvalidOperationException("Assertion failed: " + message);
    }
}
