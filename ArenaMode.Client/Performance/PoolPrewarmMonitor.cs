using ArenaMode.Config;
using ArenaMode.Session;
using EFT;
using EFT.AssetsManager;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace ArenaMode.Performance;

/// <summary>
/// Harmony/EFT-facing half of the pool-prewarm clamp. PoolPrewarmPolicy
/// decides WHETHER and BY HOW MUCH to shrink one ExpandPool call; this
/// resolves the seven closed pool types, installs the shared prefix, and
/// logs. See Config.Settings.PoolPrewarmKeepIdle for the setting and
/// POOL-PREWARM-REVIEW.md for the measurements this design answers to.
///
/// ObjectsFactory.InitializeAssetsPool (../_decompiled/EFT/ObjectsFactory.cs:
/// 269-303) instantiates exactly seven closed types over AbstractAssetsPool
/// &lt;TAssetPoolObject&gt; for every ResourceType branch it handles: AssetPool
/// &lt;UsableHandsPrefab&gt;, &lt;AmmoPoolObject&gt;, &lt;WeaponPrefab&gt;,
/// &lt;MagazineInHandsVisualController&gt;, &lt;MagVisualController&gt;,
/// &lt;WeaponModPoolObject&gt; and, for every other ResourceType, &lt;AssetPoolObject&gt;.
/// Neither AssetPool&lt;T&gt; nor its sibling AssetsPool&lt;T&gt; overrides ExpandPool —
/// the only implementation in the whole hierarchy lives on the abstract base
/// — so this patches AbstractAssetsPool&lt;&gt; directly, once per closed T, rather
/// than either concrete subclass: there is exactly one ExpandPool today, and
/// resolving it from the type that actually owns the body cannot silently
/// start resolving a DIFFERENT, overriding method if a future SPT build adds
/// one on a subclass, the way resolving through a subclass's inheritance
/// lookup could.
/// </summary>
internal static class PoolPrewarmMonitor
{
    /// <summary>
    /// The seven closed AbstractAssetsPool&lt;T&gt; instantiations
    /// ObjectsFactory.InitializeAssetsPool ever creates. Verified against
    /// the decompile, not assumed — a future SPT build adding an eighth
    /// ResourceType branch leaves that branch unclamped (logged as a
    /// resolve failure at Initialize time for nothing, since there is
    /// nothing to resolve there), never broken.
    /// </summary>
    private static readonly Type[] PooledResourceTypes =
    {
        typeof(UsableHandsPrefab), typeof(AmmoPoolObject), typeof(WeaponPrefab),
        typeof(MagazineInHandsVisualController), typeof(MagVisualController),
        typeof(WeaponModPoolObject), typeof(AssetPoolObject),
    };

    private static readonly Harmony Patches = new(Plugin.PluginGuid + ".PoolPrewarm");

    private static bool _installed;

    /// <summary>Fail-open for the rest of the current raid, once any patched call throws. See DisableForRaid.</summary>
    private static bool _disabledForRaid;
    private static bool _warnedThisRaid;

    // Per-raid ledger. Keyed by IAssetPool.PoolShortName rather than the
    // closed T, so the logged count reflects the actual resource-path
    // variety Arena clamped (kits, weapon families, ...), not the fixed
    // count of 7 generic instantiations.
    private static readonly HashSet<string> _poolsTouched = new();
    private static int _expansionsClamped;
    // No "instances avoided" total: because RequestCount never drops, every
    // declined top-up asks for RequestCount - FuturePoolSize, a number that
    // grows with each bot spawned, so summing the declined amounts said
    // "~424,046 instances not created" for a raid in which vanilla would
    // have created about one per top-up (GPU raid 2026-09-19, 6,948 top-ups
    // declined). The count of declined top-ups IS the honest estimate.

    /// <summary>
    /// Called once from Plugin.Awake, alongside the other Harmony
    /// `.Enable()` calls (this feature owns a Harmony instance and installs
    /// several patches from a loop, unlike GcThresholdMonitor.Initialize's
    /// zero-Harmony event subscription — it belongs in that group instead).
    ///
    /// Root cause of why this loop, and why the prefix below never declares
    /// a MethodBase __originalMethod parameter: RespawnLeakProbe.Timing.
    /// MethodTimer.IsPatchable (../RespawnLeakProbe/Timing/MethodTimer.cs:
    /// 47-58) documents HarmonyX throwing "Cannot resolve method ... because
    /// the declaring type of the method handle ... is generic" on EVERY call
    /// of a patch that asks for __originalMethod on a method declared on a
    /// generic type — even a CLOSED instantiation. Never add __originalMethod
    /// back to Prefix's signature; it would reintroduce that crash on every
    /// single ExpandPool call this feature patches.
    /// </summary>
    internal static void Initialize()
    {
        if (_installed)
            return;

        _installed = true;
        var prefix = new HarmonyMethod(typeof(PoolPrewarmMonitor).GetMethod(nameof(Prefix), BindingFlags.NonPublic | BindingFlags.Static));
        var patched = 0;

        foreach (var resourceType in PooledResourceTypes)
        {
            try
            {
                var closed = typeof(AbstractAssetsPool<>).MakeGenericType(resourceType);
                var method = AccessTools.DeclaredMethod(closed, "ExpandPool");
                if (method == null)
                {
                    Plugin.ModLogger.LogWarning($"Arena pool prewarm clamp: ExpandPool not found on AbstractAssetsPool<{resourceType.Name}>; that pool type keeps EFT's own unbounded prewarm.");
                    continue;
                }

                Patches.Patch(method, prefix: prefix);
                patched++;
            }
            catch (Exception exception)
            {
                // One failing closed type must never block the other six —
                // each resolve-and-patch is its own try/catch, mirroring
                // RespawnLeakProbe.Timing.SystemTiming.PatchTick's own
                // resolve-then-patch-then-warn-and-skip-only-that-target
                // convention.
                Plugin.ModLogger.LogWarning($"Arena pool prewarm clamp: failed to patch AbstractAssetsPool<{resourceType.Name}>.ExpandPool: {exception.Message}");
            }
        }

        Plugin.ModLogger.LogInfo($"Arena pool prewarm clamp ready: {patched}/{PooledResourceTypes.Length} pool types patched.");
    }

    /// <summary>
    /// Called at every raid boundary from ArenaRespawnDriver.ResetSession —
    /// the same place Performance.GcThresholdMonitor.Reset and its
    /// neighbours run. ResetSession itself fires more than once around a
    /// single raid boundary in some paths, so this must not double-log: the
    /// log-worthiness decision (PoolPrewarmPolicy.ShouldLogLedger) is
    /// evaluated BEFORE the ledger is cleared, on every call. The first call
    /// in a boundary sees a non-empty ledger, logs once, then clears
    /// everything; every later call in the same boundary sees an empty
    /// ledger, ShouldLogLedger(0) is false, and clearing an already-empty
    /// ledger and already-false flags is a no-op.
    /// </summary>
    public static void Reset()
    {
        if (PoolPrewarmPolicy.ShouldLogLedger(_poolsTouched.Count))
        {
            Plugin.ModLogger.LogInfo(
                $"Arena pool prewarm: {_poolsTouched.Count} pool(s) capped at {Settings.PoolPrewarmKeepIdle.Value} idle "
                + $"this raid, {_expansionsClamped} top-up(s) declined (vanilla adds about one instance per top-up).");

            if (_disabledForRaid)
                Plugin.ModLogger.LogInfo("Arena pool prewarm: the clamp disabled itself partway through this raid (see the warning above) — the ledger above stops at that point, not a complete picture of the whole raid.");
        }

        _poolsTouched.Clear();
        _expansionsClamped = 0;
        _disabledForRaid = false;
        _warnedThisRaid = false;
    }

    /// <summary>
    /// Shared prefix for all seven closed ExpandPool instantiations.
    ///
    /// `object __instance`, not IAssetPool: the same static method is
    /// registered against seven distinct closed MethodInfos, so a
    /// strongly-typed parameter would only bind for one of them — cast to
    /// IAssetPool inside the body instead, a fully public interface needing
    /// no reflection at call time.
    ///
    /// `ref int objectsCount` must be spelled exactly as the IMPLEMENTATION
    /// spells it (AbstractAssetsPool&lt;T&gt;.ExpandPool). IAssetPool's own
    /// interface declaration misspells the same parameter "objectsCoun" — no
    /// final t. Harmony binds a prefix's by-name pass-through parameters
    /// against the method it actually patched, the concrete implementation —
    /// spelling it the interface's way here would silently fail to bind as
    /// the real ref parameter and this whole feature would do nothing while
    /// looking installed.
    ///
    /// `ref Task __result`: the compiler turns `async Task ExpandPool(...)`
    /// into a synchronous stub that builds the state machine and returns its
    /// Task — that stub is what Harmony patches, and this prefix runs before
    /// the state machine exists, so `__result = Task.CompletedTask; return
    /// false;` is a standard, valid async-skip. Every false-return path below
    /// sets __result first — leaving it unset would hand the caller a null
    /// Task and crash either ObjectsFactory.InitAndFillPools call site.
    /// </summary>
    private static bool Prefix(object __instance, ref int objectsCount, string reason, ref Task __result)
    {
        if (!ArenaSession.IsActive || _disabledForRaid)
            return true; // Fast path: a normal raid, or a raid where this already failed once, never enters the try.

        try
        {
            var pool = (IAssetPool)__instance;
            var decision = PoolPrewarmPolicy.Decide(
                ArenaSession.IsActive, reason, Settings.PoolPrewarmKeepIdle.Value, pool.FuturePoolSize, objectsCount);

            if (!decision.Clamped)
                return true; // Feature off, wrong reason, or already within budget — vanilla, unchanged.

            RecordClamp(pool.PoolShortName);

            // Written back unconditionally, on BOTH branches below, not only the
            // RunOriginal-true one: this ref parameter is the only channel a
            // second, repeated invocation of this shared prefix (see the class
            // doc comment on Mono generic-code sharing) has for observing what
            // the first invocation already decided. A full skip (RunOriginal
            // false) clamps to 0 — leaving objectsCount at the caller's original,
            // unclamped value here would make a repeated invocation recompute
            // the SAME clamp against the SAME (unmoved) futurePoolSize and double
            // the ledger for what is really one clamp. Writing 0 here means a
            // repeated call sees requestedCount == 0, which Decide's own
            // requestedCount &lt;= 0 guard resolves to Unclamped — no second
            // RecordClamp, matching the idempotence PoolPrewarmPolicy already
            // assumes.
            objectsCount = decision.ObjectsCount;

            if (!decision.RunOriginal)
            {
                __result = Task.CompletedTask;
                return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            DisableForRaid(exception);
            return true; // Fail open: vanilla ExpandPool runs, uncapped, rather than risk a broken spawn.
        }
    }

    private static void RecordClamp(string poolShortName)
    {
        _poolsTouched.Add(poolShortName);
        _expansionsClamped++;
    }

    /// <summary>
    /// Disables the WHOLE clamp for the rest of the current raid, not just
    /// the pool type that threw — a deliberate, conservative choice: one
    /// obviously-correct fail-open branch instead of per-pool disable
    /// bookkeeping this feature's blast radius does not justify. Clears at
    /// the next raid boundary (Reset), so a bad raid never disables the
    /// feature permanently.
    /// </summary>
    private static void DisableForRaid(Exception exception)
    {
        _disabledForRaid = true;
        if (_warnedThisRaid)
            return;

        _warnedThisRaid = true;
        Plugin.ModLogger.LogWarning($"Arena pool prewarm clamp disabled for the rest of this raid: {exception.Message}");
    }
}
