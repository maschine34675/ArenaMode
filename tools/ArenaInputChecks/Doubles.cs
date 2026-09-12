// Native objects and detour I/O only. Production owns every filtering/release decision.
using System.Reflection;
namespace EFT
{
    public class AbstractGame { }
    public class Player { public bool IsYourPlayer=true, IsAI; }
    public class GameWorld { public Player MainPlayer; }
    public class GamePlayerOwner
    {
        public static Player MyPlayer;
        public static bool bool_0;
        public static void SetIgnoreInput(bool ignore) { bool_0=ignore; }
    }
}
namespace Comfort.Common { public static class Singleton<T> { public static T Instance; } }
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool Active;
        public static EFT.AbstractGame Game; public static EFT.GameWorld World;
        public static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world)
            => Active && game!=null && world!=null && ReferenceEquals(game,Game) && ReferenceEquals(world,World);
    }
}
namespace UnityEngine
{
    public static class Input
    {
        public static bool[] Held=new bool[3], Down=new bool[3];
        public static bool GetMouseButton(int i)=>Held[i];
        public static bool GetMouseButtonDown(int i)=>Down[i];
    }
}
namespace ArenaMode
{
    public static class Plugin { public const string PluginGuid="test"; public static Log ModLogger=new(); }
    public class Log
    {
        public List<string> Lines=new();
        public void LogInfo(object x)=>Lines.Add(x.ToString());
        public void LogWarning(object x)=>Lines.Add(x.ToString());
    }
}
namespace EFT.InputSystem
{
    public enum ECommand { ToggleShooting, EndShooting, Escape, ReloadWeapon, ToggleSprint, EndSprint, BlindShootEnd, StopTalk, ReleaseInteraction }
    public class InputCombination { }
    public class InputPreset
    {
        public InputCombination[] _workingKeyCombinations=[];
        public void UpdateInput(List<ECommand> commands,float[] axes,float dt) { }
    }
    public class InputManager
    {
        public bool _isMain=true;
        public List<ECommand> _commandsList=new(), _emptyCommandsList=new();
        public float[] _axesList=new float[3];
        public static InputPreset _currentPreset=new();
        public void Update() { }
        public void OnDestroy() { }
        public void DispatchInput(List<ECommand> commands,float[] axes) { }
    }
}
namespace HarmonyLib
{
    public static class Priority { public const int Normal=400, Last=0; }
    public class HarmonyMethod(MethodInfo method) { public MethodInfo Method=method; public int priority; }
    public static class AccessTools
    {
        public static Type TypeByName(string name)=>typeof(EFT.InputSystem.InputManager).Assembly.GetType(name);
        public static MethodInfo Method(Type type,string name)=>type.GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public);
    }
    public class Harmony(string id)
    {
        public record Entry(string Id,HarmonyMethod Prefix,HarmonyMethod Postfix);
        public static Dictionary<MethodInfo,Entry> Hooks=new();
        public static int FailAt;
        public static bool FailUnpatch;
        public void Patch(MethodInfo original,HarmonyMethod prefix=null,HarmonyMethod postfix=null)
        { if(FailAt>0 && --FailAt==0)throw new Exception("detour rejected"); Hooks[original]=new(id,prefix,postfix); }
        public void UnpatchSelf()
        {
            if(FailUnpatch) throw new Exception("unpatch rejected");
            foreach(var key in Hooks.Where(x=>x.Value.Id==id).Select(x=>x.Key).ToArray())Hooks.Remove(key);
        }
    }
}
