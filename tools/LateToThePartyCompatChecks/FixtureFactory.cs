using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BepInEx;
using EFT.Interactive;
using EFT.InventoryLogic;
using UnityEngine;

internal sealed record Fixture(BaseUnityPlugin Instance, Type Helpers, Type Discovery, Type Manager, BepInPlugin Metadata)
{
    internal MethodInfo Start => Helpers.GetMethod("StartDestruction");
    internal MethodInfo Destroy => Helpers.GetMethod("DestroyViaLTTP");
    internal MethodInfo Eligible => Helpers.GetMethod("IsEligibleForDestruction");
    internal MethodInfo Found => Discovery.GetMethod("ProcessFoundLooseLootItem");
}

// Independently authored IL fixtures exercise call topology and real production
// prefix bodies through a controlled boundary. No downloaded code is executed.
internal static class FixtureFactory
{
    private static int _fixtureRevision;
    private static readonly Dictionary<string, Assembly> Assemblies = new();
    static FixtureFactory() { AppDomain.CurrentDomain.AssemblyResolve += (_, args) => Assemblies.GetValueOrDefault(args.Name); }
    internal static Fixture Make(string version = "3.1.1", string defect = null)
    {
        if (defect != null && defect != "version") version = new Version(3, 1, 1, ++_fixtureRevision).ToString();
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(defect == "assembly" ? "Wrong" : "LateToTheParty-Client")
            { Version = new(defect == "version" ? "3.1.2" : version) }, AssemblyBuilderAccess.Run);
        Assemblies[assembly.FullName] = assembly;
        var module = assembly.DefineDynamicModule("fixture");
        var plugin = module.DefineType("LateToTheParty.LateToThePartyPlugin", TypeAttributes.Public, typeof(BaseUnityPlugin));
        plugin.DefineDefaultConstructor(MethodAttributes.Public);
        if (defect != "attribute") plugin.SetCustomAttribute(new CustomAttributeBuilder(typeof(BepInPlugin).GetConstructor(new[] { typeof(string), typeof(string), typeof(string) }),
            new object[] { "com.danw.latetotheparty", "fixture", version }));
        var helpers = module.DefineType("LateToTheParty.Helpers.Loot.LootDestructionHelpers", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var discovery = module.DefineType("LateToTheParty.Helpers.Loot.LootDiscoveryHelpers", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var manager = module.DefineType("LateToTheParty.Controllers.LootManager", TypeAttributes.Public);
        var managerCtor = manager.DefineDefaultConstructor(MethodAttributes.Public);
        const MethodAttributes pub = MethodAttributes.Public | MethodAttributes.Static;
        var start = helpers.DefineMethod("StartDestruction", pub, typeof(void), new[] { typeof(Item) });
        var destroy = helpers.DefineMethod("DestroyViaLTTP", pub, defect == "signature" ? typeof(bool) : typeof(void), new[] { typeof(Item) });
        var eligible = helpers.DefineMethod("IsEligibleForDestruction", pub, typeof(bool), new[] { typeof(Item), typeof(IEnumerable<Vector3>), typeof(double) });
        var update = helpers.DefineMethod("UpdateLootEligibility", pub, typeof(void), new[] { typeof(Item), typeof(IEnumerable<Vector3>), typeof(double) });
        var found = discovery.DefineMethod("ProcessFoundLooseLootItem", pub, typeof(void), new[] { typeof(LootItem), typeof(double) });
        var door = helpers.DefineMethod("openNearbyDoorForLoot", MethodAttributes.Private | MethodAttributes.Static, typeof(void), new[] { typeof(Item) });
        var confirm = manager.DefineMethod("ConfirmItemDestruction", MethodAttributes.Public, typeof(void), new[] { typeof(Item) });
        var enumerate = manager.DefineMethod("FindAndDestroyLoot", MethodAttributes.Public, typeof(IEnumerator), new[] { typeof(IEnumerable<Vector3>), typeof(float), typeof(double) });
        var callback = helpers.DefineField("OnDestroyLoot", typeof(Action<Item>), FieldAttributes.Public | FieldAttributes.Static);
        var dispatch = typeof(Program).GetMethod(nameof(Program.Dispatch));
        var record = typeof(Program).GetMethod(nameof(Program.Record));
        void Record(ILGenerator code, string kind) { code.Emit(OpCodes.Ldstr, kind); code.Emit(OpCodes.Call, record); }
        void Guard(ILGenerator code, string method, bool boolean = false)
        {
            var allowed = code.DefineLabel(); code.Emit(OpCodes.Ldstr, method); code.Emit(OpCodes.Ldarg_0); code.Emit(OpCodes.Call, dispatch);
            code.Emit(OpCodes.Brtrue, allowed); if (boolean) code.Emit(OpCodes.Ldc_I4_0); code.Emit(OpCodes.Ret); code.MarkLabel(allowed);
        }
        var il = eligible.GetILGenerator(); Guard(il, eligible.Name, true); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);
        il = update.GetILGenerator();
        if (defect != "eligibility-edge") { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Call, eligible); il.Emit(OpCodes.Pop); }
        il.Emit(OpCodes.Ret);
        il = found.GetILGenerator(); Guard(il, found.Name); Record(il, "discovered"); il.Emit(OpCodes.Ret);
        il = door.GetILGenerator(); Record(il, "door"); il.Emit(OpCodes.Ret);
        il = confirm.GetILGenerator(); Record(il, "confirmed"); il.Emit(OpCodes.Ret);
        il = destroy.GetILGenerator(); Guard(il, destroy.Name, defect == "signature");
        // Two native operations represent the two destructive address cases.
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, typeof(Item).GetField(nameof(Item.Parent)));
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, typeof(ItemAddress).GetMethod("RemoveWithoutRestrictions"));
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, typeof(Item).GetField(nameof(Item.Parent)));
        il.Emit(OpCodes.Callvirt, typeof(ItemAddress).GetMethod(nameof(ItemAddress.GetOwner)));
        il.Emit(OpCodes.Newobj, typeof(RemoveItemEventArgs).GetConstructor(Type.EmptyTypes));
        il.Emit(OpCodes.Callvirt, typeof(IItemOwner).GetMethod("RaiseRemoveEvent"));
        if (defect == "signature") il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);
        il = start.GetILGenerator(); Guard(il, start.Name);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, door);
        if (defect != "destroy-edge") { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, destroy); if (defect == "signature") il.Emit(OpCodes.Pop); }
        il.Emit(OpCodes.Ldsfld, callback); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, typeof(Action<Item>).GetMethod("Invoke"));
        il.Emit(OpCodes.Newobj, managerCtor); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, confirm);
        if (defect == "bypass" || defect == "concrete-bypass")
        {
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, typeof(Item).GetField(nameof(Item.Parent)));
            if (defect == "bypass") { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, typeof(ItemAddress).GetMethod("RemoveWithoutRestrictions")); }
            else { il.Emit(OpCodes.Callvirt, typeof(ItemAddress).GetMethod("GetOwner")); il.Emit(OpCodes.Castclass, typeof(ItemController));
                il.Emit(OpCodes.Newobj, typeof(RemoveItemEventArgs).GetConstructor(Type.EmptyTypes)); il.Emit(OpCodes.Callvirt, typeof(ItemController).GetMethod("RaiseRemoveEvent")); }
        }
        MethodBuilder delayed = null;
        if (defect == "delayed-bypass")
        {
            var state = helpers.DefineNestedType("DelayedCleanup", TypeAttributes.NestedPrivate);
            var step = state.DefineMethod("MoveNext", MethodAttributes.Private, typeof(void), Type.EmptyTypes);
            var code = step.GetILGenerator(); code.Emit(OpCodes.Ldnull); code.Emit(OpCodes.Ldnull);
            code.Emit(OpCodes.Callvirt, typeof(ItemAddress).GetMethod("RemoveWithoutRestrictions")); code.Emit(OpCodes.Ret);
            delayed = helpers.DefineMethod("Delayed", MethodAttributes.Private | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
            delayed.SetCustomAttribute(new CustomAttributeBuilder(typeof(AsyncStateMachineAttribute).GetConstructor(new[] { typeof(Type) }), new object[] { state }));
            delayed.GetILGenerator().Emit(OpCodes.Ret); state.CreateType();
            il.Emit(OpCodes.Call, delayed);
        }
        il.Emit(OpCodes.Ret);
        var iterator = manager.DefineNestedType("CleanupIterator", TypeAttributes.NestedPrivate | TypeAttributes.Sealed, typeof(object), new[] { typeof(IEnumerator) });
        var ctor = iterator.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, new[] { typeof(int) });
        il = ctor.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)); il.Emit(OpCodes.Ret);
        var move = iterator.DefineMethod("MoveNext", MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.NewSlot, typeof(bool), Type.EmptyTypes);
        il = move.GetILGenerator();
        if (defect != "discovery-edge") { il.Emit(OpCodes.Ldsfld, typeof(Program).GetField(nameof(Program.QueueLoot))); il.Emit(OpCodes.Ldc_R8, 1d); il.Emit(OpCodes.Call, found); }
        il.Emit(OpCodes.Ldsfld, typeof(Program).GetField(nameof(Program.QueueItem))); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ldc_R8, 1d); il.Emit(OpCodes.Call, update);
        if (defect != "queue-edge") { il.Emit(OpCodes.Ldsfld, typeof(Program).GetField(nameof(Program.QueueItem))); il.Emit(OpCodes.Call, start); }
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
        iterator.DefineMethodOverride(move, typeof(IEnumerator).GetMethod("MoveNext"));
        var current = iterator.DefineMethod("get_Current", MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.NewSlot, typeof(object), Type.EmptyTypes);
        il = current.GetILGenerator(); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ret); iterator.DefineMethodOverride(current, typeof(IEnumerator).GetProperty("Current").GetMethod);
        var reset = iterator.DefineMethod("Reset", MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.NewSlot, typeof(void), Type.EmptyTypes);
        reset.GetILGenerator().Emit(OpCodes.Ret); iterator.DefineMethodOverride(reset, typeof(IEnumerator).GetMethod("Reset"));
        il = enumerate.GetILGenerator(); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Newobj, ctor); il.Emit(OpCodes.Ret);
        enumerate.SetCustomAttribute(new CustomAttributeBuilder(typeof(IteratorStateMachineAttribute).GetConstructor(new[] { typeof(Type) }), new object[] { iterator }));
        var h = helpers.CreateType(); var d = discovery.CreateType(); var m = manager.CreateType(); iterator.CreateType(); var p = plugin.CreateType();
        h.GetField(callback.Name).SetValue(null, (Action<Item>)(_ => Program.Record("event")));
        return new((BaseUnityPlugin)Activator.CreateInstance(p), h, d, m, new("com.danw.latetotheparty", "fixture", version));
    }
}
