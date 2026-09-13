// Native game/Harmony/Unity I/O only. No readiness, purchase or Escape decision
// is implemented here. Production methods decide; stubs expose side effects.
using System.Reflection;
namespace EFT
{
    public enum EBotState { PreActive, Active, NonActive }
    public enum EDamageType { Undefined }
    public enum GameStatus { Started, Stopping }
    public class AbstractGame{}
    public class Health { public bool IsAlive=true; public void Kill(EDamageType _) {IsAlive=false;} }
    public class Player { public bool IsYourPlayer=true,IsAI; public Health HealthController=new(); public Health ActiveHealthController=>HealthController; }
    public class LocalGame:AbstractGame { public GameStatus Status=GameStatus.Started; public Player LocalPlayer; public EftGamePlayerOwner Owner; }
    public class GameWorld { public Player MainPlayer; }
    public class BotOwner { public Player GetPlayer=new(); public Health HealthController=>GetPlayer?.HealthController; public bool IsDead; public EBotState BotState=EBotState.PreActive; public string ProfileId="bot"; }
    public class PlayerOwner { public enum EState { Started,Stopped } public EState State=EState.Started; public Player Player; }
    public class GamePlayerOwner:PlayerOwner { public static Player MyPlayer; }
    public class EftGamePlayerOwner:GamePlayerOwner
    {
        public int Menus; public bool Throw,Accept=true;
        public InputSystem.InputNode.ETranslateResult TranslateCommand(InputSystem.ECommand c)=>InputSystem.InputNode.ETranslateResult.Ignore;
        public bool TranslateExitScreenInput(InputSystem.ECommand c){if(Throw)throw new Exception("native menu rejected");if(!Accept)return false;Menus++;UI.Screens.EftScreenManager.Instance.Screen=UI.Screens.EEftScreenType.MainMenu;return true;}
    }
}
namespace EFT.InputSystem {public enum ECommand { Escape,ToggleShooting,ReloadWeapon } public class InputNode { public enum ETranslateResult {Ignore,BlockAll} } }
namespace EFT.UI.Screens
{
    public enum EEftScreenType { MainMenu,Settings,BattleUI,Inventory }
    public class EftScreenManager {public static EftScreenManager Instance=new(); public EEftScreenType Screen=EEftScreenType.BattleUI; public bool CheckCurrentScreen(EEftScreenType x)=>Screen==x;}
}
namespace Comfort.Common { public static class Singleton<T>{public static T Instance;} }
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsActive=true;
        public static EFT.AbstractGame Game;public static EFT.GameWorld World;
        public static bool IsExactActiveRaid(EFT.AbstractGame g,EFT.GameWorld w)=>IsActive&&g!=null&&w!=null&&ReferenceEquals(Game,g)&&ReferenceEquals(World,w);
    }
}
namespace ArenaMode.Kits
{
    public class KitDefinition {public string Id,Name;public int Price;public bool Invalid;}
    public static class KitBuilder {public static List<string> ValidateKit(KitDefinition k)=>k.Invalid?["invalid"]:[];}
    public static class KitCatalog
    {
        public static IReadOnlyList<KitDefinition> Kits=[];public static Func<KitDefinition> KitProvider;
        public static KitDefinition GetBaseKit()=>Kits.OrderByDescending(x=>x.Price).First();
    }
    public static class KitSummary {public static string For(KitDefinition k)=>"";}
}
namespace ArenaMode.Config
{
    public class Entry<T>(T v){public T Value=v;}
    public static class Settings
    {
        public static Entry<float> TacticalRoundSeconds=new(600),SpawnProtectionSeconds=new(5),TacticalBuySeconds=new(5);
        public static Entry<int> TacticalSquadSize=new(5);
        public static Entry<bool> TacticalSquadIsPmc=new(true);
        public static Entry<BepInEx.Configuration.KeyboardShortcut> PanelHotkey=new(new());
    }
}
namespace ArenaMode.Bots
{
    public static class ArenaPressure {public static object DifficultyFor(bool _) => null;}
    public static class BurstSpawnService
    {
        public record Request(int Expected,Action<EFT.BotOwner> Observer,Func<bool> Lease);
        public static List<Request> Requests=[];public static bool Reject;
        public static int SpawnSquad(int count,bool asPmc,object difficulty,Action<EFT.BotOwner> observer,Func<bool> requestStillCurrent){Requests.Add(new(count,observer,requestStillCurrent));return Reject?0:count;}
    }
}
namespace ArenaMode.Respawn
{
    public static class ArenaRespawnDriver {public static bool StandDown;}
    public static class LiveKitRespawn {public static int Swaps;public static bool AtRoundBreak(EFT.LocalGame g,float s,Func<bool> wanted,Action<bool> done){Swaps++;return true;}}
}
namespace ArenaMode.Combat { public static class ArenaAdministrativeDeathGate {public static IDisposable Enter(string _) => new End(); private class End:IDisposable{public void Dispose(){}}} }
namespace ArenaMode
{
    public static class ArenaToasts {public static void Show(string x){}}
    public static class Plugin {public static Logger ModLogger=new();}
    public class Logger {public void LogInfo(string x){} public void LogWarning(string x){} public void LogError(string x){}}
}
namespace Redeploy.Core.Engine {public static class RespawnEngineState {public static bool IsPipelineRunning;}}
namespace Redeploy.Core.GameAccess {public static class LocalGameAccessor {public static EFT.EftGamePlayerOwner GetPlayerOwner(EFT.LocalGame g)=>g.Owner;}}
namespace Redeploy.Core.Ui {}
namespace ArenaMode.Hud
{
    public static class CursorVote {public static void Set(object x,bool b){}}
    public static class ArenaInputBlocker {public static bool Open,Drained;public static void Request(object x,bool open,bool drainOnRelease=false){Open=open;Drained=!open&&drainOnRelease;}}
}
namespace BepInEx.Configuration { public class KeyboardShortcut {public UnityEngine.KeyCode MainKey;public UnityEngine.KeyCode[] Modifiers=[];} }
namespace HarmonyLib {public static class AccessTools {public static MethodInfo DeclaredMethod(Type t,string n,Type[] a)=>t.GetMethod(n,a);} }
namespace SPT.Reflection.Patching {public class PatchPrefix:Attribute{} public abstract class ModulePatch {protected abstract MethodBase GetTargetMethod();public void Enable(){if(GetTargetMethod()==null)throw new Exception("missing hook");}}}
namespace UnityEngine
{
    public class MonoBehaviour{}
    public static class Time {public static float time,unscaledTime;}
    public static class Mathf {public static float Max(float a,float b)=>Math.Max(a,b);public static int Max(int a,int b)=>Math.Max(a,b);public static float Min(float a,float b)=>Math.Min(a,b);}
    public enum KeyCode {Escape,F9}
    public static class Input {public static bool GetKeyDown(KeyCode k)=>false;public static bool GetKey(KeyCode k)=>false;}
    public enum EventType {KeyDown,Repaint}
    public class Event {public static Event current=new();public EventType type;public KeyCode keyCode;public bool Used;public void Use()=>Used=true;}
    public struct Vector2{}
    public struct Rect(float x,float y,float w,float h){public float width=w;}
    public class RectOffset(int a,int b,int c,int d){}
    public enum FontStyle {Bold}
    public enum TextAnchor {MiddleLeft}
    public struct Color(float a,float b,float c,float d){}
    public class GUIStyleState {public Color textColor;}
    public class GUIStyle {public RectOffset padding;public int fontSize;public FontStyle fontStyle;public TextAnchor alignment;public bool wordWrap;public GUIStyleState normal=new();public GUIStyle(){}public GUIStyle(GUIStyle s){} }
    public class GUISkin {public GUIStyle window=new(),label=new(),button=new();}
    public static class GUI {public static int depth;public static bool enabled;public static GUISkin skin=new();public static Rect Window(int id,Rect r,Action<int> draw,string title,GUIStyle s)=>r;public static void DragWindow(Rect r){} }
    public static class Screen {public static int width=1920,height=1080;}
    public static class GUILayout
    {
        public static void Space(float x){} public static void Label(string x,GUIStyle s,params object[] options){} public static Vector2 BeginScrollView(Vector2 v)=>v;public static void EndScrollView(){}
        public static void BeginHorizontal(){} public static void EndHorizontal(){} public static object MinWidth(float x)=>null;public static object Width(float x)=>null;public static object Height(float x)=>null;
        public static bool Button(string x,GUIStyle s,params object[] options)=>false;
    }
}
