using System.Reflection;
using System.Reflection.Emit;
using ArenaMode.Compat;
using ArenaMode.Config;
using ArenaMode.Kits;
using ArenaMode.Session;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;

internal static class Program
{
    private static int _checks;
    internal static Type ForeignQuery;
    private static Player _player;
    private static InventoryController _controller;
    private static readonly MethodInfo[] Consumers = {
        typeof(FirearmHandsInputTranslator).GetMethod("ReloadExternalMagazine"),
        typeof(AmmoSelector.CG_FindAmmoForWeapon).GetMethod("method_0"),
        typeof(ItemUiContext).GetMethod("FindSuitableMagazine")
    };

    private static void Check(bool yes, string reason)
    { if (!yes) throw new Exception("ASSERT: " + reason); _checks++; }

    private static void Fresh()
    {
        ArenaSession.IsActive = ArenaSession.Exact = Settings.UseItemsAnywhere.Value = true;
        _player = new Player(); _controller = _player.InventoryController;
        var pockets = new ContainerCollection(); pockets.Items.Add(new Magazine { Id = "rig" });
        var backpack = new ContainerCollection(); backpack.Items.Add(new Magazine { Id = "bag" });
        backpack.Items.Add(new Meds { Id = "meds" });
        _controller.Inventory.Equipment.GetSlot(EquipmentSlot.Pockets).ContainedItem = pockets;
        _controller.Inventory.Equipment.GetSlot(EquipmentSlot.Backpack).ContainedItem = backpack;
        var world = new GameWorld { MainPlayer = _player }; Singleton<GameWorld>.Instance = world;
        Singleton<AbstractGame>.Instance = new LocalGame { GameWorld = world, LocalPlayer = _player };
    }

    private static void Results(string expected, string reason, Predicate<Magazine> predicate = null)
    {
        var results = new[] {
            new FirearmHandsInputTranslator().ReloadExternalMagazine(_controller, predicate),
            new AmmoSelector.CG_FindAmmoForWeapon().method_0(_controller, predicate),
            new ItemUiContext().FindSuitableMagazine(_controller, predicate)
        };
        foreach (var result in results) Check(string.Join(",", result.Select(m => m.Id)) == expected, reason);
    }

    private static void ForeignEnable(int priority)
    {
        var plugin = ForeignQuery.Assembly.GetType("UseItemsAnywhere.Plugin");
        Chainloader.PluginInfos[UseItemsAnywhereCompat.PluginGuid] = new PluginInfo {
            Instance = Activator.CreateInstance(plugin), Metadata = plugin.GetCustomAttribute<BepInPlugin>()
        };
        var harmony = new Harmony("uia-checks-foreign");
        foreach (var consumer in Consumers)
            harmony.Patch(consumer, transpiler: new HarmonyMethod(typeof(Program), nameof(ForeignTranspiler)) { priority = priority });
        foreach (var name in new[] { "IsAtBindablePlace", "IsAtReachablePlace" })
            harmony.Patch(typeof(InventoryController).GetMethod(name), postfix: new HarmonyMethod(typeof(Program), nameof(ForeignReach)) { priority = priority });
    }

    // Independently written adversarial test double: like reviewed UIA 2.1.6,
    // demand the one native query and replace it. No foreign implementation is linked.
    private static IEnumerable<CodeInstruction> ForeignTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var calls = code.Where(i => (i.opcode == OpCodes.Call || i.opcode == OpCodes.Callvirt)
            && i.operand is MethodInfo m && m.DeclaringType == typeof(InventoryController)
            && m.IsGenericMethod && m.Name.StartsWith("GetReachableItemsOfType", StringComparison.Ordinal)).ToArray();
        if (calls.Length != 1) throw new InvalidOperationException("foreign requires exactly one native query");
        var query = (MethodInfo)calls[0].operand;
        calls[0].operand = ForeignQuery.GetMethod(query.ReturnType == typeof(void) ? "CollectReloadItems" : "ReloadItems", BindingFlags.Static | BindingFlags.NonPublic).MakeGenericMethod(typeof(Magazine));
        calls[0].opcode = OpCodes.Call;
        return code;
    }

    private static void ForeignReach(Item item, ref bool __result)
        => __result = ForeignBoundary.Features && ForeignBoundary.Backpack || item.NativeReach;

    // Old Arena's destructive rewrite is a regression fixture, not production.
    private static IEnumerable<CodeInstruction> OldArena(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var call = code.Where(i => i.operand is MethodInfo m && m.DeclaringType == typeof(InventoryController)
            && m.IsGenericMethod && m.GetGenericArguments().Single() == typeof(Magazine)).ToArray();
        if (call.Length != 1) throw new InvalidOperationException("old Arena requires exactly one native query");
        var original = (MethodInfo)call[0].operand;
        call[0].operand = typeof(ForeignBoundary).GetMethod(original.ReturnType == typeof(void) ? "Collect" : "Find").MakeGenericMethod(typeof(Magazine));
        call[0].opcode = OpCodes.Call;
        return code;
    }

    private static void EnableArena(bool old)
    {
        if (old)
            foreach (var method in Consumers) new Harmony("uia-checks-old-arena").Patch(method, transpiler: new HarmonyMethod(typeof(Program), nameof(OldArena)));
        else { ArenaBackpackReload.Enable(); ArenaReachPatches.Enable(); }
    }

    private static void VerifyIlBoundaries()
    {
        var extend = typeof(ArenaBackpackReload).GetMethod("ExtendQuery", BindingFlags.NonPublic | BindingFlags.Static);
        foreach (bool nonAlloc in new[] { true, false })
        {
            var generator = new DynamicMethod("labels", typeof(void), Type.EmptyTypes).GetILGenerator();
            var query = typeof(InventoryController).GetMethod(nonAlloc ? "GetReachableItemsOfTypeNonAlloc" : "GetReachableItemsOfType").MakeGenericMethod(typeof(Magazine));
            var call = new CodeInstruction(OpCodes.Callvirt, query);
            var label = generator.DefineLabel(); call.labels.Add(label);
            call.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
            call.blocks.Add(new ExceptionBlock(ExceptionBlockType.EndExceptionBlock));
            var result = ((IEnumerable<CodeInstruction>)extend.Invoke(null, new object[] { new[] { call }, generator, nonAlloc })).ToArray();
            Check(result.Count(i => Equals(i.operand, query)) == 1 && call.opcode == OpCodes.Callvirt, "native query remains available for later foreign transpiler");
            Check(result.First().labels.Contains(label) && result.First().blocks.Single().blockType == ExceptionBlockType.BeginExceptionBlock
                && result.Last().blocks.Single().blockType == ExceptionBlockType.EndExceptionBlock, "branch and exception boundaries enclose extension");
            foreach (var invalid in new[] { new CodeInstruction[0], new[] { new CodeInstruction(OpCodes.Callvirt, query), new CodeInstruction(OpCodes.Callvirt, query) } })
            {
                bool refused = false;
                try { extend.Invoke(null, new object[] { invalid, generator, nonAlloc }); }
                catch (TargetInvocationException ex) { refused = ex.InnerException is InvalidOperationException; }
                Check(refused && invalid.All(i => i.opcode == OpCodes.Callvirt && Equals(i.operand, query)), "query drift is rejected before IL mutation");
            }
            if (ForeignQuery != null)
            {
                var foreign = ForeignQuery.GetMethod(nonAlloc ? "CollectReloadItems" : "ReloadItems", BindingFlags.Static | BindingFlags.NonPublic)?.MakeGenericMethod(typeof(Magazine));
                if (foreign != null)
                {
                    var unchanged = new CodeInstruction(OpCodes.Call, foreign);
                    var delegated = ((IEnumerable<CodeInstruction>)extend.Invoke(null, new object[] { new[] { unchanged }, generator, nonAlloc })).ToArray();
                    Check(delegated.Length == 1 && ReferenceEquals(unchanged, delegated[0]), "foreign-first transpilation delegates without adding instructions");
                    bool refused = false;
                    try { extend.Invoke(null, new object[] { new[] { new CodeInstruction(OpCodes.Callvirt, query), unchanged }, generator, nonAlloc }); }
                    catch (TargetInvocationException ex) { refused = ex.InnerException is InvalidOperationException; }
                    Check(refused, "mixed foreign-native queries fail closed");
                }
            }
        }
    }

    public static int Main(string[] args)
    {
        try
        {
            var mode = args.Length == 0 ? "absent" : args[0];
            bool old = mode == "old";
            bool arenaFirst = args.Length < 2 || args[1] == "arena-first";
            int priority = args.Length > 2 && args[2] == "first" ? Priority.First : Priority.Last;
            Fresh();
            if (mode != "absent") BuildForeign(mode == "drift", mode == "newer" ? "2.1.7" : "2.1.6");
            if (mode == "drift")
            {
                var type = ForeignQuery.Assembly.GetType("UseItemsAnywhere.Plugin");
                Chainloader.PluginInfos[UseItemsAnywhereCompat.PluginGuid] = new PluginInfo { Instance = Activator.CreateInstance(type), Metadata = type.GetCustomAttribute<BepInPlugin>() };
                Check(UseItemsAnywhereCompat.OwnsItemAccess, "contract drift never reclaims foreign slots");
                Check(!UseItemsAnywhereCompat.TryCollectMagazines(_controller, new List<Magazine>(), null), "drift hides unavailable HUD candidates");
                Check(ArenaMode.Plugin.ModLogger.Warnings.Count == 1, "drift has one explicit warning");
                UseItemsAnywhereCompat.Enable(); Check(ArenaMode.Plugin.ModLogger.Warnings.Count == 1, "contract warning is bounded");
            }
            else
            {
                bool rejected = false;
                try
                {
                    if (arenaFirst || mode == "absent") EnableArena(old);
                    if (mode != "absent") ForeignEnable(priority);
                    if (!arenaFirst && mode != "absent") EnableArena(old);
                }
                catch (Exception ex) when (old)
                { rejected = true; Console.WriteLine("EXPECTED_OLD_COMPOSITION_FAILURE: " + ex.GetBaseException().Message); }
                if (old) Check(rejected, "old composition fails in either loading order");
                else
                {
                    Check(!ArenaMode.Plugin.ModLogger.Warnings.Any(w => w.Contains("extension unavailable")), "fixed composition installs every consumer");
                    foreach (var target in Consumers)
                        Check(Harmony.GetPatchInfo(target).Transpilers.Count == (mode == "absent" ? 1 : 2), "every overlapping transpiler is installed");
                    VerifyIlBoundaries();
                    if (mode == "absent")
                    {
                        Results("rig,bag", "Arena absent-mod candidates preserved");
                        ArenaSession.IsActive = false; Results("rig", "ordinary native candidates unchanged");
                        ArenaSession.IsActive = true; Settings.UseItemsAnywhere.Value = false; Results("rig", "Arena setting off retains native candidates");
                        Settings.UseItemsAnywhere.Value = true;
                        var med = ((ContainerCollection)_controller.Inventory.Equipment.GetSlot(EquipmentSlot.Backpack).ContainedItem).Items.OfType<Meds>().Single();
                        Check(_controller.IsAtBindablePlace(med) && _controller.IsAtReachablePlace(med), "absent-mod Arena reach preserved");
                        ArenaSession.IsActive = false;
                        Check(!_controller.IsAtBindablePlace(med) && !_controller.IsAtReachablePlace(med), "ordinary reach unchanged");
                    }
                    else
                    {
                        ForeignBoundary.Features = true; ForeignBoundary.Backpack = false;
                        foreach (bool active in new[] { false, true }) foreach (bool arenaSetting in new[] { false, true })
                        {
                            ArenaSession.IsActive = active; Settings.UseItemsAnywhere.Value = arenaSetting;
                            Results("rig", "foreign restricted slots survive Arena on/off");
                            Check(!ArenaBackpackReload.AppendsBackpackFor(_controller), "HUD never claims an Arena append under foreign ownership");
                            var med = ((ContainerCollection)_controller.Inventory.Equipment.GetSlot(EquipmentSlot.Backpack).ContainedItem).Items.OfType<Meds>().Single();
                            Check(!_controller.IsAtBindablePlace(med) && !_controller.IsAtReachablePlace(med), "foreign reach refusal survives either postfix order");
                            ForeignBoundary.Backpack = true;
                            Results("bag", "foreign slots replace native slots without Arena duplicate");
                            Check(_controller.IsAtBindablePlace(med) && _controller.IsAtReachablePlace(med), "foreign reach allowance preserved");
                            var hud = new List<Magazine>();
                            Check(UseItemsAnywhereCompat.TryCollectMagazines(_controller, hud, null) && string.Join(",", hud.Select(m => m.Id)) == "bag", "HUD follows current foreign candidate query");
                            Results("", "foreign predicate preserved", m => false);
                            ForeignBoundary.Features = false; Results("rig", "disabled foreign feature keeps its native fallback");
                            ForeignBoundary.Features = true; ForeignBoundary.Backpack = false;
                        }
                        if (mode == "newer")
                        {
                            var info = Chainloader.PluginInfos[UseItemsAnywhereCompat.PluginGuid];
                            info.Instance = Activator.CreateInstance(info.Instance.GetType());
                            Check(UseItemsAnywhereCompat.OwnsItemAccess, "new plugin instance retains foreign ownership");
                            Check(ArenaMode.Plugin.ModLogger.Warnings.Count(w => w.Contains("newer than reviewed")) == 1, "newer reviewed-contract version warns once");
                        }
                    }
                }
            }
            Console.WriteLine($"USE_ITEMS_ANYWHERE_CHECKS_PASSED: {mode}, {string.Join(" ", args.Skip(1))}; {_checks} assertions; installed Harmony, explicit EFT/loader doubles");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void BuildForeign(bool drift, string version)
    {
        var assembly = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("UseItemsAnywhere"), AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("fixture");
        var plugin = module.DefineType("UseItemsAnywhere.Plugin", TypeAttributes.Public);
        plugin.SetCustomAttribute(new CustomAttributeBuilder(typeof(BepInPlugin).GetConstructor(new[] { typeof(string), typeof(string), typeof(string) }), new object[] { UseItemsAnywhereCompat.PluginGuid, "Test boundary, not foreign implementation", version }));
        plugin.DefineDefaultConstructor(MethodAttributes.Public); plugin.CreateType();
        var queries = module.DefineType("UseItemsAnywhere.Patches.InventorySlotQueries", TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed);
        foreach (bool nonAlloc in new[] { false, true })
        {
            var method = queries.DefineMethod(nonAlloc ? "CollectReloadItems" : "ReloadItems", MethodAttributes.Assembly | MethodAttributes.Static);
            var item = method.DefineGenericParameters("TItem")[0]; item.SetBaseTypeConstraint(typeof(Item));
            var parameters = nonAlloc ? new[] { typeof(InventoryController), typeof(IList<>).MakeGenericType(item), typeof(Predicate<>).MakeGenericType(item) }
                : new[] { typeof(InventoryController), typeof(Predicate<>).MakeGenericType(item) };
            method.SetParameters(parameters); method.SetReturnType(nonAlloc ? typeof(void) : typeof(IEnumerable<>).MakeGenericType(item));
            var il = method.GetILGenerator();
            for (int i = 0; i < parameters.Length; i++) il.Emit(OpCodes.Ldarg, i);
            il.Emit(OpCodes.Call, typeof(ForeignBoundary).GetMethod(nonAlloc ? "Collect" : "Find").MakeGenericMethod(item)); il.Emit(OpCodes.Ret);
        }
        ForeignQuery = queries.CreateType();
        if (drift)
        {
            // A same-name assembly with a missing reviewed query type models a
            // future binary that keeps the GUID but changes its API.
            var second = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("UseItemsAnywhere"), AssemblyBuilderAccess.Run).DefineDynamicModule("drift");
            var changed = second.DefineType("UseItemsAnywhere.Plugin", TypeAttributes.Public);
            changed.SetCustomAttribute(new CustomAttributeBuilder(typeof(BepInPlugin).GetConstructor(new[] { typeof(string), typeof(string), typeof(string) }), new object[] { UseItemsAnywhereCompat.PluginGuid, "Changed API boundary", version }));
            changed.DefineDefaultConstructor(MethodAttributes.Public); changed.CreateType();
            ForeignQuery = second.DefineType("ChangedQuery", TypeAttributes.Public).CreateType();
        }
    }
}

public static class ForeignBoundary
{
    public static bool Features = true, Backpack;
    public static void Collect<T>(InventoryController controller, IList<T> output, Predicate<T> predicate) where T : Item
        => controller.GetAcceptableItemsNonAlloc(Features && Backpack ? new[] { EquipmentSlot.Backpack }
            : new[] { EquipmentSlot.Pockets, EquipmentSlot.TacticalVest }, output, predicate);
    public static IEnumerable<T> Find<T>(InventoryController controller, Predicate<T> predicate) where T : Item
    { var result = new List<T>(); Collect(controller, result, predicate); return result; }
}
