using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using EFT;
using EFT.HealthSystem;
using EFT.Ballistics;
using HarmonyLib;
using ArenaMode.Compat;

internal sealed record Fixture(BaseUnityPlugin Instance, BepInPlugin Metadata, MethodInfo[] Methods)
{
    internal void Register()
    {
        var targets = ArenaDeminvincibilityContract.NativeTargets();
        for (int i = 0; i < 4; ++i) Harmony.Bound[targets[i]] = new() { Prefixes = new() { new() { owner = "Demi", PatchMethod = Methods[i] } } };
        BepInEx.Bootstrap.Chainloader.PluginInfos[ArenaDeminvincibilityContract.PluginGuid] = new() { Instance = Instance, Metadata = Metadata };
    }
}
internal static class FixtureFactory
{
    internal static Fixture Make(string version = "2.2.0", string metadataVersion = null, string guid = "com.hazelify.deminvincibility",
        string assemblyName = "Deminvincibility", string assemblyVersion = "1.0.0.0", string defect = null)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(assemblyName) { Version = new(assemblyVersion) }, AssemblyBuilderAccess.RunAndCollect);
        var module = assembly.DefineDynamicModule("fixture");
        var plugin = module.DefineType(defect == "identity" ? "Other.Plugin" : "Deminvincibility.DeminvicibilityPlugin", TypeAttributes.Public, typeof(BaseUnityPlugin));
        plugin.DefineDefaultConstructor(MethodAttributes.Public);
        plugin.DefineField("UnexpectedState", typeof(bool), FieldAttributes.Public | FieldAttributes.Static);
        if (defect != "attribute") plugin.SetCustomAttribute(new CustomAttributeBuilder(typeof(BepInPlugin).GetConstructor(new[] { typeof(string), typeof(string), typeof(string) }), new object[] { guid, "fixture", version }));
        var prop = plugin.DefineProperty("Keep1Health", PropertyAttributes.None, typeof(ConfigEntry<bool>), Type.EmptyTypes);
        var getter = plugin.DefineMethod("get_Keep1Health", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName, typeof(ConfigEntry<bool>), Type.EmptyTypes);
        var il = getter.GetILGenerator(); il.Emit(OpCodes.Call, typeof(Program).GetProperty(nameof(Program.Keep)).GetMethod); il.Emit(OpCodes.Ret); prop.SetGetMethod(getter);
        var pluginType = plugin.CreateType();
        var methods = new List<MethodInfo>();
        string[] names = { "Kill", "DestroyBodyPartPatch", "ApplyDamage", "DoFracture" };
        Type[][] args = {
            new[] { typeof(ActiveHealthController), typeof(EDamageType), typeof(Player) },
            new[] { typeof(ActiveHealthController), typeof(Player), typeof(EBodyPart), typeof(EDamageType) },
            new[] { typeof(ActiveHealthController), typeof(Player), typeof(float).MakeByRefType(), typeof(EBodyPart), typeof(DamageInfo) },
            new[] { typeof(ActiveHealthController), typeof(EBodyPart), typeof(Player) }
        };
        for (int i = 0; i < 4; ++i)
        {
            var type = module.DefineType("Deminvincibility.Patches." + names[i], TypeAttributes.Public);
            var method = type.DefineMethod(defect == "name" && i == 0 ? "Changed" : "Prefix", MethodAttributes.Public | MethodAttributes.Static, typeof(bool), args[i]);
            il = method.GetILGenerator();
            if (i == 2) {
                if (defect == "keep-missing") il.Emit(OpCodes.Ldc_I4_0);
                else {
                    il.Emit(OpCodes.Call, pluginType.GetProperty("Keep1Health").GetMethod);
                    il.Emit(OpCodes.Callvirt, typeof(ConfigEntry<bool>).GetProperty("Value").GetMethod);
                    if (defect == "keep-duplicate") {
                        il.Emit(OpCodes.Pop); il.Emit(OpCodes.Call, pluginType.GetProperty("Keep1Health").GetMethod);
                        il.Emit(OpCodes.Callvirt, typeof(ConfigEntry<bool>).GetProperty("Value").GetMethod);
                    }
                }
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Call, typeof(Program).GetMethod(nameof(Program.ForeignDamage)));
            } else {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, typeof(Program).GetMethod(i == 0 ? nameof(Program.ForeignKill) : i == 1 ? nameof(Program.ForeignDestroy) : nameof(Program.ForeignFracture)));
            }
            il.Emit(OpCodes.Ret); methods.Add(type.CreateType().GetMethod(method.Name));
        }
        return new((BaseUnityPlugin)Activator.CreateInstance(pluginType), new(guid, "fixture", metadataVersion ?? version), methods.ToArray());
    }
}
