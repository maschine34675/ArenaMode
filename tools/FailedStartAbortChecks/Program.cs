using ArenaMode;
using ArenaMode.Bots;
using ArenaMode.Compat;
using ArenaMode.Modes;
using ArenaMode.Respawn;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using UnityEngine;

static class Program
{
    static int checks;
    static LocalGame game;
    static GameWorld world;
    static void Check(bool result,string label) { checks++;if(!result)throw new Exception(label); }
    static void Fresh()
    {
        ArenaMatchService.ResetFailedStartAbort(); Plugin.ModLogger=new();
        world=new();game=new(){GameWorld=world};
        Singleton<AbstractGame>.Instance=game;Singleton<GameWorld>.Instance=world;
        ArenaSession.StartedGame=game;ArenaSession.StartedWorld=world;
        ArenaSession.IsActive=ArenaSession.LastRaidWasArena=true;
        ArenaRespawnDriver.StandDown=false;PitFireTeamCompat.FinalizeCalls=BurstSpawnService.Resets=0;
        Time.frameCount=100;Time.unscaledTime=10;
    }
    static void Next(float seconds=0.1f) { Time.frameCount++;Time.unscaledTime+=seconds;ArenaMatchService.TickFailedStartAbort(); }
    static void Schedule()=>ArenaMatchService.ScheduleFailedStartAbort("area preparation failed");
    static void Excluded(Action change,string why)
    {
        Fresh();change();Schedule();Next();
        Check(!ArenaMatchService.TeamStartAbortPending,"ORDINARY/LIFECYCLE "+why);
        Check(game.StopCalls==0&&BurstSpawnService.Resets==0&&PitFireTeamCompat.FinalizeCalls==0,"NO-SIDE-EFFECTS "+why);
    }
    static void Main()
    {
        Fresh();Schedule();
        Check(ArenaMatchService.TeamStartAbortPending&&ArenaRespawnDriver.StandDown,"IMMEDIATE-PROTECTION");
        Check(game.StopCalls==0,"NO-SYNCHRONOUS-STOP");
        ArenaMatchService.TickFailedStartAbort();Check(game.StopCalls==0,"SAME-FRAME-DEFER");
        Check(BurstSpawnService.Resets==1&&PitFireTeamCompat.FinalizeCalls>0,"DETACHED-REQUESTS-INVALIDATED");
        Next();Check(game.StopCalls==1&&game.Status==GameStatus.Stopping,"NEXT-FRAME-STOP");
        Check(game.StopId==game.ProfileId&&game.StopId!=world.MainPlayer.ProfileId,"IMMUTABLE-PROFILE");
        Check(game.Exit==ExitStatus.MissingInAction,"LIVING-EXIT");
        Check(ArenaMatchService.TeamStartAbortPending,"COMMITTED-LATCH");
        game.Status=GameStatus.Started;Next(10);Check(game.StopCalls==1,"NEVER-RETRY-COMMITTED");
        Check(Plugin.ModLogger.Info.Count==1,"ACCEPT-ONCE");
        ArenaMatchService.ResetFailedStartAbort();Check(!ArenaMatchService.TeamStartAbortPending,"END-RESET");

        Fresh();Schedule();Time.frameCount++;Schedule();ArenaMatchService.TickFailedStartAbort();
        Check(game.StopCalls==1&&BurstSpawnService.Resets==1,"DUPLICATE-SCHEDULE-PRESERVES-FRAME");
        Fresh();game.OnStop=()=>{};Schedule();Next();
        Check(game.StopCalls==1&&ArenaMatchService.TeamStartAbortPending&&Plugin.ModLogger.Info.Count==0,"NO-OP-NOT-COMMIT");
        Next(0.1f);Check(game.StopCalls==1,"RETRY-PACED");
        Next(1);Check(game.StopCalls==2,"NO-OP-RETRIES");
        Check(Plugin.ModLogger.Warnings.Count==1,"RETRY-LOG-BOUNDED");
        game.OnStop=()=>game.Status=GameStatus.Stopped;Next(1);
        Check(game.StopCalls==3&&Plugin.ModLogger.Info.Count==1,"LATER-STOP-COMMIT");

        Fresh();game.OnStop=()=>throw new InvalidOperationException("pre-commit");Schedule();Next();
        Check(game.StopCalls==1&&Plugin.ModLogger.Info.Count==0&&ArenaMatchService.TeamStartAbortPending,"THROW-BEFORE-COMMIT");
        Next(1);Check(game.StopCalls==2,"THROW-RETRY");
        Fresh();game.OnStop=()=>{game.Status=GameStatus.Stopping;throw new Exception("post-commit");};Schedule();Next();Next(10);
        Check(game.StopCalls==1&&Plugin.ModLogger.Info.Count==1&&ArenaMatchService.TeamStartAbortPending,"THROW-AFTER-COMMIT-NO-RETRY");
        Fresh();game.OnStop=()=>{game.Status=GameStatus.SoftStopping;throw new Exception("not native teardown");};Schedule();Next();Next(10);
        Check(game.StopCalls==1&&Plugin.ModLogger.Info.Count==0,"NONSTARTED-NONTEARDOWN-NOT-COMMIT");
        game.Status=GameStatus.Started;game.OnStop=null;Next();Check(game.StopCalls==2,"RETRY-ONLY-STARTED");

        foreach(var status in new[]{GameStatus.Created,GameStatus.Running,GameStatus.Starting,GameStatus.SoftStopping})
        {
            Fresh();game.Status=status;Schedule();Next();
            Check(game.StopCalls==0&&Plugin.ModLogger.Info.Count==0&&ArenaMatchService.TeamStartAbortPending,"WAIT-STARTED "+status);
            game.Status=GameStatus.Started;Next();Check(game.StopCalls==1,"LATE-START "+status);
        }
        foreach(var status in new[]{GameStatus.Stopping,GameStatus.Stopped})
        {
            Fresh();game.Status=status;Schedule();Next();Next();
            Check(game.StopCalls==0&&Plugin.ModLogger.Info.Count==1&&ArenaMatchService.TeamStartAbortPending,"OBSERVED-TEARDOWN "+status);
        }

        Fresh();world.MainPlayer=null;game.LocalPlayer.HealthController.IsAlive=false;Schedule();Next();
        Check(game.Exit==ExitStatus.Killed&&game.StopId==game.ProfileId,"NO-MAIN-PLAYER");
        Fresh();game.ProfileId=null;Schedule();Next();Check(game.StopCalls==0&&ArenaMatchService.TeamStartAbortPending,"MISSING-ID-HOLD");
        game.ProfileId="late-profile";Next();Check(game.StopId=="late-profile","PUBLISH-CANONICAL-ID");
        Fresh();Schedule();game.ProfileId="different-profile";Next();Check(game.StopCalls==0&&ArenaMatchService.TeamStartAbortPending,"PROFILE-DRIFT-NO-STOP");
        Fresh();game.ProfileId=" canonical ";Schedule();Next();Check(game.StopId==" canonical ","NATIVE-ID-NO-REWRITE");

        Excluded(()=>{ArenaSession.IsActive=ArenaSession.LastRaidWasArena=false;},"ordinary");
        Excluded(()=>Singleton<AbstractGame>.Instance=null,"missing game");
        Excluded(()=>Singleton<AbstractGame>.Instance=new AbstractGame(),"not LocalGame");
        Excluded(()=>Singleton<GameWorld>.Instance=null,"missing world");
        Excluded(()=>ArenaSession.StartedGame=new LocalGame(),"wrong started game");
        Excluded(()=>ArenaSession.StartedWorld=new GameWorld(),"wrong started world");
        Excluded(()=>game.GameWorld=new GameWorld(),"wrong owned world");
        Fresh();ArenaSession.IsActive=false;Schedule();Next();Check(game.StopCalls==1,"EXACT-REJECTED-SERVER-START");
        foreach(var stale in new Action[]{()=>Singleton<AbstractGame>.Instance=new LocalGame(),()=>Singleton<GameWorld>.Instance=new GameWorld(),()=>game.GameWorld=new GameWorld(),()=>ArenaSession.StartedGame=new LocalGame()})
        {
            Fresh();Schedule();stale();ArenaRespawnDriver.StandDown=false;int before=PitFireTeamCompat.FinalizeCalls;Next();
            Check(game.StopCalls==0&&!ArenaMatchService.TeamStartAbortPending,"STALE-ABORT-DISCARDED");
            Check(!ArenaRespawnDriver.StandDown&&PitFireTeamCompat.FinalizeCalls==before,"NO-FOREIGN-GUARD-REFRESH");
        }
        Fresh();game.OnStop=()=>ArenaMatchService.ResetFailedStartAbort();Schedule();Next();
        Check(!ArenaMatchService.TeamStartAbortPending&&Plugin.ModLogger.Info.Count==0,"SYNCHRONOUS-END-NO-RELATCH");

        // Real integration seams, checked separately from the linked state machine.
        var root=Directory.GetCurrentDirectory();while(!File.Exists(Path.Combine(root,"AGENTS.md")))root=Directory.GetParent(root)?.FullName??throw new Exception("repo");
        var burst=File.ReadAllText(Path.Combine(root,"ArenaMode.Client/Bots/BurstSpawnService.cs"));
        var tick=burst[burst.IndexOf("public static void Tick()")..];
        Check(tick.IndexOf("TeamStartAbortPending")<tick.IndexOf("ArenaSoloPopulation.IsSelected"),"BURST-BEFORE-SOLO-DISPATCH");
        Check(burst.Contains("|| Modes.ArenaMatchService.TeamStartAbortPending")&&burst.Contains("&& !Modes.ArenaMatchService.TeamStartAbortPending"),"ASYNC-LEASES-REJECT-ABORT");
        var controller=File.ReadAllText(Path.Combine(root,"ArenaMode.Client/ArenaController.cs"));
        Check(controller.Contains("|| Modes.ArenaMatchService.TeamStartAbortPending"),"OPTIONAL-WARMUP-BLOCKED");
        var match=File.ReadAllText(Path.Combine(root,"ArenaMode.Client/Modes/ArenaMatchService.cs"));
        Check(match.Contains("TickFailedStartAbort();")&&match.Contains("ResetFailedStartAbort();"),"MATCH-TICK-END-WIRED");
        Console.WriteLine($"FAILED_START_ABORT_CHECKS_PASSED: {checks} assertions; linked production schedule/tick, native boundaries doubled, not GPU proof.");
    }
}
