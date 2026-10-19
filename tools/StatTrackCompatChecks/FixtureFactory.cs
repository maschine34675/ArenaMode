using System.Reflection;
using System.Reflection.Emit;
using ArenaMode.Compat;
using BepInEx;
using EFT;
using EFT.Ballistics;
using EFT.InventoryLogic;
using HarmonyLib;

internal sealed record Fixture(BaseUnityPlugin Instance, BepInPlugin Metadata, MethodInfo[] Hooks)
{
    internal static MethodInfo[] Targets => new[] {
        typeof(Weapon).GetMethod("OnShot"), typeof(Player).GetMethod("OnBeenKilledByAggressor"),
        typeof(Player).GetMethod("OnHealthApplyDamage"), typeof(GameWorld).GetMethod("UnregisterPlayer")
    };
    internal void Register()
    {
        for (int i = 0; i < Hooks.Length; ++i)
            Harmony.Bound[Targets[i]] = new() { Postfixes = new() { new() { owner = "foreign", PatchMethod = Hooks[i] } } };
        BepInEx.Bootstrap.Chainloader.PluginInfos[ArenaStatTrackContract.PluginGuid] = new() { Instance = Instance, Metadata = Metadata };
    }
}

internal static class FixtureFactory
{
    private static int _revision;
    // Independent counter fixtures expose the verified public seams. They do
    // not contain the downloaded mod's algorithms or execute its assembly.
    internal static Fixture Make(string version = "2.1.1", string metadataVersion = null, string guid = "com.acidphantasm.stattrack",
        string assemblyName = "acidphantasm-stattrack", string defect = null)
    {
        string attributedVersion = version + "." + (++_revision);
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(assemblyName)
            { Version = defect == "assembly-version" ? new Version(999, 0, 0, 0) : new Version(attributedVersion) }, AssemblyBuilderAccess.RunAndCollect);
        var module = assembly.DefineDynamicModule("independent-counter-fixture");
        var plugin = module.DefineType(defect == "identity" ? "StattrackClient.OtherPlugin" : "StattrackClient.Plugin", TypeAttributes.Public, typeof(BaseUnityPlugin));
        plugin.DefineDefaultConstructor(MethodAttributes.Public);
        if (defect != "attribute") plugin.SetCustomAttribute(new CustomAttributeBuilder(
            typeof(BepInPlugin).GetConstructor(new[] { typeof(string), typeof(string), typeof(string) }), new object[] { guid, "fixture", attributedVersion }));
        var pluginType = plugin.CreateType();
        var utility = module.DefineType("StattrackClient.Utils.JsonFileUtils", TypeAttributes.Public);
        var addArgs = new[] { typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(float) };
        var add = utility.DefineMethod("TemporaryAddData", MethodAttributes.Public | MethodAttributes.Static, typeof(void), addArgs);
        var il = add.GetILGenerator();
        for (int i = 0; i < addArgs.Length; ++i) il.Emit(OpCodes.Ldarg, i);
        il.Emit(OpCodes.Call, typeof(Program).GetMethod(nameof(Program.Add))); il.Emit(OpCodes.Ret);
        var save = utility.DefineMethod("SaveRaidEndInServer", MethodAttributes.Private | MethodAttributes.Static, typeof(Task), Type.EmptyTypes);
        il = save.GetILGenerator(); il.Emit(OpCodes.Call, typeof(Program).GetMethod(nameof(Program.BeginSave))); il.Emit(OpCodes.Ret);
        var load = utility.DefineMethod("LoadFromServer", MethodAttributes.Public | MethodAttributes.Static, typeof(Task), Type.EmptyTypes);
        il = load.GetILGenerator(); il.Emit(OpCodes.Call, typeof(Task).GetProperty(nameof(Task.CompletedTask)).GetMethod); il.Emit(OpCodes.Ret);
        var merge = utility.DefineMethod("EndRaidMergeData", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        il = merge.GetILGenerator(); il.Emit(OpCodes.Call, typeof(Program).GetMethod(nameof(Program.Merge)));
        il.Emit(OpCodes.Call, save); il.Emit(OpCodes.Pop); il.Emit(OpCodes.Ret);
        MethodBuilder accessor = null;
        if (defect == "extra-helper-writer" || defect == "nested-writer" || defect == "extra-accessor")
        {
            var field = utility.DefineField("_persistentTestField", typeof(int), FieldAttributes.Private | FieldAttributes.Static);
            if (defect == "extra-accessor")
            {
                accessor = utility.DefineMethod("get_WeaponInfoForRaid", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
                il = accessor.GetILGenerator(); il.Emit(OpCodes.Ldsfld, field); il.Emit(OpCodes.Ret);
            }
            else
            {
                TypeBuilder writer = defect == "nested-writer" ? utility.DefineNestedType("NewWriter", TypeAttributes.NestedPublic) : utility;
                var method = writer.DefineMethod("RecordDamage", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
                il = method.GetILGenerator(); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Stsfld, field); il.Emit(OpCodes.Ret);
                if (writer != utility) writer.CreateType();
            }
        }
        var utilityType = utility.CreateType();
        var addMethod = utilityType.GetMethod(add.Name); var mergeMethod = utilityType.GetMethod(merge.Name);
        var names = new[] { "WeaponOnShotPatch", "PlayerPatch", "PlayerPatch2", "GameWorldPatch" };
        var argSets = new[] { new[] { typeof(Weapon) }, new[] { typeof(Player), typeof(IPlayer), typeof(DamageInfo), typeof(EBodyPart), typeof(EDamageType) },
            new[] { typeof(Player), typeof(EBodyPart), typeof(float), typeof(DamageInfo) }, new[] { typeof(IPlayer) } };
        var hooks = new List<MethodInfo>();
        for (int n = 0; n < 4; ++n)
        {
            var type = module.DefineType("StattrackClient.Patches." + names[n], TypeAttributes.Public);
            var method = type.DefineMethod(defect == "method-name" && n == 2 ? "Changed" : "PatchPostfix",
                MethodAttributes.Private | MethodAttributes.Static, typeof(void), argSets[n]);
            il = method.GetILGenerator();
            if (n == 3) { il.Emit(OpCodes.Call, mergeMethod); }
            else
            {
                foreach (var key in new[] { "instance", "template" })
                {
                    il.Emit(OpCodes.Ldstr, key);
                    for (int flag = 0; flag < 6; ++flag) il.Emit(flag == (n == 0 ? 2 : n == 1 ? 0 : 3) ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ldc_R4, n == 2 ? 25f : 0f); il.Emit(OpCodes.Call, addMethod);
                }
            }
            il.Emit(OpCodes.Ret); hooks.Add(type.CreateType().GetMethod(method.Name, BindingFlags.NonPublic | BindingFlags.Static));
        }
        if (defect == "extra-producer" || defect == "extra-merge" || defect == "extra-accessor")
        {
            var type = module.DefineType("StattrackClient.Patches.NewCallback", TypeAttributes.Public);
            var method = type.DefineMethod("Postfix", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
            il = method.GetILGenerator();
            if (defect == "extra-producer")
            { il.Emit(OpCodes.Ldstr, "extra"); for (int n = 0; n < 6; ++n) il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldc_R4, 0f); il.Emit(OpCodes.Call, addMethod); }
            else if (defect == "extra-accessor") { il.Emit(OpCodes.Call, utilityType.GetMethod(accessor.Name)); il.Emit(OpCodes.Pop); }
            else il.Emit(OpCodes.Call, mergeMethod);
            il.Emit(OpCodes.Ret); type.CreateType();
        }
        return new((BaseUnityPlugin)Activator.CreateInstance(pluginType), new(guid, "fixture", metadataVersion ?? attributedVersion), hooks.ToArray());
    }
}
