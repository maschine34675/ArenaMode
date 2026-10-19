using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using ArenaMode.Server.Patches;
using SPTarkov.Server.Core.Callbacks;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Services.InRaid;

namespace ArenaMode.Server.Compat;

internal sealed record ArenaSvmContract(MethodInfo Callback, MethodInfo Controller, MethodInfo Lifecycle, MethodInfo ForeignPrefix)
{
    private static readonly SemanticVersioning.Version Minimum = new("2.2.3");

    internal static ArenaSvmContract Resolve(SptMod mod)
    {
        if (mod.ModMetadata.ModGuid != ArenaSvmCompat.ModGuid || mod.ModMetadata.Version < Minimum)
            throw new NotSupportedException("expected SVM 2.2.3 or newer");
        var assemblies = mod.Assemblies.Where(a => a.GetName().Name == "ServerValueModifier").ToArray();
        if (assemblies.Length != 1) throw new InvalidOperationException("SVM assembly identity is absent or ambiguous");
        Assembly assembly = assemblies[0];
        var version = mod.ModMetadata.Version;
        var av = assembly.GetName().Version;
        if (av == null || av.Major != version.Major || av.Minor != version.Minor || av.Build != version.Patch)
            throw new InvalidOperationException("SVM assembly and metadata versions disagree");
        Type patchType = assembly.GetType("ServerValueModifier.HarmonyOverrides.LocalRaidPatch", true)!;
        MethodInfo foreign = Exact(patchType, "Prefix", true, typeof(bool),
            typeof(ValueTask<string>).MakeByRefType(), typeof(MongoId), typeof(EndLocalRaidRequestData), typeof(CancellationToken));
        var p = foreign.GetParameters();
        if (!p.Select(x => x.Name).SequenceEqual(new[] { "__result", "sessionID", "info", "cancellationToken" }))
            throw new InvalidOperationException("SVM prefix argument binding changed");
        var contract = new ArenaSvmContract(
            Exact(typeof(MatchCallbacks), nameof(MatchCallbacks.EndLocalRaidAsync), false, typeof(ValueTask<string>),
                typeof(string), typeof(EndLocalRaidRequestData), typeof(MongoId), typeof(CancellationToken)),
            Exact(typeof(MatchController), nameof(MatchController.EndLocalRaidAsync), false, typeof(Task),
                typeof(MongoId), typeof(EndLocalRaidRequestData), typeof(CancellationToken)),
            Exact(typeof(LocationLifecycleService), nameof(LocationLifecycleService.EndLocalRaidAsync), false, typeof(Task),
                typeof(MongoId), typeof(EndLocalRaidRequestData), typeof(CancellationToken)), foreign);
        contract.ValidateReceipts(mod);
        return contract;
    }

    internal void ValidateReceipts(SptMod mod)
    {
        var foreignAssemblies = mod.Assemblies.ToHashSet();
        foreach (var method in new[] { Callback, Controller, Lifecycle })
        {
            var info = Harmony.GetPatchInfo(method);
            var foreign = info == null ? [] : info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers)
                .Concat(info.Finalizers).Concat(info.ILManipulators).Where(p => foreignAssemblies.Contains(p.PatchMethod.DeclaringType!.Assembly)).ToArray();
            if (method == Callback)
            {
                if (foreign.Length != 1 || foreign[0].PatchMethod != ForeignPrefix
                    || foreign[0].owner != ArenaSvmCompat.ForeignHarmonyId
                    || info!.Prefixes.Count(p => p.PatchMethod == ForeignPrefix) != 1)
                    throw new InvalidOperationException("SVM native end prefix is missing, ambiguous or supplemented by an unknown hook");
                MethodInfo ordinary = typeof(ArenaEndLocalRaidPatch).GetMethod(nameof(ArenaEndLocalRaidPatch.Prefix))!;
                MethodInfo enter = typeof(ArenaSvmCompat).GetMethod("EnterCallback", BindingFlags.Static | BindingFlags.NonPublic)!;
                MethodInfo leave = typeof(ArenaSvmCompat).GetMethod("LeaveCallback", BindingFlags.Static | BindingFlags.NonPublic)!;
                if (info.Prefixes.Count(p => p.PatchMethod == ordinary && p.owner == nameof(ArenaEndLocalRaidPatch)) != 1
                    || info.Prefixes.Any(p => p.PatchMethod != ForeignPrefix && p.PatchMethod != ordinary
                        && !(p.PatchMethod == enter && p.owner == ArenaSvmCompat.HarmonyId))
                    || info.Postfixes.Count != 0 || info.Transpilers.Count != 0 || info.ILManipulators.Count != 0
                    || info.Finalizers.Any(p => p.PatchMethod != leave || p.owner != ArenaSvmCompat.HarmonyId))
                    throw new InvalidOperationException("SVM ordinary callback bridge has an unreviewed outer hook topology");
            }
            else if (foreign.Length != 0)
                throw new InvalidOperationException("SVM has an unreviewed downstream end hook");
        }
    }

    private static MethodInfo Exact(Type type, string name, bool isStatic, Type returns, params Type[] args)
    {
        MethodInfo? method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic |
            (isStatic ? BindingFlags.Static : BindingFlags.Instance), null, args, null);
        if (method == null || method.ReturnType != returns || method.IsGenericMethod
            || (isStatic && method.GetCustomAttribute<AsyncStateMachineAttribute>() != null))
            throw new MissingMethodException(type.FullName, name);
        return method;
    }
}
