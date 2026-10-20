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
        ArenaMatchService.ResetFailedStartAbort(); TdmMatchExit.Reset(); ArenaMatchService.Current=null; Plugin.ModLogger=new();
        world=new();game=new(){GameWorld=world};
        world.MainPlayer=game.LocalPlayer;
        Redeploy.Core.GameAccess.LocalGameAccessor.Owner=new(){Player=game.LocalPlayer};
        Redeploy.Core.Engine.RespawnEngineState.IsPipelineRunning=false;
        Time.timeScale=1;
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
        Check(game.StopCalls==0,"NO-MAIN-PLAYER-WAIT");
        world.MainPlayer=game.LocalPlayer;Next();
        Check(game.Exit==ExitStatus.Killed&&game.StopId==game.ProfileId,"DEAD-REBOUND-OWNER");
        foreach(var blocked in new Action[]{()=>Time.timeScale=0,
            ()=>Redeploy.Core.Engine.RespawnEngineState.IsPipelineRunning=true,
            ()=>Redeploy.Core.GameAccess.LocalGameAccessor.Owner=null,
            ()=>Redeploy.Core.GameAccess.LocalGameAccessor.Owner.Player=new Player(),
            ()=>world.MainPlayer=new Player()})
        {
            Fresh();blocked();Schedule();Next();Check(game.StopCalls==0,"NATIVE-OWNER-READINESS");
        }
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

        IntroChecks();

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
        Check(File.ReadAllText(Path.Combine(root,"ArenaMode.Client/Plugin.cs")).Contains("ArenaFailedStartIntro.Enable();"),"INTRO-PATCH-WIRED");
        Check(match.Contains("private static void OnRaidEnded()\r\n    {\r\n        TdmMatchExit.Reset();")
            || match.Contains("private static void OnRaidEnded()\n    {\n        TdmMatchExit.Reset();"),"TERMINAL-END-RESET");
        var recover=match[match.IndexOf("internal static void RecoverAtNewRaidStart()")..];
        Check(recover.IndexOf("TdmMatchExit.Reset();")<recover.IndexOf("if (Current == null"),"TERMINAL-RECOVERY-RESET");
        TerminalIntroChecks();
        Console.WriteLine($"FAILED_START_ABORT_CHECKS_PASSED: {checks} assertions; linked production schedule/tick/terminal exit, native boundaries doubled, not GPU proof.");
    }

    static void IntroChecks()
    {
        Fresh();ArenaSession.IsActive=false;ArenaSession.StartedGame=null;ArenaSession.StartedWorld=null;
        var earlyUi=new EFT.UI.PreloaderUI();MonoBehaviourSingleton<EFT.UI.PreloaderUI>.Instance=earlyUi;
        int earlyRan=0;
        var scope=typeof(ArenaFailedStartIntro).GetNestedType("ArenaFailedStartIntroIntroScopePatch",System.Reflection.BindingFlags.NonPublic);
        var prefix=scope.GetMethod("Prefix",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
        var finalizer=scope.GetMethod("Finalizer",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
        Check(prefix.IsDefined(typeof(SPT.Reflection.Patching.PatchPrefix),false)
            &&finalizer.IsDefined(typeof(SPT.Reflection.Patching.PatchFinalizer),false),"SCOPE-PATCH-ATTRIBUTES");
        object[] args={earlyUi,null};prefix.Invoke(null,args);
        Coroutine early=null;
        try
        {
            early=ArenaFailedStartIntro.WaitForFade(earlyUi,.5f,()=>earlyRan++);
            throw new InvalidOperationException("native ShowIntro failure after fade scheduled");
        }
        catch(InvalidOperationException) { }
        finally { finalizer.Invoke(null,new[]{args[1]}); }
        ArenaSession.StartedGame=game;ArenaSession.StartedWorld=world;Schedule();Next();
        early.Run();Check(earlyRan==0,"PRE-CONFIRM-SERVER-REJECTED-INTRO");
        ArenaFailedStartIntro.WaitForFade(earlyUi,.5f,()=>earlyRan++).Run();
        Check(earlyRan==1,"FINALIZER-THROW-SCOPE-RESTORED");

        // Native boundary model: Stop installs GameEnd on the one black-screen
        // coroutine; the delayed intro replaces it, without throwing. Keep a
        // positive unpatched control so 'no errors' cannot masquerade as an exit.
        foreach(bool guarded in new[]{false,true})
        foreach(bool introBeforeAbort in new[]{false,true})
        {
            Fresh();var ui=new EFT.UI.PreloaderUI();MonoBehaviourSingleton<EFT.UI.PreloaderUI>.Instance=ui;
            Action black=null;int ended=0,cosmetic=0;
            game.OnStop=()=>{game.Status=GameStatus.Stopping;black=()=>{ended++;game.Status=GameStatus.Stopped;};};
            Action intro=()=>black=()=>cosmetic++;
            Coroutine delayed=null;
            void Queue()
            {
                ArenaFailedStartIntro.EnterIntro(ui,out var previous);
                try { delayed=guarded?ArenaFailedStartIntro.WaitForFade(ui,.5f,intro):Utils.WaitSeconds(ui,.5f,intro); }
                finally { ArenaFailedStartIntro.LeaveIntro(previous); }
            }
            if(introBeforeAbort)Queue();Schedule();Next();if(!introBeforeAbort)Queue();
            delayed.Run();black();
            Check(guarded?ended==1&&cosmetic==0&&game.Status==GameStatus.Stopped
                :ended==0&&cosmetic==1&&game.Status==GameStatus.Stopping,"DEFERRED-INTRO-CANCELLATION "+guarded+"/"+introBeforeAbort);
            Next(5);Check(game.StopCalls==1,"SINGLE-NATIVE-STOP");
        }

        // The delayed action belongs to the captured raid, not whatever raid
        // happens to exist when the callback is finally delivered.
        foreach(string bypass in new[]{"normal-start","ordinary","pending-only","foreign-game","foreign-world",
            "foreign-owned-world","foreign-started","epoch","profile","reset","preloader","non-intro","nested-scope"})
        {
            Fresh();var ui=new EFT.UI.PreloaderUI();MonoBehaviourSingleton<EFT.UI.PreloaderUI>.Instance=ui;
            int ran=0;Coroutine delayed;
            if(bypass=="non-intro")delayed=ArenaFailedStartIntro.WaitForFade(ui,.5f,()=>ran++);
            else
            {
                ArenaFailedStartIntro.EnterIntro(ui,out var previous);
                if(bypass=="nested-scope")
                {
                    ArenaFailedStartIntro.EnterIntro(new EFT.UI.PreloaderUI(),out var parent);
                    ArenaFailedStartIntro.LeaveIntro(parent);
                }
                delayed=ArenaFailedStartIntro.WaitForFade(ui,.5f,()=>ran++);
                ArenaFailedStartIntro.LeaveIntro(previous);
            }
            if(bypass=="ordinary")ArenaSession.IsActive=ArenaSession.LastRaidWasArena=false;
            if(bypass!="normal-start")Schedule();
            if(bypass!="pending-only")Next();
            switch(bypass)
            {
                case "foreign-game": Singleton<AbstractGame>.Instance=new LocalGame();break;
                case "foreign-world": Singleton<GameWorld>.Instance=new GameWorld();break;
                case "foreign-owned-world": game.GameWorld=new();break;
                case "foreign-started": ArenaSession.StartedGame=new LocalGame();break;
                case "epoch": ArenaSession.ArmEpoch++;break;
                case "profile": game.ProfileId="successor";break;
                case "reset": ArenaMatchService.ResetFailedStartAbort();break;
                case "preloader": MonoBehaviourSingleton<EFT.UI.PreloaderUI>.Instance=new();break;
            }
            delayed.Run();Check(ran==(bypass=="nested-scope"?0:1),"FADE-SCOPE "+bypass);
            // The finalizer must restore the normal fade path, even on failure.
            ArenaFailedStartIntro.WaitForFade(ui,.5f,()=>ran++).Run();
            Check(ran==(bypass=="nested-scope"?1:2),"FADE-SCOPE-RESTORED "+bypass);
        }

        var type=typeof(ArenaFailedStartIntro).GetNestedType("ArenaFailedStartIntroDeferredFadePatch",System.Reflection.BindingFlags.NonPublic);
        var transpile=type.GetMethod("Transpiler",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
        var native=typeof(Utils).GetMethod(nameof(Utils.WaitSeconds));
        var instruction=new HarmonyLib.CodeInstruction(System.Reflection.Emit.OpCodes.Call,native);
        var label=default(System.Reflection.Emit.Label);instruction.labels.Add(label);
        var patched=((IEnumerable<HarmonyLib.CodeInstruction>)transpile.Invoke(null,new object[]{new[]{instruction}})).Single();
        Check(((System.Reflection.MethodInfo)patched.operand).Name=="WaitForFade"&&patched.labels.Count==1,"TRANSPILER-EXACT-CALL-LABELS");
        foreach(var count in new[]{0,2})
        {
            bool refused=false;
            try { transpile.Invoke(null,new object[]{Enumerable.Range(0,count).Select(_=>new HarmonyLib.CodeInstruction(System.Reflection.Emit.OpCodes.Call,native))}); }
            catch(System.Reflection.TargetInvocationException e) { refused=e.InnerException is InvalidOperationException; }
            Check(refused,"TRANSPILER-DRIFT-REFUSED "+count);
        }
    }

    static void TerminalIntroChecks()
    {
        // Same native black-screen boundary, now reached by a legitimate result,
        // not ScheduleFailedStartAbort. Both ordering variants are supported.
        foreach(bool guarded in new[]{false,true})
        foreach(bool introBeforeStop in new[]{false,true})
        {
            Fresh();game.LocalPlayer.ProfileId=game.ProfileId;
            var mode=new ResultMode{Owns=true};ArenaMatchService.Current=new(){Mode=mode};
            var ui=new EFT.UI.PreloaderUI();MonoBehaviourSingleton<EFT.UI.PreloaderUI>.Instance=ui;
            Action black=null;int ended=0,cosmetic=0;Coroutine delayed=null;
            game.OnStop=()=>{
                game.Status=GameStatus.Stopping;mode.Owns=false;
                black=()=>{ended++;game.Status=GameStatus.Stopped;TdmMatchExit.Reset();};
            };
            void Queue()
            {
                ArenaFailedStartIntro.EnterIntro(ui,out var previous);
                try { delayed=guarded?ArenaFailedStartIntro.WaitForFade(ui,.5f,()=>black=()=>cosmetic++)
                    :Utils.WaitSeconds(ui,.5f,()=>black=()=>cosmetic++); }
                finally { ArenaFailedStartIntro.LeaveIntro(previous); }
            }
            if(introBeforeStop)Queue();
            TdmMatchExit.TryStop(game,world,game.ProfileId);
            if(!introBeforeStop)Queue();
            Check(game.StopCalls==1&&!ArenaMatchService.TeamStartAbortPending,"TERMINAL-NO-FAILED-START-LEASE");
            delayed.Run();black();
            Check(guarded?ended==1&&cosmetic==0&&game.Status==GameStatus.Stopped
                :ended==0&&cosmetic==1&&game.Status==GameStatus.Stopping,"TERMINAL-INTRO-CANCELLATION "+guarded+"/"+introBeforeStop);
            Check(!TdmMatchExit.OwnsTerminalTeardown(game,world,game.ProfileId)||!guarded,"TERMINAL-NATIVE-END-RESET");
        }
        foreach(string bypass in new[]{"none","ordinary","no-result","no-op","precommit-throw","postcommit-throw",
            "synchronous-end","synchronous-end-throw","soft-stopping","foreign-game","foreign-world","foreign-owned-world",
            "foreign-started","epoch","profile","reset","preloader","non-intro","standdown-only"})
        {
            Fresh();game.LocalPlayer.ProfileId=game.ProfileId;
            var mode=new ResultMode{Owns=true};ArenaMatchService.Current=new(){Mode=mode};
            var ui=new EFT.UI.PreloaderUI();MonoBehaviourSingleton<EFT.UI.PreloaderUI>.Instance=ui;
            int ran=0;Coroutine delayed;
            if(bypass=="non-intro")delayed=ArenaFailedStartIntro.WaitForFade(ui,.5f,()=>ran++);
            else
            {
                ArenaFailedStartIntro.EnterIntro(ui,out var previous);
                delayed=ArenaFailedStartIntro.WaitForFade(ui,.5f,()=>ran++);
                ArenaFailedStartIntro.LeaveIntro(previous);
            }
            if(bypass=="ordinary")ArenaSession.IsActive=false;
            if(bypass=="no-result")mode.Owns=false;
            if(bypass=="no-op")game.OnStop=()=>{};
            if(bypass=="precommit-throw")game.OnStop=()=>throw new Exception("before commit");
            if(bypass=="postcommit-throw")game.OnStop=()=>{game.Status=GameStatus.Stopping;throw new Exception("after commit");};
            if(bypass.StartsWith("synchronous-end"))game.OnStop=()=>{
                game.Status=GameStatus.Stopped;TdmMatchExit.Reset();
                if(bypass=="synchronous-end-throw")throw new Exception("after end");
            };
            if(bypass=="soft-stopping")game.OnStop=()=>game.Status=GameStatus.SoftStopping;
            if(bypass=="standdown-only"){ArenaRespawnDriver.StandDown=true;game.Status=GameStatus.Stopping;}
            TdmMatchExit.TryStop(game,world,game.ProfileId);
            mode.Owns=false;
            switch(bypass)
            {
                case "foreign-game":Singleton<AbstractGame>.Instance=new LocalGame();break;
                case "foreign-world":Singleton<GameWorld>.Instance=new GameWorld();break;
                case "foreign-owned-world":game.GameWorld=new();break;
                case "foreign-started":ArenaSession.StartedGame=new LocalGame();break;
                case "epoch":ArenaSession.ArmEpoch++;break;
                case "profile":game.ProfileId="successor";break;
                case "reset":TdmMatchExit.Reset();break;
                case "preloader":MonoBehaviourSingleton<EFT.UI.PreloaderUI>.Instance=new();break;
            }
            delayed.Run();
            Check(ran==((bypass=="none"||bypass=="postcommit-throw")?0:1),"TERMINAL-FADE-SCOPE "+bypass);
        }
    }
}
