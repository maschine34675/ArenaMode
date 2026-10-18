using ArenaMode.Areas;
using ArenaMode.Pickups;
using UnityEngine;
using UnityEngine.AI;

internal static partial class Program
{
    private static ArenaPickupLayout FactoryAnchorLayout(int count=8)
        => new()
        {
            MapId="factory4_day",
            Points=Enumerable.Range(0,count).Select(i=>new ArenaPickupPoint
            {
                Id="anchor-"+i,Kind=ArenaPickupKind.DoubleDamage,X=i*10,Y=0,Z=0
            }).ToList()
        };

    private static void IsolatedFactory(string map="factory4_day")
    {
        Fixture();
        System.IO.File.Delete(LayoutPath);
        _world.LocationId=map;
        _local.Position=new(1000,0,0);
        // Real paths inside the pickup network succeed, but the actor's valid
        // NavMesh sample is on a completely disconnected player-only island.
        NavMesh.AllowPath=(from,to)=>from.x<100&&to.x<100;
    }

    private static void FactoryAnchorCases()
    {
        foreach(string map in new[]{"factory4_day","factory4_night"})
        {
            IsolatedFactory(map);Step();Step();
            var originals=ArenaPickupView.Created.Where(v=>!v.Disposed).ToDictionary(v=>v.Point.Id);
            Check(originals.Count==6&&originals.Values.Select(v=>v.Point.Kind).Distinct().Count()==6,
                "isolated local Factory spawn still places all six connected pickups: "+map);
            Check(originals.Values.All(v=>v.Floor.x<100),
                "Factory recovery never moves pickup placement onto the player's isolated island: "+map);
            At(61);
            var rotated=ArenaPickupView.Created.Where(v=>!v.Disposed).ToArray();
            Check(rotated.Length==6&&rotated.All(v=>(v.Floor-originals[v.Point.Id].Floor).sqrMagnitude>=9),
                "Factory rotation uses the same independent network while player remains isolated: "+map);
            var retained=rotated.ToDictionary(v=>v.Point.Id);
            NavMesh.PathOk=false;At(122);
            Check(ArenaPickupView.Created.Count(v=>!v.Disposed)==6
                &&retained.All(pair=>ReferenceEquals(pair.Value,ActiveView(pair.Key))),
                "lost Factory network retains existing appearances instead of using stale anchor success: "+map);
        }

        Fixture();var layout=FactoryAnchorLayout();
        Check(ArenaPickupGeometry.TryFactoryAnchor(layout,out var first),
            "connected distinct Factory pool elects an independent navigation anchor");
        Check(NavMesh.Paths<=64&&NavMesh.Samples<=264,
            "Factory anchor election stays within eight seeds and bounded directed connectivity probes");
        layout.Points.Reverse();
        Check(ArenaPickupGeometry.TryFactoryAnchor(layout,out var reversed)&&(first-reversed).sqrMagnitude==0,
            "Factory anchor election is independent of source position order");
        NavMesh.PathOk=false;
        Check(!ArenaPickupGeometry.TryFactoryAnchor(layout,out _),
            "Factory anchor election rechecks native paths on every call");

        foreach(string failure in new[]{"false","partial","self-only","one-way","split","collapsed","clearance","floor","steep","offlevel"})
        {
            Fixture();layout=FactoryAnchorLayout();
            switch(failure)
            {
                case "false":NavMesh.PathOk=false;break;
                case "partial":NavMesh.Status=NavMeshPathStatus.PathPartial;break;
                case "self-only":NavMesh.AllowPath=(from,to)=>(from-to).sqrMagnitude==0;break;
                case "one-way":NavMesh.AllowPath=(from,to)=>from.x<=to.x;break;
                case "split":NavMesh.AllowPath=(from,to)=>(from.x<40)==(to.x<40);break;
                case "collapsed":NavMesh.Sample=_=>new(0,0,0);break;
                case "clearance":Physics.Overlaps=[new Collider()];break;
                case "floor":Physics.Rays=(_,_,_)=>[];break;
                case "steep":Physics.Rays=(from,_,_)=>[new(){collider=new Collider(),point=from-Vector3.up*.65f,normal=new(1,0,0)}];break;
                case "offlevel":NavMesh.Sample=p=>p+Vector3.up;break;
            }
            Check(!ArenaPickupGeometry.TryFactoryAnchor(layout,out _),
                "Factory quorum cannot bypass native navigation or physical safety: "+failure);
        }

        Fixture();layout=FactoryAnchorLayout();
        NavMesh.AllowPath=(from,to)=>(from.x<50)==(to.x<50);
        Check(ArenaPickupGeometry.TryFactoryAnchor(layout,out var majority)&&majority.x<50,
            "Factory anchor belongs to the strict majority instead of a smaller disconnected component");
        Fixture();layout=FactoryAnchorLayout(2);
        Check(!ArenaPickupGeometry.TryFactoryAnchor(layout,out _),
            "two connected Factory positions are insufficient independent connectivity evidence");
        Fixture();layout=FactoryAnchorLayout(3);
        Check(ArenaPickupGeometry.TryFactoryAnchor(layout,out _),
            "three separated connected Factory positions meet minimum quorum");
        Fixture();layout=FactoryAnchorLayout(3);layout.Points[1].X=1;layout.Points[2].X=2;
        Check(!ArenaPickupGeometry.TryFactoryAnchor(layout,out _),
            "nearby Factory positions cannot inflate the three-spatially-distinct quorum");

        foreach(string map in new[]{"laboratory","labyrinth","arena_bay5","bigmap"})
        {
            Fixture();layout=FactoryAnchorLayout();layout.MapId=map;
            Check(!ArenaPickupGeometry.TryFactoryAnchor(layout,out _)&&NavMesh.Paths==0,
                "Factory helper never supplies anchor authority to another map: "+map);
        }
        Fixture();layout=FactoryAnchorLayout();layout.AreaId="bounded";
        Check(!ArenaPickupGeometry.TryFactoryAnchor(layout,out _)&&NavMesh.Paths==0,
            "Factory helper never substitutes whole-map connectivity for an authored area");

        IsolatedFactory();NavMesh.AllowPath=(from,to)=>from.x==1000;Step();Step(16);
        Check(ArenaPickupView.Created.Count==0,
            "failed built-in Factory quorum cannot fall back to the isolated local anchor");
        IsolatedFactory();_local.MovementContext.IsGrounded=false;Step();
        Check(ArenaPickupView.Created.Count==0&&NavMesh.Paths==0,
            "independent Factory anchor retains grounded live-body readiness");
        _local.MovementContext.IsGrounded=true;Step(.3f);
        Check(ArenaPickupView.Created.Count==6,
            "Factory setup resumes after grounded body readiness without requiring local connectivity");

        IsolatedFactory();
        System.IO.File.WriteAllText(LayoutPath,ArenaPickupLayouts.Serialize(ArenaPickupLayouts.Factory("spartan")));
        Step();Step(16);
        Check(ArenaPickupView.Created.Count==0,
            "custom Factory layouts retain authored local connectivity even with the same eight positions");
        IsolatedFactory("laboratory");
        _game.Location.SpawnPointParams=Enumerable.Range(0,8).Select(i=>Seed("native-"+i,i*10)).ToArray();
        Step();Step(16);
        Check(ArenaPickupView.Created.Count==0,
            "non-Factory generated layouts retain their original local navigation authority");
        IsolatedFactory();var areaFile=FileFor();areaFile.Layouts[0].AreaId="bounded";
        ArenaAreaRuntime.CurrentArea=new(){Id="bounded"};
        System.IO.File.WriteAllText(LayoutPath,ArenaPickupLayouts.Serialize(areaFile));Step();Step(16);
        Check(ArenaPickupView.Created.Count==0,
            "bounded custom Factory layout cannot inherit full-map independent placement");
        Console.WriteLine("ARENA_FACTORY_PICKUP_ANCHOR_PASSED");
    }
}
