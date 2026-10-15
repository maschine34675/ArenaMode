using System.Reflection;
using EFT;
using Comfort.Common;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b) => ReferenceEquals(a,b) || (ReferenceEquals(a,null) ? b.Destroyed : ReferenceEquals(b,null) && a.Destroyed);
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object obj) => ReferenceEquals(this,obj);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
}
namespace Comfort.Common { public static class Singleton<T> where T:class { public static T Instance; public static bool Instantiated => Instance != null; } }
namespace EFT
{
    public enum GameStatus { Started, Stopping, Stopped }
    public enum EBotState { Active, PreActive }
    public class AbstractGame { public GameStatus Status=GameStatus.Started; }
    public class LocalGame:AbstractGame {}
    public class Profile { public string AccountId; }
    public interface IPlayer { string ProfileId {get;} Profile Profile {get;} }
    public class Health { public bool IsAlive=true; }
    public class AIData { public BotOwner BotOwner; }
    public class Player:UnityEngine.Object,IPlayer
    {
        public string ProfileId {get;set;}
        public Profile Profile {get;set;}=new();
        public Health HealthController=new();
        public AIData AIData=new();
        public int Side;
    }
    public class BotOwner:UnityEngine.Object,IPlayer
    {
        public Player GetPlayer;
        public string ProfileId => GetPlayer.ProfileId;
        public Profile Profile=>GetPlayer.Profile;
        public EBotState BotState=EBotState.Active;
        public bool IsDead => !GetPlayer.HealthController.IsAlive;
    }
    public interface IObserverToPlayerBridge { IPlayer iPlayer {get;} }
    public class Bridge:IObserverToPlayerBridge { public IPlayer Body; public IPlayer iPlayer=>Body; }
    public class PlayerEmitterCache { public int Value; }
    public class GameWorld:UnityEngine.Object
    {
        public Player MainPlayer;
        public Dictionary<string,Player> Alive=new(), Ever=new();
        public Player GetAlivePlayerByProfileID(string id)=>id!=null&&Alive.TryGetValue(id,out var b)?b:null;
        public Player GetEverExistedPlayerByID(string id)=>id!=null&&Ever.TryGetValue(id,out var b)?b:null;
        public IObserverToPlayerBridge GetEverExistedBridgeByProfileID(string id)=>new Bridge{Body=GetEverExistedPlayerByID(id)};
    }
    public static class ExplosionSharedMethods { public static Dictionary<IObserverToPlayerBridge,PlayerEmitterCache> _playerBuffer=new(); public static void Explosion(){} public static void AffectPlayersShared(){} }
    public static class FirearmControllerSharedMethods { public static void ApplyLightAndSoundHealthEffects(){} }
}
namespace HarmonyLib
{
    public static class Priority { public const int Last=0; }
    public class HarmonyPriority:Attribute { public HarmonyPriority(int value){} }
    public static class AccessTools { public static MethodInfo Method(Type type,string name)=>type.GetMethod(name); }
}
namespace SPT.Reflection.Patching
{
    public class PatchPrefix:Attribute{}
    public class PatchFinalizer:Attribute{}
    public abstract class ModulePatch { protected abstract MethodBase GetTargetMethod(); public void Enable(){} }
}
namespace ArenaMode
{
    public static class Plugin { public static Logger ModLogger=new(); }
    public class Logger { public readonly List<string> Infos=new(); public bool Throw; public void LogWarning(string s){} public void LogInfo(string s){if(Throw)throw new InvalidOperationException("log failure");Infos.Add(s);} public void LogDebug(string s){if(Throw)throw new InvalidOperationException("log failure");Infos.Add(s);} }
}
namespace ArenaMode.Session
{
    internal static class ArenaSession
    {
        internal static bool IsActive=true, FriendlyFireEnabledSelection=false;
        internal static AbstractGame Game; internal static GameWorld World;
        internal static bool IsExactActiveRaid(AbstractGame g,GameWorld w)=>IsActive&&ReferenceEquals(g,Game)&&ReferenceEquals(w,World);
    }
}
namespace ArenaMode.Compat { internal static class PitFireTeamCompat { internal static bool TeamArenaActive=true; } }
namespace ArenaMode.Bots
{
    // Existing TDM relation is exercised independently by TeamDeathmatchRuntimeChecks.
    // This test seam verifies the new central decision delegates only when OFF.
    internal static class TeamDeathmatchRelations
    {
        internal static bool ShouldBlockFriendlyDamage(IPlayer victim,IPlayer attacker)
            => victim is Player v && attacker is Player a && v.Side==a.Side && v.ProfileId!=a.ProfileId;
    }
}
namespace ArenaMode.Modes
{
    public class ArenaMatch { public object Mode; }
    public static class ArenaMatchService { public static ArenaMatch Current; }
    public class TeamDeathmatchMode {}
    public class ZombieMode
    {
        internal ZombieTeamRuntime Team;
        internal bool ShouldBlockFriendlyDamage(Player victim,IPlayer attacker)=>ReferenceEquals(ArenaMatchService.Current?.Mode,this)&&Team?.ShouldBlockFriendlyDamage(victim,attacker)==true;
    }
    public sealed partial class TacticalMode
    {
        private int TeamCompanionCount;
        private enum Phase { Live, Staging }
        private bool _teamArena=true; private Phase _phase=Phase.Live; private int _teamRoundEpoch=1;
        private ArenaMatch _match; private LocalGame _teamGame;
        private Player _boundLocalPlayer; private string _boundLocalProfileId;
        private BotOwner[] _boundTeamFollowers,_boundTeamEnemies;
        private string[] _boundTeamFollowerProfileIds,_boundTeamEnemyProfileIds;
        public bool IsComplete;
        internal void BindForTest(Fixture f)
        {
            TeamCompanionCount=f.CompanionCount;
            _match=f.Match;_teamGame=f.Game;_boundLocalPlayer=f.Local;_boundLocalProfileId=f.Local.ProfileId;
            _boundTeamFollowers=f.Followers;_boundTeamEnemies=f.Enemies;
            _boundTeamFollowerProfileIds=f.Followers.Select(x=>x.ProfileId).ToArray();
            _boundTeamEnemyProfileIds=f.Enemies.Select(x=>x.ProfileId).ToArray();BindFriendlyFireRoster();
        }
        internal void SetScope(bool team,bool live,int epoch=1){_teamArena=team;_phase=live?Phase.Live:Phase.Staging;_teamRoundEpoch=epoch;}
    }
    internal sealed partial class ZombieTeamRuntime
    {
        private int TeamCompanionCount;
        private ArenaMatch _match; private LocalGame _game; private GameWorld _world;
        private Player _boundLocalPlayer; private string _boundLocalProfileId;
        private BotOwner[] _boundTeamFollowers; private string[] _boundTeamFollowerProfileIds;
        internal bool Live=true;
        private bool IsLive=>Live&&ReferenceEquals(ArenaMatchService.Current,_match)
            && ReferenceEquals(Singleton<AbstractGame>.Instance,_game)&&ReferenceEquals(Singleton<GameWorld>.Instance,_world)
            && ArenaMode.Session.ArenaSession.IsExactActiveRaid(_game,_world)&&_game.Status==GameStatus.Started;
        internal void BindForTest(Fixture f)
        {
            TeamCompanionCount=f.CompanionCount;
            _match=f.Match;_game=f.Game;_world=f.World;_boundLocalPlayer=f.Local;_boundLocalProfileId=f.Local.ProfileId;
            _boundTeamFollowers=f.Followers;_boundTeamFollowerProfileIds=f.Followers.Select(x=>x.ProfileId).ToArray();BindFriendlyFireRoster();
        }
    }
}

// Explicitly enabled for these diagnostic-observation scenarios; the real
// default-off BepInEx routing is exercised by tools/LoggingChecks.
namespace ArenaMode.Diagnostics
{
    internal static class ArenaLog
    {
        internal static bool DeveloperEnabled = true;
        internal static void Developer(string message)
        {
            if (DeveloperEnabled) ArenaMode.Plugin.ModLogger?.LogInfo("[dev] " + message);
        }
    }
}
