using System.Text.Json;
using ArenaMode.Areas;
using ArenaMode.Shared;
using EFT;
using EFT.Game.Spawning;
using Redeploy.Core.Spawning;
using UnityEngine;

internal static class Program
{
    private static int _checks;
    private static string _database;
    private static readonly ArenaAreaDefinition Area=ArenaAreaCatalog.CustomsFactoryCenterTeams;
    private sealed class Fallback : ISpawnSelector
    {
        public int Calls;
        public (Vector3 position,Quaternion rotation) Resolve(LocalGame game) {Calls++;return (game.LocalPlayer.Position,default);}
    }
    private static void Need(bool value,string name) { _checks++;if(!value)throw new Exception("ASSERT: "+name); }
    private static void Throws(Action action,string name)
    {
        bool thrown=false;try {action();}catch(InvalidOperationException){thrown=true;}Need(thrown,name);
    }
    private static Point[] ReadPoints(ArenaAreaDefinition area=null)
    {
        area??=Area;
        using var data=JsonDocument.Parse(File.ReadAllText(_database));
        var result=data.RootElement.GetProperty("SpawnPointParams").EnumerateArray().Select(e=>
        {
            var pos=e.GetProperty("Position");
            var sides=e.GetProperty("Sides").EnumerateArray().Aggregate(EPlayerSideMask.None,(s,n)=>s|Enum.Parse<EPlayerSideMask>(n.GetString()));
            var categories=e.GetProperty("Categories").EnumerateArray().Aggregate(ESpawnCategoryMask.None,(s,n)=>s|Enum.Parse<ESpawnCategoryMask>(n.GetString()));
            return new Point {Id=e.GetProperty("Id").GetString(),Position=new Vector3(pos.GetProperty("x").GetSingle(),pos.GetProperty("y").GetSingle(),pos.GetProperty("z").GetSingle()),
                BotZoneName=e.GetProperty("BotZoneName").GetString(),Sides=sides,Categories=categories};
        }).Where(p=>area.AllowsSpawn(p.Position.x,p.Position.y,p.Position.z,p.Id)).ToArray();
        return result;
    }
    private static bool Bot(ISpawnPoint p)=>!string.IsNullOrEmpty(p.BotZoneName)&&((p.Categories&(ESpawnCategoryMask.Bot|ESpawnCategoryMask.BotPmc))!=0);
    private static SpawnSystem Setup(EPlayerSide side=EPlayerSide.Usec,int size=5,ArenaAreaDefinition area=null)
    {
        area??=Area;
        ArenaMode.Bots.ArenaBotSpawnFilter.ExcludedIds.Clear();ArenaMode.Bots.ArenaBotSpawnFilter.ExcludedZones.Clear();
        ArenaAreaRuntime.Aborted=false;
        return ArenaAreaRuntime.TestSetup(area,side,size,ReadPoints(area));
    }
    private static ISpawnPoint Initial(SpawnSystem system,EPlayerSide side)
        =>((ISpawnSystem)system).SelectSpawnPoint(ESpawnCategory.Player,side,profileId:"local");
    private static List<BotZone> Zones=>ArenaAreaRuntime.TestGame.BotsController.BotSpawner.Zones;
    private static Player Actor(Vector3 pos,bool usec,bool alive=true)
        =>new() {Position=pos,Profile=new Profile {Id=Guid.NewGuid().ToString("N"),Info=new ProfileInfo {Side=usec?EPlayerSide.Usec:EPlayerSide.Bear}},HealthController=new Health {IsAlive=alive}};
    private static ArenaAreaRuntime.TeamSpawnLease Reserve(bool usec,string name)
    {
        bool success=ArenaAreaRuntime.TryPlanTeamBot(Zones,usec,true,out var zone,out var point,out var lease);
        Need(success&&point!=null&&zone!=null&&lease!=null,name);
        Need(zone.SpawnPoints.Contains(point),"reserved point belongs to exact native dispatch zone");return lease;
    }
    private static float D2(Vector3 a,Vector3 b)=>(a.x-b.x)*(a.x-b.x)+(a.z-b.z)*(a.z-b.z);

    private static void InitialAndCapacity()
    {
        ArenaAreaInitialSpawnPatch.EnablePatch();
        Need(ArenaAreaInitialSpawnPatch.IsReady,"initial native patch installs");
        var target=SPT.Reflection.Patching.ModulePatch.Target;
        Need(target!=null&&target.IsPrivate&&target.Name.EndsWith("ISpawnSystem.SelectSpawnPoint"),"initial hook binds explicit native interface implementation");
        foreach(var side in new[]{EPlayerSide.Usec,EPlayerSide.Bear})
            for(int size=2;size<=5;size++)
            {
                var system=Setup(side,size);var point=Initial(system,side);
                Need(system.NativeSelections==0&&Area.TeamStarts.Allows(side==EPlayerSide.Usec,true,point.Id),"initial PMC uses faction start pool");
                Need(ArenaAreaSpawnPolicy.IsPlayerPoint(point,side),"initial PMC honors native player side mask");
                ArenaAreaRuntime.TestActivate(point);
                Need(ArenaAreaRuntime.CurrentTeamSize==size,"runtime exposes frozen selected team size");
                Need(ArenaAreaRuntime.TestValidate(out var problem),"actual Customs start corpus validates: "+problem);
                var leases=new List<ArenaAreaRuntime.TeamSpawnLease>();
                foreach(bool usec in new[]{true,false})
                {
                    int targetBots=size-(usec==(side==EPlayerSide.Usec)?1:0);
                    for(int i=0;i<targetBots;i++)
                    {
                        var lease=Reserve(usec,"both factions can reserve requested initial formation");
                        Need(Area.TeamStarts.Allows(usec,false,lease.Point.Id),"initial bots use their assigned side only");
                        var body=Actor(lease.Point.Position,usec);
                        Need(lease.Accept(body),"valid reserved arrival is admitted");
                        ArenaAreaRuntime.TestWorld.AllAlivePlayersList.Add(body);lease.Dispose();leases.Add(lease);
                    }
                }
                Need(leases.Count==size*2-1&&leases.Select(l=>l.Point.Id).Distinct().Count()==leases.Count,"requested match population uses distinct native bot starts");
                Need(leases.All(l=>D2(l.Point.Position,point.Position)>=2.25f),"initial bots remain clear of either local PMC faction");
                Need(ArenaAreaRuntime.TestLeases.Count()==0,"accepted arrivals release all reservations");
                ArenaAreaRuntime.FinishTeamAssembly();Need(!ArenaAreaRuntime.TestStarting,"full formation releases start-side restriction");
            }
        foreach(string mismatch in new[]{"world","collection","profile","side","category","normal","legacy"})
        {
            var s=Setup();ESpawnCategory category=ESpawnCategory.Player;EPlayerSide side=EPlayerSide.Usec;string profile="local";
            switch(mismatch)
            {
                case "world":s._players=new GameWorld();break;
                case "collection":var copy=new PointsCollection();copy.AddRange(s._spawnPoints);s._spawnPoints=copy;break;
                case "profile":profile="other";break;
                case "side":side=EPlayerSide.Bear;break;
                case "category":category=ESpawnCategory.Bot;break;
                case "normal":ArenaAreaRuntime.Loading=false;break;
                case "legacy":s=Setup(area:ArenaAreaCatalog.CustomsFactoryCenterPolygon);break;
            }
            ((ISpawnSystem)s).SelectSpawnPoint(category,side,profileId:profile);
            Need(s.NativeSelections==1,"initial native pass-through for "+mismatch);
        }
        var blocked=Setup();foreach(var p in blocked._spawnPoints.OfType<Point>().Where(p=>Area.TeamStarts.Allows(true,true,p.Id)))p.SpawnBlocked=true;
        Throws(()=>Initial(blocked,EPlayerSide.Usec),"no initial side point rejects without native outside fallback");
        Need(blocked.NativeSelections==0,"missing initial formation never calls native fallback");
    }

    private static void OppositeVirtualTeams()
    {
        foreach (var native in new[] { EPlayerSide.Usec, EPlayerSide.Bear })
        {
            var system = Setup(native);
            var selected = native == EPlayerSide.Usec ? EPlayerSide.Bear : EPlayerSide.Usec;
            bool usec = selected == EPlayerSide.Usec;
            ArenaMode.Session.ArenaSession.SelectedTeam = selected;
            ISpawnPoint initial = null;
            try { initial = Initial(system, native); } catch (InvalidOperationException) { }
            Need(initial != null && system.NativeSelections == 0
                && Area.TeamStarts.Allows(usec, true, initial.Id),
                "virtual team controls initial Customs pool while native request identity stays unchanged");
            ArenaAreaRuntime.TestActivate(initial);
            Need(ArenaAreaRuntime.TestGame.Profile.Info.Side == native, "virtual initial placement never mutates PMC faction");
            Need(ArenaAreaRuntime.TestValidate(out var problem), "opposite selected team formation validates: " + problem);
            var world = ArenaAreaRuntime.TestWorld;
            var initialLease = ArenaAreaRuntime.ReservePlayerPosition(initial.Position);
            Need(initialLease != null && initialLease.Usec == usec && initialLease.Accept(world.MainPlayer),
                "virtual player arrival accepts selected side despite opposite native faction");
            initialLease.Dispose();
            ArenaAreaRuntime.FinishTeamAssembly();

            // Native-faction bots are opponents after switching sides. Keep the
            // PMC alive so the ranking must not mistake that same native side
            // for a second opponent at the original player location.
            var enemyPoint = system._spawnPoints.First(p => Area.TeamStarts.Allows(usec, false, p.Id));
            world.AllAlivePlayersList.Add(Actor(enemyPoint.Position, !usec));
            float EnemyDistance(Vector3 p) => world.AllAlivePlayersList.OfType<Player>()
                .Where(actor => actor.HealthController.IsAlive
                    && (ReferenceEquals(actor, world.MainPlayer) ? selected : actor.Profile.Info.Side) != selected)
                .Min(actor => D2(actor.Position, p));
            var eligible = system._spawnPoints.Where(p => ArenaAreaSpawnPolicy.IsPlayerPoint(p, selected)
                && ArenaAreaRuntime.TestUnoccupied(p.Position, world.MainPlayer)).ToArray();
            var respawn = ArenaAreaRuntime.PickRespawn(initial.Position, 0).position;
            Need(Math.Abs(EnemyDistance(respawn) - eligible.Max(p => EnemyDistance(p.Position))) < .01f,
                "virtual player respawn ranks opponents by selected team");
            var lease = ArenaAreaRuntime.ReservePlayerPosition(respawn);
            world.MainPlayer.Position = respawn;
            Need(lease.Usec == usec && lease.Accept(world.MainPlayer),
                "virtual respawn lease admits unchanged native PMC on selected team");
            Need(world.MainPlayer.Profile.Info.Side == native, "virtual respawn preserves native faction");
            lease.Dispose();
        }
    }

    private static void ReadinessRejections()
    {
        var s=Setup();var p=Initial(s,EPlayerSide.Usec);ArenaAreaRuntime.TestActivate(p);
        ArenaAreaRuntime.TestDisconnect(p);Need(!ArenaAreaRuntime.TestValidate(out _),"disconnected initial PMC rejects readiness");
        s=Setup();p=Initial(s,EPlayerSide.Usec);ArenaAreaRuntime.TestActivate(p);
        var missing=s._spawnPoints.First(q=>Area.TeamStarts.Allows(false,false,q.Id));
        foreach(var z in Zones)z.SpawnPoints=z.SpawnPoints.Where(q=>q.Id!=missing.Id).ToArray();
        Need(!ArenaAreaRuntime.TestValidate(out var why)&&why.Contains("bots"),"missing native dispatch marker rejects readiness");
        s=Setup();p=Initial(s,EPlayerSide.Usec);ArenaAreaRuntime.TestActivate(p);
        missing=s._spawnPoints.First(q=>Area.TeamStarts.Allows(false,false,q.Id));
        foreach(var z in Zones)z.SpawnPoints=z.SpawnPoints.Select(q=>q.Id==missing.Id
            ?new Point {Id=q.Id,Position=q.Position,Rotation=q.Rotation,Sides=q.Sides,Categories=q.Categories,BotZoneName=q.BotZoneName,SpawnBlocked=true}:q).ToArray();
        Need(!ArenaAreaRuntime.TestValidate(out _),"blocked distinct native zone wrapper rejects readiness");
        s=Setup();p=Initial(s,EPlayerSide.Usec);ArenaAreaRuntime.TestActivate(p);
        Zones.Clear();Need(!ArenaAreaRuntime.TestValidate(out _),"empty native dispatch zones reject readiness");
        s=Setup();p=Initial(s,EPlayerSide.Usec);ArenaAreaRuntime.TestActivate(p);
        var bots=s._spawnPoints.OfType<Point>().Where(q=>Area.TeamStarts.Allows(true,false,q.Id)).ToArray();
        bots[1].Position=bots[0].Position;Need(!ArenaAreaRuntime.TestValidate(out _),"collapsed same-side markers reject readiness");
        s=Setup();p=Initial(s,EPlayerSide.Usec);ArenaAreaRuntime.TestActivate(p);
        var east=s._spawnPoints.OfType<Point>().First(q=>Area.TeamStarts.Allows(false,true,q.Id));
        east.Position=p.Position+new Vector3(10,0,0);Need(!ArenaAreaRuntime.TestValidate(out _),"opposing start separation drift rejects readiness");
    }

    private static void ExactRosterCapacity()
    {
        // Reproduce the GPU report: no unused opponent PMC marker, exactly
        // N opponent bot points, and only N-1 allied bot points are connected.
        foreach(var side in new[]{EPlayerSide.Usec,EPlayerSide.Bear})
            for(int size=2;size<=5;size++)
            {
                var s=Setup(side,size);var initial=Initial(s,side);ArenaAreaRuntime.TestActivate(initial);
                bool localUsec=side==EPlayerSide.Usec;
                foreach(var p in s._spawnPoints.Where(p=>Area.TeamStarts.Allows(!localUsec,true,p.Id)))
                    ArenaAreaRuntime.TestDisconnect(p);
                foreach(bool usec in new[]{true,false})
                {
                    int needed=size-(usec==localUsec?1:0);
                    foreach(var p in s._spawnPoints.Where(p=>Area.TeamStarts.Allows(usec,false,p.Id)).Skip(needed))
                        ArenaAreaRuntime.TestDisconnect(p);
                }
                Need(ArenaAreaRuntime.TestValidate(out var why),"exact roster needs no opponent PMC or surplus ally bot: "+why);
                var lastAlly=s._spawnPoints.OfType<Point>().First(p=>Area.TeamStarts.Allows(localUsec,false,p.Id));
                lastAlly.SpawnBlocked=true;
                Need(!ArenaAreaRuntime.TestValidate(out why)&&why.Contains("bots"),"one missing required ally rejects readiness");
                lastAlly.SpawnBlocked=false;
                var lastEnemy=s._spawnPoints.OfType<Point>().First(p=>Area.TeamStarts.Allows(!localUsec,false,p.Id));
                lastEnemy.SpawnBlocked=true;
                Need(!ArenaAreaRuntime.TestValidate(out why)&&why.Contains("bots"),"one missing required opponent rejects readiness");
                lastEnemy.SpawnBlocked=false;
                foreach(var p in s._spawnPoints.OfType<Point>().Where(p=>Area.TeamStarts.Allows(localUsec,true,p.Id)))p.SpawnBlocked=true;
                Need(!ArenaAreaRuntime.TestValidate(out why)&&why.Contains("PMC 0/1"),"missing local PMC still rejects readiness");
            }
    }

    private static void ReservationLifetimes()
    {
        var s=Setup();ArenaAreaRuntime.TestActivate(Initial(s,EPlayerSide.Usec));
        var lease=Reserve(true,"reserve first pending body");
        Need(ArenaAreaRuntime.TryPlanTeamBot(Zones,true,false,out _,out var planned,out _)&&planned.Id!=lease.Point.Id,
            "pending reservations excluded from preflight selection");
        var second=Reserve(true,"reserve second pending body");
        Need(lease.Point.Id!=second.Point.Id&&D2(lease.Point.Position,second.Point.Position)>=2.25f,"simultaneous reservations never overlap");
        var body=Actor(lease.Point.Position,true);
        Need(!lease.Accept(Actor(lease.Point.Position,false)),"wrong-faction arrival rejects lease");
        Need(!lease.Accept(Actor(lease.Point.Position+new Vector3(2.01f,0,0),true)),"displaced arrival rejects reserved marker");
        ArenaAreaRuntime.TestWorld.AllAlivePlayersList.Add(body);
        Need(lease.Accept(body),"arrival ignores its own live body for occupancy");
        var blocker=Actor(body.Position,false);ArenaAreaRuntime.TestWorld.AllAlivePlayersList.Add(blocker);
        Need(!lease.Accept(body),"late hostile occupant rejects reserved arrival");
        blocker.HealthController.IsAlive=false;Need(lease.Accept(body),"dead occupant does not block body acceptance");
        ArenaAreaRuntime.TestWorld.AllAlivePlayersList.Remove(body);
        ArenaAreaRuntime.Sample=pos=>false;Need(!lease.Accept(body),"failed arrival navmesh sample rejects release");
        ArenaAreaRuntime.Sample=pos=>true;ArenaAreaRuntime.Path=(a,b)=>false;Need(!lease.Accept(body),"failed arrival path rejects release");
        ArenaAreaRuntime.Path=(a,b)=>true;
        Time.time=30;Need(!lease.IsCurrent&&!lease.Accept(body),"reservation expires at exact deadline");
        var replacement=Reserve(true,"expired reservation point can be replaced");
        lease.Dispose();second.Dispose();Need(replacement.IsCurrent,"old callback cannot dispose replacement reservation");
        ArenaAreaRuntime.TestToken="new-token";Need(!replacement.IsCurrent,"new exact raid token rejects old lease");
        replacement.Dispose();
        s=Setup();ArenaAreaRuntime.TestActivate(Initial(s,EPlayerSide.Usec));
        lease=Reserve(true,"reserve before reset");ArenaAreaRuntime.TestReset();Need(!lease.IsCurrent&&!ArenaAreaRuntime.TestLeases.Any(),"reset clears all reservations");
        s=Setup();ArenaAreaRuntime.TestActivate(Initial(s,EPlayerSide.Usec));
        lease=Reserve(true,"reserve before native marker mutation");
        var marker=(Point)lease.Point;var frozen=marker.Position;marker.Position+=new Vector3(3,0,0);
        Need(!lease.IsCurrent&&!lease.Accept(Actor(marker.Position,true)),"native marker position mutation expires reserved arrival");
        marker.Position=frozen;marker.Id="changed-native-id";
        Need(!lease.IsCurrent,"native marker identity mutation expires reservation");lease.Dispose();
        Need(!ArenaAreaRuntime.TestLeases.Any(),"reservation disposal uses frozen identity after native marker rename");
    }

    private static void RespawnRankingAndOccupancy()
    {
        foreach(bool usec in new[]{true,false})
        {
            var side=usec?EPlayerSide.Usec:EPlayerSide.Bear;
            var s=Setup(side);var local=Initial(s,side);ArenaAreaRuntime.TestActivate(local);
            var world=ArenaAreaRuntime.TestWorld;
            world.MainPlayer.HealthController.IsAlive=false;
            var home=s._spawnPoints.First(p=>Area.TeamStarts.Allows(usec,false,p.Id));
            world.AllAlivePlayersList.Add(Actor(home.Position,!usec));
            var initial=Reserve(usec,"initial side remains mandatory despite nearer enemy");
            Need(Area.TeamStarts.Allows(usec,false,initial.Point.Id),"initial grouping dominates enemy-distance ranking");initial.Dispose();
            ArenaAreaRuntime.FinishTeamAssembly();
            foreach(var own in s._spawnPoints.Where(p=>Area.TeamStarts.Allows(usec,false,p.Id)&&p.Id!=home.Id))
                world.AllAlivePlayersList.Add(Actor(own.Position,!usec));
            float EnemyDistance(Vector3 p)=>world.AllAlivePlayersList.OfType<Player>()
                .Where(a=>a.HealthController.IsAlive&&a.Profile.Info.Side!=side).Min(a=>D2(a.Position,p));
            var eligible=s._spawnPoints.Where(Bot).Where(p=>ArenaAreaRuntime.TestUnoccupied(p.Position)).ToArray();
            float optimum=eligible.Max(p=>EnemyDistance(p.Position));
            var respawn=Reserve(usec,"subsequent bot respawn can choose all connected points");
            Need(Math.Abs(EnemyDistance(respawn.Point.Position)-optimum)<.01f,"bot respawn maximizes nearest live enemy distance");
            Need(!Area.TeamStarts.Allows(usec,false,respawn.Point.Id),"released bot can use safer opposing start region");
            respawn.Dispose();
            var players=s._spawnPoints.Where(p=>ArenaAreaSpawnPolicy.IsPlayerPoint(p,side)&&ArenaAreaRuntime.TestUnoccupied(p.Position)).ToArray();
            var selected=ArenaAreaRuntime.PickRespawn(default,99999).position;
            Need(Math.Abs(EnemyDistance(selected)-players.Max(p=>EnemyDistance(p.Position)))<.01f,"player respawn maximizes live enemy distance before death distance");
            // Allied positions block overlap, but do not become hostile distance anchors.
            var ally=Actor(selected,usec);world.AllAlivePlayersList.Add(ally);
            var next=ArenaAreaRuntime.PickRespawn(default,0).position;
            Need(D2(next,selected)>=2.25f,"live ally blocks player respawn overlap");
            ally.HealthController.IsAlive=false;
            Need(D2(ArenaAreaRuntime.PickRespawn(default,0).position,selected)<.01f,"dead ally no longer blocks safer player respawn");
            foreach(var p in s._spawnPoints.Where(Bot))world.AllAlivePlayersList.Add(Actor(p.Position,usec));
            Need(!ArenaAreaRuntime.TryPlanTeamBot(Zones,usec,true,out _,out _,out _),"fully occupied bot corpus has no unsafe fallback");
            foreach(var p in players)world.AllAlivePlayersList.Add(Actor(p.Position,usec));
            Throws(()=>ArenaAreaRuntime.PickRespawn(default,0),"fully occupied player corpus has no outside fallback");
        }
    }

    private static void PlayerPreflightReservation()
    {
        var s=Setup();var start=Initial(s,EPlayerSide.Usec);ArenaAreaRuntime.TestActivate(start);ArenaAreaRuntime.FinishTeamAssembly();
        var world=ArenaAreaRuntime.TestWorld;world.MainPlayer.HealthController.IsAlive=false;
        var fallback=new Fallback();var selector=new ArenaMode.Respawn.ArenaSpawnSelector(fallback,()=>start.Position,40);
        Need(ArenaAreaRespawn.TryPrepare(ArenaAreaRuntime.TestGame,selector,out var prepared),"player preflight reserves before handover");
        var lease=ArenaAreaRuntime.TestLeases.Single();
        Need(lease.CanReleasePlayer&&fallback.Calls==0,"prepared team player retains exact bounded reservation");
        var selected=prepared.Resolve(ArenaAreaRuntime.TestGame).position;
        Need(D2(selected,lease.Point.Position)<.01f,"prepared player resolves same reserved position without redraw");
        Need(!ArenaAreaRuntime.TestUnoccupied(selected),"player reservation blocks pending bot selection overlap");
        var hostile=Actor(selected,false);world.AllAlivePlayersList.Add(hostile);
        Throws(()=>prepared.Resolve(ArenaAreaRuntime.TestGame),"late occupant blocks final prepared player release");
        world.AllAlivePlayersList.Remove(hostile);
        var marker=(Point)lease.Point;marker.Position+=new Vector3(3,0,0);
        Throws(()=>prepared.Resolve(ArenaAreaRuntime.TestGame),"mutated marker cannot redirect prepared player occupancy check");
        marker.Position=selected;
        Time.time=30;Throws(()=>prepared.Resolve(ArenaAreaRuntime.TestGame),"expired reservation blocks prepared player release");
        Time.time=0;
        var newer=ArenaAreaRuntime.ReservePlayerPosition(selected);lease.Dispose();Need(newer.IsCurrent,"old prepared player cannot release new reservation");
        ArenaAreaRuntime.ReleasePlayerReservation(Actor(selected,true));Need(newer.IsCurrent,"foreign player callback cannot release local reservation");
        ArenaAreaRuntime.ReleasePlayerReservation(world.MainPlayer);Need(!newer.IsCurrent,"exact local respawn callback releases player reservation");
        Need(ArenaAreaRespawn.TryPrepare(ArenaAreaRuntime.TestGame,fallback,out var inPlace)&&!ArenaAreaRuntime.TestLeases.Any(),
            "in-place kit replacement does not reserve unrelated PMC marker");
        Need(D2(inPlace.Resolve(ArenaAreaRuntime.TestGame).position,world.MainPlayer.Position)<.01f,
            "in-place replacement preserves current body position");
    }

    private static void OtherModeReservations()
    {
        foreach (string mode in new[] { "deathmatch", "gungame", "tactical", "zombie" })
        {
            var system = Setup();
            var start = Initial(system, EPlayerSide.Usec);
            ArenaAreaRuntime.TestActivate(start);
            ArenaAreaRuntime.TestMode = mode;
            Need(!ArenaAreaRuntime.HasTeamStarts && ArenaAreaRuntime.CurrentTeamSize == null,
                "other directors never inherit independent TDM capacity");
            var selected = ArenaAreaRuntime.PickRespawn(start.Position, 40).position;
            var playerLease = ArenaAreaRuntime.ReservePlayerPosition(selected);
            Need(playerLease != null && playerLease.CanReleasePlayer,
                "every bounded mode reserves player handover");
            var occupant = Actor(selected, false);
            ArenaAreaRuntime.TestWorld.AllAlivePlayersList.Add(occupant);
            Need(!playerLease.CanReleasePlayer, "nonTDM player late occupancy rejects handover");
            ArenaAreaRuntime.TestWorld.AllAlivePlayersList.Remove(occupant);
            ArenaAreaRuntime.ReleasePlayerReservation(ArenaAreaRuntime.TestWorld.MainPlayer);
            Need(!playerLease.IsCurrent, "nonTDM exact local release retires reservation");
            Need(!ArenaAreaRuntime.TryReserveCompanionSpawn(out _, out _, out _),
                "companion placement requires frozen authenticated roster");
            ArenaMode.Compat.PitFireTeamCompat.HasFrozenTeamRoster = true;
            if (mode is not ("tactical" or "zombie"))
            {
                Need(!ArenaAreaRuntime.TryReserveCompanionSpawn(out _, out _, out _),
                    "frozen roster cannot enable followers in solo formats");
                continue;
            }
            var leases = new List<ArenaAreaRuntime.TeamSpawnLease>();
            for (int i = 0; i < 4; i++)
            {
                Need(ArenaAreaRuntime.TryReserveCompanionSpawn(out var zone, out var point, out var lease),
                    "bounded protected format reserves all four companions");
                Need(zone.SpawnPoints.Contains(point) && leases.All(old => D2(old.Position, point.Position) >= 2.25f),
                    "companion pending leases never overlap");
                Need(lease.Accept(Actor(point.Position, i % 2 == 0)),
                    "mixed faction companion still requires exact connected arrival");
                leases.Add(lease);
            }
            var first = leases[0];
            Need(!first.Accept(Actor(first.Position + new Vector3(10, 0, 0), true)),
                "companion far arrival refused");
            Time.time = 119;
            Need(first.IsCurrent, "slow companion activation retains bounded lease until deadline");
            Time.time = 120;
            Need(!first.IsCurrent, "companion reservation still expires");
            Time.time = 0;
            ArenaAreaRuntime.TestToken = Guid.NewGuid().ToString("N");
            Need(!first.IsCurrent, "companion exact raid token protects next match");
        }
    }

    private static void ProtectedInitialCoopCategory()
    {
        foreach (string mode in new[] { "deathmatch", "gungame", "teamdeathmatch", "tactical", "zombie" })
        foreach (bool frozen in new[] { false, true })
        foreach (var side in new[] { EPlayerSide.Usec, EPlayerSide.Bear })
        {
            var system = Setup(side);
            ArenaAreaRuntime.TestMode = mode;
            ArenaMode.Compat.PitFireTeamCompat.HasFrozenTeamRoster = frozen;
            bool accepted = ArenaAreaRuntime.TryInitialTeamSpawn(system, ESpawnCategory.Coop, side, "local", out var point);
            Need(accepted == (frozen && mode is "tactical" or "zombie"),
                "PFT Coop initial request requires exact protected format and frozen roster");
            if (!accepted) continue;
            Need(Area.TeamStarts.Allows(side == EPlayerSide.Usec, true, point.Id),
                "protected Coop request still uses surveyed native PMC faction marker");
            Need(!ArenaAreaRuntime.TryInitialTeamSpawn(system, ESpawnCategory.Coop, side, "foreign-profile", out _),
                "protected Coop request cannot select another profile");
            var collection = system._spawnPoints;
            system._spawnPoints = new PointsCollection();
            Need(!ArenaAreaRuntime.TryInitialTeamSpawn(system, ESpawnCategory.Coop, side, "local", out _),
                "protected Coop request cannot use another spawn collection");
            system._spawnPoints = collection;
            ArenaAreaRuntime.Loading = false;
            Need(!ArenaAreaRuntime.TryInitialTeamSpawn(system, ESpawnCategory.Coop, side, "local", out _),
                "protected Coop initial placement cannot run outside matching load");
        }
    }

    public static int Main(string[] args)
    {
        try
        {
            _database=Path.Combine(args.Length==0?"D:\\SPT41":args[0],"SPT_Runtime","SPT_Data","database","locations","bigmap","base.json");
            Need(ReadPoints().Length==29&&ReadPoints().Count(Bot)==11,"refined Customs Center corpus retains 29 markers and 11 bot points");
            InitialAndCapacity();OppositeVirtualTeams();ReadinessRejections();ExactRosterCapacity();ReservationLifetimes();RespawnRankingAndOccupancy();PlayerPreflightReservation();OtherModeReservations();ProtectedInitialCoopCategory();
            ArenaAreaInitialSpawnPatch.Shutdown();Need(!ArenaAreaInitialSpawnPatch.IsReady,"initial spawn patch shutdown releases hook");
            Console.WriteLine($"ARENA_TEAM_RUNTIME_CONTRACTS_PASSED checks={_checks}");return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
