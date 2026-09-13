using ArenaMode.Bots;
using ArenaMode.Hud;
using ArenaMode.Kits;
using ArenaMode.Modes;
using ArenaMode.Respawn;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.InputSystem;
using EFT.UI.Screens;
using System.Reflection;
using UnityEngine;

static class Program
{
    static int assertions;
    static void Check(bool ok,string message){assertions++;if(!ok)throw new Exception(message);}
    static TacticalMode Fresh()
    {
        Time.time=Time.unscaledTime=0;BurstSpawnService.Requests.Clear();BurstSpawnService.Reject=false;
        LiveKitRespawn.Swaps=0;ArenaRespawnDriver.StandDown=false;Redeploy.Core.Engine.RespawnEngineState.IsPipelineRunning=false;
        var player=new Player();var owner=new EftGamePlayerOwner{Player=player};
        var game=new LocalGame{LocalPlayer=player,Owner=owner};var world=new GameWorld{MainPlayer=player};
        Singleton<AbstractGame>.Instance=ArenaSession.Game=game;Singleton<GameWorld>.Instance=ArenaSession.World=world;
        GamePlayerOwner.MyPlayer=player;ArenaSession.IsActive=true;EftScreenManager.Instance=new();
        KitCatalog.Kits=[new(){Id="costly",Price=900},new(){Id="starter",Price=100},new(){Id="invalid",Price=1,Invalid=true}];
        var mode=new TacticalMode();mode.Prepare();return mode;
    }
    static BotOwner Arrive(int request=0,bool active=false)
    {
        var bot=new BotOwner{BotState=active?EBotState.Active:EBotState.PreActive};
        BurstSpawnService.Requests[request].Observer(bot);return bot;
    }
    static void Cue()
    {
        var mode=Fresh();mode.Issue();mode.Start();
        Check(mode.Cues.Count==0,"dispatch must not announce round start");
        Time.time=10;var bots=Enumerable.Range(0,5).Select(_=>Arrive()).ToArray();mode.Tick(10);
        Check(mode.Cues.Count==0,"PreActive callbacks do not announce");
        foreach(var bot in bots.Take(4))bot.BotState=EBotState.Active;
        mode.Tick(0);Check(mode.Cues.Count==0,"incomplete active roster waits");
        bots[4].BotState=EBotState.Active;Time.time=11;mode.Tick(1);
        Check(mode.Cues.Count==1&&mode.PhaseRemaining==600,"complete active roster announces once and starts fresh combat timer");
        mode.Tick(0);Check(mode.Cues.Count==1,"polls cannot repeat cue");
        Check(LiveKitRespawn.Swaps==1,"one round has one kit swap");

        mode=Fresh();mode.Start();Time.time=44.5f;bots=Enumerable.Range(0,5).Select(_=>Arrive()).ToArray();
        Time.time=45;mode.Tick(.5f);Check(mode.Cues.Count==0&&BurstSpawnService.Requests.Count==1&&bots.All(x=>x.HealthController.IsAlive),"late callbacks receive independent physical activation grace");
        foreach(var bot in bots)bot.BotState=EBotState.Active;Time.time=49;mode.Tick(4);
        Check(mode.Cues.Count==1&&mode.PhaseRemaining==600,"late healthy activation starts without retry or lost combat time");

        mode=Fresh();mode.Start();Check(mode.KillForTest()=="FIRST BLOOD","round dispatch begins award scope");BurstSpawnService.Reject=true;Time.time=45;mode.Tick(45);
        Check(mode.Cues.Count==0&&BurstSpawnService.Requests.Count==2,"empty timeout retries silently");
        var stale=Arrive(0,true);Check(!stale.HealthController.IsAlive,"retire stale epoch callback");
        Check(!BurstSpawnService.Requests[0].Lease(),"old request lease revoked");
        Time.time=47.9f;mode.Tick(2.9f);Check(BurstSpawnService.Requests.Count==2,"rejected plan does not spin");
        Time.time=48;mode.Tick(.1f);Check(BurstSpawnService.Requests.Count==3,"rejected plan retries at three seconds");
        Check(LiveKitRespawn.Swaps==1&&mode.Money==1000&&mode.Cues.Count==0,"spawn retries do not charge, swap or announce");
        Check(mode.KillForTest()!= "FIRST BLOOD","spawn retry does not reset award eligibility");

        mode=Fresh();mode.Start();var pending=Arrive();Time.time=45;mode.Tick(45);
        Check(!pending.HealthController.IsAlive&&mode.Cues.Count==0,"unready timeout retires pending body without announcing");
        Check(BurstSpawnService.Requests.Count==2,"unready-only request renews");

        mode=Fresh();mode.Start();Arrive(active:true);pending=Arrive();Time.time=44;mode.Tick(44);
        Check(mode.Cues.Count==0,"partial roster waits assembly window");Time.time=45;mode.Tick(1);
        Check(mode.Cues.Count==1&&mode.PhaseRemaining==600&&!pending.HealthController.IsAlive,"partial fallback announces ready subset and retires unreachable pending activation");
        mode.Tick(0);Check(mode.Cues.Count==1,"partial fallback announces exactly once");

        mode=Fresh();mode.Start();var early=Arrive(active:true);early.HealthController.IsAlive=false;
        Check(mode.KillForTest()=="FIRST BLOOD","early assembly kill consumes First Blood");
        foreach(var _ in Enumerable.Range(0,4))Arrive(active:true);
        mode.Tick(1);Check(mode.Cues.Count==1&&mode.Chain==1&&mode.KillForTest()!="FIRST BLOOD","delayed cue preserves early First Blood and multi-kill chain");

        mode=Fresh();mode.Start();foreach(var _ in Enumerable.Range(0,5)){var dead=Arrive(active:true);dead.HealthController.IsAlive=false;}
        mode.Tick(1);Check(mode.Cues.Count==0&&mode.Wins==1,"already eliminated squad wins without a late fight cue");
        mode=Fresh();mode.Start();mode.EndForTest();pending=Arrive(active:true);mode.Tick(1);
        Check(mode.Cues.Count==0&&!pending.HealthController.IsAlive&&!BurstSpawnService.Requests[0].Lease(),"teardown rejects callbacks and cannot announce");

        mode=Fresh();mode.Prepare(team:true);mode.Tick(1);Check(mode.Cues.Count==0&&BurstSpawnService.Requests.Count==0,"solo polling does not drive protected team start");
    }
    static void Kit()
    {
        var mode=Fresh();var starter=mode.Issue();
        Check(starter.Id=="starter","opening kit must be the cheapest buildable starter, never random paid gear");
        Check(mode.NoPurchaseDescription.Contains("free starter")&&mode.NoPurchaseDescription.Contains("starter"),"opening UI says which free starter is retained");
        Check(ReferenceEquals(mode.Issue(),starter),"opening supplier is stable across calls");
        mode.Start();Check(mode.Money==1000&&mode.Bought==null,"no purchase keeps only free starter and costs nothing");
        mode.SurvivedForTest();mode.Start();Check(ReferenceEquals(mode.Issue(),starter)&&mode.Money==1000,"survivor resupplies same previous loadout without purchase");
        Check(mode.NoPurchaseDescription.Contains("previous round"),"survivor UI describes previous loadout retention");
        mode=Fresh();mode.Issue();Check(mode.TryBuy(KitCatalog.Kits[0]),"affordable selection accepted");
        Check(mode.Money==1000,"browsing does not charge");mode.Start();
        Check(mode.Money==100&&mode.Issue().Id=="costly","paid kit charged once and issued");
        mode.Start();Check(mode.Money==100,"duplicate round start cannot charge twice");
        mode.SurvivedForTest();mode.Start();Check(mode.Money==100&&mode.Issue().Id=="costly","winning keeps purchased gear when its price is no longer affordable");
        mode=Fresh();mode.Prepare(down:true);mode.Start();Check(mode.Bought.Id=="costly"&&mode.Money==100,"existing death/no-buy auto-purchase remains");
        mode=Fresh();mode.Prepare(team:true);Check(!mode.TryBuy(KitCatalog.Kits[0]),"protected team rejects buying");
        mode=Fresh();mode.LoseStarterForTest();bool refused=false;try{mode.Issue();}catch(InvalidOperationException){refused=true;}
        Check(refused,"missing supplier state refuses instead of drawing random");
        KitCatalog.Kits=[new(){Id="invalid",Invalid=true}];Check(TacticalMode.SelectOpeningKit()==null,"no usable catalogue never selects invalid starter");
    }
    static void Escape()
    {
        var mode=Fresh();var game=(LocalGame)Singleton<AbstractGame>.Instance;var owner=game.Owner;
        owner.Player.HealthController.IsAlive=false;
        Check(TacticalBuyPanel.TryOpenCountdownMenu(owner,ECommand.Escape),"dead countdown owner must reach native Escape menu");
        Check(owner.Menus==1&&!owner.Player.HealthController.IsAlive&&!ArenaRespawnDriver.StandDown,"native menu opens without revival or early stand-down");
        Check(!TacticalBuyPanel.TryOpenCountdownMenu(owner,ECommand.Escape)&&owner.Menus==1,"existing native menu cannot be opened twice");
        Check(!TacticalBuyPanel.TryOpenCountdownMenu(owner,ECommand.ToggleShooting),"only Escape bypasses corpse command rejection");
        mode.ReadyUp();mode.Tick(5);Check(BurstSpawnService.Requests.Count==0,"buy deadline must not swap native menu owner");
        EftScreenManager.Instance.Screen=EEftScreenType.Settings;mode.Tick(5);Check(BurstSpawnService.Requests.Count==0,"settings keep countdown parked");
        EftScreenManager.Instance.Screen=EEftScreenType.BattleUI;Time.time=11;mode.Tick(1);Check(BurstSpawnService.Requests.Count==1,"countdown resumes after menu closes");
        Check(!TacticalBuyPanel.TryOpenCountdownMenu(owner,ECommand.Escape),"live mode leaves Escape to existing native/spectator paths");

        var rejects=new Action<LocalGame,EftGamePlayerOwner>[] {
            (g,o)=>ArenaSession.IsActive=false,
            (g,o)=>ArenaSession.Game=new LocalGame(),
            (g,o)=>ArenaSession.World=new GameWorld(),
            (g,o)=>g.Status=GameStatus.Stopping,
            (g,o)=>o.State=PlayerOwner.EState.Stopped,
            (g,o)=>g.Owner=new(),
            (g,o)=>o.Player=new(),
            (g,o)=>g.LocalPlayer=new(),
            (g,o)=>Singleton<GameWorld>.Instance.MainPlayer=new(),
            (g,o)=>GamePlayerOwner.MyPlayer=new(),
            (g,o)=>g.LocalPlayer.IsYourPlayer=false,
            (g,o)=>g.LocalPlayer.IsAI=true,
            (g,o)=>ArenaRespawnDriver.StandDown=true,
            (g,o)=>Redeploy.Core.Engine.RespawnEngineState.IsPipelineRunning=true,
            (g,o)=>EftScreenManager.Instance.Screen=EEftScreenType.Inventory,
            (g,o)=>EftScreenManager.Instance=null,
            (g,o)=>o.Accept=false,
            (g,o)=>o.Throw=true
        };
        foreach(var reject in rejects){mode=Fresh();game=(LocalGame)Singleton<AbstractGame>.Instance;owner=game.Owner;reject(game,owner);Check(!TacticalBuyPanel.TryOpenCountdownMenu(owner,ECommand.Escape),"ordinary/stale/foreign/pipeline/failed native menu is not authorized");}

        mode=Fresh();var panel=new TacticalBuyPanel();var panelType=typeof(TacticalBuyPanel);
        void Invoke(string name)=>panelType.GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(panel,null);
        Invoke("Update");Check(ArenaInputBlocker.Open,"buy panel owns gameplay filter");
        EftScreenManager.Instance.Screen=EEftScreenType.MainMenu;Invoke("Update");
        Check(!ArenaInputBlocker.Open&&ArenaInputBlocker.Drained,"native menu dismisses overlay with pointer drain");
        EftScreenManager.Instance.Screen=EEftScreenType.BattleUI;Invoke("Update");Check(!ArenaInputBlocker.Open,"native resume does not reopen dismissed buy panel");
        panelType.GetMethod("SetOpen",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(panel,[true]);
        Event.current=new(){type=EventType.KeyDown,keyCode=KeyCode.Escape};Invoke("OnGUI");
        Check(!Event.current.Used&&!ArenaInputBlocker.Open&&ArenaInputBlocker.Drained,"IMGUI Escape closes with drain but does not consume native menu key");
        TacticalBuyPanel.ApplyInputPatch();
        var hold=new SoloTacticalRoundStartPolicy.State();
        Check(SoloTacticalRoundStartPolicy.Evaluate(ref hold,true,10)==SoloTacticalRoundStartDecision.HoldBegun,"swap hold starts");
        SoloTacticalRoundStartPolicy.Pause(ref hold,120);
        Check(SoloTacticalRoundStartPolicy.Evaluate(ref hold,true,131)==SoloTacticalRoundStartDecision.Hold,"native menu time cannot force kit-swap hold give-up");
        Check(SoloTacticalRoundStartPolicy.Evaluate(ref hold,true,190)==SoloTacticalRoundStartDecision.StartAfterGivingUp,"real active wait still has original cap");
    }
    public static int Main(string[] args)
    {
        try{var part=args.FirstOrDefault()??"all";if(part is "all" or "cue")Cue();if(part is "all" or "kit")Kit();if(part is "all" or "escape")Escape();Console.WriteLine($"TACTICAL POLISH CHECKS PASSED ({assertions} assertions)");return 0;}
        catch(Exception ex){Console.Error.WriteLine($"TACTICAL POLISH CHECKS FAILED ({assertions} assertions): {ex.GetBaseException().Message}");return 1;}
    }
}
