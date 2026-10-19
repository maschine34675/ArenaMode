using System.Reflection;
using System.Reflection.Emit;
using ArenaMode.Compat;
using BepInEx;
using EFT;
using HarmonyLib;

internal sealed record Fixture(BaseUnityPlugin Instance, BepInPlugin Metadata, MethodInfo[] Methods, object Config)
{
    internal void Set(string name, bool value) => Config.GetType().GetField("<" + name + ">k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(Config, value);
    internal bool Get(string name) => (bool)Config.GetType().GetField("<" + name + ">k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(Config);
    internal void Register()
    {
        var state = new PatchInfo();
        if (Get("NoInertia")) state.Prefixes.Add(new() { owner = "foreign", PatchMethod = Methods[0] });
        Harmony.Bound[typeof(MovementContext).GetMethod("GetNewState")] = state;
        Harmony.Bound[typeof(MovementContext).GetMethod("ManualUpdate")] = new() { Postfixes = new() { new() { owner = "foreign", PatchMethod = Methods[1] } } };
        BepInEx.Bootstrap.Chainloader.PluginInfos[ArenaClassicMovementContract.PluginGuid] = new() { Instance = Instance, Metadata = Metadata };
    }
}

internal static class FixtureFactory
{
    internal static Fixture Make(string version = "1.1.3", string metadataVersion = null, string assemblyVersion = "1.0.0.0",
        string assemblyName = "ClassicMovement", string guid = "com.boogle.classicmovement", string defect = null, bool noInertia = true)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(assemblyName) { Version = new(assemblyVersion) }, AssemblyBuilderAccess.RunAndCollect);
        var module = assembly.DefineDynamicModule("independent-fixture");
        var config = module.DefineType("OldTarkovMovement.OldTarkovMovementConfig", TypeAttributes.Public);
        config.DefineDefaultConstructor(MethodAttributes.Public);
        foreach (var name in new[] { "NostalgiaMode", "NoInertia" })
            config.DefineField("<" + name + ">k__BackingField", defect == "config-type" && name == "NoInertia" ? typeof(int) : typeof(bool), FieldAttributes.Private);
        var configType = config.CreateType();
        var plugin = module.DefineType(defect == "identity" ? "OldTarkovMovement.Impostor" : "OldTarkovMovement.Plugin", TypeAttributes.Public, typeof(BaseUnityPlugin));
        plugin.DefineDefaultConstructor(MethodAttributes.Public);
        plugin.DefineField("ModConfig", configType, FieldAttributes.Public | FieldAttributes.Static);
        if (defect != "attribute") plugin.SetCustomAttribute(new CustomAttributeBuilder(typeof(BepInPlugin).GetConstructor(new[] { typeof(string), typeof(string), typeof(string) }), new object[] { guid, "fixture", version }));
        var pluginType = plugin.CreateType();
        var configValue = Activator.CreateInstance(configType);
        if (defect != "config-type") configType.GetField("<NoInertia>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(configValue, noInertia);
        if (defect != "config-null") pluginType.GetField("ModConfig").SetValue(null, configValue);
        var methods = new List<MethodInfo>();
        foreach (bool state in new[] { true, false })
        {
            var type = module.DefineType("OldTarkovMovement." + (state ? "StateReplacer" : "SmoothSpeedFix"), TypeAttributes.Public);
            var args = state ? new[] { typeof(MovementContext), typeof(BaseMovementState).MakeByRefType(), typeof(EPlayerState), typeof(bool) } : new[] { typeof(MovementContext), typeof(float) };
            var result = state ? typeof(bool) : typeof(void);
            var attrs = MethodAttributes.Private | (defect == "instance" && state ? 0 : MethodAttributes.Static);
            var method = type.DefineMethod(defect == "name" && state ? "Changed" : "Prefix", attrs, defect == "result" && state ? typeof(void) : result, args);
            var il = method.GetILGenerator();
            if (defect is "instance" or "result") { if (state && defect != "result") il.Emit(OpCodes.Ldc_I4_1); }
            else
            {
                il.Emit(OpCodes.Ldarg_0);
                if (state) il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, typeof(Program).GetMethod(state ? nameof(Program.ForeignState) : nameof(Program.ForeignSpeed)));
            }
            il.Emit(OpCodes.Ret);
            methods.Add(type.CreateType().GetMethod(method.Name, BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance));
        }
        return new((BaseUnityPlugin)Activator.CreateInstance(pluginType), new(guid, "fixture", metadataVersion ?? version), methods.ToArray(), configValue);
    }
}
