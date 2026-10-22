namespace ArenaMode.Config {
    public sealed class Setting<T>(T value) { public T Value=value; }
    public static class Settings { public static Setting<bool> MaxSkills=new(true); }
}
namespace UnityEngine { public static class Time { public static float realtimeSinceStartup, time; } }
namespace Comfort.Common {
    public static class Singleton<T> where T:class { public static T Instance; public static bool Instantiated=>Instance!=null; }
}
namespace ArenaMode.Session {
    public static class ArenaSession {
        public static bool IsActive;
        public static EFT.AbstractGame Game;
        public static EFT.GameWorld World;
        public static bool IsExactActiveRaid(EFT.AbstractGame game,EFT.GameWorld world)
            => IsActive && ReferenceEquals(game,Game) && ReferenceEquals(world,World);
    }
}
namespace ArenaMode {
    public static class Plugin { public static Logger ModLogger=new(); }
    public sealed class Logger {
        public List<string> Warnings=new();
        public void LogWarning(object message)=>Warnings.Add(message.ToString());
        public void LogDebug(object message) { }
    }
}
namespace EFT {
    public class AbstractGame { }
    public class GameWorld { public Player MainPlayer=new(); }
    public class Player {
        public bool IsYourPlayer=true, IsAI;
        public SkillManager Skills=new();
        public object HandsController=new FirearmController();
        public void RecalculateEquipmentParams() { }
        public class FirearmController { public void SyncWithCharacterSkills() { } }
    }
    // Native Skill.Buff writes its field, recomputes rules, then invokes events.
    // A throwing listener is the boundary fault, not a mock of the restore code.
    public class Skill {
        public int Level=1, Writes;
        private int _buff;
        public Action<int> OnWrite;
        public int Buff { get=>_buff; set { _buff=value; Writes++; OnWrite?.Invoke(value); } }
    }
    public class Mastering { public float Current; public int Lvl1=100, Lvl2=200; }
    public class SkillManager {
        public Skill[] DisplayList={new(),new()};
        public Skill RecoilControl=new();
        public Dictionary<string,Mastering> Mastering=new(){["entry"]=new(){Current=17}};
        public FloatBuff ImmunityAvoidPoisonChance=new(), ImmunityAvoidMiscEffectsChance=new(),
            CharismaEliteScavCaseDiscount=new(), CharismaEliteFenceRepPenaltyReduction=new(),
            CharismaEliteAdditionalDailyQuests=new();
        public class FloatBuff { public float Value; }
    }
}
