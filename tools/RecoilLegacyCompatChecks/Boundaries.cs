using System.Reflection;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static void Destroy(Object o) { if (!ReferenceEquals(o, null)) o.Destroyed = true; }
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Destroyed, bn = ReferenceEquals(b, null) || b.Destroyed;
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object o) => ReferenceEquals(this, o);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public T GetComponent<T>() where T : class => GetComponent(typeof(T)) as T;
        public Component GetComponent(Type t) => gameObject?.Components.FirstOrDefault(c => !c.Destroyed && t.IsAssignableFrom(c.GetType()));
    }
    public class Behaviour : Component { public bool enabled = true; }
    public class MonoBehaviour : Behaviour { }
    public class GameObject : Object
    {
        public readonly List<Component> Components = new();
        public T Attach<T>(T component) where T : Component { component.gameObject = this; Components.Add(component); return component; }
        public Component AddComponent(Type t)
        {
            var component = Attach((Component)Activator.CreateInstance(t));
            t.GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(component, null);
            return component;
        }
    }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x=x; this.y=y; }
        public static Vector2 zero => new(0,0);
        public static Vector2 one => new(1,1);
    }
}
namespace BepInEx
{
    public class BaseUnityPlugin { }
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class BepInPlugin(string guid, string name, string version) : Attribute
    { public string GUID => guid; public Version Version => new(version); }
    public class PluginInfo { public BaseUnityPlugin Instance; public BepInPlugin Metadata; }
}
namespace BepInEx.Bootstrap
{ public static class Chainloader { public static readonly Dictionary<string, BepInEx.PluginInfo> PluginInfos = new(); } }
namespace HarmonyLib
{
    public static class Priority { public const int First=800, Last=0; }
    public class HarmonyMethod(Type type, string name)
    {
        public int priority;
        public MethodInfo Method=type.GetMethod(name, BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public);
    }
    public class Patch { public string owner; public MethodInfo PatchMethod; }
    public class PatchInfo { public List<Patch> Prefixes=new(),Postfixes=new(),Transpilers=new(),Finalizers=new(),ILManipulators=new(); }
    public class Harmony(string id)
    {
        public string Id=>id;
        public static readonly Dictionary<MethodBase,PatchInfo> Bound=new();
        public static bool DropReceipt;
        public static PatchInfo GetPatchInfo(MethodBase target)=>Bound.GetValueOrDefault(target);
        public void Patch(MethodInfo target, HarmonyMethod prefix=null, HarmonyMethod postfix=null, HarmonyMethod transpiler=null, HarmonyMethod finalizer=null)
        {
            if(!Bound.TryGetValue(target,out var info)) Bound[target]=info=new();
            if(DropReceipt)return;
            if(prefix!=null)info.Prefixes.Add(new(){owner=id,PatchMethod=prefix.Method});
            if(postfix!=null)info.Postfixes.Add(new(){owner=id,PatchMethod=postfix.Method});
            if(finalizer!=null)info.Finalizers.Add(new(){owner=id,PatchMethod=finalizer.Method});
        }
        public void UnpatchSelf()
        {
            foreach(var p in Bound.Values)
            { p.Prefixes.RemoveAll(p=>p.owner==id);p.Postfixes.RemoveAll(p=>p.owner==id);p.Finalizers.RemoveAll(p=>p.owner==id); }
        }
    }
}
namespace Comfort.Common { public static class Singleton<T> where T:class { public static T Instance; public static bool Instantiated=>Instance!=null; } }
public enum GameStatus { Running, Starting, Started, Stopping, Stopped }
public class GlobalConfiguration { public class AimingConfiguration { } }
namespace JsonType
{
    public enum ESideType { Pmc, Savage }
    public class LocalRaidSettings { public string location="factory4_day",serverId; public ESideType playerSide=ESideType.Pmc; }
}
namespace EFT.InventoryLogic { public class WeaponTemplate { public float Value; } }
namespace EFT.Animations.NewRecoil
{
    public class NewRecoilShotEffect
    {
        public void RecalculateRecoilParamsOnChangeWeapon(EFT.InventoryLogic.WeaponTemplate t, GlobalConfiguration.AimingConfiguration a,
            EFT.Player.FirearmController fc,float x,float y,float z,float m) { }
    }
}
namespace EFT
{
    public interface IBotGame { }
    public class AbstractGame { }
    public class LocalGame : AbstractGame,IBotGame
    {
        public GameWorld GameWorld; public LocalPlayer LocalPlayer; public object Profile=new();
        public GameStatus Status=GameStatus.Started; public JsonType.LocalRaidSettings Settings=new();
    }
    public class GameWorld { public string LocationId="factory4_day";public Player MainPlayer; }
    public class HideoutGameWorld : GameWorld { }
    public class Player : UnityEngine.MonoBehaviour
    {
        public GameWorld GameWorld;public object Profile;public bool IsYourPlayer=true;
        public Health HealthController=new();public object HandsController;public Pwa ProceduralWeaponAnimation=new();
        public int Rotations;
        public Task Init()=>Task.CompletedTask;
        public class FirearmController : UnityEngine.MonoBehaviour { public InventoryLogic.WeaponTemplate Template=new(); }
    }
    public class LocalPlayer:Player { }
    public class Health { public bool IsAlive=true; }
    public class Pwa { public Shooting Shootingg=new(); }
    public class Shooting
    {
        public Animations.NewRecoil.NewRecoilShotEffect NewShotRecoil=new();
        public Action Calculate;
        public void OnWeaponParametersChanged()=>Calculate?.Invoke();
    }
}
namespace Redeploy.Core.GameAccess { public static class LocalGameAccessor { public static JsonType.LocalRaidSettings GetLocalRaidSettings(EFT.LocalGame game)=>game.Settings; } }
namespace Redeploy.Core.Api
{
    public static class RespawnApi
    {
        public static bool IsReplacing;
        public static event Action<EFT.Player,EFT.Player,string> Replaced;
        public static event Action<EFT.Player,string> ReplacementFailed;
        public static event Action RaidEnded;
        public static void Replace(EFT.Player old,EFT.Player next)=>Replaced?.Invoke(old,next,"swap");
        public static void Fail(EFT.Player old)=>ReplacementFailed?.Invoke(old,"rewire");
        public static void End()=>RaidEnded?.Invoke();
    }
}
namespace ArenaMode
{
    public static class Plugin { public const string PluginGuid="com.maschine.ArenaMode";public static readonly Logger ModLogger=new(); }
    public class Logger { public void LogDebug(string s){}public void LogWarning(string s){} }
}
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsActive,ArmedForNextRaid;public static string ArmedMap;
        public static EFT.LocalGame ActiveGame;public static EFT.GameWorld ActiveWorld;
        public static bool IsExactActiveRaid(EFT.LocalGame g,EFT.GameWorld w)=>IsActive&&ReferenceEquals(g,ActiveGame)&&ReferenceEquals(w,ActiveWorld);
    }
    public static class ArenaServerClient { public static bool HasPendingArm;public static string PendingArmToken; }
}
