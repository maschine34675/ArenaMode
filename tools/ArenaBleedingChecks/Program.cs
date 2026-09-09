using ArenaMode.Combat;
using ArenaMode.Config;
using ArenaMode.Modes;
using ArenaMode.Respawn;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;

internal static partial class Program
{
    private static int checks;
    private static bool Protected;
    private static bool ShouldBlockProtectedTeamMutation(Player _, string __) => Protected;
    private static void Check(bool value, string label) { checks++; if(!value) throw new Exception("FAIL: "+label); }
    private static ActiveHealthController Setup(bool ai=false)
    {
        var player=new Player { IsAI=ai, IsYourPlayer=!ai };
        var health=new ActiveHealthController { Player=player }; player.ActiveHealthController=health;
        if(ai) player.AIData=new AIData { BotOwner=new BotOwner { GetPlayer=player } };
        var world=new GameWorld { MainPlayer=player }; world.Alive[player.ProfileId]=player;
        var game=new LocalGame { LocalPlayer=player };
        Singleton<GameWorld>.Instance=world; Singleton<AbstractGame>.Instance=game;
        ArenaSession.Game=game; ArenaSession.World=world; ArenaSession.IsActive=true;
        ArenaMatchService.Current=new Match(); ArenaRespawnDriver.StandDown=false; ArenaRespawnDriver.IsRespawnInProgress=false;
        Settings.BleedingDamageMultiplier=new(3); Protected=false; ArenaMode.Bots.TeamDeathmatchRelations.Block=false; ArenaHitHistory.Damage.Clear();
        return health;
    }
    private static float Adjust(ActiveHealthController health,float damage=2,EDamageType type=EDamageType.LightBleeding)
        => ArenaBleedingDamage.Adjust(health,damage,type);
    private static void Main()
    {
        foreach(bool ai in new[]{false,true}) foreach(var type in new[]{EDamageType.LightBleeding,EDamageType.HeavyBleeding})
        {
            var health=Setup(ai);
            for(int tick=0;tick<3;tick++) for(int limb=0;limb<7;limb++) Check(Adjust(health,2,type)==6,"sequential native tick scales exactly once");
            float damage=2, result=-1; var info=new DamageInfo { DamageType=type };
            Check(Prefix(health,ref damage,info,ref result) && damage==6 && result==-1,"actual prefix forwards adjusted transaction to native");
            Check(info.DamageType==type && ArenaHitHistory.Damage.SequenceEqual(new[]{6f}),"damage descriptor unchanged and hit history sees final transaction");
        }
        foreach(var type in new[]{EDamageType.Bullet,EDamageType.Fall,EDamageType.Poison,EDamageType.Dehydration}) Check(Adjust(Setup(),2,type)==2,"other damage untouched");
        foreach(float amount in new[]{0,-1,float.NaN,float.PositiveInfinity,float.MaxValue})
        { var actual=Adjust(Setup(),amount); Check(actual.Equals(amount),"invalid or overflowing damage retains native value"); }
        foreach(float factor in new[]{0,-1,1,float.NaN,float.PositiveInfinity})
        { var h=Setup(); Settings.BleedingDamageMultiplier.Value=factor; Check(Adjust(h)==2,"disabled or invalid factor fails open"); }
        { var h=Setup(); Settings.BleedingDamageMultiplier.Value=100; Check(Adjust(h)==20,"factor bounded to ten"); }
        var exclusions=new Action<ActiveHealthController>[] {
            h=>ArenaSession.IsActive=false, h=>ArenaSession.Game=new object(),h=>ArenaSession.World=new object(),
            h=>Singleton<AbstractGame>.Instance.Status=GameStatus.Stopping,h=>ArenaMatchService.Current=null,
            h=>ArenaMatchService.Current.Mode.IsComplete=true,h=>ArenaRespawnDriver.StandDown=true,
            h=>h.IsAlive=false,h=>h.DamageCoeff=0,h=>h.Player.ActiveHealthController=new(),
            h=>Singleton<GameWorld>.Instance.Alive[h.Player.ProfileId]=new Player(),h=>h.Player.ProfileId="",
            h=>h.Player.IsYourPlayer=false,h=>Singleton<GameWorld>.Instance.MainPlayer=new Player(),
            h=>((LocalGame)Singleton<AbstractGame>.Instance).LocalPlayer=new Player(),h=>ArenaRespawnDriver.IsRespawnInProgress=true
        };
        foreach(var exclude in exclusions) { var h=Setup(); exclude(h); Check(Adjust(h)==2,"ordinary stale protected local bodies remain unchanged"); }
        foreach(var mutate in new Action<ActiveHealthController>[] { h=>h.Player.AIData=null,h=>h.Player.AIData.BotOwner.IsDead=true,h=>h.Player.AIData.BotOwner.BotState=EBotState.NonActive,h=>h.Player.AIData.BotOwner.GetPlayer=new() })
        { var h=Setup(true); mutate(h); Check(Adjust(h)==2,"retired or foreign AI body untouched"); }
        { var h=Setup(true); ArenaRespawnDriver.IsRespawnInProgress=true; Check(Adjust(h)==6,"bots bleed during local respawn"); }
        foreach(bool friendly in new[]{false,true})
        {
            var h=Setup(); Protected=!friendly; ArenaMode.Bots.TeamDeathmatchRelations.Block=friendly;
            float damage=2,result=-1;
            Check(!Prefix(h,ref damage,new DamageInfo { DamageType=EDamageType.HeavyBleeding },ref result)
                && damage==2 && result==0 && ArenaHitHistory.Damage.Count==0,"existing protection veto executes before bleed scaling");
        }
        Console.WriteLine($"ARENA_BLEEDING_BASELINE_PASSED checks={checks}");
    }
}
