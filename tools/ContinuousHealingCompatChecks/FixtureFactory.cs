using System.Reflection;
using System.Reflection.Emit;
using ArenaMode.Compat;
using BepInEx;
using EFT;
using EFT.HealthSystem;
using HarmonyLib;

internal sealed record Fixture(BaseUnityPlugin Instance, BepInPlugin Metadata, MethodInfo[] Prefixes)
{
    internal void Register()
    {
        var targets = new[] {
            typeof(Player.MedsController.MedsInHandsOperation).GetMethod("OnEffectRemoved"),
            typeof(Player.MedsController).GetMethod("Spawn"), typeof(PlayerHealthController).GetMethod("CancelApplyingItem")
        };
        for (int i = 0; i < targets.Length; ++i)
            Harmony.Bound[targets[i]] = new() { Prefixes = new() { new() { owner = "foreign", PatchMethod = Prefixes[i] } } };
        BepInEx.Bootstrap.Chainloader.PluginInfos[ArenaContinuousHealingContract.PluginGuid] = new() { Instance = Instance, Metadata = Metadata };
    }
}

internal static class FixtureFactory
{
    // Independent method-shape fixtures call the harness sentinel. They contain
    // no downloaded implementation and never load a game or foreign assembly.
    internal static Fixture Make(string version = "1.6.3", string metadataVersion = null,
        string assemblyVersion = null, string assemblyName = "ContinuousHealing", string guid = "com.lacyway.ch", string defect = null)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(assemblyName)
            { Version = new(assemblyVersion ?? version) }, AssemblyBuilderAccess.RunAndCollect);
        var module = assembly.DefineDynamicModule("independent-fixture");
        var plugin = module.DefineType(defect == "identity" ? "ContinuousHealing.OtherPlugin" : "ContinuousHealing.CH_Plugin",
            TypeAttributes.Public, typeof(BaseUnityPlugin));
        plugin.DefineDefaultConstructor(MethodAttributes.Public);
        if (defect != "attribute") plugin.SetCustomAttribute(new CustomAttributeBuilder(
            typeof(BepInPlugin).GetConstructor(new[] { typeof(string), typeof(string), typeof(string) }),
            new object[] { guid, "fixture", version }));
        var pluginType = plugin.CreateType();
        var methods = new List<MethodInfo>();
        var names = new[] { "CH_EndHeal_Patch", "CH_StartHeal_Patch", "CH_CancelHeal_Patch" };
        for (int i = 0; i < names.Length; ++i)
        {
            var type = module.DefineType("ContinuousHealing.Patches." + names[i], TypeAttributes.Public);
            var args = i == 0 ? new[] { typeof(Player.MedsController.MedsInHandsOperation), typeof(IHealthEffect) } : new[] { typeof(Player) };
            var result = i == 0 ? typeof(bool) : typeof(void);
            var attrs = MethodAttributes.Public | (defect == "instance" && i == 0 ? 0 : MethodAttributes.Static);
            var method = type.DefineMethod(defect == "name" && i == 2 ? "MovedPrefix" : "Prefix", attrs,
                defect == "result" && i == 0 ? typeof(void) : result, args);
            var il = method.GetILGenerator();
            if (defect == "instance" || defect == "result")
            {
                if (result == typeof(bool) && defect != "result") il.Emit(OpCodes.Ldc_I4_1);
            }
            else
            {
                il.Emit(OpCodes.Ldarg_0);
                if (i == 0) il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, typeof(Program).GetMethod(i == 0 ? nameof(Program.ForeignEnd)
                    : i == 1 ? nameof(Program.ForeignStart) : nameof(Program.ForeignCancel)));
            }
            il.Emit(OpCodes.Ret);
            methods.Add(type.CreateType().GetMethod(method.Name));
        }
        return new((BaseUnityPlugin)Activator.CreateInstance(pluginType), new(guid, "fixture", metadataVersion ?? version), methods.ToArray());
    }
}
