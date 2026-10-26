using EFT;
using EFT.AssetsManager;
using EFT.CameraControl;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using UnityEngine;

namespace ArenaMode.Kits;

/// <summary>
/// A cold WeaponMod pool instantiates its role model active, then deactivates it
/// in AbstractAssetsPool.RunInitialStageBody. An unmounted OpticSight can publish
/// itself during that first Instantiate: CameraManager.method_10 expects a weapon
/// parent. Suppress only that role model's paired callbacks, before publication.
/// Neither the camera manager nor normal/mounted sight callbacks are patched out.
///
/// Engaged for the loading-screen kit preload as well, which runs while
/// IsActive is still false: the raid camera comes alive partway through the
/// level load, and a batch still building pools at that moment is exposed
/// exactly like an in-raid load. Such a request stays current until the native
/// call it wraps returns, whatever the session flags do meanwhile — a raid the
/// server then refuses clears the arming while the batch is still filling
/// pools, and the guard must not go dark on its own objects at that moment.
/// That is a deliberate bend of the IsActive rule, recorded in AGENTS.md; what
/// makes it safe is that every gate below still asks whether the pool being
/// built is one of OUR request's resources, so a vanilla object never sees
/// anything but vanilla.
/// </summary>
internal static class KitOpticPreloadGuard
{
    private static readonly Harmony Patches = new Harmony(Plugin.PluginGuid + ".KitOpticPreload");
    private static readonly AsyncLocal<Request> Current = new AsyncLocal<Request>();
    private static readonly ConditionalWeakTable<OpticSight, Request> Suppressed =
        new ConditionalWeakTable<OpticSight, Request>();
    [ThreadStatic] private static Frame _frame;
    private static int _epoch;
    private static int _loadingScreenRequests;
    private static bool _installed;
    private static bool _installAttempted;
    private static bool _reported;

    private sealed class Request : IDisposable
    {
        internal readonly ObjectsFactory Factory;
        internal readonly ObjectsFactory.Pools Pools;
        internal readonly HashSet<ResourceKey> Resources;
        private readonly Request _previous;
        private readonly int _generation;
        private readonly bool _loadingScreen;
        private volatile bool _disposed;

        internal Request(ObjectsFactory factory, IEnumerable<ResourceKey> resources, bool loadingScreen)
        {
            Factory = factory;
            Pools = factory.GetPools(ObjectsFactory.PoolsCategory.Raid);
            Resources = new HashSet<ResourceKey>(resources);
            _previous = Current.Value;
            _generation = _epoch;
            _loadingScreen = loadingScreen;
            if (loadingScreen) _loadingScreenRequests++;
            Current.Value = this;
        }

        // A loading-screen request outlives the per-raid generation on purpose:
        // Reset runs at the start of the very raid it was made for, and the
        // request must stay current across that boundary. It outlives the
        // session flags too: it is current from Begin until Dispose, which the
        // preloader's using-block reaches only when the native call returns,
        // and it is bound to its own resources by every caller.
        internal bool IsCurrent => (_loadingScreen || (Session.ArenaSession.IsActive && _generation == _epoch))
            && !_disposed
            && Pools != null
            && ReferenceEquals(Factory.GetPools(ObjectsFactory.PoolsCategory.Raid), Pools);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_loadingScreen && _loadingScreenRequests > 0) _loadingScreenRequests--;
            if (ReferenceEquals(Current.Value, this)) Current.Value = _previous;
        }
    }

    private sealed class Frame
    {
        internal readonly Frame Previous;
        internal readonly Request Request;
        internal readonly Transform Parent;
        internal Frame(Request request, Transform parent)
        {
            Previous = _frame;
            Request = request;
            Parent = parent;
        }
    }

    internal static IDisposable Begin(ObjectsFactory factory, IEnumerable<ResourceKey> resources, bool loadingScreen = false)
    {
        if (!Gate(loadingScreen) || factory == null || !EnsureInstalled()) return null;
        return new Request(factory, resources, loadingScreen);
    }

    /// <summary>
    /// When the guard may act at all: in an arena raid, or — for the
    /// loading-screen preload only — while one is armed or one of its own
    /// requests is still in flight. The second clause is what keeps a batch
    /// covered to its end in a raid that never becomes an arena raid: the
    /// arming is cleared the moment the server refuses the token, the batch
    /// is not. Such a raid still had its kit pools built by us, and their role
    /// models are the only objects this ever touches.
    /// </summary>
    private static bool Gate(bool loadingScreen)
        => Session.ArenaSession.IsActive
           || (loadingScreen && (Session.ArenaSession.ArmedForNextRaid || _loadingScreenRequests > 0));

    internal static void Reset()
    {
        unchecked { _epoch++; }
        _reported = false;
        // Old async continuations keep their own Request, which now fails the
        // generation check. Do not replace another operation's execution context.
    }

    internal static void Shutdown()
    {
        Reset();
        _loadingScreenRequests = 0;
        Patches.UnpatchSelf();
        _installed = false;
        _installAttempted = false;
    }

    private static bool EnsureInstalled()
    {
        if (_installAttempted) return _installed;
        _installAttempted = true;
        try
        {
            var original = AccessTools.DeclaredMethod(typeof(AssetPool<WeaponModPoolObject>),
                nameof(AssetPool<WeaponModPoolObject>.GetOriginalGameObject), new[] { typeof(Transform) });
            var enable = AccessTools.DeclaredMethod(typeof(OpticSight), nameof(OpticSight.OnEnable));
            var disable = AccessTools.DeclaredMethod(typeof(OpticSight), nameof(OpticSight.OnDisable));
            if (original == null || original.ReturnType != typeof(GameObject)
                || enable == null || disable == null)
                throw new MissingMethodException("SPT optic pool initialization contract changed");
            Patches.Patch(original,
                prefix: new HarmonyMethod(typeof(KitOpticPreloadGuard), nameof(BeforeOriginal)),
                finalizer: new HarmonyMethod(typeof(KitOpticPreloadGuard), nameof(FinishOriginal)));
            Patches.Patch(enable, prefix: new HarmonyMethod(typeof(KitOpticPreloadGuard), nameof(BeforeEnable)));
            Patches.Patch(disable, prefix: new HarmonyMethod(typeof(KitOpticPreloadGuard), nameof(BeforeDisable)),
                finalizer: new HarmonyMethod(typeof(KitOpticPreloadGuard), nameof(FinishDisable)));
            _installed = true;
        }
        catch (Exception ex)
        {
            Patches.UnpatchSelf();
            Plugin.ModLogger.LogWarning($"Arena optic preload guard unavailable; vanilla pool initialization retained: {ex.Message}");
        }
        return _installed;
    }

    private static void BeforeOriginal(object __instance, Transform parent, out Frame __state)
    {
        Request request = Current.Value;
        var pool = __instance as AssetPool<WeaponModPoolObject>;
        bool owned = request != null && request.IsCurrent && pool != null
            && pool.AssemblyType == ObjectsFactory.AssemblyType.Local
            && ReferenceEquals(pool.IEasyAssets, request.Factory.EasyAssets)
            && request.Resources.Contains(pool.ResourceKey)
            && request.Pools.PoolsDictionary.TryGetValue(pool.ResourceKey, out var registered)
            && registered.Source?.Task.Status == System.Threading.Tasks.TaskStatus.RanToCompletion
            && ReferenceEquals(registered.Source.Task.Result, pool)
            && pool.ParentTransform != null
            && (ReferenceEquals(pool.ParentTransform, request.Pools.UnloadableParent)
                || ReferenceEquals(pool.ParentTransform, request.Pools._dontDestroyParent))
            && pool.ParentObject != null && ReferenceEquals(parent, pool.ParentObject.transform);
        // Even an unowned nested call gets an empty frame. A shared generic
        // method body must never inherit an outer pool's ownership window.
        __state = new Frame(owned ? request : null, owned ? parent : null);
        _frame = __state;
    }

    private static Exception FinishOriginal(Exception __exception, Frame __state)
    {
        if (__state != null) _frame = __state.Previous;
        return __exception; // Release the window on exceptions; never swallow one.
    }

    private static bool BeforeEnable(OpticSight __instance)
    {
        if (!Gate(loadingScreen: true) || __instance == null) return true;
        // A later genuine activation must not inherit a role-model disable marker.
        Suppressed.Remove(__instance);
        Frame frame = _frame;
        if (frame?.Request == null || !frame.Request.IsCurrent || __instance == null
            || frame.Parent == null || !__instance.transform.IsChildOf(frame.Parent)
            || __instance.GetComponentInParent<WeaponPrefab>(includeInactive: true) != null)
            return true;
        Suppressed.Add(__instance, frame.Request);
        if (!_reported)
        {
            _reported = true;
            Plugin.ModLogger.LogDebug("Arena kit preload: withheld unmounted optic role-model callbacks from the active camera.");
        }
        return false;
    }

    private static bool BeforeDisable(OpticSight __instance)
    {
        if (!Gate(loadingScreen: true) || __instance == null) return true;
        if (!Suppressed.TryGetValue(__instance, out var request)) return true;
        // RunInitialStageBody deactivates immediately after GetOriginalGameObject
        // returns. Suppress only the counterpart that never published an enable.
        return !request.IsCurrent
            || __instance.GetComponentInParent<WeaponPrefab>(includeInactive: true) != null;
    }

    // Foreign OnEnable/OnDisable postfixes still run when the native callback is
    // withheld. Keep the exact role-model marker through those postfixes.
    internal static bool IsWithheldCallback(OpticSight sight)
        => sight != null && Suppressed.TryGetValue(sight, out var request) && request.IsCurrent
           && sight.GetComponentInParent<WeaponPrefab>(includeInactive: true) == null;

    private static Exception FinishDisable(OpticSight __instance, Exception __exception)
    {
        if (!ReferenceEquals(__instance, null)) Suppressed.Remove(__instance);
        return __exception;
    }
}
