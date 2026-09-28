using ArenaMode.Shared;
using ArenaMode.Session;
using System.Text.Json;
using System.Security.Cryptography;

int checks=0;
void Need(bool ok,string code){checks++;if(!ok)throw new Exception(code);}
try
{
    string fixtures=args[0],database=args[1];
    using var native=JsonDocument.Parse(File.ReadAllText(database));
    var entries=native.RootElement.GetProperty("SpawnPointParams").EnumerateArray().Select(p=>new Entry(
        p.GetProperty("Id").GetString()!,p.GetProperty("Position").GetProperty("x").GetSingle(),
        p.GetProperty("Position").GetProperty("y").GetSingle(),p.GetProperty("Position").GetProperty("z").GetSingle(),
        p.GetProperty("Categories").EnumerateArray().Select(x=>x.GetString()!).ToArray(),
        p.GetProperty("Sides").EnumerateArray().Select(x=>x.GetString()!).ToArray(),p.GetProperty("BotZoneName").GetString()!)).ToArray();
    var specs=new[]{(ArenaAreaCatalog.CustomsFactoryCenterTeams,"172827",40,29),
        (ArenaAreaCatalog.CustomsFactoryEastPolygon,"172225",32,34),(ArenaAreaCatalog.CustomsYard,"173324",28,12)};
    var ids=specs.Select(x=>x.Item1.Id).ToArray();
    foreach(var (area,time,count,nativeCount) in specs)
    {
        var file=Directory.GetFiles(fixtures,"*20260928-"+time+"*").Single();
        using var draft=JsonDocument.Parse(File.ReadAllBytes(file));
        var raw=draft.RootElement.GetProperty("geometry").GetProperty("vertices").EnumerateArray()
            .Select(p=>new ArenaAreaVertex(p.GetProperty("x").GetSingle(),p.GetProperty("z").GetSingle())).ToArray();
        Need(raw.Length==count&&area.Bounds.Outline.Count==count,"VERTEX-COUNT");
        for(int i=0;i<count;i++) Need(raw[i].X==area.Bounds.Outline[i].X&&raw[i].Z==area.Bounds.Outline[i].Z,"EXACT-CONTOUR");
        Need(!area.Bounds.LimitsHeight&&area.MaxTeamSize==5&&area.TeamStarts!=null,"AREA-CAPACITY");
        for(int xi=0;xi<=20;xi++)for(int zi=0;zi<=20;zi++)
        {
            float x=area.Bounds.MinX-1+xi*(area.Bounds.MaxX-area.Bounds.MinX+2)/20;
            float z=area.Bounds.MinZ-1+zi*(area.Bounds.MaxZ-area.Bounds.MinZ+2)/20;
            foreach(float y in new[]{-10000f,1f,10000f})
                Need(area.Bounds.Contains(x,y,z)==Inside(raw,x,z),"INDEPENDENT-CONTAINMENT");
        }
        var points=entries.Where(e=>area.AllowsSpawn(e.X,e.Y,e.Z,e.Id)).ToList();
        Need(points.Count==nativeCount,"NATIVE-CORPUS");
        if(area.Id==ArenaAreaCatalog.CustomsYardId)
        {
            Need(ArenaAreaCatalog.CustomsYardSpawns.Count==20,"EXTRA-COUNT");
            foreach(var p in ArenaAreaCatalog.CustomsYardSpawns)
            {
                var donor=entries.Single(e=>e.Id==p.SourceId);
                Need(donor.X==p.SourcePosition.X&&donor.Y==p.SourcePosition.Y&&donor.Z==p.SourcePosition.Z
                    &&donor.Zone=="ZoneCustoms"&&donor.Categories.Any(c=>c is "Bot" or "BotPmc"),"NATIVE-DONOR");
                Need(area.AllowsSpawn(p.Position.X,p.Position.Y,p.Position.Z,p.Id),"EXTRA-CONTAINMENT");
                Need(Math.Abs(p.Position.X-donor.X)<=4&&Math.Abs(p.Position.Z-donor.Z)<=6&&p.Position.Y==donor.Y,"COMPACT-GROUND-CANDIDATE");
                Need(!entries.Any(e=>e.Id==p.Id),"EXTRA-ID-COLLISION");
                points.Add(new Entry(p.Id,p.Position.X,p.Position.Y,p.Position.Z,new[]{p.Player?"Player":"BotPmc"},new[]{"Pmc"},"ZoneCustoms"));
            }
        }
        var sectors=new List<Entry[]>();
        foreach(bool usec in new[]{true,false})
        {
            var pmcs=points.Where(e=>area.TeamStarts.Allows(usec,true,e.Id)).ToArray();
            var bots=points.Where(e=>area.TeamStarts.Allows(usec,false,e.Id)).ToArray();
            Need(pmcs.Length>=1&&bots.Length>=5,"START-CAPACITY");
            Need(pmcs.All(e=>e.Categories.Contains("Player")&&e.Sides.Any(v=>v is "All" or "Pmc"||v==(usec?"Usec":"Bear"))),"PMC-CATEGORY");
            Need(bots.All(e=>e.Categories.Any(v=>v is "Bot" or "BotPmc")&&!string.IsNullOrEmpty(e.Zone)),"BOT-CATEGORY");
            for(int i=0;i<bots.Length;i++) foreach(var other in bots.Skip(i+1).Concat(pmcs)) Need(Distance(bots[i],other)>=1.5,"START-SPACING");
            sectors.Add(pmcs.Concat(bots).ToArray());
        }
        double separation=sectors[0].Min(a=>sectors[1].Min(b=>Distance(a,b)));
        Need(separation>=25,"OPPOSING-SPACING");
        Console.WriteLine($"{area.Id}: {count} corners, {nativeCount} native candidates; USEC {sectors[0].Length}, BEAR {sectors[1].Length}, opposing {separation:F3}m");
        for(int size=2;size<=5;size++)
        {
            Need(ArenaAreaCatalog.TryResolve(area.Id,"bigmap","teamdeathmatch",false,out var resolved,out _)&&ReferenceEquals(resolved,area),"RESOLVE");
            Need(ArenaAreaCatalog.TryResolveTeamSize(area,size,out int selected,out _)&&selected==size,"TEAM-SIZE");
            var state=ArenaAreaSelectionState.Create("bigmap","teamdeathmatch",false,area.Id,12,size);
            Need(state.Options.Select(o=>o.Id).SequenceEqual(ids)&&state.SelectedId==area.Id&&state.SelectedTeamSize==size,"UNIFIED-MENU");
            Need(state.Options.Select(o=>o.Label).Distinct().Count()==3&&!state.Options.Any(o=>o.Label.Contains("team starts")||o.Label.Contains("contour")),"UNIFIED-LABELS");
        }
        foreach(string mode in new[]{"deathmatch","gungame","tactical"}) Need(!ArenaAreaCatalog.TryResolve(area.Id,"bigmap",mode,false,out _,out _),"UNSUPPORTED-MODE");
        Need(!ArenaAreaCatalog.TryResolve(area.Id,"bigmap","teamdeathmatch",true,out _,out _),"PFT-REJECTED");
        Need(!ArenaAreaCatalog.TryResolve(area.Id,"factory4_day","teamdeathmatch",false,out _,out _),"WRONG-MAP");
    }
    foreach(string id in new[]{"customs-factory-center-polygon-v1","customs-factory-center-teams-v1","customs-factory-east-polygon-v1","customs-factory-center-v1","factory-south-v1","full-map"})
        Need(!ArenaAreaCatalog.TryResolve(id,"bigmap","teamdeathmatch",false,out _,out _),"RETIRED-WIRE");
    Console.WriteLine($"CUSTOMS AREA REFRESH PRODUCTION PASSED ({checks} assertions)"); return 0;
}
catch(Exception ex){Console.Error.WriteLine("CUSTOMS AREA REFRESH REJECTED: "+ex.Message);return 1;}
static double Distance(Entry a,Entry b)=>Math.Sqrt(Math.Pow((double)a.X-b.X,2)+Math.Pow((double)a.Z-b.Z,2));
static bool Inside(ArenaAreaVertex[] vertices,double x,double z)
{
    bool inside=false;
    for(int i=0,j=vertices.Length-1;i<vertices.Length;j=i++)
    {
        var a=vertices[j];var b=vertices[i];
        double cross=(b.X-(double)a.X)*(z-a.Z)-(b.Z-(double)a.Z)*(x-a.X);
        if(cross==0&&x>=Math.Min(a.X,b.X)&&x<=Math.Max(a.X,b.X)&&z>=Math.Min(a.Z,b.Z)&&z<=Math.Max(a.Z,b.Z))return true;
        if((a.Z>z)!=(b.Z>z)&&x<(b.X-(double)a.X)*(z-a.Z)/(b.Z-(double)a.Z)+a.X)inside=!inside;
    }
    return inside;
}
record Entry(string Id,float X,float Y,float Z,string[] Categories,string[] Sides,string Zone);
