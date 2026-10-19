using System.Reflection;
using ArenaMode.Server.Services;
using ArenaMode.Server.Patches;
using ArenaMode.Shared;
using HarmonyLib;
using SPTarkov.Server.Core.Callbacks;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Spt.Mod;

namespace ArenaMode.Server.Compat;

/// <summary>
/// SVM may replace the outer local-end callback with NullResponse. Let Arena's
/// existing protected end transaction decide that exact request instead. No
/// profile, result, SVM setting or ordinary callback is rewritten here.
/// </summary>
internal static class ArenaSvmCompat
{
    internal const string ModGuid = "fika.ghostfenixx.svm";
    internal const string HarmonyId = "com.maschine.ArenaMode.svm-end";
    internal const string ForeignHarmonyId = "LocalRaidPatch";
    internal const string EndRoute = "/client/match/local/end";
    private static readonly SemanticVersioning.Version Reviewed = new("2.2.3");
    private static SptMod? _mod;
    private static ArenaSvmContract? _contract;
    private static string? _failure;
    private static bool _present;
    private static bool _warned;
    private static bool _bridgeEnabled;
    private static Patch? _originalOrdinaryPrefix;
    internal static bool Enabled => _bridgeEnabled;
    [ThreadStatic] private static CallbackLease? _current;

    internal static void TryEnable(IReadOnlyList<SptMod> loadedMods)
    {
        if (_contract != null) return;
        Patch? originalOrdinaryPrefix = null;
        MethodInfo? reorderedCallback = null;
        var candidates = loadedMods.Where(m => m.ModMetadata.ModGuid == ModGuid).ToArray();
        _present = candidates.Length != 0;
        if (!_present) return;
        try
        {
            if (candidates.Length != 1) throw new InvalidOperationException("SVM metadata is ambiguous");
            _mod = candidates[0];
            var resolved = ArenaSvmContract.Resolve(_mod);
            var harmony = new Harmony(HarmonyId);
            // The observer has no result/argument mutations. Harmony runs it
            // even when a preceding prefix elects to skip the native method.
            harmony.Patch(resolved.Callback, prefix: new HarmonyMethod(typeof(ArenaSvmCompat), nameof(EnterCallback))
            {
                priority = Priority.First,
                before = [ForeignHarmonyId, "ArenaEndLocalRaidPatch"],
            }, finalizer: new HarmonyMethod(typeof(ArenaSvmCompat), nameof(LeaveCallback)));
            harmony.Patch(resolved.ForeignPrefix,
                prefix: new HarmonyMethod(typeof(ArenaSvmCompat), nameof(ForeignPrefix)) { priority = Priority.First });
            // Reorder only Arena's own wrapper, after every foreign contract
            // passed. Unknown/absent SVM keeps the established ordinary path.
            MethodInfo ordinary = typeof(ArenaEndLocalRaidPatch).GetMethod(nameof(ArenaEndLocalRaidPatch.Prefix))!;
            originalOrdinaryPrefix = Harmony.GetPatchInfo(resolved.Callback)!.Prefixes.Single(p => p.PatchMethod == ordinary);
            reorderedCallback = resolved.Callback;
            var wrapper = new Harmony(originalOrdinaryPrefix.owner);
            wrapper.Unpatch(resolved.Callback, ordinary);
            wrapper.Patch(resolved.Callback, prefix: new HarmonyMethod(ordinary)
            {
                priority = originalOrdinaryPrefix.priority,
                before = originalOrdinaryPrefix.before.Concat([ForeignHarmonyId]).Distinct().ToArray(),
                after = originalOrdinaryPrefix.after,
            });
            _contract = resolved;
            ValidateInstalled();
            _originalOrdinaryPrefix = originalOrdinaryPrefix;
            _bridgeEnabled = true;
            if (_mod.ModMetadata.Version > Reviewed)
                ArenaContext.Logger?.Warning($"[ArenaMode] SVM {_mod.ModMetadata.Version} is newer than the reviewed 2.2.3 adapter. Required APIs passed validation; full compatibility is unverified.");
            ArenaContext.Logger?.Debug("[ArenaMode] SVM protected-end adapter enabled; ordinary raid options remain unchanged.");
        }
        catch (Exception ex)
        {
            _failure = ex.Message;
            _contract = null;
            if (originalOrdinaryPrefix != null && reorderedCallback != null)
            {
                try
                {
                    var wrapper = new Harmony(originalOrdinaryPrefix.owner);
                    wrapper.Unpatch(reorderedCallback, originalOrdinaryPrefix.PatchMethod);
                    wrapper.Patch(reorderedCallback, prefix: new HarmonyMethod(originalOrdinaryPrefix.PatchMethod)
                    { priority = originalOrdinaryPrefix.priority, before = originalOrdinaryPrefix.before, after = originalOrdinaryPrefix.after });
                }
                catch (Exception rollback) { _failure += $"; ordinary wrapper order rollback failed: {rollback.Message}"; }
            }
            try { new Harmony(HarmonyId).UnpatchSelf(); }
            catch (Exception rollback) { _failure += $"; rollback failed: {rollback.Message}"; }
            Warn();
        }
    }

    internal static bool TryGetAdmissionFailure(out string? reason)
    {
        reason = null;
        if (!_present) return false;
        try
        {
            if (_contract == null) throw new InvalidOperationException(_failure ?? "SVM end adapter is unavailable");
            if (!_bridgeEnabled) throw new InvalidOperationException(_failure ?? "SVM callback bridge was revoked after contract drift");
            ValidateInstalled();
            return false;
        }
        catch (Exception ex)
        {
            _failure = ex.Message;
            reason = $"SVM protected Arena end compatibility is unavailable: {_failure}";
            RevokeFutureBridge();
            Warn();
            return true;
        }
    }

    internal static bool TryUseOrdinaryBridge()
    {
        if (!_bridgeEnabled) return false;
        try { ValidateInstalled(); return true; }
        catch (Exception ex)
        {
            _failure = ex.Message;
            RevokeFutureBridge();
            Warn();
            return false;
        }
    }

    private static void RevokeFutureBridge()
    {
        if (!_bridgeEnabled) return;
        _bridgeEnabled = false;
        // Keep exact guards for captured transactions, but never replay a new
        // unknown hook in future ordinary ends. Restore only our own order.
        if (_contract == null || _originalOrdinaryPrefix == null) return;
        try
        {
            var wrapper = new Harmony(_originalOrdinaryPrefix.owner);
            wrapper.Unpatch(_contract.Callback, _originalOrdinaryPrefix.PatchMethod);
            wrapper.Patch(_contract.Callback, prefix: new HarmonyMethod(_originalOrdinaryPrefix.PatchMethod)
            {
                priority = _originalOrdinaryPrefix.priority,
                before = _originalOrdinaryPrefix.before,
                after = _originalOrdinaryPrefix.after,
            });
        }
        catch (Exception ex) { _failure += $"; wrapper order restore failed: {ex.Message}"; }
    }

    private static void Warn()
    {
        if (_warned) return;
        _warned = true;
        ArenaContext.Logger?.Warning($"[ArenaMode] SVM compatibility failed ({_failure}); Arena admission is disabled. Ordinary PMC/Scav raids retain SVM.");
    }

    private static void ValidateInstalled()
    {
        if (_mod == null || _contract == null) throw new InvalidOperationException("SVM end contract is absent");
        _contract.ValidateReceipts(_mod);
        if (!Owns(_contract.Callback, nameof(EnterCallback), true)
            || !Owns(_contract.Callback, nameof(LeaveCallback), false)
            || !Owns(_contract.ForeignPrefix, nameof(ForeignPrefix), true))
            throw new InvalidOperationException("SVM protected-end hooks are not installed");
        var prefixes = Harmony.GetPatchInfo(_contract.Callback)!.Prefixes;
        if (!prefixes.Single(p => p.PatchMethod.DeclaringType == typeof(ArenaEndLocalRaidPatch)).before.Contains(ForeignHarmonyId)
            || !prefixes.Single(p => p.PatchMethod.DeclaringType == typeof(ArenaSvmCompat)).before.Contains("ArenaEndLocalRaidPatch"))
            throw new InvalidOperationException("SVM callback ownership ordering changed");
        var sorted = PatchProcessor.GetSortedPatchMethods(_contract.Callback, prefixes.ToArray());
        int capture = sorted.IndexOf(typeof(ArenaSvmCompat).GetMethod(nameof(EnterCallback), BindingFlags.Static | BindingFlags.NonPublic)!);
        int wrapper = sorted.IndexOf(typeof(ArenaEndLocalRaidPatch).GetMethod(nameof(ArenaEndLocalRaidPatch.Prefix))!);
        int foreign = sorted.IndexOf(_contract.ForeignPrefix);
        if (!(capture >= 0 && capture < wrapper && wrapper < foreign))
            throw new InvalidOperationException("SVM effective callback order does not preserve exact ownership");
    }

    private static bool Owns(MethodInfo method, string name, bool prefix)
    {
        var info = Harmony.GetPatchInfo(method);
        var patches = prefix ? info?.Prefixes : info?.Finalizers;
        return patches?.Count(p => p.owner == HarmonyId && p.PatchMethod ==
            typeof(ArenaSvmCompat).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)) == 1;
    }

    private static void EnterCallback(MatchCallbacks __instance, string __0, EndLocalRaidRequestData __1, MongoId __2, out CallbackLease __state)
    {
        __state = new CallbackLease(_current, __instance, __0, __1, __2.ToString());
        _current = __state;
        // After a topology revocation our wrapper returns to its old order.
        // Captured pending/completed transactions still own their exact HTTP
        // retry before SVM can run; a genuine inner call is excluded by the
        // service's active execution lease and receives a fresh unmarked scope.
        if (OwnsExactCallback(__2, __1)
            && ArenaContext.OrdinaryRaids?.OwnsCapturedCallbackBridge(__2, __1.ServerId) == true)
            __state.OrdinaryWrapperOwned = true;
    }

    private static void LeaveCallback(CallbackLease? __state)
    {
        if (__state != null && ReferenceEquals(_current, __state)) _current = __state.Previous;
    }

    private static bool ForeignPrefix(MongoId __1, EndLocalRaidRequestData __2, ref bool __result)
    {
        if (_contract == null || !OwnsExactCallback(__1, __2)) return true;
        // Returning true *from SVM's prefix* retains the native callback. This
        // adapter does not return success for the raid, claim a snapshot or
        // accept a forged identity. Rejected/stale Arena attempts must reach
        // the same authoritative rejection as valid attempts reach restoration.
        if (!_current!.OrdinaryWrapperOwned && !NeedsProtectedEnd(__1, __2)) return true;
        __result = true;
        return false;
    }

    // HarmonyX executes later prefixes even when an earlier one skipped the
    // callback. SVM must not replace the outer wrapper's pending ValueTask;
    // its real decisions run once in the wrapper's exact inner callback.
    internal static void MarkOrdinaryWrapperOwned(MatchCallbacks instance, string url,
        MongoId session, EndLocalRaidRequestData request)
    {
        if (_contract != null && OwnsExactCallback(session, request)
            && ReferenceEquals(_current!.Instance, instance)
            && string.Equals(_current.Url, url, StringComparison.Ordinal))
            _current.OrdinaryWrapperOwned = true;
    }

    internal static bool OwnsExactCallback(MongoId session, EndLocalRaidRequestData? request)
        => _current != null && request != null && ReferenceEquals(_current.Request, request)
           && string.Equals(_current.Session, session.ToString(), StringComparison.Ordinal)
           && string.Equals(_current.Url, EndRoute, StringComparison.Ordinal);

    internal static bool NeedsProtectedEnd(MongoId session, EndLocalRaidRequestData request)
    {
        // Match the existing LocationLifecycleService protection, including
        // its authoritative ordinary-generation execution exception.
        if (ArenaContext.OrdinaryRaids?.IsExecutingExactEnd(session, request.ServerId) == true) return false;
        var sessions = ArenaContext.Sessions;
        if (sessions == null) return Protocol.HasNativeEndMarker(request.ServerId ?? string.Empty);
        return sessions.ClassifyLocalRaidEnd(session, request.ServerId) != ArenaLocalRaidEndDisposition.Ordinary;
    }

    private sealed record CallbackLease(CallbackLease? Previous, MatchCallbacks Instance, string Url,
        EndLocalRaidRequestData? Request, string Session)
    {
        internal bool OrdinaryWrapperOwned;
    }
}
