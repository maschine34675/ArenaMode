using System.Reflection;
using System.Runtime.CompilerServices;
using ArenaMode.Server.Services;
using HarmonyLib;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Generators.Bot;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Bots;
using SPTarkov.Server.Core.Models.Spt.Mod;

namespace ArenaMode.Server.Patches;

/// <summary>
/// Optional APBS 2.4.1 adapter. Replace only the final level range for explicitly
/// owned Arena bots, leaving APBS's level roll, experience, tier and inventory
/// pipeline intact. No copied APBS implementation or global config mutation.
/// </summary>
internal static class ApbsBotTierCompatibility
{
    internal const string ModGuid = "com.acidphantasm.progressivebotsystem";
    internal const string HarmonyId = "com.maschine.ArenaMode.apbs-tier";
    internal static bool Enabled { get; private set; }
    internal static string? FailureReason { get; private set; }
    private static int _reportedFailure;
    private static int _lastAppliedLevel = -1;

    internal static void TryEnable(IReadOnlyList<SptMod> loadedMods)
    {
        if (Enabled) return;
        FailureReason = null;
        var harmony = new Harmony(HarmonyId);
        try
        {
            var candidates = loadedMods.Where(m => m.ModMetadata.ModGuid == ModGuid).ToArray();
            if (candidates.Length == 0) return;
            if (candidates.Length != 1 || candidates[0].ModMetadata.Version.ToString() != "2.4.1")
                throw new NotSupportedException("expected one APBS mod with metadata version 2.4.1");

            var types = candidates[0].Assemblies.Select(a => a.GetType("ProgressiveBotSystem.Patches.GenerateBotLevel"))
                .Where(t => t != null).ToArray();
            if (types.Length != 1) throw new MissingMemberException("APBS level generator is ambiguous or absent");
            var range = Exact(types[0]!, "GetRelativePmcBotLevelRange", BindingFlags.Static | BindingFlags.NonPublic,
                typeof(MinMax<int>), typeof(BotGenerationDetails), typeof(MinMax<int>), typeof(int));
            var nativeLevel = AccessTools.Method(typeof(BotLevelGenerator), nameof(BotLevelGenerator.GenerateBotLevel));
            if (Harmony.GetPatchInfo(nativeLevel)?.Prefixes.Any(p =>
                    p.PatchMethod.DeclaringType == types[0] && p.PatchMethod.Name == "Prefix") != true)
                throw new InvalidOperationException("APBS level prefix is not active");

            var single = Exact(typeof(BotController), "TryGenerateSingleBot", BindingFlags.Instance | BindingFlags.NonPublic,
                typeof(BotBase), typeof(MongoId), typeof(BotGenerationDetails), typeof(int));
            var prepare = Exact(typeof(BotGenerator), nameof(BotGenerator.PrepareAndGenerateBot), BindingFlags.Instance | BindingFlags.Public,
                typeof(BotBase), typeof(MongoId), typeof(BotGenerationDetails));

            // Resolve everything before mutation, install consumer first. No
            // marker may cross a partially installed ownership boundary.
            harmony.Patch(range, postfix: Hook(nameof(RangePostfix)));
            harmony.Patch(prepare, prefix: Hook(nameof(PreparePrefix)), finalizer: Hook(nameof(PrepareFinalizer)));
            harmony.Patch(single, prefix: Hook(nameof(SinglePrefix)), finalizer: Hook(nameof(SingleFinalizer)));
            Enabled = true;
            ArenaContext.Logger?.Debug("[ArenaMode] APBS 2.4.1 Arena level-range adapter enabled; normal raids and protected PFT unchanged.");
        }
        catch (Exception ex)
        {
            Enabled = false;
            FailureReason = ex.Message;
            try { harmony.UnpatchSelf(); }
            catch (Exception rollback)
            {
                // Range application stays disabled even if detours cannot be
                // removed. Never abort the remaining Arena startup patches.
                FailureReason += $"; rollback failed: {rollback.Message}";
            }
            Warn($"adapter unavailable ({FailureReason}); APBS retains its own level selection");
        }
    }

    private static MethodInfo Exact(Type type, string name, BindingFlags flags, Type result, params Type[] parameters)
    {
        var method = type.GetMethod(name, flags, null, parameters, null);
        if (method == null || method.ReturnType != result || method.ContainsGenericParameters
            || method.GetCustomAttribute<AsyncStateMachineAttribute>() != null
            || method.GetCustomAttribute<IteratorStateMachineAttribute>() != null)
            throw new MissingMethodException(type.FullName, name);
        return method;
    }

    private static HarmonyMethod Hook(string name) => new(typeof(ApbsBotTierCompatibility), name);

    private static void SinglePrefix(MongoId __0, BotGenerationDetails __1, out ArenaBotTierOwnership.Scope? __state)
    {
        __state = null;
        try { __state = ArenaBotTierOwnership.Begin(__0, __1); }
        catch (Exception ex) { Warn(ex.Message); }
    }

    private static void SingleFinalizer(ArenaBotTierOwnership.Scope? __state) => ArenaBotTierOwnership.End(__state);

    private static void PreparePrefix(MongoId __0, BotGenerationDetails __1)
    {
        try { ArenaBotTierOwnership.BindClone(__0, __1); }
        catch (Exception ex) { Warn(ex.Message); }
    }

    private static void PrepareFinalizer(BotGenerationDetails __1) => ArenaBotTierOwnership.ReleaseClone(__1);

    private static void RangePostfix(BotGenerationDetails __0, MinMax<int> __1, int __2, ref MinMax<int> __result)
    {
        if (!Enabled) return;
        try
        {
            if (!ArenaBotTierOwnership.TryGetRange(__0, __1, __2, out var range)) return;
            __result = range!;
            // Report the actually applied bounds, not merely the wave request.
            int level = __0.PlayerLevel.GetValueOrDefault();
            if (Interlocked.Exchange(ref _lastAppliedLevel, level) != level)
                ArenaContext.Logger?.Debug($"[ArenaMode] APBS Arena range applied: {__0.Location}, level {range!.Min}-{range.Max} (anchor {level}).");
        }
        catch (Exception ex) { Warn(ex.Message); }
    }

    private static void Warn(string message)
    {
        if (Interlocked.Exchange(ref _reportedFailure, 1) == 0)
            ArenaContext.Logger?.Warning($"[ArenaMode] APBS tier compatibility: {message}.");
    }

    internal static void ResetReporting() => _lastAppliedLevel = -1;

    internal static void Disable()
    {
        Enabled = false;
        new Harmony(HarmonyId).UnpatchSelf();
        ResetReporting();
        _reportedFailure = 0;
    }
}
