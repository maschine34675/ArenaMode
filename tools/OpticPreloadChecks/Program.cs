using System.Collections;
using System.Runtime.CompilerServices;
using ArenaMode.Kits;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.AssetsManager;
using EFT.CameraControl;
using UnityEngine;

internal static class Program
{
    private static int _assertions;
    private sealed class ScheduledYield : INotifyCompletion
    {
        // Like native IJobAwaiter: the scheduler stores a plain continuation and
        // invokes it later, rather than knowing anything about Arena ownership.
        internal Action Continuation;
        public ScheduledYield GetAwaiter() => this;
        public bool IsCompleted => false;
        public void OnCompleted(Action continuation) => Continuation = continuation;
        public void GetResult() { }
    }
    private sealed class Fixture
    {
        internal readonly ObjectsFactory Factory = new();
        internal readonly ResourceKey Key = new("scope.bundle", "scope");
        internal readonly AssetPool<WeaponModPoolObject> Pool;
        internal OpticSight Sight;
        internal Fixture()
        {
            Pool = NewPool(Factory, Key);
            Pool.Instantiate = parent => { Sight = NewSight(parent); Sight.OnEnable(); return Sight.gameObject; };
        }
        internal IDisposable Begin() => KitOpticPreloadGuard.Begin(Factory, new[] { Key });
        internal void Original() => Pool.GetOriginalGameObject(Pool.ParentObject.transform);
    }

    private static AssetPool<WeaponModPoolObject> NewPool(ObjectsFactory factory, ResourceKey key)
    {
        var pool = new AssetPool<WeaponModPoolObject>
        {
            IEasyAssets = factory.EasyAssets, ResourceKey = key,
            AssemblyType = ObjectsFactory.AssemblyType.Local,
            ParentTransform = factory.RaidPools.UnloadableParent
        };
        var source = new TaskCompletionSource<IAssetPool>();
        source.SetResult(pool);
        factory.RaidPools.PoolsDictionary[key] = new ObjectsFactory.Pools.AssetPoolData { Source = source };
        return pool;
    }

    private static OpticSight NewSight(Transform parent, bool mounted = false)
    {
        var go = new GameObject();
        go.transform.parent = parent;
        if (mounted) go.AddComponent<WeaponPrefab>();
        return go.AddComponent<OpticSight>();
    }

    private static void Check(bool value, string name)
    {
        _assertions++;
        if (!value) throw new Exception("ASSERT: " + name);
    }

    private static void Orphan(Action action, string name)
    {
        bool failed = false;
        try { action(); }
        catch (InvalidOperationException ex) when (ex.Message == "orphan optic published") { failed = true; }
        Check(failed, name);
    }

    private static void Protected(Action action, string name)
    {
        try { action(); }
        catch (Exception ex) { throw new Exception("ASSERT: " + name, ex); }
        Check(true, name);
    }

    private static void Reset()
    {
        ArenaSession.IsActive = true;
        KitOpticPreloadGuard.Shutdown();
        KitBundlePreloader.Reset();
        HarmonyLib.Harmony.FailInstall = false;
        HarmonyLib.Harmony.FailAfter = int.MaxValue;
        FakeCamera.Current = null;
        FakeCamera.ReticleClears = 0;
        ArenaMode.Plugin.ModLogger.Lines.Clear();
    }

    private static void BasicOwnership()
    {
        Reset();
        var f = new Fixture();
        Orphan(f.Original, "native cold standalone optic reproduces publication hazard");
        var mounted = NewSight(null, mounted: true);
        mounted.OnEnable();
        int clears = FakeCamera.ReticleClears;
        using (f.Begin())
        {
            Protected(f.Original, "owned original factory suppresses orphan publication");
            Check(f.Sight.NativeEnables == 0, "owned cold role model is withheld before camera publication");
            Check(ReferenceEquals(FakeCamera.Current, mounted), "preload preserves current mounted camera");
            f.Sight.OnDisable();
            Check(f.Sight.NativeDisables == 0 && FakeCamera.ReticleClears == clears,
                "paired role model disable leaves active reticle untouched");
            f.Sight.OnDisable();
            Check(f.Sight.NativeDisables == 1, "disable marker consumed once");
        }
        Check(HarmonyLib.Harmony.Hooks.Count == 3, "only original factory and paired optic callbacks patched");
        var methods = HarmonyLib.Harmony.Hooks.Keys.ToArray();
        Check(methods.Any(m => m.DeclaringType == typeof(AssetPool<WeaponModPoolObject>) && m.Name == "GetOriginalGameObject"),
            "native weapon mod original factory hook selected");
        Check(methods.All(m => m.DeclaringType != typeof(FakeCamera)), "no camera exception patch");
        Orphan(f.Original, "completed scope does not own later pool request");

        Reset(); f = new Fixture();
        using (f.Begin())
        {
            ArenaSession.IsActive = false;
            Orphan(f.Original, "normal raid stays vanilla inside stale Arena context");
        }
        Reset(); f = new Fixture();
        using (f.Begin())
        {
            f.Pool.Instantiate = parent => { f.Sight = NewSight(parent, mounted: true); f.Sight.OnEnable(); return f.Sight.gameObject; };
            f.Original();
            Check(f.Sight.NativeEnables == 1, "mounted optic passes through during owned pool call");
            f.Sight.OnDisable();
            Check(f.Sight.NativeDisables == 1, "mounted optic disable remains vanilla");
        }

        Reset(); f = new Fixture();
        using (f.Begin())
        {
            f.Pool.Instantiate = parent =>
            {
                var foreign = NewSight(new GameObject().transform);
                Orphan(foreign.OnEnable, "unrelated optic hierarchy passes through inside owned call");
                f.Sight = NewSight(parent); f.Sight.OnEnable(); return f.Sight.gameObject;
            };
            f.Original();
            Check(f.Sight.NativeEnables == 0, "unrelated hierarchy check keeps genuine role model protected");
        }
    }

    private static void OwnershipExclusions()
    {
        foreach (string mismatch in new[] { "resource", "asset service", "raid parent", "original parent", "registry", "assembly", "pool replacement" })
        {
            Reset(); var f = new Fixture();
            using (f.Begin())
            {
                switch (mismatch)
                {
                    case "resource":
                        f.Pool.ResourceKey = new ResourceKey(f.Key.path, "other-subasset");
                        f.Factory.RaidPools.PoolsDictionary[f.Pool.ResourceKey] = f.Factory.RaidPools.PoolsDictionary[f.Key];
                        break;
                    case "asset service": f.Pool.IEasyAssets = new object(); break;
                    case "raid parent": f.Pool.ParentTransform = new GameObject().transform; break;
                    case "original parent":
                        Orphan(() => f.Pool.GetOriginalGameObject(new GameObject().transform), "foreign original parent passes through");
                        continue;
                    case "registry":
                        var other = NewPool(f.Factory, f.Key);
                        Check(!ReferenceEquals(other, f.Pool), "distinct registered pool fixture");
                        break;
                    case "assembly": f.Pool.AssemblyType = ObjectsFactory.AssemblyType.Online; break;
                    case "pool replacement": f.Factory.RaidPools = new ObjectsFactory.Pools(); break;
                }
                Orphan(f.Original, "foreign " + mismatch + " passes through");
            }
        }
        Reset(); var g = new Fixture();
        using (g.Begin())
        {
            KitBundlePreloader.Reset();
            Orphan(g.Original, "raid reset expires old request ownership");
        }
        Reset(); g = new Fixture();
        using (g.Begin())
        {
            g.Original();
            KitBundlePreloader.Reset();
            g.Sight.OnDisable();
            Check(g.Sight.NativeDisables == 1, "raid reset expires pending disable marker");
        }
        foreach (string state in new[] { "pending", "canceled", "faulted" })
        {
            Reset(); g = new Fixture();
            using (g.Begin())
            {
                var source = new TaskCompletionSource<IAssetPool>();
                if (state == "canceled") source.SetCanceled();
                if (state == "faulted") source.SetException(new InvalidOperationException("pool failed"));
                g.Factory.RaidPools.PoolsDictionary[g.Key].Source = source;
                Orphan(g.Original, state + " registered pool task passes through without blocking");
            }
        }
    }

    private static void NestedAndExceptionCleanup()
    {
        Reset(); var outer = new Fixture(); var inner = new Fixture();
        using (outer.Begin())
        {
            outer.Original();
            using (inner.Begin())
            {
                inner.Original();
                Orphan(outer.Original, "nested request masks outer resource ownership");
            }
            outer.Original();
            Check(outer.Sight.NativeEnables == 0, "nested request disposal restores outer request");
            var exception = new InvalidOperationException("instantiate failed");
            outer.Pool.Instantiate = parent => throw exception;
            Exception caught = null;
            try { outer.Original(); } catch (Exception ex) { caught = ex; }
            Check(ReferenceEquals(caught, exception), "pool exception retains exact native exception");
            var stray = NewSight(outer.Pool.ParentObject.transform);
            Orphan(stray.OnEnable, "failed original call clears synchronous ownership window");
        }

        Reset(); outer = new Fixture();
        var foreign = NewPool(outer.Factory, new ResourceKey("other.bundle"));
        // An unrelated reentrant load even sharing the same transform is masked.
        foreign.ParentObject = outer.Pool.ParentObject;
        foreign.Instantiate = parent => { var optic = NewSight(parent); optic.OnEnable(); return optic.gameObject; };
        outer.Pool.Instantiate = parent =>
        {
            Orphan(() => foreign.GetOriginalGameObject(parent), "nested foreign pool does not inherit outer synchronous frame");
            outer.Sight = NewSight(parent); outer.Sight.OnEnable(); return outer.Sight.gameObject;
        };
        using (outer.Begin())
        {
            outer.Original();
            Check(outer.Sight.NativeEnables == 0, "nested foreign call restores outer synchronous frame");
            var model = outer.Sight;
            model.gameObject.AddComponent<WeaponPrefab>();
            model.OnEnable();
            Check(model.NativeEnables == 1, "later mounted activation is not permanently suppressed");
            model.OnDisable();
            Check(model.NativeDisables == 1, "later mounted activation clears role model marker");
        }
    }

    private static async Task AsyncOwnership()
    {
        Reset(); var f = new Fixture();
        using (f.Begin())
        {
            await Task.Run(() =>
            {
                Protected(f.Original, "request ownership flows across asynchronous continuation");
                Check(f.Sight.NativeEnables == 0, "request ownership flows across asynchronous continuation");
                f.Sight.OnDisable();
            });
        }
        Reset(); f = new Fixture();
        var start = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        async Task Owned()
        {
            using (f.Begin())
            {
                start.SetResult();
                await release.Task;
                f.Original();
                Check(f.Sight.NativeEnables == 0, "suspended owned preload retains its own continuation");
            }
        }
        Task operation = Owned();
        await start.Task;
        Orphan(f.Original, "concurrent unrelated operation does not inherit suspended request");
        release.SetResult();
        await operation;

        Reset(); f = new Fixture();
        release = new TaskCompletionSource();
        Task escaped;
        using (f.Begin())
        {
            escaped = Task.Run(async () =>
            {
                await release.Task;
                Orphan(f.Original, "disposed request invalidates escaped child continuation");
            });
        }
        release.SetResult(); await escaped;

        Reset(); f = new Fixture();
        var scheduled = new ScheduledYield();
        async Task NativeStyleJob()
        {
            using (f.Begin())
            {
                await scheduled;
                Protected(f.Original, "ownership survives native style manually scheduled awaiter");
                Check(f.Sight.NativeEnables == 0, "manually scheduled job retains its own request");
                f.Sight.OnDisable();
            }
        }
        operation = NativeStyleJob();
        Orphan(f.Original, "queued native style job does not own unrelated caller");
        Task dispatch;
        using (ExecutionContext.SuppressFlow()) dispatch = Task.Run(() => scheduled.Continuation());
        await dispatch; await operation;
    }

    private static async Task Drain(IEnumerator routine)
    {
        int count = 0;
        while (routine.MoveNext())
        {
            if (routine.Current is IEnumerator nested) await Drain(nested);
            else await Task.Delay(1);
            if (++count >= 5000) Check(false, "preload coroutine completes within bounded test");
        }
        Check(true, "preload coroutine completes within bounded test");
    }

    private static KitDefinition SetupKit(Fixture f)
    {
        Singleton<ObjectsFactory>.Instance = f.Factory;
        Singleton<ItemFactory>.Instance = new ItemFactory();
        Singleton<ItemFactory>.Instance.ItemTemplates[new MongoID("scope")] = new ItemTemplate { AllResources = new() { f.Key } };
        return new KitDefinition { Id = "optic-kit", Items = new() { new KitItemNode { Tpl = "scope", ParentId = "weapon" } } };
    }

    private static async Task PreloaderIntegration()
    {
        Reset(); var f = new Fixture(); var kit = SetupKit(f);
        f.Factory.Load = async () =>
        {
            await Task.Yield();
            f.Original();
            Check(f.Sight.NativeEnables == 0, "production preloader owns asynchronous native pool work");
            f.Sight.OnDisable();
        };
        await Drain(KitBundlePreloader.Preload(kit));
        Check(ArenaMode.Plugin.ModLogger.Lines.Any(l => l.Contains("1 new item bundles preloaded")), "successful protected preload warms template");
        await Drain(KitBundlePreloader.Preload(kit));
        Check(f.Factory.Loads == 1, "warm template cache still avoids repeated native loads");
        Orphan(f.Original, "production preloader completion releases ownership");

        Reset(); f = new Fixture(); kit = SetupKit(f);
        TaskCompletionSource release = new(); Task nativeTask = null;
        f.Factory.Load = () => nativeTask = LoadAfterRelease();
        async Task LoadAfterRelease()
        {
            await release.Task;
            f.Original();
            Check(f.Sight.NativeEnables == 0, "disposing polling coroutine does not abandon native task protection");
            f.Sight.OnDisable();
        }
        var polling = KitBundlePreloader.Preload(kit);
        Check(polling.MoveNext(), "asynchronous native preload yields polling coroutine");
        (polling as IDisposable)?.Dispose();
        release.SetResult(); await nativeTask;
        Orphan(f.Original, "abandoned polling coroutine does not leak request ownership");

        Reset(); f = new Fixture(); kit = SetupKit(f);
        f.Factory.Load = () => throw new InvalidOperationException("synchronous load failure");
        await Drain(KitBundlePreloader.Preload(kit));
        Check(ArenaMode.Plugin.ModLogger.Lines.Any(l => l.Contains("synchronous load failure")), "synchronous pool failure remains visible");
        Orphan(f.Original, "throwing native load releases production request");
        f.Factory.Load = () => Task.FromCanceled(new CancellationToken(canceled: true));
        await Drain(KitBundlePreloader.Preload(kit));
        Check(f.Factory.Loads == 2, "failed and canceled native loads never publish warm cache");

        Reset(); f = new Fixture(); kit = SetupKit(f);
        release = new TaskCompletionSource();
        f.Factory.Load = async () => { await release.Task; Orphan(f.Original, "reset expires native task that outlives its coroutine"); };
        polling = KitBundlePreloader.Preload(kit);
        Check(polling.MoveNext(), "reset test waits for native completion");
        KitBundlePreloader.Reset();
        release.SetResult(); await Drain(polling);
        f.Factory.Load = () => Task.CompletedTask;
        await Drain(KitBundlePreloader.Preload(kit));
        Check(f.Factory.Loads == 2, "late completion cannot warm templates in next raid");
    }

    public static async Task<int> Main()
    {
        try
        {
            BasicOwnership();
            OwnershipExclusions();
            NestedAndExceptionCleanup();
            await AsyncOwnership();
            await PreloaderIntegration();
            Reset(); var f = new Fixture(); HarmonyLib.Harmony.FailInstall = true;
            Check(f.Begin() == null && HarmonyLib.Harmony.Hooks.Count == 0, "failed hook install rolls back and fails open");
            Orphan(f.Original, "failed hook install leaves native factory untouched");
            Reset(); f = new Fixture(); HarmonyLib.Harmony.FailAfter = 1;
            Check(f.Begin() == null && HarmonyLib.Harmony.Hooks.Count == 0, "partial hook install rolls back before admitting ownership");
            KitOpticPreloadGuard.Shutdown();
            Check(HarmonyLib.Harmony.Hooks.Count == 0, "shutdown removes all owned hooks");
            Console.WriteLine($"ARENA_OPTIC_PRELOAD_CONTRACTS_PASSED assertions={_assertions}");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
