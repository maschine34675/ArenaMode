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
        ArenaServerClient.ActiveArmToken="arm-current";
        return (provider,new BotCreationData { _botCreator=creator },mode);
    }
    private static object Begin(BotProfileClient provider,BotCreationData data,bool withDelete=true,CancellationToken token=default)
    { object[] args={provider,data,token,withDelete,null}; Method(Nested("ArenaBotDifficultyClockDemandPatch"),"Prefix").Invoke(null,args); return args[4]; }
    private static void End(object state)=>Method(Nested("ArenaBotDifficultyClockDemandPatch"),"Postfix").Invoke(null,new[]{state});
    private static List<CountTypeBotWave> Decorate(BotProfileClient provider,List<CountTypeBotWave> waves)
    { object[] args={provider._backEndSession,waves}; Method(Patch,"Prefix").Invoke(null,args); return (List<CountTypeBotWave>)args[1]; }
    private static List<CountTypeBotWave> Waves()=>new(){new(3,WildSpawnType.pmcUSEC,BotDifficulty.hard)};
    private static bool Backup(BotProfileClient p,BotCreationData data)=>(bool)Method(Nested("ArenaBotDifficultyClockBackupPatch"),"Prefix").Invoke(null,new object[]{p._backupLoader,data})!;
    private sealed class ForeignWave():CountTypeBotWave(3,WildSpawnType.pmcUSEC,BotDifficulty.hard) { public int External=7; }
    // --- the client reader of the shared policy (ArenaDifficulty) -------------------------------------
    private static readonly string[] FixedModes={"deathmatch","teamdeathmatch","domination","tactical","zombie"};
    private static void Arm(string mode,ArenaDifficultyStep step,float elapsed,int streak)
    {
        ArenaSession.IsActive=true; ArenaSession.ActiveMode=mode; ArenaSession.Difficulty=step;
        ArenaMatchService.Current=new Match { Mode=new Mode(),Elapsed=elapsed,Streak=streak };
        ArenaClientConfig.RampMinutes=15; Settings.DifficultyStep.Value=step;
    }
    private static void Difficulty()
    {
        // The expected values are written out here, not read from the policy: a policy edit must show up.
        var table=new Dictionary<ArenaDifficultyStep,(BotDifficulty Pmc,BotDifficulty Scav,int Bonus,float Cooldown)>
        {
            [ArenaDifficultyStep.Maximum]=(BotDifficulty.impossible,BotDifficulty.hard,4,4f),
            [ArenaDifficultyStep.Reduced]=(BotDifficulty.hard,BotDifficulty.normal,2,6f),
        };
        foreach(var mode in FixedModes) foreach(var (step,row) in table)
            foreach((float elapsed,int streak) in new[]{(0f,0),(0f,8),(1f,0),(900f,0),(900f,8),(7200f,8)})
            {
                Arm(mode,step,elapsed,streak);
                Check(ArenaDifficulty.DifficultyFor(true)==row.Pmc,$"fixed mode ignores elapsed time and streak: PMC level {mode} {step} {elapsed}/{streak}");
                Check(ArenaDifficulty.DifficultyFor(false)==row.Scav,$"fixed mode ignores elapsed time and streak: scav level {mode} {step} {elapsed}/{streak}");
                Check(ArenaDifficulty.PmcFloor(8)==8+row.Bonus,$"fixed mode ignores elapsed time and streak: PMC floor {mode} {step} {elapsed}/{streak}");
                Check(ArenaDifficulty.PmcFloor(0)==0,"PmcFloor zero stays off at every step");
                Check(ArenaDifficulty.BurstCooldown(8f)==row.Cooldown,$"refill cooldown factor follows the step {mode} {step}");
                Check(ArenaDifficulty.Progress==1f,"progress is one outside Gun Game");
                Check(ArenaDifficulty.Describe()==step.ToString(),"panel line names only the frozen step outside Gun Game");
            }
        // No match at all (loading, between rounds) is still the fixed row, never a throw.
        Arm("tactical",ArenaDifficultyStep.Maximum,0,0); ArenaMatchService.Current=null;
        Check(ArenaDifficulty.DifficultyFor(true)==BotDifficulty.impossible,"fixed mode needs no match");
        // The frozen step wins over the live setting, both ways round, and the live one answers outside a raid.
        Arm("deathmatch",ArenaDifficultyStep.Reduced,0,0); Settings.DifficultyStep.Value=ArenaDifficultyStep.Maximum;
        Check(ArenaDifficulty.DifficultyFor(true)==BotDifficulty.hard && ArenaDifficulty.PmcFloor(8)==10,"frozen step wins over a live setting change (reduced frozen)");
        Arm("deathmatch",ArenaDifficultyStep.Maximum,0,0); Settings.DifficultyStep.Value=ArenaDifficultyStep.Reduced;
        Check(ArenaDifficulty.DifficultyFor(true)==BotDifficulty.impossible && ArenaDifficulty.DifficultyFor(false)==BotDifficulty.hard,"frozen step wins over a live setting change (maximum frozen)");
        ArenaSession.Difficulty=null;
        Check(ArenaDifficulty.Step==ArenaDifficultyStep.Reduced,"without a frozen step the live setting answers");
        // Gun Game: the curve over the map's ramp length, one clock for level, bonus and the panel line.
        foreach(var (step,row) in table)
        {
            foreach((double portion,BotDifficulty pmc,BotDifficulty scav,int bonus) in new[]
            {
                (0d,BotDifficulty.hard,BotDifficulty.normal,0),
                (.2d,BotDifficulty.hard,BotDifficulty.normal,row.Bonus==4?1:0),
                (.49d,BotDifficulty.hard,BotDifficulty.normal,row.Bonus==4?2:1),
                (.5d,BotDifficulty.hard,row.Scav,row.Bonus==4?2:1),
                (.79d,BotDifficulty.hard,row.Scav,row.Bonus==4?3:2),
                (.8d,row.Pmc,row.Scav,row.Bonus==4?3:2),
                (1d,row.Pmc,row.Scav,row.Bonus),
                (3d,row.Pmc,row.Scav,row.Bonus),
            })
            {
                Arm("gungame",step,(float)(portion*900),0);
                Check(ArenaDifficulty.DifficultyFor(true)==pmc,$"Gun Game PMC level follows the curve {step} {portion}");
                Check(ArenaDifficulty.DifficultyFor(false)==scav,$"Gun Game scav level follows the curve {step} {portion}");
                Check(ArenaDifficulty.PmcFloor(8)==8+bonus,$"Gun Game PMC bonus follows the curve {step} {portion}");
                Check(ArenaDifficulty.PmcFloor(0)==0,"Gun Game keeps PmcFloor zero off");
            }
            Arm("gungame",step,0,0);
            Check(ArenaDifficulty.DifficultyFor(true)==BotDifficulty.hard && ArenaDifficulty.PmcFloor(8)==8,$"Gun Game opens with today's opening {step}");
            Arm("gungame",step,450,0);
            Check(Math.Abs(ArenaDifficulty.Progress-.5f)<.00001f && ArenaDifficulty.Describe()==$"{step}, build-up 50%","Gun Game panel line shows the build-up");
            // A kill streak or a finished ladder changes nothing: progress stays where the clock is.
            Arm("gungame",step,100,8);
            Check(ArenaDifficulty.DifficultyFor(true)==BotDifficulty.hard && ArenaDifficulty.PmcFloor(8)==8 && Math.Abs(ArenaDifficulty.Progress-100f/900f)<.00001f,
                $"kill streak never feeds the Gun Game curve {step}");
        }
        // The ramp length is the map's value; an absent one falls back to fifteen minutes.
        Arm("gungame",ArenaDifficultyStep.Maximum,300,0); ArenaClientConfig.RampMinutes=10;
        Check(ArenaDifficulty.DifficultyFor(false)==BotDifficulty.hard && Math.Abs(ArenaDifficulty.Progress-.5f)<.00001f,"Gun Game uses the map's ramp length");
        ArenaClientConfig.RampMinutes=0; Check(Math.Abs(ArenaDifficulty.Progress-300f/900f)<.00001f,"an absent ramp length falls back to fifteen minutes");
        ArenaClientConfig.RampMinutes=15;
        ArenaMatchService.Current=null; Check(ArenaDifficulty.Progress==0f && ArenaDifficulty.DifficultyFor(true)==BotDifficulty.hard,"Gun Game without a match is the opening");
        ArenaSession.ActiveMode=null; ArenaSession.Difficulty=null; ArenaMatchService.Current=null; Settings.DifficultyStep.Value=ArenaDifficultyStep.Maximum;
    }
    // --- wiring that cannot be linked (Unity/EFT-heavy) is pinned as text; mutants override the files -------
    private static string Text(string key)
    {
        var path=Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().Single(a=>a.Key==key).Value!;
        return System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(path),@"\s+"," ");
    }
    private static void WiringPins()
    {
        Check(ArenaDifficultyPolicy.SweepInterval(15,false,ArenaDifficultyStep.Maximum)==15 && ArenaDifficultyPolicy.SweepInterval(15,false,ArenaDifficultyStep.Reduced)==22.5
            && ArenaDifficultyPolicy.SweepInterval(15,true,ArenaDifficultyStep.Reduced)==11.25 && ArenaDifficultyPolicy.SweepInterval(3,true,ArenaDifficultyStep.Reduced)==3,
            "sweep interval takes the step factor last");
        Check(ArenaDifficultyPolicy.BotSpeedFactor(99,ArenaDifficultyStep.Reduced)==1.25 && ArenaDifficultyPolicy.BotSpeedFactor(99,ArenaDifficultyStep.Maximum)==1.5,
            "bot speed caps the setting before scaling the surplus");
        var session=Text("SessionText");
        Check(session.Contains("ClearParticipants(); Participants = participants; ParticipantBosses = participantBosses; Difficulty = difficulty;"),
            "MarkArmed freezes the acknowledged difficulty step after clearing the old raid");
        Check(session.Contains("ParticipantBosses = false; Difficulty = null;"),"ClearParticipants resets the frozen difficulty step");
        var config=Text("ClientConfigText");
        Check(config.Contains("\"botTierRampMinutes\"") && Text("ServerConfigText").Contains("JsonPropertyName(\"botTierRampMinutes\")"),
            "client and server read the same botTierRampMinutes key");
        Check(config.Contains("value.HasValue ? Math.Max(1, value.Value) : fallback"),
            "a present non-positive ramp length clamps to one minute like the server");
        var hostility=Text("HostilityText");
        Check(hostility.Contains("interval = (float)ArenaDifficultyPolicy.SweepInterval( interval, Modes.ArenaMatchService.Current?.Mode is Modes.TacticalMode, ArenaDifficulty.Step);"),
            "the HuntPlayer sweep interval comes from the policy with the frozen step");
        Check(Text("MovementText").Contains("requested = (float)ArenaDifficultyPolicy.BotSpeedFactor(requested, ArenaDifficulty.Step);"),
            "bot speed comes from the policy with the frozen step");
    }
    private static void Main()
    {
        ArenaBotDifficultyClockPatch.EnablePatch();
        Difficulty();
        WiringPins();
        // The wire clock helper stays until the clock channel is removed with its patch.
        Check(ArenaDifficultyClock.Progress(450,0,900)==.5,"unlimited tier retains fifteen minute ramp");
        Check(ArenaDifficultyClock.Progress(24,60,0)==.5 && ArenaDifficultyClock.Progress(48,60,0)==1,"finite wire clock peaks at eighty percent");
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
            Check(ReferenceEquals(Method(Nested("ArenaBotDifficultyClockDemandPatch"),"Finalizer").Invoke(null,new[]{exception,state}),exception),"native exception preserved");
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
