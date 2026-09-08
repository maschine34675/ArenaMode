// Deliberate doubles for the observed native call order. These exercise linked
// production code and exact hook selection, not Unity/Mono/Harmony detouring.
using System.Reflection;
using System.Runtime.ExceptionServices;
using UnityEngine;

namespace HarmonyLib
{
    public sealed class HarmonyMethod
    {
        internal MethodInfo Method;
        public HarmonyMethod(Type type, string name) => Method = AccessTools.DeclaredMethod(type, name);
    }
    public static class AccessTools
    {
        public static MethodInfo DeclaredMethod(Type type, string name, Type[] parameters = null) => parameters == null
            ? type.GetMethod(name, BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
            : type.GetMethod(name, BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance, null, parameters, null);
    }
    public sealed class Harmony
    {
        public static bool FailInstall;
        public static int FailAfter = int.MaxValue;
        public static readonly Dictionary<MethodBase, (HarmonyMethod Prefix, HarmonyMethod Finalizer)> Hooks = new();
        public Harmony(string id) { }
        public void Patch(MethodBase original, HarmonyMethod prefix = null, HarmonyMethod finalizer = null)
        {
            if (FailInstall || Hooks.Count >= FailAfter) throw new InvalidOperationException("hook installation failed");
            Hooks.Add(original, (prefix, finalizer));
        }
        public void UnpatchSelf() => Hooks.Clear();
        private static object Invoke(MethodInfo method, Dictionary<string, object> args)
        {
            if (method == null) return null;
            var parameters = method.GetParameters();
            var values = parameters.Select(p => args.TryGetValue(p.Name, out var value) ? value : null).ToArray();
            try
            {
                var result = method.Invoke(null, values);
                for (int i = 0; i < parameters.Length; i++)
                    if (parameters[i].IsOut) args[parameters[i].Name] = values[i];
                return result;
            }
            catch (TargetInvocationException ex)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }
        public static bool Before(MethodBase method, Dictionary<string, object> args)
            => !Hooks.TryGetValue(method, out var hook) || Invoke(hook.Prefix?.Method, args) is not false;
        public static Exception Finish(MethodBase method, Dictionary<string, object> args)
            => Hooks.TryGetValue(method, out var hook) && hook.Finalizer != null
                ? (Exception)Invoke(hook.Finalizer.Method, args) : (Exception)args["__exception"];
    }
}
namespace UnityEngine
{
    public class Object { }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public T GetComponentInParent<T>(bool includeInactive) where T : Component
        {
            for (Transform t = transform; t != null; t = t.parent)
                if (t.gameObject.Components.OfType<T>().FirstOrDefault() is T value) return value;
            return null;
        }
    }
    public class Transform : Component
    {
        public Transform parent;
        public bool IsChildOf(Transform root)
        {
            for (Transform t = this; t != null; t = t.parent) if (ReferenceEquals(t, root)) return true;
            return false;
        }
    }
    public class GameObject : Object
    {
        public readonly Transform transform;
        public readonly List<Component> Components = new();
        public GameObject() { transform = new Transform { gameObject = this }; Components.Add(transform); }
        public T AddComponent<T>() where T : Component, new()
        {
            var c = new T { gameObject = this }; Components.Add(c); return c;
        }
    }
}
namespace EFT.CameraControl
{
    public class OpticSight : Component
    {
        public int NativeEnables, NativeDisables;
        public void OnEnable()
        {
            var args = new Dictionary<string, object> { ["__instance"] = this };
            if (!HarmonyLib.Harmony.Before(HarmonyLib.AccessTools.DeclaredMethod(typeof(OpticSight), nameof(OnEnable)), args)) return;
            NativeEnables++;
            FakeCamera.ReticleClears++;
            FakeCamera.Current = this;
            // CameraManager.method_10 requires the just-published sight's weapon.
            if (GetComponentInParent<EFT.AssetsManager.WeaponPrefab>(true) == null)
                throw new InvalidOperationException("orphan optic published");
        }
        public void OnDisable()
        {
            var args = new Dictionary<string, object> { ["__instance"] = this };
            if (!HarmonyLib.Harmony.Before(HarmonyLib.AccessTools.DeclaredMethod(typeof(OpticSight), nameof(OnDisable)), args)) return;
            NativeDisables++;
            FakeCamera.ReticleClears++;
            if (ReferenceEquals(FakeCamera.Current, this)) FakeCamera.Current = null;
        }
    }
    public static class FakeCamera
    {
        public static OpticSight Current;
        public static int ReticleClears;
    }
}
namespace EFT.AssetsManager
{
    public class AssetPoolObject : Component { }
    public class WeaponModPoolObject : AssetPoolObject { }
    public class WeaponPrefab : AssetPoolObject { }
    public class PoolContainerObject : Component { }
    public interface IAssetPool { }
    public class AssetPool<T> : IAssetPool where T : AssetPoolObject
    {
        public object IEasyAssets;
        public EFT.ResourceKey ResourceKey;
        public EFT.ObjectsFactory.AssemblyType AssemblyType;
        public Transform ParentTransform;
        public PoolContainerObject ParentObject = new GameObject().AddComponent<PoolContainerObject>();
        public Func<Transform, GameObject> Instantiate;
        public GameObject GetOriginalGameObject(Transform parent)
        {
            var method = HarmonyLib.AccessTools.DeclaredMethod(typeof(AssetPool<T>), nameof(GetOriginalGameObject), new[] { typeof(Transform) });
            var args = new Dictionary<string, object> { ["__instance"] = this, ["parent"] = parent };
            GameObject result = null;
            Exception error = null;
            try { if (HarmonyLib.Harmony.Before(method, args)) result = Instantiate(parent); }
            catch (Exception ex) { error = ex; }
            args["__exception"] = error;
            error = HarmonyLib.Harmony.Finish(method, args);
            if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
            return result;
        }
    }
}
namespace EFT
{
    public sealed record ResourceKey(string path, string rcid = null)
    {
        public static readonly ResourceKey EmptyResourceKey = new("");
    }
    public class ItemFactory { public Dictionary<MongoID, ItemTemplate> ItemTemplates = new(); }
    public class ItemTemplate { public List<ResourceKey> AllResources = new(); }
    public class ObjectsFactory
    {
        public enum PoolsCategory { Raid, Menu }
        public enum AssemblyType { Local, Online }
        public class Pools
        {
            public class AssetPoolData { public TaskCompletionSource<EFT.AssetsManager.IAssetPool> Source; }
            public Transform UnloadableParent = new GameObject().transform;
            public Transform _dontDestroyParent = new GameObject().transform;
            public Dictionary<ResourceKey, AssetPoolData> PoolsDictionary = new();
        }
        public object EasyAssets = new();
        public Pools RaidPools = new();
        public Func<Task> Load;
        public int Loads;
        public static CancellationToken DefaultCancellationToken => default;
        public Pools GetPools(PoolsCategory category) => RaidPools;
        public Task LoadBundlesAndCreatePools(PoolsCategory category, AssemblyType assembly,
            ICollection<ResourceKey> keys, object priority, object progress, CancellationToken cancellation)
        {
            Loads++;
            if (category != PoolsCategory.Raid || assembly != AssemblyType.Local) throw new Exception("wrong pool arguments");
            return Load();
        }
    }
}
public readonly record struct MongoID(string Value);
namespace Diz.Jobs { public static class JobYieldPriority { public static object General => null; } }
namespace Comfort.Common { public static class Singleton<T> { public static T Instance; } }
namespace ArenaMode
{
    public static class Plugin
    {
        public const string PluginGuid = "com.maschine.ArenaMode";
        public static readonly FakeLogger ModLogger = new();
    }
    public sealed class FakeLogger
    {
        public readonly List<string> Lines = new();
        public void LogInfo(string text) => Lines.Add(text);
        public void LogWarning(string text) => Lines.Add(text);
    }
}
namespace ArenaMode.Session { public static class ArenaSession { public static bool IsActive; } }
namespace ArenaMode.Modes
{
    public sealed class GunGameMode { }
    public sealed class ArenaMatchService { public static ArenaMatchService Current; public object Mode; }
}
namespace ArenaMode.Kits
{
    public class KitDefinition { public string Id; public List<KitItem> Items = new(); public KitFill Fill; }
    public class KitItem { public string ParentId; public string Tpl; }
    public class KitStackedItem { public string Tpl; }
    public class KitFill { public List<KitStackedItem> RigContents = new(); }
    public static class KitCatalog { public static List<KitDefinition> Kits; }
    public static class LadderCatalog { public static List<KitDefinition> Rungs; }
}
