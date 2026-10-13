using ArenaMode.Bots;
using ArenaMode.Compat;
using ArenaMode.Config;
using ArenaMode.Kits;
using ArenaMode.Modes;
using ArenaMode.Respawn;
using ArenaMode.Session;
using ArenaMode.Spectator;
using Comfort.Common;
using EFT;
using UnityEngine;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string reason)
    { _checks++; if (!value) throw new InvalidOperationException(reason); }

    private sealed class Fixture
    {
        internal readonly GameWorld World = new();
        internal readonly LocalGame Game = new();
        internal readonly BotGame Bots = new();
        internal readonly ArenaMatch Match = new();
        internal LocalPlayer Local => Game.LocalPlayer;
        internal Fixture(int count = 4, bool team = true, int expectedCount = 4)
        {
            Time.time=0;
            Singleton<AbstractGame>.Instance=Game; Singleton<GameWorld>.Instance=World; Singleton<IBotGame>.Instance=Bots;
            ArenaSession.Active=true; ArenaSession.ZombieOwnGearActive=true; ArenaSession.ReplaySeconds=0;
            ArenaMatchService.Current=Match;
            ArenaRespawnDriver.IsRespawnInProgress=false; ArenaRespawnDriver.StandDown=false;
            PitFireTeamCompat.FrozenCompanionCount=team?expectedCount:0; PitFireTeamCompat.TeamArenaActive=team; PitFireTeamCompat.PersistenceSuppressionActive=true;
            PitFireTeamCompat.RoundTransitionActive=false; PitFireTeamCompat.BindOkay=true; PitFireTeamCompat.GetOkay=true;
            PitFireTeamCompat.HostilityOkay=true; PitFireTeamCompat.BeginOkay=true; PitFireTeamCompat.HostilityCalls=0;
            PitFireTeamCompat.Followers=new();
            PitFireTeamRoundLifecycle.Status=new(); PitFireTeamRoundLifecycle.Rebuilt=0; PitFireTeamRoundLifecycle.Teardowns=0;
            PitFireTeamRoundLifecycle.RetireReady=PitFireTeamRoundLifecycle.ProfilesReady=PitFireTeamRoundLifecycle.FollowersReady=true;
            PitFireTeamRoundLifecycle.Spawn=()=> AddFollowers(expectedCount,2);
            TeamArenaLoadoutSnapshot.CaptureOkay=TeamArenaLoadoutSnapshot.ArmOkay=TeamArenaLoadoutSnapshot.PrepareOkay=true;
            TeamArenaLoadoutSnapshot.Captures=TeamArenaLoadoutSnapshot.Arms=TeamArenaLoadoutSnapshot.Prepares=0;
            KillcamReplayCompat.CameraReleased=true; ArenaSpectatorService.Request=null;
            ArenaMode.Effects.ArenaTracerTeams.BindOkay=true; ArenaMode.Effects.ArenaTracerTeams.Binds=0;
            ZombieLoadoutRefill.Calls=0; ZombieSpawnService.Reset();
            Settings.ZombieIntermissionSeconds.Value=2; Settings.ZombieWaveStartSize.Value=2;
            Settings.ZombieStuckWaveTimeoutSeconds.Value=100;
            ReplaceLocal(); AddFollowers(count,1);
        }
        internal void ReplaceLocal()
        {
            var player=new LocalPlayer(){IsYourPlayer=true,Profile=new(){Id="local",AccountId="human",Nickname="Human"}};
            if (Game.LocalPlayer != null) World.RegisteredPlayers.Remove(Game.LocalPlayer);
            Game.LocalPlayer=player; World.MainPlayer=player;
            World.Ever[player.ProfileId]=player; World.RegisteredPlayers.Add(player);
        }
        internal void AddFollowers(int count, int epoch)
        {
            PitFireTeamCompat.Followers.Clear();
            for(int i=0;i<count;i++)
            {
                var player=new Player(){IsAI=true,Profile=new(){Id=$"follower-{epoch}-{i}",AccountId=$"account-{i}",Nickname=$"Follower {i}"}};
                var bot=new BotOwner(){GetPlayer=player}; player.AIData=new(){BotOwner=bot};
                Bots.BotsController.Bots.BotOwners.Add(bot); World.RegisteredPlayers.Add(player); World.Ever[player.ProfileId]=player;
                PitFireTeamCompat.Followers.Add(bot);
            }
        }
        internal BotOwner AddInfected()
        {
            var player=new Player(){IsAI=true,Profile=new(){Id="infected",Info=new(){Settings=new(){Role=WildSpawnType.Infected}}}};
            var bot=new BotOwner(){GetPlayer=player}; player.AIData=new(){BotOwner=bot};
            Bots.BotsController.Bots.BotOwners.Add(bot); World.RegisteredPlayers.Add(player); World.Ever[player.ProfileId]=player;
            return bot;
        }
        internal ZombieTeamRuntime Runtime() => new(Match,Game,World,Local);
        internal ZombieMode Mode()
        { var mode=new ZombieMode(); Match.Mode=mode;mode.OnMatchStart(Match);return mode; }
        internal void Die()
        { Local.HealthController.IsAlive=false;World.RegisteredPlayers.Remove(Local);ArenaRespawnDriver.IsRespawnInProgress=true; }
    }

    private static bool Step(ZombieTeamRuntime runtime,float seconds=.3f)
    { Time.time+=seconds;return runtime.Tick(); }
    private static void Steps(ZombieTeamRuntime runtime,int count=12)
    { for(int i=0;i<count;i++) Step(runtime); }
    private static void Tick(ZombieMode mode,float seconds=.3f)
    { Time.time+=seconds;mode.Tick(seconds); }

    private static void CheckVariableSquads()
    {
        foreach (int invalid in new[] {0,5})
        {
            var f=new Fixture(0,expectedCount:invalid);
            bool rejected=false;
            try { f.Runtime(); } catch (InvalidOperationException) { rejected=true; }
            Check(rejected && TeamArenaLoadoutSnapshot.Captures==0,"invalid frozen count rejects before inventory capture");
        }
        foreach (int n in new[] {1,2,3,4})
        {
            var f=new Fixture(n,expectedCount:n);var runtime=f.Runtime();
            Check(Step(runtime) && runtime.LiveTeamMembers.Count==n+1,$"{n} followers start exact squad");
            Check(runtime.ShouldBlockFriendlyDamage(f.Local,PitFireTeamCompat.Followers[0].GetPlayer),"variable squad admits friendly protection");
            runtime.WaveCleared();Steps(runtime);
            Check(runtime.IsLive && runtime.LiveTeamMembers.Count==n+1 && runtime.Epoch==2,"surviving leader rebuild retains frozen count");
            f.Die();runtime.OnLocalDeath();Step(runtime);
            Check(ArenaSpectatorService.Request?.ExpectedCompanionCount==n && ArenaSpectatorService.Request.Targets().Count==n,"spectator freezes independent expected count");
            runtime.WaveCleared();Step(runtime);Step(runtime);
            Check(runtime.TryTakeTeamRespawnAuthorization(f.Game,f.Local,out _,out _,out _),"dead leader receives one prepared authorization");
            f.ReplaceLocal();Check(runtime.OnLocalRespawn(f.Local),"replacement body binds");
            ArenaRespawnDriver.IsRespawnInProgress=false;Steps(runtime);
            Check(runtime.IsLive && runtime.LiveTeamMembers.Count==n+1,"dead leader rebuild retains frozen count");
            f.Die();runtime.OnLocalDeath();foreach(var b in PitFireTeamCompat.Followers)b.HealthController.IsAlive=false;
            Check(!Step(runtime,.001f) && runtime.IsComplete,"all frozen members dead ends survival");
            f=new Fixture(n,expectedCount:n);runtime=f.Runtime();Step(runtime);
            PitFireTeamCompat.FrozenCompanionCount=n==4?3:n+1;
            Check(!Step(runtime) && runtime.IsComplete,"mid-match frozen count drift ends no contest");
            f=new Fixture(n,expectedCount:n);runtime=f.Runtime();Step(runtime);runtime.WaveCleared();
            PitFireTeamCompat.FrozenCompanionCount=n==4?3:n+1;
            Check(!Step(runtime) && runtime.IsComplete && PitFireTeamRoundLifecycle.Teardowns==0,"rebind count drift fails before teardown");
            f=new Fixture(n-1,expectedCount:n);runtime=f.Runtime();
            Check(!Step(runtime) && !runtime.IsComplete,"missing member cannot redefine complete roster");
            Step(runtime,46);Check(runtime.IsComplete,"incomplete squad times out");
        }
    }

    public static void Main()
    {
        CheckVariableSquads();
        var f=new Fixture(3);var runtime=f.Runtime();
        Check(!Step(runtime),"three companions must hold initial wave");
        Check(!runtime.IsComplete && runtime.ShouldBlockStagingDamage(f.Local),"assembly is protected, not defeat");
        Check(TeamArenaLoadoutSnapshot.Captures==1,"capture own entry gear once");
        f.AddFollowers(4,2);Check(Step(runtime),"exact four companions release initial wave");
        Check(PitFireTeamCompat.HostilityCalls==0,"empty initial infected roster is valid; no Tactical empty-hostility rejection");
        Check(runtime.LiveTeamMembers.Count==5,"live anchor includes exact five");
        Check(runtime.ShouldBlockFriendlyDamage(f.Local,PitFireTeamCompat.Followers[0].GetPlayer),"production initial Live transition admits exact friendly damage roster");
        Check(runtime.TrySynchronizeScoreboardRoster(out _) && f.Match.Scoreboard.Friends.Length==4,"stable account scoreboard has four allies");
        Check(f.Match.Scoreboard.Friends.Select(x=>x.Account).Distinct().Count()==4,"account identity survives profile remap");
        var infected=f.AddInfected();Check(Step(runtime),"active infected hostility admitted");
        Check(PitFireTeamCompat.HostilityCalls==1,"hostility reached for real infected");
        Check(runtime.IsOpposingAwardKill(infected.GetPlayer,f.Local),"human infected kill eligible");
        Check(!runtime.IsOpposingAwardKill(PitFireTeamCompat.Followers[0].GetPlayer,f.Local),"teamkill cannot earn award");
        for(int i=0;i<10;i++) Step(runtime,.001f);
        Check(PitFireTeamCompat.HostilityCalls==1,"live reflection scan is throttled");

        f=new Fixture();runtime=f.Runtime();Step(runtime);
        f.Die();runtime.OnLocalDeath();
        Check(runtime.HoldRespawn && Step(runtime),"death holds respawn while companions fight");
        Check(ArenaSpectatorService.Request?.Targets().Count==4 && ArenaSpectatorService.Request.Eligible(),"spectator admits exact four allies");
        Check(runtime.LiveTeamMembers.Count==4,"dead PMC is never infected hunt anchor");
        PitFireTeamCompat.Followers[0].HealthController.IsAlive=false;
        Check(Step(runtime) && runtime.LiveTeamMembers.Count==3,"one companion casualty does not wipe or respawn midwave");
        Check(PitFireTeamRoundLifecycle.Teardowns==0,"death alone never rebuilds follower roster");
        foreach(var bot in PitFireTeamCompat.Followers) bot.HealthController.IsAlive=false;
        Check(!Step(runtime,.001f) && runtime.IsComplete && runtime.HoldRespawn,"physical total wipe wins immediately over throttle");
        Check(f.Game.Stops==0,"result shows briefly with death held");
        Step(runtime,4);Check(f.Game.Stops==1 && f.Game.LastStatus==ExitStatus.Killed,"team wipe finishes protected raid");
        Step(runtime,4);Check(f.Game.Stops==1,"native stop is one-shot");

        f=new Fixture();runtime=f.Runtime();Step(runtime);
        var original=f.Local;var oldFollowers=PitFireTeamCompat.Followers.ToArray();
        oldFollowers[0].HealthController.IsAlive=false;
        runtime.WaveCleared();Check(!runtime.IsLive && runtime.ShouldBlockStagingDamage(original),"wave-clear gates all damage during restore");
        Steps(runtime);
        Check(runtime.IsLive && ReferenceEquals(original,f.Local),"surviving PMC stays exact same body");
        Check(PitFireTeamRoundLifecycle.Rebuilt==1 && PitFireTeamRoundLifecycle.Teardowns==1,"one complete lifecycle per wave");
        Check(TeamArenaLoadoutSnapshot.Prepares==0,"living PMC never uses respawn restore transaction");
        Check(PitFireTeamCompat.Followers.All(b=>oldFollowers.All(old=>!ReferenceEquals(old,b))),"all companions rebuilt through proven body barrier");
        Check(runtime.LiveTeamMembers.Count==5 && runtime.Epoch==2,"wave two releases only with five ready");
        Check(runtime.ShouldBlockFriendlyDamage(f.Local,oldFollowers[0].GetPlayer),"production rebound Live transition preserves historical friendly source");
        Check(!runtime.ShouldBlockFriendlyDamage(PitFireTeamCompat.Followers[0].GetPlayer,oldFollowers[0].GetPlayer),"same follower's old grenade remains self damage after actual wave rebuild");
        Check(runtime.TrySynchronizeScoreboardRoster(out _) && f.Match.Scoreboard.Epoch==2,"scoreboard rebind epoch advances");

        f=new Fixture();runtime=f.Runtime();Step(runtime);f.Die();runtime.OnLocalDeath();runtime.WaveCleared();
        Step(runtime);Step(runtime);
        Check(!runtime.HoldRespawn && TeamArenaLoadoutSnapshot.Arms==1 && TeamArenaLoadoutSnapshot.Prepares==1,"release follows complete detached loadout preparation");
        Check(!runtime.TryTakeTeamRespawnAuthorization(f.Game,new LocalPlayer(),out _,out _,out _),"wrong corpse cannot consume prepared authorization");
        Check(runtime.TryTakeTeamRespawnAuthorization(f.Game,f.Local,out var prepared,out int epoch,out _) && prepared.RoundEpoch==2 && epoch==2,"matching corpse consumes epoch-two authorization");
        Check(!runtime.TryTakeTeamRespawnAuthorization(f.Game,f.Local,out _,out _,out _),"authorization is single use");
        var dead=f.Local;f.ReplaceLocal();Check(runtime.OnLocalRespawn(f.Local),"exact replacement local body accepted after consumed authorization");
        ArenaRespawnDriver.IsRespawnInProgress=false;Steps(runtime);
        Check(runtime.IsLive && runtime.LiveTeamMembers.Count==5 && !ReferenceEquals(dead,runtime.Leader),"dead leader restored before fresh follower bind");

        f=new Fixture();runtime=f.Runtime();Step(runtime);f.Die();runtime.OnLocalDeath();runtime.WaveCleared();
        TeamArenaLoadoutSnapshot.PrepareOkay=false;Step(runtime);Step(runtime);
        Check(runtime.IsComplete && runtime.HoldRespawn,"loadout preparation failure never releases corpse");
        Check(!runtime.TryTakeTeamRespawnAuthorization(f.Game,f.Local,out _,out _,out _),"failure supplies no authorization");
        Check(PitFireTeamRoundLifecycle.Rebuilt==0,"failed loadout cannot bind a leader");

        f=new Fixture();runtime=f.Runtime();Step(runtime);f.Die();runtime.OnLocalDeath();runtime.WaveCleared();
        ArenaSession.ReplaySeconds=5;KillcamReplayCompat.CameraReleased=false;Step(runtime);Step(runtime);
        Check(runtime.HoldRespawn && TeamArenaLoadoutSnapshot.Prepares==0,"killcam ownership holds prior to prepare");
        KillcamReplayCompat.CameraReleased=true;Step(runtime);
        Check(!runtime.HoldRespawn && TeamArenaLoadoutSnapshot.Prepares==1,"released camera permits exact prepared transition");

        f=new Fixture();runtime=f.Runtime();Step(runtime);f.ReplaceLocal();
        Check(!Step(runtime) && runtime.IsComplete,"unrequested living body swap fails closed");
        f=new Fixture();runtime=f.Runtime();Step(runtime);
        f.World.Ever["local"] = new LocalPlayer(){Profile = new(){Id="local"}};
        Check(!Step(runtime) && runtime.IsComplete,"a transient replacement in persistent registry is rejected even after leaving active registry");
        f=new Fixture();runtime=f.Runtime();Step(runtime);
        Check(!runtime.OnLocalRespawn(f.Local) && runtime.IsComplete,"unrequested replacement event cannot rebind PFT");
        f=new Fixture();runtime=f.Runtime();Step(runtime);runtime.WaveCleared();Step(runtime);
        PitFireTeamRoundLifecycle.Status.Epoch++;
        Check(!Step(runtime) && runtime.IsComplete,"cross-epoch lifecycle completion rejected");
        f=new Fixture(3);runtime=f.Runtime();Step(runtime,46);
        Check(runtime.IsComplete,"incomplete initial roster expires boundedly");
        f=new Fixture();runtime=f.Runtime();Step(runtime);runtime.WaveCleared();Step(runtime);
        PitFireTeamRoundLifecycle.RetireReady=false;Step(runtime,46);
        Check(runtime.IsComplete,"old follower retirement cannot wait forever");
        f=new Fixture();runtime=f.Runtime();Step(runtime);f.Die();runtime.OnLocalDeath();runtime.WaveCleared();
        foreach(var b in PitFireTeamCompat.Followers) b.HealthController.IsAlive=false;
        Step(runtime);Check(runtime.IsComplete && PitFireTeamRoundLifecycle.Teardowns==0,"last casualty before teardown is defeat, not free resurrection");
        f=new Fixture();runtime=f.Runtime();Step(runtime);runtime.Dispose();
        Check(!Step(runtime) && runtime.HoldRespawn,"disposed epoch cannot restart");

        // Exercise the actual mode and pure wave director together, not a mirror
        // of the policy: held assembly, pending tail, rebind clock and solo path.
        f=new Fixture(3);var mode=f.Mode();Tick(mode);
        Check(ZombieSpawnService.Dispatched==0,"mode cannot spawn before fourth follower");
        f.AddFollowers(4,2);Tick(mode);Check(ZombieSpawnService.Dispatched==1,"first infected starts only after team-ready");
        ZombieSpawnService.Pending=0;ZombieSpawnService.Alive=1;ZombieSpawnService.ConfirmedArrivals=1;
        Tick(mode);Check(ZombieSpawnService.Dispatched==2,"remaining wave slot dispatched");
        ZombieSpawnService.Alive=0;Tick(mode);
        Check(PitFireTeamRoundLifecycle.Teardowns==0 && mode.HasFriendlyRoster,
            "dispatched final infected must settle before entering any wave-clear teardown phase");
        ZombieSpawnService.Pending=0;ZombieSpawnService.ConfirmedArrivals=2;Tick(mode);
        Check(mode.HudStatusLine.Contains("RESTORING"),"cleared wave enters team restore barrier");
        Check(ZombieLoadoutRefill.Calls==0,"team never falls through solo refill/body swap");
        for(int i=0;i<12;i++) Tick(mode);
        Check(ZombieSpawnService.Dispatched==2,"next wave still waits post-readiness intermission");
        Tick(mode,3);Check(ZombieSpawnService.Dispatched==3,"next wave begins after complete fresh team and countdown");
        Check(ZombieLoadoutRefill.Calls==0,"all team wave transitions exclude generic refill");
        mode.OnMatchEnd();

        f=new Fixture(4,false);mode=f.Mode();Tick(mode);
        Check(!mode.IsTeamArena && mode.HoldRespawn && !mode.IsComplete && ZombieSpawnService.Dispatched==1,"solo Zombie starts live with permanent death-respawn hold");
        ZombieSpawnService.Pending=0;ZombieSpawnService.Alive=1;Tick(mode);
        ZombieSpawnService.Pending=0;ZombieSpawnService.Alive=0;Tick(mode);
        Check(ZombieLoadoutRefill.Calls==1,"solo own-gear intermission refill remains unchanged");
        mode.OnMatchEnd();
        Console.WriteLine($"ZOMBIE_TEAM_CHECKS_PASSED {_checks} assertions");
    }
}
