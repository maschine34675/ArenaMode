using ArenaMode.Modes;
using ArenaMode.Pickups;
using EFT.Game.Spawning;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;

internal static class DominationPlacementEntry
{
    private static void Main() => Program.RunDominationChecks();
}

internal static partial class Program
{
    private static int _domChecks;
    private static readonly Vector3[] FactoryObjectives =
    [new(-5.89f,.031f,-27.46f),new(-6.77f,.031f,10.01f),new(-20.756f,.031f,22.769f)];
    private static void AssertDom(bool pass, string text)
    { _domChecks++; if (!pass) throw new Exception("FAIL: " + text); }
    private static TeamDeathmatchMode Dom(Func<Vector3,bool> allows)
    {
        var mode = new TeamDeathmatchMode { IsDomination = true, PickupPlacement = allows };
        ArenaMatchService.Current.Mode = mode;
        return mode;
    }
    private static void WriteOne(float x, params float[] alternatives)
    {
        var point = Point(); point.X=x;
        point.Alternatives = alternatives.Select(a=>new ArenaPickupPosition{X=a}).ToList();
        System.IO.File.WriteAllText(LayoutPath, ArenaPickupLayouts.Serialize(FileFor(point)));
    }
    private static bool Clear(Vector3 p) => ArenaObjectivePickupSpacing.Allows(p, FactoryObjectives, 4, 1);

    internal static void RunDominationChecks()
    {
        var center = new[] { new Vector3(0,0,0) };
        AssertDom(!ArenaObjectivePickupSpacing.Allows(new(0,0,0),center,4,1), "objective center excluded");
        AssertDom(!ArenaObjectivePickupSpacing.Allows(new(6,0,0),center,4,1), "six metre clearance boundary excluded");
        AssertDom(ArenaObjectivePickupSpacing.Allows(new(6.01f,0,0),center,4,1), "outside clearance remains available");
        AssertDom(!ArenaObjectivePickupSpacing.Allows(new(0,2,0),center,4,1), "vertical clearance boundary excluded");
        AssertDom(ArenaObjectivePickupSpacing.Allows(new(0,2.01f,0),center,4,1), "separate floors remain independent");
        AssertDom(!ArenaObjectivePickupSpacing.Allows(new(float.NaN,0,0),center,4,1), "nonfinite pickup rejected");
        AssertDom(!ArenaObjectivePickupSpacing.Allows(new(50,0,0),null,4,1), "missing objective positions fail closed");
        AssertDom(!ArenaObjectivePickupSpacing.Allows(new(50,0,0),[],4,1), "unprepared objective positions fail closed");

        Fixture(); WriteOne(0,10); var original=System.IO.File.ReadAllText(LayoutPath);
        _game.Location.SpawnPointParams=[Seed("unused-custom-expansion",50)];
        Dom(p=>ArenaObjectivePickupSpacing.Allows(p,center,4,1)); Step(); Step();
        AssertDom(ActiveView().Floor.x==10, "initial candidate exclusion chooses a different authored point");
        AssertDom(System.IO.File.ReadAllText(LayoutPath)==original, "custom pickup file is never rewritten");
        AssertDom(ArenaPickupView.Created.Count==1, "custom layouts do not acquire generated pickup definitions");

        Fixture(); WriteOne(0); Dom(p=>p.x!=0); NavMesh.Sample=p=>p.x==0?new(10,p.y,p.z):p;
        Step(); Step();
        AssertDom(ArenaPickupView.Created.Count==0, "rejected original candidate is not repaired into permission");

        Fixture(); WriteOne(6.2f); Dom(p=>ArenaObjectivePickupSpacing.Allows(p,center,4,1));
        NavMesh.Sample=p=>Math.Abs(p.x-6.2f)<.01f?new(5.8f,p.y,p.z):p;
        Step(); Step();
        AssertDom(ArenaPickupView.Created.Count==0, "final repaired floor cannot enter objective clearance");

        Fixture(); WriteOne(10); var unavailable=Dom(_=>false); Step(); Step();
        AssertDom(ArenaPickupView.Created.Count==0, "unready domination authority cannot place pickups");
        unavailable.PickupPlacement=_=>true; Step(1.1f);
        AssertDom(ArenaPickupView.Created.Count==1, "placement retries after objective authority becomes ready");

        foreach(bool repaired in new[]{false,true})
        {
            RotationReady(); var mode=Dom(_=>true); var old=ActiveView();
            mode.PickupPlacement=p=>Math.Abs(p.x-(repaired?9.8f:10f))>.01f;
            if(repaired) NavMesh.Sample=p=>p.x==10?new(9.8f,p.y,p.z):p;
            At(61);
            AssertDom(ReferenceEquals(old,ActiveView()), repaired
                ? "rotation final repaired floor cannot enter objective clearance"
                : "rotation excludes newly reserved candidate");
        }

        RotationReady(); var cooldownMode=Dom(p=>p.x!=10); Move(_local,0); Move(_local,-3); At(State().ReadyAt);
        AssertDom(ActiveView().Floor.x==0, "cooldown rearm cannot move into objective clearance");

        // With two occupied reciprocal slots, no free candidate exists. Recheck
        // both authored and actual exchange destinations, not only direct moves.
        Fixture(); var two=FileFor(); two.Layouts[0].Points[0].Alternatives=[new(){X=10}];
        var other=Point(ArenaPickupKind.Shield); other.Id="second"; other.X=10; other.Alternatives=[new(){X=0}];
        two.Layouts[0].Points.Add(other); System.IO.File.WriteAllText(LayoutPath,ArenaPickupLayouts.Serialize(two));
        var exchangeMode=Dom(_=>true); Step(); Step(); var oldViews=ArenaPickupView.Created.ToArray();
        exchangeMode.PickupPlacement=p=>p.x!=10; At(61);
        AssertDom(oldViews.All(v=>!v.Disposed)&&ArenaPickupView.Created.Count==2,
            "exchange cannot bypass objective exclusion");

        Fixture(); System.IO.File.WriteAllText(LayoutPath,ArenaPickupLayouts.Serialize(two));
        exchangeMode=Dom(_=>true); Step(); Step(); oldViews=ArenaPickupView.Created.ToArray();
        exchangeMode.PickupPlacement=p=>Math.Abs(p.x-9.8f)>.01f;
        NavMesh.Sample=p=>p.x==10?new(9.8f,p.y,p.z):p; At(61);
        AssertDom(oldViews.All(v=>!v.Disposed)&&ArenaPickupView.Created.Count==2,
            "exchange final repaired floor cannot enter objective clearance");

        Fixture();
        _game.Location.SpawnPointParams=[Seed("c671337f-e58f-4de6-a387-307d877954a3",100),
            Seed("sniper",110,zone:"ZoneSnipeFactory"),Seed("player",120,category:ESpawnCategoryMask.Player),
            Seed("safe",130),Seed("pmc",140,category:ESpawnCategoryMask.BotPmc)];
        var filtered=ArenaPickupLayouts.Select(ArenaPickupLayouts.Factory("spartan"),"factory4_day",null);
        ArenaPickupMapLayout.ExtendFactoryForObjectives(_game,filtered,Clear);
        var filteredPool=Pool(filtered);
        AssertDom(!filteredPool.Any(p=>p.X is 100 or 110 or 120), "DOM native expansion excludes trapped sniper and player-only seeds");
        AssertDom(filteredPool.Any(p=>p.X==130)&&filteredPool.Any(p=>p.X==140), "DOM native expansion permits safe bot and PMC seeds");

        foreach(string map in new[]{"factory4_day","factory4_night"})
        {
            Fixture(); _world.LocationId=map; System.IO.File.Delete(LayoutPath); Dom(Clear);
            _game.Location.SpawnPointParams=Enumerable.Range(0,20).Select(i=>Seed("dom-"+i,50+i*5,z:40)).ToArray();
            var rows=Newtonsoft.Json.JsonConvert.SerializeObject(_game.Location.SpawnPointParams);
            var layout=ArenaPickupLayouts.Select(ArenaPickupLayouts.Factory("spartan"),map,null);
            var kept=Pool(layout).Where(p=>Clear(new(p.X,p.Y,p.Z))).Select(p=>(p.X,p.Y,p.Z)).ToArray();
            ArenaPickupMapLayout.ExtendFactoryForObjectives(_game,layout,Clear); var pool=Pool(layout);
            AssertDom(pool.Length>8&&pool.Length<=16, "DOM adds enough bounded native candidates: "+map);
            AssertDom(pool.All(p=>Clear(new(p.X,p.Y,p.Z))), "DOM frozen candidate pool excludes objectives: "+map);
            AssertDom(kept.All(p=>pool.Any(q=>(q.X,q.Y,q.Z)==p)), "DOM preserves free reviewed positions: "+map);
            AssertDom(rows==Newtonsoft.Json.JsonConvert.SerializeObject(_game.Location.SpawnPointParams), "native spawn records stay read-only");
            Step(); Step();
            AssertDom(ArenaPickupView.Created.Count==6&&ArenaPickupView.Created.All(v=>Clear(v.Floor)),
                "all six types place using independent original Factory anchor: "+map);
            At(61);
            AssertDom(ArenaPickupView.Created.Where(v=>!v.Disposed).Count()==6
                && ArenaPickupView.Created.All(v=>Clear(v.Floor)), "all rotations preserve objective clearance: "+map);
        }

        Fixture(); _world.LocationId="laboratory"; System.IO.File.Delete(LayoutPath);
        _game.Location.SpawnPointParams=Enumerable.Range(0,10).Select(i=>Seed("ordinary-"+i,i*5)).ToArray();
        Step(); Step();
        AssertDom(typeof(ArenaPickupRuntime).GetField("_factoryAnchorLayout",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null)==null,
            "DOM reset cannot carry a Factory anchor into another map");
        AssertDom(ArenaPickupView.Created.Count==6, "ordinary next map still places all pickup types");

        Fixture(); WriteOne(0); ArenaMatchService.Current.Mode=new TeamDeathmatchMode{PickupPlacement=_=>false}; Step(); Step();
        AssertDom(ArenaPickupView.Created.Count==1, "ordinary TDM ignores objective exclusion");
        ArenaPickupRuntime.Reset();
        Console.WriteLine($"DOMINATION_PLACEMENT_BASELINE_PASSED checks={_domChecks}");
    }
}
