// I/O spies, not a Unity/SAIN runtime or replacement assembly for distribution.
using System.Reflection;
using System.Runtime.CompilerServices;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b)
        { bool an = ReferenceEquals(a, null) || a.Destroyed, bn = ReferenceEquals(b, null) || b.Destroyed; return an || bn ? an == bn : ReferenceEquals(a,b); }
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    }
    public class Component : Object
    {
        public Dictionary<Type, Component> Components = new();
        public Component GetComponent(Type type) => Components.GetValueOrDefault(type);
    }
    public class MonoBehaviour : Component { public int CoroutineStops; public void StopAllCoroutines() => CoroutineStops++; }
}
namespace EFT
{
    public interface IHandsController { }
    public enum EBotState { PreActive, Active, NonActive, Disposed }
    public class AbstractGame : UnityEngine.Object { }
    public class Player : UnityEngine.Component
    {
        public string ProfileId = Guid.NewGuid().ToString(); public bool IsAI = true;
        public MovementContext MovementContext = new();
    }
    public class MovementContext { public Action PickupAction; }
    public class BotOwner : UnityEngine.Component
    {
        public bool IsDead; public BotWeaponManager WeaponManager;
        public Player GetPlayer; public EBotState BotState = EBotState.Active;
        public BotItemTaker ItemTaker; public bool MedecinePresent = true; public int Disposals;
        public void Dispose() { if (BotState == EBotState.PreActive) return; Disposals++; MedecinePresent = false; ItemTaker = null; BotState = EBotState.NonActive; }
    }
    public class GameWorld : UnityEngine.Object
    {
        public string LocationId = "factory4_day";
        public Dictionary<string, Player> Players = new();
        public Player GetEverExistedPlayerByID(string id) => Players.GetValueOrDefault(id);
    }
}
public class BotItemTaker
{
    public EFT.BotOwner _owner;
    public class CG_PickupAction
    {
        public EFT.Player owner; public BotItemTaker botItemTaker_0;
        public int Calls;
        public void method_0() { if (!botItemTaker_0._owner.MedecinePresent) throw new ArgumentException("retired pickup receiver"); Calls++; }
        public void OtherAction() => Calls++;
    }
}
namespace Comfort.Common
{
    public readonly struct Result<T>(T value) { public T Value => value; }
    public static class Singleton<T> { public static T Instance; }
}
public class BotWeaponManager { public EFT.BotOwner _owner; public bool _disposed; }
public class BotWeaponSelector
{
    public EFT.BotOwner _owner; public bool IsChanging=true, IsWeaponReady=true;
    public void OnWeaponTaken(Comfort.Common.Result<EFT.IHandsController> result) { }
}
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsActive, ArmedForNextRaid; public static string ArmedMap;
        public static EFT.AbstractGame Game; public static EFT.GameWorld World;
        public static bool IsExactActiveRaid(EFT.AbstractGame g, EFT.GameWorld w) => IsActive && ReferenceEquals(g, Game) && ReferenceEquals(w, World);
    }
}
namespace ArenaMode
{
    public static class Plugin { public const string PluginGuid = "test"; public static Logger ModLogger = new(); }
    public class Logger
    {
        public List<string> Lines = new();
        public void LogInfo(object text) => Lines.Add(text.ToString());
        public void LogWarning(object text) => Lines.Add(text.ToString());
    }
}
namespace HarmonyLib
{
    public static class Priority { public const int First = 800; }
    public class HarmonyMethod(MethodInfo method) { public MethodInfo method = method; public int priority; }
    public class Harmony(string id)
    {
        public static Dictionary<MethodInfo, (string Id, HarmonyMethod Prefix)> Hooks = new();
        public static bool Reject;
        public void Patch(MethodInfo original, HarmonyMethod prefix)
        { if (Reject) throw new InvalidOperationException("binding spy rejected"); Hooks[original] = (id, prefix); }
        public void UnpatchSelf() { foreach (var k in Hooks.Where(x=>x.Value.Id==id).Select(x=>x.Key).ToArray()) Hooks.Remove(k); }
    }
}
namespace SAIN.SAINComponent.Classes
{
    public class SAINActivationClass
    {
        public int Calls; public bool LastValue, Throw;
        public void SetActive(bool value) { Calls++; LastValue=value; if (Throw) throw new InvalidOperationException("live activation fault"); }
    }
}
namespace SAIN.Components
{
    public class BotComponent : UnityEngine.MonoBehaviour
    {
        public EFT.BotOwner BotOwner { get; set; }
        public SAIN.SAINComponent.Classes.SAINActivationClass BotActivation { get; set; }
        // Minimal contract fixture: records activation and coroutine-cleanup calls.
        private void OnDisable() { BotActivation.SetActive(false); StopAllCoroutines(); }
        private void ChangedDisable() { BotActivation.SetActive(false); StopAllCoroutines(); CoroutineStops++; }
    }
}
