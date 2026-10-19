using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using EFT;
using UnityEngine;

// Authored call-shape fixture, not downloaded code. The shared method dispatches
// our controlled Harmony boundary; it never touches a native Unity object.
internal sealed record Fixture(BaseUnityPlugin Instance, MethodInfo Remove, MethodInfo Manual,
    MethodInfo Queue, BepInPlugin Metadata);

internal static class FixtureFactory
{
    internal static Fixture Make(string version = "4.1.3", string metadataVersion = null,
        string assemblyVersion = null, string assemblyName = "RemoveTheDead", string guid = "com.jbs4bmx.RemoveTheDead",
        string defect = null)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(assemblyName)
            { Version = new(assemblyVersion ?? version) }, AssemblyBuilderAccess.RunAndCollect);
        var module = assembly.DefineDynamicModule("review-fixture");
        var plugin = module.DefineType("RemoveTheDead.RTDPlugin", TypeAttributes.Public,
            defect == "base" ? typeof(object) : typeof(BaseUnityPlugin));
        plugin.DefineDefaultConstructor(MethodAttributes.Public);
        if (defect != "attribute") plugin.SetCustomAttribute(new CustomAttributeBuilder(
            typeof(BepInPlugin).GetConstructor(new[] { typeof(string), typeof(string), typeof(string) }),
            new object[] { guid, "test fixture", version }));
        var settings = plugin.DefineField("UntouchedConfig", typeof(int), FieldAttributes.Public);
        var timer = plugin.DefineField("UntouchedTimer", typeof(float), FieldAttributes.Public);
        var remove = plugin.DefineMethod("RemoveBotAndWeapons", MethodAttributes.Private,
            defect == "signature" ? typeof(bool) : typeof(void), new[] { typeof(BotOwner) });
        var weapons = plugin.DefineMethod("RemoveNearbyWeapons", MethodAttributes.Private, typeof(void), new[] { typeof(Vector3), typeof(float) });
        var manual = plugin.DefineMethod("RunRemovalNow", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        var queue = plugin.DefineMethod("QueueRemoval", MethodAttributes.Private, typeof(IEnumerator), Type.EmptyTypes);
        var update = plugin.DefineMethod("Update", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        var button = plugin.DefineMethod("RTDButtonActionDrawer", MethodAttributes.Public, typeof(void), new[] { typeof(ConfigEntryBase) });
        var helper = plugin.DefineMethod("NewCleanupHelper", MethodAttributes.Private, typeof(void), Type.EmptyTypes);

        var state = plugin.DefineNestedType("RemovalIterator", TypeAttributes.NestedPrivate | TypeAttributes.Sealed,
            typeof(object), new[] { typeof(IEnumerator) });
        var owner = state.DefineField("Owner", plugin, FieldAttributes.Public);
        var ctor = state.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard,
            new[] { defect == "iterator" ? typeof(bool) : typeof(int) });
        var il = ctor.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)); il.Emit(OpCodes.Ret);
        var move = state.DefineMethod("MoveNext", MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final
            | MethodAttributes.NewSlot, typeof(bool), Type.EmptyTypes);
        il = move.GetILGenerator();
        if (defect != "auto-chain")
        {
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, owner); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Call, remove);
            if (defect == "signature") il.Emit(OpCodes.Pop);
        }
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
        state.DefineMethodOverride(move, typeof(IEnumerator).GetMethod(nameof(IEnumerator.MoveNext)));
        var current = state.DefineMethod("get_Current", MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final
            | MethodAttributes.NewSlot | MethodAttributes.SpecialName, typeof(object), Type.EmptyTypes);
        il = current.GetILGenerator(); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ret);
        state.DefineMethodOverride(current, typeof(IEnumerator).GetProperty(nameof(IEnumerator.Current)).GetMethod);
        var reset = state.DefineMethod("Reset", MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final
            | MethodAttributes.NewSlot, typeof(void), Type.EmptyTypes);
        reset.GetILGenerator().Emit(OpCodes.Ret);
        state.DefineMethodOverride(reset, typeof(IEnumerator).GetMethod(nameof(IEnumerator.Reset)));
        if (defect != "iterator") queue.SetCustomAttribute(new CustomAttributeBuilder(
            typeof(IteratorStateMachineAttribute).GetConstructor(new[] { typeof(Type) }), new object[] { state }));
        il = queue.GetILGenerator(); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Newobj, ctor);
        il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Stfld, owner); il.Emit(OpCodes.Ret);

        var destroy = typeof(UnityEngine.Object).GetMethod(nameof(UnityEngine.Object.Destroy));
        il = helper.GetILGenerator(); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Call, destroy); il.Emit(OpCodes.Ret);
        il = weapons.GetILGenerator(); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Call, destroy); il.Emit(OpCodes.Ret);
        il = remove.GetILGenerator();
        var permitted = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, typeof(Program).GetMethod(nameof(Program.BeforeForeignRemoval)));
        il.Emit(OpCodes.Brtrue, permitted);
        if (defect == "signature") il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret); il.MarkLabel(permitted);
        il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Call, destroy);
        if (defect != "weapon-chain")
        {
            var vector = il.DeclareLocal(typeof(Vector3));
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, vector); il.Emit(OpCodes.Ldc_R4, 3f); il.Emit(OpCodes.Call, weapons);
        }
        if (defect == "signature") il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il = manual.GetILGenerator();
        if (defect == "bypass") { il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Call, destroy); }
        if (defect == "helper-bypass") { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, helper); }
        if (defect != "manual-chain")
        {
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Call, remove);
            if (defect == "signature") il.Emit(OpCodes.Pop);
        }
        il.Emit(OpCodes.Ret);
        il = update.GetILGenerator();
        if (defect != "update-chain") { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, queue); il.Emit(OpCodes.Pop); }
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, manual); il.Emit(OpCodes.Ret);
        il = button.GetILGenerator();
        if (defect != "button-chain") { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, manual); }
        il.Emit(OpCodes.Ret);
        var type = plugin.CreateType(); state.CreateType();
        if (defect == "base") return new(null, type.GetMethod("RemoveBotAndWeapons", BindingFlags.Instance | BindingFlags.NonPublic),
            type.GetMethod("RunRemovalNow"), type.GetMethod("QueueRemoval", BindingFlags.Instance | BindingFlags.NonPublic), new(guid, "fixture", metadataVersion ?? version));
        var instance = (BaseUnityPlugin)Activator.CreateInstance(type);
        type.GetField(settings.Name).SetValue(instance, 37);
        type.GetField(timer.Name).SetValue(instance, 721.5f);
        return new(instance, type.GetMethod(remove.Name, BindingFlags.Instance | BindingFlags.NonPublic),
            type.GetMethod(manual.Name), type.GetMethod(queue.Name, BindingFlags.Instance | BindingFlags.NonPublic), new(guid, "fixture", metadataVersion ?? version));
    }
}
