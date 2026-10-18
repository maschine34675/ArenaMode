using ArenaMode.Combat;
using ArenaMode.Modes;
using ArenaMode.Session;
using ArenaMode.Compat;
using Comfort.Common;
using EFT;
using System.Reflection;

internal sealed class Fixture
{
    internal readonly LocalGame Game=new();
    internal readonly GameWorld World=new();
    internal readonly ArenaMatch Match=new();
    internal Player Local;
    internal BotOwner[] Followers,Enemies;
    internal TacticalMode Tactical;
    internal ZombieTeamRuntime Zombies;
    internal readonly int CompanionCount;
    private int _round;
    internal Fixture(string mode="tactical",int companionCount=4)
    {
        CompanionCount=companionCount;
        Singleton<GameWorld>.Instance=World; Singleton<AbstractGame>.Instance=Game;
        ArenaSession.World=World;ArenaSession.Game=Game;ArenaSession.IsActive=true;
        ArenaSession.FriendlyFireEnabledSelection=false;PitFireTeamCompat.TeamArenaActive=true;
        ArenaMatchService.Current=Match;
        Local=Body("local");World.MainPlayer=Local;
        if(mode=="tactical"){Tactical=new();Match.Mode=Tactical;}
        else if(mode=="zombie"){Zombies=new();Match.Mode=new ZombieMode{Team=Zombies};}
        else Match.Mode=new TeamDeathmatchMode();
        NextRound();
    }
    internal Player Body(string id,int side=1,string account=null)
    {
        var body=new Player{ProfileId=id,Side=side,Profile=new(){AccountId=account}};
        World.Alive[id]=body;World.Ever[id]=body;return body;
    }
    internal BotOwner Bot(string id,int side,string account)
    {var p=Body(id,side,account);var b=new BotOwner{GetPlayer=p};p.AIData.BotOwner=b;return b;}
    internal void NextRound(bool replaceLeader=false)
    {
        ++_round;
        if(replaceLeader){Local=Body("local");World.MainPlayer=Local;}
        Followers=Enumerable.Range(0,CompanionCount).Select(i=>Bot($"friend-{_round}-{i}",1,$"account{i}")).ToArray();
        Enemies=Enumerable.Range(0,5).Select(i=>Bot($"enemy-{_round}-{i}",2,$"enemy{i}")).ToArray();
        Tactical?.BindForTest(this);Zombies?.BindForTest(this);
    }
    internal void Die(Player body,bool destroy=false){body.HealthController.IsAlive=false;World.Alive.Remove(body.ProfileId);body.Destroyed=destroy;}
}

internal static class Program
{
    private static int _checks;
    private static void Check(bool value,string name){++_checks;if(!value)throw new Exception("FAILED: "+name);}
    private static bool Block(Player victim,IPlayer source)=>ArenaFriendlyFire.ShouldBlockFriendlyDamage(victim,source);
    private static void Main()
    {
        try { Run(); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode=1; }
    }
    private static void Run()
    {
        foreach(string mode in new[]{"tactical","zombie","tdm"})
        {
            var f=new Fixture(mode);var a=f.Followers[0].GetPlayer;var b=f.Followers[1].GetPlayer;var enemy=f.Enemies[0].GetPlayer;
            Check(Block(f.Local,a),mode+" follower to local");Check(Block(a,f.Local),mode+" local to follower");Check(Block(a,b),mode+" ally pair");
            Check(!Block(f.Local,f.Local)&&!Block(a,a),mode+" self");Check(!Block(f.Local,enemy)&&!Block(enemy,f.Local),mode+" enemy");
            Check(!Block(f.Local,null),mode+" environment");
            Check(!Block(f.Local,new Player{ProfileId=a.ProfileId}),mode+" unknown physical impostor");
            Check(!Block(new Player{ProfileId=f.Local.ProfileId},a),mode+" impostor victim");
            Check(Block(enemy,f.Enemies[1].GetPlayer)==(mode!="zombie"),mode+" opponent team or infected native");
            ArenaSession.FriendlyFireEnabledSelection=true;Check(!Block(a,b),mode+" ON preserves native");ArenaSession.FriendlyFireEnabledSelection=false;
            ArenaSession.IsActive=false;Check(!Block(a,b),mode+" ordinary inactive");ArenaSession.IsActive=true;
            f.Game.Status=GameStatus.Stopping;Check(!Block(a,b),mode+" stopping");f.Game.Status=GameStatus.Started;
            Singleton<GameWorld>.Instance=new GameWorld();Check(!Block(a,b),mode+" other world");Singleton<GameWorld>.Instance=f.World;
            Singleton<AbstractGame>.Instance=new LocalGame();Check(!Block(a,b),mode+" other game");Singleton<AbstractGame>.Instance=f.Game;
            f.World.Alive.Remove(a.ProfileId);Check(!Block(a,b),mode+" unregistered victim");f.World.Alive[a.ProfileId]=a;
            f.World.Ever[a.ProfileId]=new Player{ProfileId=a.ProfileId};Check(!Block(a,b),mode+" overwritten victim");f.World.Ever[a.ProfileId]=a;
            a.HealthController.IsAlive=false;Check(!Block(a,b),mode+" dead victim");a.HealthController.IsAlive=true;
        }
        Historical("tactical");Historical("zombie");RosterAdmission();Scopes();Explosion();Diagnostics();Wiring();
        Console.WriteLine($"FRIENDLY_FIRE_RUNTIME_PASSED {_checks} assertions");
    }
    private static void Historical(string mode)
    {
        var f=new Fixture(mode);var source=f.Followers[0].GetPlayer;var victim=f.Followers[1].GetPlayer;var own=f.Local;
        f.Die(source);Check(Block(victim,source),mode+" dead source grenade");
        source.Destroyed=true;Check(Block(victim,source),mode+" cleaned source frozen bridge");
        f.Die(own);f.NextRound(replaceLeader:true);
        Check(Block(f.Followers[1].GetPlayer,source),mode+" past round grenade to another participant");
        Check(!Block(f.Followers[0].GetPlayer,source),mode+" own follower replacement keeps self damage");
        Check(!Block(f.Local,own),mode+" old own grenade to local replacement");
        Check(!Block(victim,f.Local),mode+" old still registered victim generation");
        Check(!Block(f.Local,new Player{ProfileId=source.ProfileId}),mode+" no profile-only source admission");
        var currentSource=f.Followers[0].GetPlayer;currentSource.ProfileId="mutated";Check(!Block(f.Local,currentSource),mode+" mutated live source");
        var oldMatch=f.Match;ArenaMatchService.Current=new ArenaMatch{Mode=oldMatch.Mode};Check(!Block(f.Local,source),mode+" another match object");ArenaMatchService.Current=oldMatch;
        f.Followers[0].GetPlayer.ProfileId="repaired";
        f.Followers[1].GetPlayer.Profile.AccountId=f.Followers[2].GetPlayer.Profile.AccountId;
        f.Tactical?.BindForTest(f);f.Zombies?.BindForTest(f);
        Check(!Block(f.Local,source),mode+" invalid next freeze revokes old victim generation");
        var fresh=new Fixture(mode);Check(!Block(fresh.Local,source),mode+" source cannot cross raids");
    }
    private static void Scopes()
    {
        var f=new Fixture();var friend=f.Followers[0].GetPlayer;
        f.Tactical.SetScope(false,true);Check(!Block(f.Local,friend),"solo Tactical excluded");
        f.Tactical.SetScope(true,false);Check(!Block(f.Local,friend),"Tactical staging delegated to staging guard");
        f.Tactical.SetScope(true,true,0);Check(!Block(f.Local,friend),"zero epoch");f.Tactical.SetScope(true,true);
        f.Tactical.IsComplete=true;Check(!Block(f.Local,friend),"complete Tactical");f.Tactical.IsComplete=false;
        PitFireTeamCompat.TeamArenaActive=false;Check(!Block(f.Local,friend),"lost protected lease");
        var z=new Fixture("zombie");z.Zombies.Live=false;Check(!Block(z.Local,z.Followers[0].GetPlayer),"Zombie not live");
        z.Match.Mode=new ZombieMode();Check(!Block(z.Local,z.Followers[0].GetPlayer),"solo Zombie excluded");
        z.Match.Mode=new object();Check(!Block(z.Local,z.Followers[0].GetPlayer),"other modes excluded");
    }
    private static void RosterAdmission()
    {
        bool Bind(ArenaFriendlyFireRoster ledger,Fixture f,string[] ids=null)
            => ledger.Bind(f.Match,f.CompanionCount,f.World,f.Local,f.Local.ProfileId,f.Followers,
                ids??f.Followers.Select(x=>x.ProfileId).ToArray(),f.Enemies,f.Enemies.Select(x=>x.ProfileId).ToArray());
        void Reject(string name,Action<Fixture> mutate)
        {
            var f=new Fixture();var ledger=new ArenaFriendlyFireRoster();Check(Bind(ledger,f),"admission initial "+name);
            mutate(f);Check(!Bind(ledger,f),"admission rejects "+name);
            Check(!ledger.AreTeammates(f.Match,f.World,f.Local,f.Followers[0].GetPlayer),"failed freeze revokes "+name);
        }
        Reject("duplicate accounts",f=>f.Followers[1].Profile.AccountId=f.Followers[0].Profile.AccountId);
        Reject("missing account",f=>f.Followers[1].Profile.AccountId=null);
        Reject("nonactive follower",f=>f.Followers[1].BotState=EBotState.PreActive);
        Reject("owner body mismatch",f=>f.Followers[1].GetPlayer.AIData.BotOwner=f.Followers[0]);
        Reject("unregistered admission",f=>f.World.Alive.Remove(f.Followers[1].ProfileId));
        Reject("dead admission",f=>f.Followers[1].GetPlayer.HealthController.IsAlive=false);
        Reject("wrong leader",f=>f.World.MainPlayer=f.Followers[0].GetPlayer);
        Reject("partial followers",f=>f.Followers=f.Followers.Take(3).ToArray());
        Reject("partial opponents",f=>f.Enemies=f.Enemies.Take(4).ToArray());
        var a=new Fixture();var roster=new ArenaFriendlyFireRoster();
        var ids=a.Followers.Select(x=>x.ProfileId).ToArray();ids[1]="wrong";
        Check(!Bind(roster,a,ids),"admission frozen ID mismatch");
        Check(Bind(roster,a),"direct ledger current bind");
        Check(!roster.AreTeammates(new object(),a.World,a.Local,a.Followers[0].GetPlayer),"direct ledger foreign match");
        var copiedWorld=new GameWorld{MainPlayer=a.Local,Alive=new(a.World.Alive),Ever=new(a.World.Ever)};
        Check(!roster.AreTeammates(a.Match,copiedWorld,a.Local,a.Followers[0].GetPlayer),"direct ledger foreign world");
        Check(roster.AreTeammates(a.Match,a.World,a.Local,a.Followers[0]),"exact native owner bridge");
        Check(!roster.AreTeammates(a.Match,a.World,a.Followers[0].GetPlayer,a.Followers[0]),"native owner self bridge");
        for(int count=1;count<=4;count++)
        foreach(string mode in new[]{"tactical","zombie"})
        {
            var f=new Fixture(mode,count);var ledger=new ArenaFriendlyFireRoster();
            var enemies=mode=="tactical"?f.Enemies:Array.Empty<BotOwner>();
            bool Freeze(int expected,BotOwner[] followers) => ledger.Bind(f.Match,expected,f.World,f.Local,f.Local.ProfileId,
                followers,followers.Select(x=>x.ProfileId).ToArray(),enemies,enemies.Select(x=>x.ProfileId).ToArray());
            Check(Block(f.Local,f.Followers[0].GetPlayer),$"{mode} mode admits {count} companions");
            Check(Freeze(count,f.Followers),$"physical ledger admits {count} {mode} companions");
            Check(!Freeze(count,f.Followers.Skip(1).ToArray()),"variable ledger rejects missing follower");
            Check(!ledger.AreTeammates(f.Match,f.World,f.Local,f.Followers[0].GetPlayer),"variable invalid freeze revokes immunity");
            Check(!Freeze(count,f.Followers.Append(f.Bot("extra",1,"extra-account")).ToArray()),"variable ledger rejects extra follower");
            int changed=count==1?2:count-1;
            var wrong=Enumerable.Range(0,changed).Select(i=>f.Bot("drift"+i,1,"drift-account"+i)).ToArray();
            Check(!Freeze(changed,wrong),"variable ledger frozen count cannot drift");
            var historical=f.Followers[0].GetPlayer;
            f.NextRound();
            enemies=mode=="tactical"?f.Enemies:Array.Empty<BotOwner>();
            Check(Freeze(count,f.Followers),"variable ledger exact replacement accepted");
            Check(ledger.AreTeammates(f.Match,f.World,f.Local,historical),"variable ledger historical follower remains attributable");
            Check(!ledger.AreTeammates(f.Match,f.World,f.Followers[0].GetPlayer,historical),"variable ledger own replacement remains self damage");
        }
        foreach(int invalid in new[]{-1,0,5})
        {
            var f=new Fixture();var ledger=new ArenaFriendlyFireRoster();
            var followers=Enumerable.Range(0,Math.Max(0,invalid)).Select(i=>f.Bot("invalid"+i,1,"invalid-account"+i)).ToArray();
            Check(!ledger.Bind(f.Match,invalid,f.World,f.Local,f.Local.ProfileId,followers,
                followers.Select(x=>x.ProfileId).ToArray(),f.Enemies,f.Enemies.Select(x=>x.ProfileId).ToArray()),
                "physical ledger rejects invalid frozen count "+invalid);
        }
    }
    private static void Diagnostics()
    {
        var f=new Fixture();int before=ArenaMode.Plugin.ModLogger.Infos.Count;
        ArenaSession.FriendlyFireEnabledSelection=true;Block(f.Followers[0].GetPlayer,f.Local);
        ArenaSession.FriendlyFireEnabledSelection=false;Block(f.Enemies[0].GetPlayer,f.Local);
        Check(ArenaMode.Plugin.ModLogger.Infos.Count==before,"no block diagnostic without veto");
        Block(f.Local,f.Followers[0].GetPlayer);
        Check(ArenaMode.Plugin.ModLogger.Infos.Count==before,"bot damage does not consume local diagnostic");
        Check(ArenaFriendlyFire.ShouldBlockFriendlyDamage(f.Followers[0].GetPlayer,f.Local,reportDamage:false),"effect predicate still blocks");
        Check(ArenaMode.Plugin.ModLogger.Infos.Count==before,"effect predicate is not damage-hook evidence");
        Block(f.Followers[0].GetPlayer,f.Local);Block(f.Followers[1].GetPlayer,f.Local);
        Check(ArenaMode.Plugin.ModLogger.Infos.Count==before+1,"one diagnostic per match");
        Check(ArenaMode.Plugin.ModLogger.Infos.Last().Contains("TacticalMode: friendly fire blocked"),"diagnostic names mode and action");
        Check(ArenaMode.Plugin.ModLogger.Infos.Last().Contains("source=local, victim=friend-1-0"),"diagnostic captures exact damage actors");
        f.NextRound();Block(f.Followers[0].GetPlayer,f.Local);
        Check(ArenaMode.Plugin.ModLogger.Infos.Count==before+1,"round does not repeat diagnostic");
        var next=new Fixture();Block(next.Followers[0].GetPlayer,next.Local);
        Check(ArenaMode.Plugin.ModLogger.Infos.Count==before+2,"new match diagnostic independent");
        var loggerFailure=new Fixture();ArenaMode.Plugin.ModLogger.Throw=true;
        Check(Block(loggerFailure.Followers[0].GetPlayer,loggerFailure.Local),"logger failure cannot restore friendly damage");
        ArenaMode.Plugin.ModLogger.Throw=false;
    }
    private static void Explosion()
    {
        var f=new Fixture();var ally=f.Followers[0].GetPlayer;var enemy=f.Enemies[0].GetPlayer;
        var own=new Bridge{Body=f.Local};var friend=new Bridge{Body=ally};var foe=new Bridge{Body=enemy};var unknown=new Bridge{Body=new Player{ProfileId=ally.ProfileId}};
        var map=new Dictionary<IObserverToPlayerBridge,PlayerEmitterCache>{{own,new(){Value=1}},{friend,new(){Value=2}},{foe,new(){Value=3}},{unknown,new(){Value=4}}};
        ExplosionSharedMethods._playerBuffer=map;
        Check(ReferenceEquals(ArenaFriendlyExplosionEffects.FilterBlast(map),map),"no source context preserves native arguments");
        ArenaSession.FriendlyFireEnabledSelection=true;
        Check(ArenaFriendlyExplosionEffects.Begin(f.Local.ProfileId,true)==null,"ON avoids idle scope");
        ArenaSession.FriendlyFireEnabledSelection=false;ArenaSession.IsActive=false;
        Check(ArenaFriendlyExplosionEffects.Begin(f.Local.ProfileId,true)==null,"ordinary avoids idle scope");
        ArenaSession.IsActive=true;
        var outer=ArenaFriendlyExplosionEffects.Begin(f.Local.ProfileId,true);
        var filtered=ArenaFriendlyExplosionEffects.FilterBlast(map);
        Check(filtered.Count==3&&!filtered.ContainsKey(friend)&&filtered.ContainsKey(own)&&filtered.ContainsKey(foe)&&filtered.ContainsKey(unknown),"blast retains self enemy unknown");
        Check(map.Count==4&&map.ContainsKey(friend)&&ReferenceEquals(map[own],filtered[own]),"blast copies and preserves cache entries");
        var list=map.Keys.ToList();var cache=map;ArenaFriendlyExplosionEffects.FilterFlash(ref list,ref cache);
        Check(list.Count==3&&!list.Contains(friend)&&cache.Count==3&&!cache.ContainsKey(friend),"flash list+cache filtered");
        Check(map.Count==4&&!ReferenceEquals(cache,map),"flash shared native buffer unmodified");
        var foreignCache=new Dictionary<IObserverToPlayerBridge,PlayerEmitterCache>(map);var foreignList=map.Keys.ToList();
        var foreignCacheOriginal=foreignCache;var foreignListOriginal=foreignList;
        Check(ReferenceEquals(ArenaFriendlyExplosionEffects.FilterBlast(foreignCache),foreignCache),"foreign blast arguments not attributed to grenade");
        ArenaFriendlyExplosionEffects.FilterFlash(ref foreignList,ref foreignCache);
        Check(ReferenceEquals(foreignCache,foreignCacheOriginal)&&ReferenceEquals(foreignList,foreignListOriginal),"reentrant non-explosion flash source not inherited");
        var blastPatch=typeof(ArenaFriendlyExplosionEffects).GetNestedType("ArenaFriendlyExplosionEffectsBlastTargetsPatch",BindingFlags.NonPublic).GetMethod("Prefix",BindingFlags.Static|BindingFlags.NonPublic);
        object[] blastArgs={false,map};blastPatch.Invoke(null,blastArgs);
        Check(ReferenceEquals(blastArgs[1],map),"suppressed blast prefix leaves arguments");
        blastArgs=new object[]{true,map};blastPatch.Invoke(null,blastArgs);
        Check(!((IReadOnlyDictionary<IObserverToPlayerBridge,PlayerEmitterCache>)blastArgs[1]).ContainsKey(friend),"production blast prefix filters");
        var flashPatch=typeof(ArenaFriendlyExplosionEffects).GetNestedType("ArenaFriendlyExplosionEffectsFlashTargetsPatch",BindingFlags.NonPublic).GetMethod("Prefix",BindingFlags.Static|BindingFlags.NonPublic);
        var flashList=map.Keys.ToList();object[] flashArgs={false,flashList,map};flashPatch.Invoke(null,flashArgs);
        Check(ReferenceEquals(flashArgs[1],flashList)&&ReferenceEquals(flashArgs[2],map),"suppressed flash prefix leaves arguments");
        flashArgs=new object[]{true,flashList,map};flashPatch.Invoke(null,flashArgs);
        Check(!((List<IObserverToPlayerBridge>)flashArgs[1]).Contains(friend)&&!((Dictionary<IObserverToPlayerBridge,PlayerEmitterCache>)flashArgs[2]).ContainsKey(friend),"production flash prefix filters");
        using(ArenaFriendlyExplosionEffects.Begin(null,true))Check(ReferenceEquals(ArenaFriendlyExplosionEffects.FilterBlast(map),map),"nested unknown shadows previous source");
        using(ArenaFriendlyExplosionEffects.Begin(enemy.ProfileId,true))Check(ArenaFriendlyExplosionEffects.FilterBlast(map).ContainsKey(friend),"nested enemy source does not inherit ally");
        using(ArenaFriendlyExplosionEffects.Begin(f.Local.ProfileId,false))Check(ReferenceEquals(ArenaFriendlyExplosionEffects.FilterBlast(map),map),"earlier Harmony skip shadows source");
        Check(!ArenaFriendlyExplosionEffects.FilterBlast(map).ContainsKey(friend),"outer context restored");
        outer.Dispose();outer.Dispose();Check(ReferenceEquals(ArenaFriendlyExplosionEffects.FilterBlast(map),map),"finalizer/disposal restores no context");
        using(ArenaFriendlyExplosionEffects.Begin(f.Local.ProfileId,true))
        {
            ArenaSession.FriendlyFireEnabledSelection=true;Check(ReferenceEquals(ArenaFriendlyExplosionEffects.FilterBlast(map),map),"ON no effects filter");ArenaSession.FriendlyFireEnabledSelection=false;
            ArenaMatchService.Current=new ArenaMatch{Mode=f.Tactical};Check(ReferenceEquals(ArenaFriendlyExplosionEffects.FilterBlast(map),map),"context cannot cross match");ArenaMatchService.Current=f.Match;
        }
        // Execute actual Harmony entry/finalizer and preserve the original exception.
        var patch=typeof(ArenaFriendlyExplosionEffects).GetNestedType("ArenaFriendlyExplosionEffectsExplosionContextPatch",BindingFlags.NonPublic);
        object[] args={f.Local.ProfileId,true,null};patch.GetMethod("Prefix",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);
        Check(!ArenaFriendlyExplosionEffects.FilterBlast(map).ContainsKey(friend),"production context prefix wired");
        var fault=new InvalidOperationException("native fault");var final=patch.GetMethod("Finalizer",BindingFlags.Static|BindingFlags.NonPublic);
        Check(ReferenceEquals(final.Invoke(null,new[]{fault,args[2]}),fault),"native exception preserved");
        Check(ReferenceEquals(ArenaFriendlyExplosionEffects.FilterBlast(map),map),"exception finalizer clears scope");
        f.Die(ally);Check(Block(f.Local,ally),"delayed fragments protected without explosion context");
    }
    private static void Wiring()
    {
        var root=Directory.GetCurrentDirectory();while(!File.Exists(Path.Combine(root,"AGENTS.md"))){root=Directory.GetParent(root)?.FullName??throw new Exception("repo missing");}
        string Read(string p)=>File.ReadAllText(Path.Combine(root,p));
        var combat=Read("ArenaMode.Client/Combat/CombatPatches.cs");
        Check(combat.Split("ArenaFriendlyFire.ShouldBlockFriendlyDamage(").Length-1==4,"four real mutation surfaces wired");
        Check(!combat.Contains("TeamDeathmatchRelations.ShouldBlockFriendlyDamage("),"no remaining hard TDM override");
        Check(combat.Contains("ArenaFriendlyExplosionEffects.Enable();"),"production effect hooks enabled");
        Check(Read("ArenaMode.Client/Modes/TacticalMode.cs").Contains("BindFriendlyFireRoster();\n                _phase = Phase.Live;")||Read("ArenaMode.Client/Modes/TacticalMode.cs").Contains("BindFriendlyFireRoster();\r\n                _phase = Phase.Live;"),"Tactical binds only verified full roster before live");
        Check(Read("ArenaMode.Client/Modes/ZombieTeamRuntime.cs").Contains("BindFriendlyFireRoster();\n                _phase = TeamPhase.Live;")||Read("ArenaMode.Client/Modes/ZombieTeamRuntime.cs").Contains("BindFriendlyFireRoster();\r\n                _phase = TeamPhase.Live;"),"Zombie initial+rebound live bind");
        Check(Read("ArenaMode.Client/Modes/ZombieMode.cs").Contains("_team?.ShouldBlockFriendlyDamage(victim, attacker) == true"),"Zombie production forwarding");
    }
}
