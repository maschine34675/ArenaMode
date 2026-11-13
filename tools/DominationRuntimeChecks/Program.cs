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
    ArenaAreaRuntime.Starts = _ => Array.Empty<Vector3>();
    ArenaMode.Logger.Lines.Clear();
    ArenaPickupGeometry.PoolOk = true; ArenaPickupGeometry.PoolCalls = 0; ArenaPickupGeometry.PoolSeeds = null;
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
    // Iceberg: authored trigger centres (A in the pool, .5 m below the basin floor), never lifted.
    ["arena_iceberg"] = new[] { ("A", new Vector3(16.77f, 4.5f, -91.39f)), ("B", new Vector3(10.614f, 3.13f, -77.727f)),
        ("C", new Vector3(21.91f, 3.15f, -47.3f)) },
    // Chop Shop: the capture-point light root's own X/Y/Z, which GPU-2 accepted unchanged (12.11.2026).
    ["arena_autoservice"] = new[] { ("X", new Vector3(-369.29f, -3.8f, 499.49f)) },
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
    var mapMarkers = ArenaMode.Compat.DynamicMapsDominationMarkers.Last;
    Need(mapMarkers.Location == map && !mapMarkers.Disposed, "map overlay belongs to this prepared location");
    mode.ObjectiveHudVisible = false;
    Need(!mapMarkers.Allowed(), "map overlay refuses a foreign or stopped round");
    mode.ObjectiveHudVisible = true;
    mode.IsSearchActive = false;
    Need(!mapMarkers.Allowed(), "map overlay refuses countdown and finished phases");
    mode.IsSearchActive = true;
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
    // Ported maps carry the ring, the HUD row and the DynamicMaps diamond already: no world letter there.
    Need(DominationPointView.Created.All(v => v.ShowLetter == factory),
        "ported maps pass showLetter false and vanilla Factory passes true");
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
    Need(mapMarkers.Points.Select(p => (p.Id, p.Position, p.OwnerUsec, p.Contested))
        .SequenceEqual(DominationObjectiveBridge.Points.Select(p => (p.Id, p.Position, p.OwnerUsec, p.Contested))),
        "map overlay receives the same validated positions and current owners as gameplay");
    Need(rules.Points.Count == expected.Length && rules.Points[0].OwnerUsec == true
        && rules.Points.Skip(1).All(p => p.OwnerUsec == false), "runtime counts and captures each authored point independently");
    Need(DominationObjectiveBridge.Points.Select(p => p.Id).SequenceEqual(expected.Select(p => p.Id))
        && DominationObjectiveBridge.Bots.Length == expected.Length-1 && DominationObjectiveBridge.Live,
        "bot bridge receives actual point labels and only classified physical bots");
    Need(expected.All(p => runtime.StatusLine.Contains(p.Id + ":")), "status line includes every actual objective label");
    int rendered = DominationPointView.Created.Sum(v => v.Ticks), published = DominationObjectiveBridge.Calls;
    runtime.Dispose(); runtime.Dispose();
    Need(mapMarkers.Disposed && !mapMarkers.Allowed(), "runtime disposal releases the overlay and revokes visibility");
    Need(DominationPointView.Created.All(v => v.Disposed) && !runtime.AllowsPickup(new(1000,0,1000)),
        "dispose removes all owned markers and pickup authority");
    runtime.Tick(local, bots, true, 9);
    Need(DominationPointView.Created.Sum(v => v.Ticks) == rendered && DominationObjectiveBridge.Calls == published,
        "disposed runtime cannot render or publish objectives");
    Refuses(runtime.VerifyTeamPaths, "disposed runtime cannot verify a new route");
    Refuses(runtime.Prepare, "disposed runtime cannot recreate markers");
}

// Tactical's capture point reads Resolve and ControlPointCount: they keep their map list, so Tactical
// gets no point on The Lab, The Labyrinth or Customs. Domination resolves by (map, area) instead.
foreach (var map in new string[] { null, "", "laboratory", "labyrinth", "bigmap", "arena_unknown" })
    Refuses(() => DominationMapLayout.Resolve(map), "Tactical's layout resolver still refuses The Lab, The Labyrinth, Customs and unknown maps");
foreach (var map in new string[] { null, "", "bigmap", "arena_unknown" })
{
    Refuses(() => DominationMapLayout.ResolveDomination(map, null), "unknown map has no invented objective layout");
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
foreach (string map in new[] { "arena_equator", "arena_bay5", "arena_airpit", "arena_bowl", "arena_iceberg", "arena_autoservice" })
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
Reset();
var borrowing = Create("arena_equator").Runtime;
ArenaAreaRuntime.CurrentArea = new AreaDouble { Id = "any-area" };
Refuses(borrowing.Prepare, "bounded area cannot borrow a full map objective layout");
Refuses(() => Create("arena_equator"), "a map without area rows resolves no layout inside a bounded area");
Refuses(() => Create("factory4_day"), "Factory resolves no layout inside a bounded area");

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

// ---- The third layout kind: surveyed slots (The Lab, The Labyrinth, later Customs areas) ----
Vector3 V(float x, float y, float z) => new(x, y, z);
string Message(Action action)
{
    try { action(); } catch (InvalidOperationException ex) { return ex.Message; }
    return null;
}
(DominationRuntime Runtime, DominationMatchPolicy Rules, TeamDeathmatchMode Mode) CreateSurveyed(string map)
{
    var mode = new TeamDeathmatchMode();
    var rules = new DominationMatchPolicy(scoreLimit: 0, pointCount: DominationMapLayout.ResolveDomination(map, null).Points.Count);
    return (new DominationRuntime(mode, rules, new GameWorld { LocationId = map }), rules, mode);
}
// Independent literal tables: the measured rows of the lead's native dry runs (GPU-2, 13.11.2026).
var surveyedCases = new (string Map, (string Id, Vector3[] Candidates)[] Slots, Vector3[] Extra)[]
{
    ("laboratory", new[]
    {
        ("A", new[] { V(-224.879f, .082f, -279.867f), V(-231.636f, .082f, -286.694f) }),
        ("B", new[] { V(-171.54f, 0f, -312.18f), V(-175.02f, .1f, -311.28f), V(-166.02f, .1f, -314.36f) }),
        ("C", new[] { V(-213.124f, 0f, -378.755f), V(-205.29f, .1f, -390.55f), V(-202.231f, .1f, -393.884f) })
    }, Array.Empty<Vector3>()),
    ("labyrinth", new[]
    {
        ("X", new[] { V(-9.92f, .1f, 23.92f), V(5.92f, .1f, 20.22f), V(-11.18f, .2f, 35.7f) })
    }, new[] { V(-23.83f, .1f, 20.08f), V(-10.91f, .1f, 6.08f), V(15.23f, .1f, 27.16f) })
};
foreach (var (map, slots, extra) in surveyedCases)
{
    Reset();
    var layout = DominationMapLayout.ResolveDomination(map, null);
    Need(layout.Surveyed && !layout.Factory, "The Lab and The Labyrinth resolve to the surveyed layout kind, not Factory's");
    Need(layout.Points.Select(p => p.Id).SequenceEqual(slots.Select(s => s.Id)), "surveyed slot ids and count are exact");
    for (int s = 0; s < slots.Length; s++)
        Need(Same(layout.Points[s].Position, slots[s].Candidates[0])
            && layout.Points[s].Alternates.Count == slots[s].Candidates.Length - 1
            && layout.Points[s].Alternates.Select((a, i) => Same(a, slots[s].Candidates[i + 1])).All(x => x),
            "surveyed slots keep their measured primary and their ordered alternates");
    Need(layout.NetworkSeeds.Count == extra.Length && layout.NetworkSeeds.Select((p, i) => Same(p, extra[i])).All(x => x)
        && layout.NavigationSeeds.Count == slots.Sum(s => s.Candidates.Length) + extra.Length,
        "the network proof pool is every slot candidate plus the extra seeds, which are never points");
    Need(ReferenceEquals(layout, DominationMapLayout.ResolveDomination(map.ToUpperInvariant(), "full-map"))
        && ReferenceEquals(layout, DominationMapLayout.ResolveDomination(map, ""))
        && DominationMapLayout.ResolveDomination("factory4_day", null).NavigationSeeds.Count == 0,
        "the whole-map key handles case, full-map and empty ids; the other kinds have no pool");
    Need(DominationMapLayout.DominationPointCount(map, null) == slots.Length
        && DominationMapLayout.DominationPointCount(map, "full-map") == slots.Length
        && DominationMapLayout.ControlPointCount(map) == 0,
        "Domination counts the surveyed slots while Tactical's count stays 0");

    var (runtime, rules, mode) = CreateSurveyed(map);
    runtime.Prepare();
    Need(ArenaPickupGeometry.FactoryCalls == 0 && ArenaPickupGeometry.PoolCalls == 1
        && ArenaPickupGeometry.PoolSeeds.SequenceEqual(layout.NavigationSeeds),
        "a surveyed layout proves the navigation network of its own seed pool, never Factory's");
    Need(ArenaPickupGeometry.Placements.Count == slots.Length
        && ArenaPickupGeometry.Placements.All(p => Same(p.Anchor, ArenaPickupGeometry.PoolAnchor) && p.AllowNearby),
        "surveyed placement uses the pool anchor and keeps the bounded 2 m relocation");
    Need(slots.Select((s, i) => Same(ArenaPickupGeometry.Placements[i].Requested, s.Candidates[0])
            && Same(DominationPointView.Created[i].Floor, s.Candidates[0]) && DominationPointView.Created[i].Id == s.Id).All(x => x),
        "every slot takes its primary when the primary passes");
    Need(DominationPointView.Created.All(v => v.ShowLetter && v.Radius == 4f), "vanilla surveyed maps show the world letter");
    Need(NavMesh.Routes.Any(r => Same(r.From, slots[0].Candidates[0]) && Same(r.To, ArenaPickupGeometry.PoolAnchor))
        && NavMesh.Routes.Any(r => Same(r.From, ArenaPickupGeometry.PoolAnchor) && Same(r.To, slots[0].Candidates[0])),
        "a surveyed point needs complete routes both ways from the anchor");
    string note = ArenaMode.Logger.Lines.Last();
    Need(note.StartsWith("Domination points verified: " + slots[0].Id + "=") && !note.Contains("alternates used"),
        "the verified line names no alternates while every slot used its primary");
    int routes = NavMesh.Routes.Count;
    bool teamProofThrew = false;
    try { runtime.VerifyTeamPaths(); } catch (InvalidOperationException) { teamProofThrew = true; }
    Need(!teamProofThrew && NavMesh.Routes.Count == routes, "a surveyed whole-map layout skips the team proofs like Factory");
    Need(!runtime.AllowsPickup(slots[0].Candidates[0]) && runtime.AllowsPickup(V(5000, 0, 5000)), "surveyed points reserve their pickup clearance");
    runtime.Dispose();

    // Determinism: the same scene gives the same points.
    var firstFloors = DominationPointView.Created.Select(v => v.Floor).ToArray();
    Reset();
    CreateSurveyed(map).Runtime.Prepare();
    Need(DominationPointView.Created.Select(v => v.Floor).SequenceEqual(firstFloors), "surveyed placement is deterministic for one scene");
}

{
    // Slot fallback, separation and abort, on The Lab (A, B, C; B has two alternates).
    var lab = surveyedCases[0].Slots;
    Vector3 bPrimary = lab[1].Candidates[0], bFirst = lab[1].Candidates[1], bSecond = lab[1].Candidates[2], aPrimary = lab[0].Candidates[0];
    Reset();
    ArenaPickupGeometry.Place = (p, _) => (!Same(p, bPrimary), p);
    var fallback = CreateSurveyed("laboratory").Runtime;
    Need(Message(fallback.Prepare) == null && DominationPointView.Created.Count == 3,
        "a refused primary falls to the first alternate and leaves the other slots alone");
    Need(Same(DominationPointView.Created[1].Floor, bFirst) && Same(DominationPointView.Created[0].Floor, aPrimary)
        && Same(DominationPointView.Created[2].Floor, lab[2].Candidates[0]),
        "a refused primary falls to the first alternate and leaves the other slots alone");
    Need(ArenaMode.Logger.Lines.Last().Contains("alternates used: B=#1") && !ArenaMode.Logger.Lines.Last().Contains("A=#"),
        "the verified line names the alternate a slot used");
    fallback.Dispose();
    Reset();
    ArenaPickupGeometry.Place = (p, _) => (!Same(p, bPrimary) && !Same(p, bFirst), p);
    Need(Message(CreateSurveyed("laboratory").Runtime.Prepare) == null && DominationPointView.Created.Count == 3,
        "alternates are tried in their listed order");
    Need(Same(DominationPointView.Created[1].Floor, bSecond) && ArenaMode.Logger.Lines.Last().Contains("alternates used: B=#2"),
        "alternates are tried in their listed order");
    Reset();
    NavMesh.Route = (from, to) => (!(Same(from, bPrimary) || Same(to, bPrimary)), NavMeshPathStatus.PathComplete);
    Need(Message(CreateSurveyed("laboratory").Runtime.Prepare) == null && DominationPointView.Created.Count == 3,
        "a candidate without return path falls to the next alternate");
    Need(Same(DominationPointView.Created[1].Floor, bFirst), "a candidate without return path falls to the next alternate");

    // Separation: a candidate whose final floor lies closer than 15 m to an earlier slot is skipped.
    Reset();
    ArenaPickupGeometry.Place = (p, _) => (true, Same(p, bPrimary) ? aPrimary + V(14.99f, 0, 0) : p);
    Need(Message(CreateSurveyed("laboratory").Runtime.Prepare) == null && DominationPointView.Created.Count == 3,
        "a surveyed slot never takes a candidate closer than 15 m to an earlier slot");
    Need(Same(DominationPointView.Created[1].Floor, bFirst), "a surveyed slot never takes a candidate closer than 15 m to an earlier slot");
    Reset();
    ArenaPickupGeometry.Place = (p, _) => (true, Same(p, bPrimary) ? aPrimary + V(15.01f, 0, 0) : p);
    Need(Message(CreateSurveyed("laboratory").Runtime.Prepare) == null && DominationPointView.Created.Count == 3,
        "a candidate 15 m or more from the earlier slots is accepted");
    Need(Same(DominationPointView.Created[1].Floor, aPrimary + V(15.01f, 0, 0)), "a candidate 15 m or more from the earlier slots is accepted");
    Reset();
    ArenaPickupGeometry.Place = (p, _) => (true, Same(p, bPrimary) ? aPrimary + V(0, 0, 14.99f) : p);
    Need(Message(CreateSurveyed("laboratory").Runtime.Prepare) == null && DominationPointView.Created.Count == 3,
        "separation is measured horizontally on the final floor in any direction");
    Need(Same(DominationPointView.Created[1].Floor, bFirst), "separation is measured horizontally on the final floor in any direction");

    // Abort: a slot without a passing candidate stops the match, naming the slot and the last refusal.
    Reset();
    ArenaPickupGeometry.Place = (p, _) => (!(Same(p, bPrimary) || Same(p, bFirst) || Same(p, bSecond)), p);
    var abortRuntime = CreateSurveyed("laboratory").Runtime;
    string abort = Message(abortRuntime.Prepare);
    Need(abort != null && abort.Contains("point B") && abort.Contains("test floor refused") && abort.Contains("3 candidate"),
        "a slot without a passing candidate aborts the match and names the slot and the last refusal");
    Need(DominationPointView.Created.Count == 0 && !abortRuntime.AllowsPickup(V(5000, 0, 5000)),
        "an aborted surveyed layout leaves no partial markers or pickup authority");
    Reset();
    ArenaPickupGeometry.Place = (p, _) => (true, Same(p, bPrimary) || Same(p, bFirst) || Same(p, bSecond) ? aPrimary + V(3, 0, 0) : p);
    string tooClose = Message(CreateSurveyed("laboratory").Runtime.Prepare);
    Need(tooClose != null && tooClose.Contains("point B") && tooClose.Contains("closer than 15 m to point A"),
        "separation refusals abort the match like any other refusal");
    Reset();
    ArenaPickupGeometry.PoolOk = false;
    string noNetwork = Message(CreateSurveyed("laboratory").Runtime.Prepare);
    Need(noNetwork != null && noNetwork.Contains("laboratory") && noNetwork.Contains("test network refused")
        && DominationPointView.Created.Count == 0 && ArenaPickupGeometry.Placements.Count == 0,
        "an unverifiable network aborts before any placement");
    // A single-slot layout aborts the same way.
    Reset();
    ArenaPickupGeometry.Place = (p, _) => (false, p);
    string single = Message(CreateSurveyed("labyrinth").Runtime.Prepare);
    Need(single != null && single.Contains("point X") && single.Contains("3 candidate") && DominationPointView.Created.Count == 0,
        "The Labyrinth's only point aborts when none of its three candidates passes");
}

{
    // The team-start proof inside a bounded area, on a synthetic layout and area (stage 3 brings the real rows).
    var synthetic = new DominationMapLayout(new[] { V(20, 0, 20) },
        new DominationMapLayout.Point("A", V(0, 0, 0), V(0, 0, 5)), new DominationMapLayout.Point("B", V(40, 0, 0)));
    var usecStarts = new[] { V(-50, 0, 0), V(-60, 0, 0) }; var bearStarts = new[] { V(90, 0, 0), V(95, 0, 5) };
    DominationRuntime SyntheticArea(string areaId = "synthetic-area")
    {
        Reset();
        ArenaAreaRuntime.CurrentArea = new AreaDouble { Id = "synthetic-area" };
        ArenaAreaRuntime.Starts = usec => usec ? usecStarts : bearStarts;
        var runtime = new DominationRuntime(new TeamDeathmatchMode(), new DominationMatchPolicy(scoreLimit: 0, pointCount: 2),
            new GameWorld { LocationId = "synthetic-map" }, areaId, synthetic);
        runtime.Prepare();
        return runtime;
    }
    bool Near(Vector3 a, Vector3 b) => Same(a, b);
    var proved = SyntheticArea();
    Need(DominationPointView.Created.Count == 2 && ArenaPickupGeometry.PoolSeeds.Count == 4,
        "a surveyed layout prepares inside a bounded area from its own pool");
    NavMesh.Routes.Clear();
    proved.VerifyTeamPaths();
    foreach (var start in new[] { usecStarts[0], bearStarts[0] })
    foreach (var point in new[] { V(0, 0, 0), V(40, 0, 0) })
        Need(NavMesh.Routes.Any(r => Near(r.From, start) && Near(r.To, point)) && NavMesh.Routes.Any(r => Near(r.From, point) && Near(r.To, start)),
            "the area team-start proof covers both factions and every point in both directions");
    int proofRoutes = NavMesh.Routes.Count;
    proved.VerifyTeamPaths();
    Need(NavMesh.Routes.Count == proofRoutes, "a passed area team-start proof is frozen");

    var oneConnected = SyntheticArea();
    NavMesh.Route = (from, to) => (!(Near(from, usecStarts[0]) || Near(to, usecStarts[0])), NavMeshPathStatus.PathComplete);
    string leftover = Message(oneConnected.VerifyTeamPaths);
    Need(leftover == null, "one connected start per faction is enough, a second start may be cut off");

    var cutOff = SyntheticArea();
    NavMesh.Route = (from, to) => (!(bearStarts.Any(b => Near(from, b) || Near(to, b)) && (Near(from, V(40, 0, 0)) || Near(to, V(40, 0, 0)))),
        NavMeshPathStatus.PathComplete);
    string cut = Message(cutOff.VerifyTeamPaths);
    Need(cut != null && cut.Contains("point B") && cut.Contains("BEAR"),
        "an area point cut off from every start of a faction aborts the match naming the point and the faction");
    NavMesh.Route = (_, _) => (true, NavMeshPathStatus.PathComplete);
    Need(Message(cutOff.VerifyTeamPaths) == null && NavMesh.Routes.Count > 0, "a failed area team-start proof is not cached and passes once the routes reconnect");

    var noStarts = SyntheticArea();
    ArenaAreaRuntime.Starts = usec => usec ? Array.Empty<Vector3>() : bearStarts;
    string none = Message(noStarts.VerifyTeamPaths);
    Need(none != null && none.Contains("USEC"), "a faction without verified starts cannot pass the area proof");

    SyntheticArea();
    var sameLayoutOtherArea = new DominationRuntime(new TeamDeathmatchMode(), new DominationMatchPolicy(scoreLimit: 0, pointCount: 2),
        new GameWorld { LocationId = "synthetic-map" }, "synthetic-area", synthetic);
    ArenaAreaRuntime.CurrentArea = new AreaDouble { Id = "another-area" };
    Need(Message(sameLayoutOtherArea.Prepare) != null, "a layout resolved for one area cannot be prepared in another");
    Need(Message(() => new DominationRuntime(new TeamDeathmatchMode(), new DominationMatchPolicy(scoreLimit: 0, pointCount: 2),
        new GameWorld { LocationId = "laboratory" })) != null, "a map layout cannot be resolved for an unlisted bounded area");
}

Console.WriteLine($"DOMINATION_RUNTIME_CHECKS_PASSED ({checks} assertions)");
