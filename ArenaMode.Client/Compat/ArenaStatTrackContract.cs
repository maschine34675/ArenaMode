using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BepInEx;
using EFT;
using EFT.Ballistics;
using EFT.InventoryLogic;
using HarmonyLib;
using Version = System.Version;

namespace ArenaMode.Compat;

/// <summary>Validate identity, native hook receipts and all upstream collection/merge callers.</summary>
internal static class ArenaStatTrackContract
{
    internal const string PluginGuid = "com.acidphantasm.stattrack";
    internal static readonly Version ReviewedVersion = new(2, 1, 1, 0);
    private static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)).ToDictionary(c => c.Value);

    internal static MethodInfo[] Resolve(Type plugin, string guid, Version reported, out Version version)
    {
        version = Normalize(reported);
        if (guid != PluginGuid || version == null || version < ReviewedVersion)
            throw new NotSupportedException("StatTrack 2.1.1 or newer is required");
        if (plugin == null || plugin.FullName != "StattrackClient.Plugin"
            || !typeof(BaseUnityPlugin).IsAssignableFrom(plugin) || plugin.IsAbstract)
            throw new MissingMemberException("StatTrack plugin identity changed");
        var identity = plugin.Assembly.GetName();
        var attribute = plugin.GetCustomAttribute<BepInPlugin>(false);
        // Released client 2.1.1 uses assembly version 1.0.0.0. The plugin's
        // attributed version is authoritative; the server has its own version.
        if (identity.Name != "acidphantasm-stattrack"
            || (Normalize(identity.Version) != new Version(1, 0, 0, 0) && Normalize(identity.Version) != version)
            || attribute?.GUID != PluginGuid || Normalize(attribute.Version) != version)
            throw new InvalidOperationException("StatTrack plugin and assembly metadata disagree");
        var methods = new[] {
            Postfix(plugin.Assembly, "WeaponOnShotPatch", typeof(Weapon)),
            Postfix(plugin.Assembly, "PlayerPatch", typeof(Player), typeof(IPlayer), typeof(DamageInfo), typeof(EBodyPart), typeof(EDamageType)),
            Postfix(plugin.Assembly, "PlayerPatch2", typeof(Player), typeof(EBodyPart), typeof(float), typeof(DamageInfo)),
            Postfix(plugin.Assembly, "GameWorldPatch", typeof(IPlayer))
        };
        VerifyNativeBindings(methods);
        VerifyCollectionGraph(plugin.Assembly, methods);
        if (AccessTools.Method(typeof(GameWorld), nameof(GameWorld.OnGameStarted), Type.EmptyTypes)?.ReturnType != typeof(void)
            || AccessTools.Method(typeof(GameWorld), nameof(GameWorld.RegisterPlayer), new[] { typeof(IPlayer) })?.ReturnType != typeof(void)
            || typeof(Player.SinglePlayerInventoryController).GetConstructor(new[] { typeof(Player), typeof(Profile), typeof(bool), typeof(bool) }) == null)
            throw new MissingMethodException("StatTrack ownership lifecycle changed");
        return methods;
    }

    internal static void VerifyNativeBindings(MethodInfo[] methods)
    {
        var targets = new[] {
            AccessTools.Method(typeof(Weapon), nameof(Weapon.OnShot)),
            AccessTools.Method(typeof(Player), nameof(Player.OnBeenKilledByAggressor)),
            AccessTools.Method(typeof(Player), nameof(Player.OnHealthApplyDamage)),
            AccessTools.Method(typeof(GameWorld), nameof(GameWorld.UnregisterPlayer), new[] { typeof(IPlayer) })
        };
        for (int i = 0; i < targets.Length; ++i)
        {
            var target = targets[i]; var info = target == null ? null : Harmony.GetPatchInfo(target);
            if (target == null || target.IsStatic || target.ReturnType != typeof(void) || info == null
                || info.Postfixes.Count(p => p.PatchMethod == methods[i]) != 1
                || info.Postfixes.Any(p => p.PatchMethod.DeclaringType?.Assembly == methods[i].DeclaringType.Assembly && p.PatchMethod != methods[i])
                || info.Prefixes.Concat(info.Transpilers).Concat(info.Finalizers)
                    .Any(p => p.PatchMethod.DeclaringType?.Assembly == methods[i].DeclaringType.Assembly))
                throw new MissingMethodException("StatTrack native hook changed: " + (target?.Name ?? "missing"));
        }
    }

    private static MethodInfo Postfix(Assembly assembly, string typeName, params Type[] args)
    {
        var type = assembly.GetType("StattrackClient.Patches." + typeName, true);
        var method = type.GetMethod("PatchPostfix", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly,
            null, args, null);
        if (method == null || method.ReturnType != typeof(void) || method.ContainsGenericParameters || method.GetMethodBody() == null)
            throw new MissingMethodException(type.FullName, "PatchPostfix");
        return method;
    }

    private static void VerifyCollectionGraph(Assembly assembly, MethodInfo[] guarded)
    {
        var utility = assembly.GetType("StattrackClient.Utils.JsonFileUtils", true);
        var add = utility.GetMethod("TemporaryAddData", BindingFlags.Public | BindingFlags.Static, null,
            new[] { typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(float) }, null);
        var merge = utility.GetMethod("EndRaidMergeData", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
        var save = utility.GetMethod("SaveRaidEndInServer", BindingFlags.NonPublic | BindingFlags.Static, null, Type.EmptyTypes, null);
        if (add?.ReturnType != typeof(void) || merge?.ReturnType != typeof(void) || save?.ReturnType != typeof(System.Threading.Tasks.Task))
            throw new MissingMethodException("StatTrack collection/save API changed");
        var load = utility.GetMethod("LoadFromServer", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
        if (load?.ReturnType != typeof(System.Threading.Tasks.Task)) throw new MissingMethodException("StatTrack load API changed");
        var saveState = save.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
        var loadState = load.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
        var addCallers = new HashSet<MethodBase>(); var mergeCallers = new HashSet<MethodBase>(); var saveCallers = new HashSet<MethodBase>();
        foreach (var type in assembly.GetTypes())
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Cast<MethodBase>().Concat(type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)))
        {
            if (method.GetMethodBody() == null) continue;
            foreach (var member in Members(method))
            {
                if (member == add) addCallers.Add(method);
                if (member == merge) mergeCallers.Add(method);
                if (member == save) saveCallers.Add(method);
                // A new producer writing either dictionary/data fields directly
                // would bypass the four body-bearing seams; refuse that contract.
                bool stateAccess = member is FieldInfo field && (field.DeclaringType == utility
                    || field.DeclaringType?.FullName == utility.FullName + "+StatTrackData"
                    || field.DeclaringType?.FullName == utility.FullName + "+RequestData")
                    || member is MethodInfo accessor && accessor.DeclaringType == utility
                        && (accessor.Name.StartsWith("get_WeaponInfo", StringComparison.Ordinal)
                            || accessor.Name.StartsWith("set_WeaponInfo", StringComparison.Ordinal));
                if (stateAccess && !AllowedStateAccess(method, utility, saveState, loadState))
                    throw new MissingMethodException("StatTrack persistent-state access bypasses the guarded producers");
                if (member is MethodInfo request && request.DeclaringType?.FullName == "SPT.Common.Http.RequestHandler"
                    && (request.Name.StartsWith("Put", StringComparison.Ordinal) || request.Name.StartsWith("Post", StringComparison.Ordinal))
                    && (method.DeclaringType != saveState || method.Name != "MoveNext"))
                    throw new MissingMethodException("StatTrack has an unguarded persistent request producer");
            }
        }
        if (!addCallers.SetEquals(guarded.Take(3)) || !mergeCallers.SetEquals(new[] { guarded[3] })
            || !saveCallers.SetEquals(new[] { merge }))
            throw new MissingMethodException("StatTrack has unguarded collection or save callers");
    }

    private static bool AllowedStateAccess(MethodBase method, Type utility, Type saveState, Type loadState)
        => (method.DeclaringType == utility && new[] { ".cctor", "get_WeaponInfoForRaid", "set_WeaponInfoForRaid",
                "get_WeaponInfoOutOfRaid", "set_WeaponInfoOutOfRaid", "TemporaryAddData", "EndRaidMergeData",
                "MergeDictionary", "ComputeDisplayStats", "GetData" }.Contains(method.Name))
            || (method.Name == "MoveNext" && (method.DeclaringType == saveState || method.DeclaringType == loadState));

    private static IEnumerable<MemberInfo> Members(MethodBase method)
    {
        var il = method.GetMethodBody().GetILAsByteArray(); int at = 0;
        while (at < il.Length)
        {
            short code = il[at++];
            if (code == 0xfe)
            { if (at >= il.Length) throw new InvalidOperationException("Truncated StatTrack IL"); code = (short)(0xfe00 | il[at++]); }
            if (!Codes.TryGetValue(code, out var op)) throw new InvalidOperationException("Unknown StatTrack IL");
            int size;
            switch (op.OperandType)
            {
                case OperandType.InlineNone: size = 0; break;
                case OperandType.ShortInlineBrTarget: case OperandType.ShortInlineI: case OperandType.ShortInlineVar: size = 1; break;
                case OperandType.InlineVar: size = 2; break;
                case OperandType.InlineI8: case OperandType.InlineR: size = 8; break;
                case OperandType.InlineSwitch:
                    if (at + 4 > il.Length) throw new InvalidOperationException("Truncated StatTrack switch");
                    int count = BitConverter.ToInt32(il, at);
                    if (count < 0 || count > (il.Length - at - 4) / 4) throw new InvalidOperationException("Invalid StatTrack switch");
                    size = 4 + count * 4; break;
                default: size = 4; break;
            }
            if (at + size > il.Length) throw new InvalidOperationException("Truncated StatTrack operand");
            if (op.OperandType == OperandType.InlineMethod || op.OperandType == OperandType.InlineField)
                yield return method.Module.ResolveMember(BitConverter.ToInt32(il, at),
                    method.DeclaringType?.GetGenericArguments(), (method as MethodInfo)?.GetGenericArguments());
            at += size;
        }
    }
    internal static Version Normalize(Version version)
        => version == null || version.Build < 0 ? null : new Version(version.Major, version.Minor, version.Build, Math.Max(0, version.Revision));
}
