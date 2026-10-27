using ArenaMode.Areas;
using ArenaMode.Bots;
using ArenaMode.Hud;
using ArenaMode.Modes;
using ArenaMode.Pickups;
using ArenaMode.Shared;
using EFT;
using UnityEngine;
using UnityEngine.AI;

int checks = 0;
void Need(bool value, string reason) { checks++; if (!value) throw new Exception("FAIL: " + reason); }
bool Same(Vector3 a, Vector3 b) => Math.Abs(a.x-b.x) < .0001f && Math.Abs(a.y-b.y) < .0001f && Math.Abs(a.z-b.z) < .0001f;
void Refuses(Action action, string reason)
{
    bool rejected = false;
    try { action(); } catch (InvalidOperationException) { rejected = true; }
    Need(rejected, reason);
}
Vector3 Position(ArenaTeamBoxCatalog.Point point) => new(point.X, point.Y, point.Z);
void Reset()
{
    ArenaAreaRuntime.CurrentArea = null;
    ArenaPickupGeometry.FactoryOk = true; ArenaPickupGeometry.FactoryCalls = 0;
    ArenaPickupGeometry.Placements.Clear();
    ArenaPickupGeometry.Place = (point, _) => (true, point);
    ArenaPickupGeometry.Sight = (_, _) => true;
    NavMesh.Sample = point => (true, point);
    NavMesh.Route = (_, _) => (true, NavMeshPathStatus.PathComplete);
    NavMesh.Routes.Clear(); NavMesh.Samples.Clear();
    DominationPointView.Created.Clear(); DominationPointView.ThrowAt = -1;
    DominationObjectiveBridge.Calls = 0; DominationObjectiveBridge.Points = null; DominationObjectiveBridge.Bots = null;
}
(DominationRuntime Runtime, DominationMatchPolicy Rules, TeamDeathmatchMode Mode) Create(string map)
{
    var mode = new TeamDeathmatchMode();
    var rules = new DominationMatchPolicy(scoreLimit: 0, pointCount: DominationMapLayout.Resolve(map).Points.Count);
    return (new DominationRuntime(mode, rules, new GameWorld { LocationId = map }), rules, mode);
}
var cases = new Dictionary<string, (string Id, Vector3 Position)[]>
{
    ["arena_equator"] = new[] { ("X", new Vector3(1.28f, .25f, 4.22f)) },
    ["arena_bay5"] = new[] { ("X", new Vector3(91.26f, -4.366f, 29.44f)) },
    ["arena_airpit"] = new[] { ("A", new Vector3(18.96f, -.1f, -1.24f)), ("B", new Vector3(-45.38f, 0f, 3.82f)) },
    ["arena_bowl"] = new[] { ("A", new Vector3(-20.51f, 0f, -37.12f)), ("B", new Vector3(16.99f, -.1f, -30.72f)) },
    ["factory4_day"] = new[] { ("A", new Vector3(-5.89f, .08f, -27.46f)), ("B", new Vector3(-6.77f, .17f, 10.01f)),
        ("C", new Vector3(-20.756f, .166f, 22.769f)) }
};
cases["factory4_night"] = cases["factory4_day"];
foreach (var (map, authored) in cases)
{
    var expected = authored;
    Reset();
    bool factory = map.StartsWith("factory");
    var layout = DominationMapLayout.Resolve(map);
    Need(layout.Points.Count == expected.Length && layout.Factory == factory, "authored map counts and Factory identity remain exact");
    Need(ReferenceEquals(layout, DominationMapLayout.Resolve(map.ToUpperInvariant())), "map identity handles case without changing layout");
    bool immutable = false;
    try { ((IList<DominationMapLayout.Point>)layout.Points)[0] = default; }
    catch (NotSupportedException) { immutable = true; }
    Need(immutable, "authored layout cannot be mutated through its view");
    var (runtime, rules, mode) = Create(map);
    Need(!runtime.AllowsPickup(new(1000,0,1000)), "unprepared layout cannot authorize pickup space");
    Refuses(runtime.VerifyTeamPaths, "team routes require preparation first");
    runtime.Prepare();
    if (factory) expected = DominationPointView.Created.Select(v => (v.Id, v.Floor)).ToArray();
    Need(DominationPointView.Created.Count == expected.Length, "preparation creates exactly the authored marker count");
    Need(ArenaPickupGeometry.FactoryCalls == (factory ? 1 : 0), "only Factory uses the reviewed Factory navigation authority");
    Need(ArenaPickupGeometry.Placements.All(p => p.AllowNearby == factory), "ports forbid nearby relocation while Factory retains it");
    Vector3 anchor = factory ? ArenaPickupGeometry.FactoryAnchor : Position(ArenaTeamBoxCatalog.Find(map).Destination);
    Need(ArenaPickupGeometry.Placements.All(p => Same(p.Anchor, anchor)), "survey uses the map court anchor rather than a closed booth");
    for (int index = 0; index < expected.Length; index++)
    {
        var point = expected[index]; var view = DominationPointView.Created[index];
        Need(layout.Points[index].Id == point.Id && (factory || Same(layout.Points[index].Position, point.Position)),
            "individual source anchors and labels are preserved instead of aggregate centroids");
        Need((factory || Same(ArenaPickupGeometry.Placements[index].Requested, point.Position)) && Same(view.Floor, point.Position)
            && view.Id == point.Id && view.Radius == 4f, "runtime uses the individual fixed anchor and its original label");
        Need(NavMesh.Routes.Any(r => Same(r.From, point.Position) && Same(r.To, anchor))
            && NavMesh.Routes.Any(r => Same(r.From, anchor) && Same(r.To, point.Position)),
            "survey requires complete routes in both directions");
        Need(!runtime.AllowsPickup(point.Position) && !runtime.AllowsPickup(point.Position + new Vector3(6,0,0)),
            "every prepared point reserves its full pickup clearance");
    }
    Need(NavMesh.Samples.All(s => s.Range == .75f && s.Areas == NavMesh.AllAreas), "navigation sampling remains bounded");
    Need(runtime.AllowsPickup(new(1000,0,1000)), "prepared layout permits distant pickup positions");
    Refuses(runtime.Prepare, "prepared layout cannot allocate duplicate markers");
    int preparationRoutes = NavMesh.Routes.Count;
    if (factory)
    {
        runtime.VerifyTeamPaths();
        Need(NavMesh.Routes.Count == preparationRoutes, "Factory does not invent ported booth routes");
    }
    else
    {
        var formation = ArenaTeamBoxCatalog.Find(map);
        var usec = Position(formation.BoothAnchor(true)); var bear = Position(formation.BoothAnchor(false));
        NavMesh.Route = (from, to) => (!(Same(from,usec) || Same(from,bear) || Same(to,usec) || Same(to,bear)), NavMeshPathStatus.PathComplete);
        Refuses(runtime.VerifyTeamPaths, "closed booth route cannot be authorized before gates reconnect");
        NavMesh.Route = (_, _) => (true, NavMeshPathStatus.PathComplete); NavMesh.Routes.Clear();
        runtime.VerifyTeamPaths();
        foreach (var booth in new[] { usec, bear })
        foreach (var point in expected)
            Need(NavMesh.Routes.Any(r => Same(r.From,booth) && Same(r.To,point.Position))
                && NavMesh.Routes.Any(r => Same(r.From,point.Position) && Same(r.To,booth)),
                "opened route proof covers both factions and every authored point in both directions");
        int verifiedRoutes = NavMesh.Routes.Count;
        runtime.VerifyTeamPaths();
        Need(NavMesh.Routes.Count == verifiedRoutes, "successful booth proof is frozen only after all routes pass");
    }
    rules.UpdateStart(0, true); rules.UpdateStart(3, true);
    var local = new Player { Position = expected[0].Position };
    var bots = expected.Skip(1).Select(p => new BotOwner { GetPlayer = new Player { Position = p.Position, Friendly = false } }).ToList();
    bots.Add(new BotOwner { GetPlayer = new Player { Position = expected[0].Position, Friendly = false, Eligible = false } });
    runtime.Tick(local, bots, true, 8);
    Need(rules.Points.Count == expected.Length && rules.Points[0].OwnerUsec == true
        && rules.Points.Skip(1).All(p => p.OwnerUsec == false), "runtime counts and captures each authored point independently");
    Need(DominationObjectiveBridge.Points.Select(p => p.Id).SequenceEqual(expected.Select(p => p.Id))
        && DominationObjectiveBridge.Bots.Length == expected.Length-1 && DominationObjectiveBridge.Live,
        "bot bridge receives actual point labels and only classified physical bots");
    Need(expected.All(p => runtime.StatusLine.Contains(p.Id + ":")), "status line includes every actual objective label");
    int rendered = DominationPointView.Created.Sum(v => v.Ticks), published = DominationObjectiveBridge.Calls;
    runtime.Dispose(); runtime.Dispose();
    Need(DominationPointView.Created.All(v => v.Disposed) && !runtime.AllowsPickup(new(1000,0,1000)),
        "dispose removes all owned markers and pickup authority");
    runtime.Tick(local, bots, true, 9);
    Need(DominationPointView.Created.Sum(v => v.Ticks) == rendered && DominationObjectiveBridge.Calls == published,
        "disposed runtime cannot render or publish objectives");
    Refuses(runtime.VerifyTeamPaths, "disposed runtime cannot verify a new route");
    Refuses(runtime.Prepare, "disposed runtime cannot recreate markers");
}

foreach (var map in new string[] { null, "", "laboratory", "bigmap", "arena_unknown" })
{
    Refuses(() => DominationMapLayout.Resolve(map), "unknown map has no invented objective layout");
    Refuses(() => new DominationRuntime(new(), new(), new() { LocationId = map }), "runtime rejects unknown maps before preparation");
}
Reset();
Refuses(() => new DominationRuntime(new(), new(pointCount: 3), new() { LocationId = "arena_airpit" }),
    "runtime rejects policy and authored count mismatch before allocation");
Need(DominationPointView.Created.Count == 0, "constructor mismatch never allocates markers");

// Even the last failing point must produce no partial presentation. Test real
// runtime ordering against controlled native floor/path responses.
foreach (var (map, expected) in cases.Where(c => !c.Key.StartsWith("factory")))
foreach (string fault in new[] { "floor", "sample", "height", "path-false", "path-partial", "return-false", "return-partial" })
{
    Reset(); var (runtime, _, _) = Create(map); var last = expected.Last().Position;
    if (fault == "floor") ArenaPickupGeometry.Place = (point, _) => (!Same(point,last),point);
    if (fault == "sample") NavMesh.Sample = point => (!Same(point,last),point);
    if (fault == "height") NavMesh.Sample = point => (true,Same(point,last) ? point + new Vector3(0,.51f,0) : point);
    if (fault.StartsWith("path")) NavMesh.Route = (from, _) => (fault != "path-false" || !Same(from,last),
        fault == "path-partial" && Same(from,last) ? NavMeshPathStatus.PathPartial : NavMeshPathStatus.PathComplete);
    if (fault.StartsWith("return")) NavMesh.Route = (_, to) => (fault != "return-false" || !Same(to,last),
        fault == "return-partial" && Same(to,last) ? NavMeshPathStatus.PathPartial : NavMeshPathStatus.PathComplete);
    Refuses(runtime.Prepare, "failed candidate or directed path rejects complete layout");
    Need(DominationPointView.Created.Count == 0 && !runtime.AllowsPickup(new(1000,0,1000)),
        "failed final point leaves no partial markers or pickup authority");
    runtime.Dispose();
}
foreach (string map in new[] { "arena_equator", "arena_bay5", "arena_airpit", "arena_bowl" })
{
    Reset(); var (runtime, _, _) = Create(map);
    ArenaPickupGeometry.Place = (point, nearby) => (nearby, point + new Vector3(1,0,0));
    Refuses(runtime.Prepare, "ported obstruction cannot be repaired by moving its authored objective");
    Need(DominationPointView.Created.Count == 0, "ported relocation failure leaves no markers");
}
Reset();
var factoryRepair = Create("factory4_day").Runtime;
ArenaPickupGeometry.Place = (point, nearby) => (nearby, point + new Vector3(.1f,0,0));
factoryRepair.Prepare();
Need(DominationPointView.Created.Count == 3, "Factory retains its existing bounded placement repair");
factoryRepair.Dispose();
// Randomization chooses only scene-verified final floors. Bad candidates are
// skipped, never used as an excuse to weaken the existing path proof.
foreach (bool day in new[] { true, false })
{
    Reset();
    ArenaPickupGeometry.Place = (point, _) => (point.z < 0, point + new Vector3(.1f,0,0));
    var varied = Create(day ? "factory4_day" : "factory4_night");
    varied.Runtime.Prepare();
    Need(DominationPointView.Created.Count == 3 && DominationPointView.Created.All(v => v.Floor.z < 0),
        "Factory picks only surviving surveyed floors");
    var before = DominationPointView.Created.Select(v => v.Floor).ToArray();
    varied.Rules.UpdateStart(0,true);varied.Rules.UpdateStart(3,true);
    varied.Runtime.Tick(new Player(), new List<BotOwner>(), true, 8);
    varied.Runtime.Tick(new Player(), new List<BotOwner>(), true, 9);
    Need(DominationPointView.Created.Select(v => v.Floor).SequenceEqual(before), "Factory points stay fixed across body replacements and ticks");
    varied.Runtime.Dispose();
}
Reset();ArenaPickupGeometry.Place = (point, _) => (false, point);
Refuses(Create("factory4_day").Runtime.Prepare, "Factory refuses an unsurveyable candidate pool");
Need(DominationPointView.Created.Count == 0, "Factory failed pool leaves no partial markers");
var pool = new[] { new Vector3(0,0,0),new Vector3(20,0,0),new Vector3(0,0,20),new Vector3(20,0,20),new Vector3(1,0,0) };
var repeatedSeed = DominationMapLayout.SelectFactory(pool,new Random(7),null);
var nextSameSeed = DominationMapLayout.SelectFactory(pool,new Random(7),repeatedSeed);
Need(!nextSameSeed.All(p=>repeatedSeed.Contains(p)), "Factory avoids repeating the previous physical triple");
Vector3[] previous = null;
var draws = new HashSet<string>();
for (int seed=0;seed<50;seed++)
{
    var selected = DominationMapLayout.SelectFactory(pool,new Random(seed),previous);
    Need(selected.Length==3&&selected.All(p=>pool.Contains(p)), "Factory triple consists of surveyed inputs only");
    for(int a=0;a<3;a++)for(int b=a+1;b<3;b++)
        Need(Math.Pow(selected[a].x-selected[b].x,2)+Math.Pow(selected[a].z-selected[b].z,2)>=144,
            "Factory objectives keep twelve metres horizontal separation");
    Need(previous==null||!selected.All(p=>previous.Contains(p)), "Factory avoids repeating the previous physical triple");
    draws.Add(string.Join("|",selected.Select(p=>p.ToString())));previous=selected;
}
Need(draws.Count>1,"Factory selection varies across match seeds");
Refuses(()=>DominationMapLayout.SelectFactory(new[]{pool[0],pool[4],new Vector3(2,0,0)},new Random(1),null),
    "Factory refuses overlapping objectives instead of reducing separation");
Reset(); ArenaPickupGeometry.FactoryOk = false;
Refuses(Create("factory4_night").Runtime.Prepare, "Factory still requires its surveyed main network");
Need(DominationPointView.Created.Count == 0, "missing Factory network cannot create markers");
Reset(); ArenaAreaRuntime.CurrentArea = new object();
Refuses(Create("arena_equator").Runtime.Prepare, "bounded area cannot borrow a full map objective layout");

// Gate proof runs later than Prepare. Failed proof must remain retryable after
// gates reconnect, and no faction/direction may be silently skipped.
foreach (string fault in new[] { "sample-start", "sample-end", "height-start", "height-end", "forward-false", "return-false", "forward-partial", "return-partial" })
{
    Reset(); var (runtime, _, _) = Create("arena_bowl"); runtime.Prepare();
    var start = Position(ArenaTeamBoxCatalog.Bowl.BoothAnchor(false));
    var finish = cases["arena_bowl"][1].Position;
    if (fault.StartsWith("sample")) NavMesh.Sample = p => (!(fault == "sample-start" ? Same(p,start) : Same(p,finish)),p);
    if (fault.StartsWith("height")) NavMesh.Sample = p => (true,(fault == "height-start" ? Same(p,start) : Same(p,finish)) ? p + new Vector3(0,.51f,0) : p);
    if (fault.EndsWith("false") || fault.EndsWith("partial")) NavMesh.Route = (a,b) =>
    {
        bool selected = fault.StartsWith("forward") ? Same(a,start) && Same(b,finish) : Same(a,finish) && Same(b,start);
        return (!selected || !fault.EndsWith("false"), selected && fault.EndsWith("partial") ? NavMeshPathStatus.PathPartial : NavMeshPathStatus.PathComplete);
    };
    Refuses(runtime.VerifyTeamPaths, "last BEAR objective route refuses bad sample height or directed path");
    NavMesh.Sample = p => (true,p); NavMesh.Route = (_,_) => (true,NavMeshPathStatus.PathComplete); NavMesh.Routes.Clear();
    runtime.VerifyTeamPaths();
    Need(NavMesh.Routes.Any(r => Same(r.From,start) && Same(r.To,finish)), "failed booth proof does not cache success and can retry after gate opening");
    runtime.Dispose();
}
Reset(); var failedView = Create("arena_airpit").Runtime; DominationPointView.ThrowAt = 1;
Refuses(failedView.Prepare, "view allocation failure is surfaced to the owning mode");
Need(!failedView.AllowsPickup(new(1000,0,1000)), "partial view creation cannot publish prepared authority");
failedView.Dispose();
Need(DominationPointView.Created.All(v => v.Disposed), "owner cleanup after a view failure releases earlier markers");
Console.WriteLine($"DOMINATION_RUNTIME_CHECKS_PASSED ({checks} assertions)");
