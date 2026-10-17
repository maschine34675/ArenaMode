using ArenaMode.Areas;
using ArenaMode.Modes;
using ArenaMode.Pickups;
using ArenaMode.Shared;
using EFT.Game.Spawning;
using Newtonsoft.Json.Linq;
using UnityEngine.AI;

internal static partial class Program
{
    private static SpawnPointParams Seed(string id,float x,float y=0,float z=0,string zone="ordinary",ESpawnCategoryMask category=ESpawnCategoryMask.Bot)
        =>new(){Id=id,Position=new(){x=x,y=y,z=z},BotZoneName=zone,Categories=category};
    private static ArenaPickupPoint[] Pool(ArenaPickupLayout layout)=>layout.Points
        .SelectMany(p=>ArenaPickupLayouts.Candidates(p,new Random(1))).GroupBy(p=>(p.X,p.Y,p.Z)).Select(g=>g.First()).ToArray();
    private static void SetArea(ArenaAreaDefinition area)
        =>ArenaAreaRuntime.CurrentArea=new(){Id=area.Id,Native=area,Bounds=new(){Native=area.Bounds}};

    private static void AllMapCases()
    {
        Fixture();
        var factory=ArenaPickupLayouts.Serialize(ArenaPickupLayouts.Factory("spartan"));
        Check(ArenaPickupLayouts.Serialize(ArenaPickupMapLayout.Create(_game,"factory4_day",null,"spartan"))==factory,
            "new map support preserves both complete reviewed Factory layouts");
        _game.Location.SpawnPointParams=Enumerable.Range(0,40).Select(i=>Seed("native-"+i,(i%8)*8,0,(i/8)*8)).ToArray();
        var original=_game.Location.SpawnPointParams;
        string snapshot=Newtonsoft.Json.JsonConvert.SerializeObject(original);
        foreach(string map in new[]{"laboratory","labyrinth","arena_equator","arena_bay5","arena_airpit"})
        {
            var layout=ArenaPickupMapLayout.Create(_game,map,null,"chosen-kit").Layouts.Single();
            Check(layout.Points.Count==6&&layout.Points.Select(p=>p.Kind).Distinct().Count()==6,"all supported full maps retain all six pickup types: "+map);
            var pool=Pool(layout);
            Check(pool.Length<=16&&pool.Length>=6,"new-map pool bounded with enough separated candidates: "+map);
            Check(pool.All(p=>original.Any(s=>s.Position.x==p.X&&s.Position.y==p.Y&&s.Position.z==p.Z)),"new candidates are native coordinates, never invented: "+map);
            Check(pool.All(p=>pool.All(q=>ReferenceEquals(p,q)||(p.X-q.X)*(p.X-q.X)+(p.Z-q.Z)*(p.Z-q.Z)>=9f)),"native pool avoids adjacent clusters: "+map);
            Check(layout.Points.Single(p=>p.Kind==ArenaPickupKind.Weapon).KitId=="chosen-kit","new maps preserve selected weapon kit");
        }
        Check(snapshot==Newtonsoft.Json.JsonConvert.SerializeObject(original),"native source records are never mutated");
        var forward=ArenaPickupLayouts.Serialize(ArenaPickupMapLayout.Create(_game,"laboratory",null,"spartan"));
        _game.Location.SpawnPointParams=original.Reverse().ToArray();
        Check(forward==ArenaPickupLayouts.Serialize(ArenaPickupMapLayout.Create(_game,"laboratory",null,"spartan")),"native backend row order cannot change frozen pool selection");
        Check(ArenaPickupMapLayout.Create(_game,"bigmap",null,"spartan").Layouts.Count==0,"whole Customs never receives an implicit layout");
        ArenaAreaRuntime.CurrentArea=new(){Id="unknown-area"};
        Check(ArenaPickupMapLayout.Create(_game,"bigmap","unknown-area","spartan").Layouts.Count==0,"unknown Customs contours cannot opt into automatic layouts");
        ArenaAreaRuntime.CurrentArea=null;
        Check(ArenaPickupMapLayout.Create(_game,"woods",null,"spartan").Layouts.Count==0,"unsupported maps never receive generated pickups");
        Check(ArenaPickupMapLayout.Create(_game,"laboratory","foreign-area","spartan").Layouts.Count==0,"full map never substitutes for an unknown area");
        _game.Location.SpawnPointParams=[Seed("gate1",0,zone:"BotZoneGate1"),Seed("gate2",5,zone:"BotZoneGate2"),
            Seed("sniper",10,zone:"ZoneSnipeFactory"),Seed("player",15,category:ESpawnCategoryMask.Player),Seed("bad",float.NaN),
            Seed("safe",20),Seed("duplicate",20),Seed("pmc",30,category:ESpawnCategoryMask.BotPmc)];
        var filtered=Pool(ArenaPickupMapLayout.Create(_game,"laboratory",null,"spartan").Layouts.Single());
        Check(filtered.Length==2&&filtered.All(p=>p.X==20||p.X==30),"sealed Labs gates, snipers, player entries and malformed seeds cannot become pickups");
        foreach(var formation in new[]{ArenaTeamBoxCatalog.Equator,ArenaTeamBoxCatalog.Bay5,ArenaTeamBoxCatalog.AirPit})
        {
            var booth=formation.Points[0];
            _game.Location.SpawnPointParams=[Seed(booth.Id,booth.X,booth.Y,booth.Z),Seed("generic-in-box",booth.X,booth.Y,booth.Z),
                Seed("common-safe",0,0,10)];
            var pool=Pool(ArenaPickupMapLayout.Create(_game,formation.MapId,null,"spartan").Layouts.Single());
            Check(pool.Length==1&&pool[0].X==0&&pool[0].Z==10,"team-box identity and generic in-box positions excluded: "+formation.MapId);
        }
        _game.Location.SpawnPointParams=[Seed("near-box",0,.1f,39),Seed("safe",0,.1f,10)];
        Check(Pool(ArenaPickupMapLayout.Create(_game,"arena_equator",null,"spartan").Layouts.Single()).Length==1,
            "built-in repair margin cannot enter a neighboring team box");
        Fixture();_world.LocationId="laboratory";System.IO.File.Delete(LayoutPath);
        _game.Location.SpawnPointParams=Enumerable.Range(0,10).Select(i=>Seed("live-"+i,i*5)).ToArray();
        Step();Step();Check(ArenaPickupView.Created.Count==6,"new-map runtime places six pickups through normal live geometry gates");
        _game.Location.SpawnPointParams=[Seed("replacement",999)];Dm.Round.Number++;Step();
        Check(ArenaPickupView.Created.Count==12&&ArenaPickupView.Created.All(v=>v.Floor.x!=999),"derived layout stays frozen across rounds despite native record replacement");
        Fixture();_world.LocationId="laboratory";var custom=FileFor();custom.Layouts[0].MapId="laboratory";
        System.IO.File.WriteAllText(LayoutPath,ArenaPickupLayouts.Serialize(custom));
        _game.Location.SpawnPointParams=Enumerable.Range(0,10).Select(i=>Seed("ignored-"+i,i*5)).ToArray();Step();Step();
        Check(ArenaPickupView.Created.Count==1&&ArenaPickupView.Created[0].Point.Id=="one","custom layout replaces generated layouts without augmentation");
        Fixture();_world.LocationId="labyrinth";System.IO.File.Delete(LayoutPath);
        _game.Location.SpawnPointParams=Enumerable.Range(0,10).Select(i=>Seed("blocked-"+i,i*5)).ToArray();NavMesh.SampleOk=false;Step(16);Step(16);
        Check(ArenaPickupView.Created.Count==0,"new-map candidates cannot bypass failing live navigation");
    }

    private static void NativeAllMapCandidates(string root)
    {
        SpawnPointParams[] Read(string path)
        {
            var json=JObject.Parse(System.IO.File.ReadAllText(path));
            return json["SpawnPointParams"].Select(row=>Seed((string)row["Id"],(float)row["Position"]["x"],
                (float)row["Position"]["y"],(float)row["Position"]["z"],(string)row["BotZoneName"],
                row["Categories"].Aggregate(ESpawnCategoryMask.None,(mask,c)=>mask|Enum.Parse<ESpawnCategoryMask>((string)c)))).ToArray();
        }
        foreach(string map in new[]{"laboratory","labyrinth","arena_equator","arena_bay5","arena_airpit"})
        {
            string path=map.StartsWith("arena_")?Path.Combine(root,"SPT_Runtime/user/mods/ArenaMapPort/db",map,"base.json")
                :Path.Combine(root,"SPT_Runtime/SPT_Data/database/locations",map,"base.json");
            if(!System.IO.File.Exists(path))throw new Exception("Native map fixture missing: "+path);
            Fixture();_game.Location.SpawnPointParams=Read(path);
            var layout=ArenaPickupMapLayout.Create(_game,map,null,"spartan").Layouts.Single();
            Check(Pool(layout).Length>=6,"installed map has at least six separated native pickup candidates: "+map);
            Console.WriteLine($"ALL_MAP_NATIVE_CANDIDATES: {map}: {Pool(layout).Length}; live geometry still required");
        }
        var customs=Read(Path.Combine(root,"SPT_Runtime/SPT_Data/database/locations/bigmap/base.json"));
        foreach(var area in new[]{ArenaAreaCatalog.CustomsFactoryCenterTeams,ArenaAreaCatalog.CustomsFactoryEastPolygon,ArenaAreaCatalog.CustomsYard})
        {
            Fixture();SetArea(area);
            // Same explicit extra rows admitted to the selected server clone;
            // the generator itself neither creates nor knows these coordinates.
            var extras=area.Id==ArenaAreaCatalog.CustomsYardId?ArenaAreaCatalog.CustomsYardSpawns
                .Select(p=>Seed(p.Id,p.Position.X,p.Position.Y,p.Position.Z,ArenaAreaSpawnAddition.Zone,
                    p.Player?ESpawnCategoryMask.Player:ESpawnCategoryMask.Bot)).ToArray():[];
            _game.Location.SpawnPointParams=customs.Concat(extras).ToArray();
            var layout=ArenaPickupMapLayout.Create(_game,"bigmap",area.Id,"spartan").Layouts.Single();
            var pool=Pool(layout);
            Check(pool.Length>=6,"selected Customs contour has at least six separated native candidates: "+area.Id);
            Check(pool.All(p=>area.Bounds.Contains(p.X,p.Y,p.Z,.5f)),"every generated Customs candidate stays inside exact selected polygon: "+area.Id);
            Console.WriteLine($"ALL_MAP_NATIVE_CANDIDATES: {area.Id}: {pool.Length}; live geometry still required");
        }
        Fixture();
    }
}
