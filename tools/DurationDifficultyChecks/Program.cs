using System.Reflection;
using ArenaMode.Bots;
using ArenaMode.Config;
using ArenaMode.Modes;
using ArenaMode.Session;
using ArenaMode.Shared;
using Comfort.Common;
using EFT;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static class Program
{
    private static int checks;
    private static readonly Type Patch=typeof(ArenaBotDifficultyClockPatch);
    private static MethodInfo Method(Type type,string method) => type.GetMethod(method,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static)!;
    private static Type Nested(string name)=>Patch.GetNestedType(name,BindingFlags.NonPublic)!;
    private static void Check(bool value,string label) { checks++; if(!value) throw new Exception("FAIL: "+label); }
    private static (BotProfileClient Provider,BotCreationData Data,TeamDeathmatchMode Mode) Setup()
    {
        var game=new LocalGame(); var world=new GameWorld(); var provider=new BotProfileClient();
        provider._backupLoader=new() { _profileCreator=provider };
        var creator=new BotCreatorClient { _game=game,_creatorProfile=provider }; game.BotsController.BotSpawner._botCreator=creator;
        ArenaSession.IsActive=true; ArenaSession.Game=game; ArenaSession.World=world;
        Singleton<AbstractGame>.Instance=game; Singleton<GameWorld>.Instance=world;
        var mode=new TeamDeathmatchMode(); ArenaMatchService.Current=new Match { Mode=mode,Elapsed=20000 };
        ArenaMode.Compat.PitFireTeamCompat.HasFrozenTeamRoster=false; ArenaMode.Respawn.ArenaRespawnDriver.StandDown=false;
        ArenaServerClient.ActiveArmToken="arm-current"; Settings.ScaleDifficulty.Value=true; Settings.MaxBotDifficulty.Value=BotDifficulty.impossible;
        return (provider,new BotCreationData { _botCreator=creator },mode);
    }
    private static object Begin(BotProfileClient provider,BotCreationData data,bool withDelete=true,CancellationToken token=default)
    { object[] args={provider,data,token,withDelete,null}; Method(Nested("DemandPatch"),"Prefix").Invoke(null,args); return args[4]; }
    private static void End(object state)=>Method(Nested("DemandPatch"),"Postfix").Invoke(null,new[]{state});
    private static List<CountTypeBotWave> Decorate(BotProfileClient provider,List<CountTypeBotWave> waves)
    { object[] args={provider._backEndSession,waves}; Method(Patch,"Prefix").Invoke(null,args); return (List<CountTypeBotWave>)args[1]; }
    private static List<CountTypeBotWave> Waves()=>new(){new(3,WildSpawnType.pmcUSEC,BotDifficulty.hard)};
    private static bool Backup(BotProfileClient p,BotCreationData data)=>(bool)Method(Nested("BackupPatch"),"Prefix").Invoke(null,new object[]{p._backupLoader,data})!;
    private sealed class ForeignWave():CountTypeBotWave(3,WildSpawnType.pmcUSEC,BotDifficulty.hard) { public int External=7; }
    private static void Main()
    {
        ArenaBotDifficultyClockPatch.EnablePatch();
        foreach(int duration in new[]{60,300,1200,7200})
        {
            var s=Setup(); s.Mode.Duration=duration;
            foreach((double portion,double pressure) in new[]{(0d,0d),(.2d,.25d),(.4d,.5d),(.8d,1d),(.99d,1d)})
            { s.Mode.Elapsed=(float)(portion*duration); Check(Math.Abs(ArenaPressure.Level-pressure)<.00001,"difficulty follows frozen match proportions"); }
            s.Mode.Elapsed=(float)(.64*duration); Check(ArenaPressure.DifficultyFor(true)==BotDifficulty.impossible,"PMC peak occurs at 64 percent without streak");
            Settings.MaxBotDifficulty.Value=BotDifficulty.hard; Check(ArenaPressure.DifficultyFor(true)==BotDifficulty.hard,"difficulty cap retained");
        }
        {
            var s=Setup(); s.Mode.Duration=0; s.Mode.Elapsed=360;
            Check(ArenaPressure.Level==.5f,"unlimited keeps twelve minute pressure ramp");
            ArenaMatchService.Current.Mode=new Mode(); ArenaMatchService.Current.Elapsed=360;
            Check(ArenaPressure.Level==.5f,"other modes retain legacy clock");
            Check(ArenaDifficultyClock.Progress(450,0,900)==.5,"unlimited tier retains fifteen minute ramp");
            s=Setup(); ArenaMatchService.Current.Streak=8;
            Check(ArenaPressure.Level==.35f,"streak surge remains additive");
            Settings.ScaleDifficulty.Value=false; Check(ArenaPressure.Level==0,"pressure off unchanged");
        }
        {
            var s=Setup(); s.Mode.Elapsed=120.25f;
            var old=new Profile(); old.Info.Settings.Role=WildSpawnType.pmcUSEC; old.Info.Settings.BotDifficulty=BotDifficulty.hard;
            var scav=new Profile(); var otherDifficulty=new Profile(); otherDifficulty.Info.Settings.Role=WildSpawnType.pmcUSEC;
            s.Provider._simpleProfiles.AddRange(new[]{old,scav,otherDifficulty});
            var state=Begin(s.Provider,s.Data); var original=Waves(); var decorated=Decorate(s.Provider,original);
            Check(!ReferenceEquals(decorated,original) && decorated[0].Limit==1 && original[0].Limit==3,"owned demand requests one fresh profile without mutating native DTO");
            Check(decorated[0].Role==original[0].Role && decorated[0].Difficulty==original[0].Difficulty,"native role and difficulty preserved");
            Check(s.Provider._simpleProfiles.SequenceEqual(new[]{scav,otherDifficulty}),"only unused matching cache references removed");
            var json=JObject.Parse(JsonConvert.SerializeObject(new {conditions=decorated}));
            Check((double)json["conditions"][0][ArenaDifficultyClock.WireKey]["PlayingElapsedSeconds"]==120.25,"clock survives base-typed wire serialization");
            Check(!Backup(s.Provider,s.Data),"finite PMC backup suppressed");
            var foreign=new List<CountTypeBotWave>{new ForeignWave()};
            Check(ReferenceEquals(Decorate(s.Provider,foreign),foreign),"foreign DTO retained");
            var oldProvider=new BotProfileClient { _backEndSession=s.Provider._backEndSession };
            var nested=Begin(oldProvider,s.Data); var waves=Waves();
            Check(ReferenceEquals(Decorate(s.Provider,waves),waves),"stale nested provider cannot borrow current scope"); End(nested);
            Check(!ReferenceEquals(Decorate(s.Provider,waves),waves),"outer scope restored after nested call");
            var exception=new InvalidOperationException("native");
            Check(ReferenceEquals(Method(Nested("DemandPatch"),"Finalizer").Invoke(null,new[]{exception,state}),exception),"native exception preserved");
            End(state); Check(ReferenceEquals(Decorate(s.Provider,waves),waves),"scope cleared before await continuation");
            // Installed old profiles are not disposed or altered by removal.
            Check(old.Info.Settings.Role==WildSpawnType.pmcUSEC,"discarding unused cache does not mutate profile");
        }
        foreach(int cause in Enumerable.Range(0,11))
        {
            var s=Setup(); var waves=Waves();
            if(cause==0) ArenaSession.IsActive=false;
            if(cause==1) ArenaSession.Game=new();
            if(cause==2) ArenaSession.World=new();
            if(cause==3) s.Mode.Current=false;
            if(cause==4) s.Mode.IsComplete=true;
            if(cause==5) s.Mode.Duration=0;
            if(cause==6) ArenaMode.Compat.PitFireTeamCompat.HasFrozenTeamRoster=true;
            if(cause==7) ArenaMode.Respawn.ArenaRespawnDriver.StandDown=true;
            if(cause==8) s.Data._botCreator=new();
            if(cause==9) Singleton<AbstractGame>.Instance.Status=GameStatus.Stopping;
            if(cause==10) s.Data._profileData.Role=WildSpawnType.assault;
            var state=Begin(s.Provider,s.Data);
            Check(ReferenceEquals(Decorate(s.Provider,waves),waves),"foreign stale ordinary unlimited PFT demand unchanged");
            Check(Backup(s.Provider,s.Data),"ineligible backup unchanged"); End(state);
        }
        {
            var s=Setup(); var state=Begin(s.Provider,s.Data); var waves=Waves(); ArenaServerClient.ActiveArmToken="new-arm";
            Check(ReferenceEquals(Decorate(s.Provider,waves),waves),"old arm cannot be adopted by new match"); End(state);
            foreach(int limit in new[]{0,-1}) { s=Setup(); state=Begin(s.Provider,s.Data); waves=Waves(); waves[0].Limit=limit;
                Check(ReferenceEquals(Decorate(s.Provider,waves),waves),"invalid native batch retained"); End(state); }
        }
        {
            var s=Setup(); var completion=new TaskCompletionSource<bool>();
            async Task NativeDemandShape()
            {
                var waves=Waves();
                Check(!ReferenceEquals(Decorate(s.Provider,waves),waves),"native synchronous request receives clock before first await");
                await completion.Task;
                Check(ReferenceEquals(Decorate(s.Provider,waves),waves),"async continuation cannot reuse synchronous request scope");
            }
            var state=Begin(s.Provider,s.Data);
            var task=NativeDemandShape();
            End(state); // Harmony postfix sees the returned Task, not its completion.
            Check(!task.IsCompleted,"native demand actually suspended");
            completion.SetResult(true); task.GetAwaiter().GetResult();
        }
        {
            var s=Setup(); var state=Begin(s.Provider,s.Data); var waves=Waves();
            ArenaMatchService.Current=new Match { Mode=new TeamDeathmatchMode() };
            Check(ReferenceEquals(Decorate(s.Provider,waves),waves),"replacement match cannot reuse provider scope under same arm"); End(state);
        }
        foreach(int failAt in new[]{2,3}) foreach(bool rollbackFails in new[]{false,true})
        {
            var s=Setup(); var cached=new Profile(); cached.Info.Settings.Role=WildSpawnType.pmcUSEC;
            cached.Info.Settings.BotDifficulty=BotDifficulty.hard; s.Provider._simpleProfiles.Add(cached);
            SPT.Reflection.Patching.ModulePatch.EnableCalls=0; SPT.Reflection.Patching.ModulePatch.FailEnableAt=failAt;
            SPT.Reflection.Patching.ModulePatch.FailDisable=rollbackFails;
            ArenaBotDifficultyClockPatch.EnablePatch(); var state=Begin(s.Provider,s.Data); var waves=Waves();
            Check(s.Provider._simpleProfiles.Contains(cached) && Backup(s.Provider,s.Data)
                && ReferenceEquals(Decorate(s.Provider,waves),waves),"partial hook installation cannot mutate native generation"); End(state);
        }
        SPT.Reflection.Patching.ModulePatch.EnableCalls=0; SPT.Reflection.Patching.ModulePatch.FailEnableAt=0;
        SPT.Reflection.Patching.ModulePatch.FailDisable=false; ArenaBotDifficultyClockPatch.EnablePatch();
        Console.WriteLine($"ARENA_DURATION_BASELINE_PASSED checks={checks}");
    }
}
