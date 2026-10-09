using ArenaMode.Bots;
using ArenaMode.Config;
using ArenaMode.Modes;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.AssetsManager;
using EFT.Interactive;
using EFT.InventoryLogic;
using UnityEngine;

int checks = 0;
void Check(bool result, string message)
{
    checks++;
    if (!result) throw new Exception(message);
}
(TacticalMode Mode, GameWorld World, Corpse[] Bodies) Setup()
{
    CorpseCleanupService.Reset();
    ArenaMode.Combat.Fire.ArenaFireRuntime.HeldBody = null;
    CorpseAggressors.Reset();
    Settings.MaxCorpses.Value = 1;
    Settings.StripCorpseGear.Value = true;
    Settings.ReleaseTrimmedCorpses.Value = true;
    ArenaMode.Compat.PitFireTeamCompat.TeamProfile = null;
    ArenaMode.Loot.OpenLootTracker.Open = null;
    Time.time = 100;
    var world = new GameWorld();
    var game = new LocalGame();
    Singleton<GameWorld>.Instance = world;
    Singleton<AbstractGame>.Instance = game;
    Singleton<IBotGame>.Instance = new IBotGame
    {
        BotsController = new BotsController { BotSpawner = new BotSpawner { _deadBodiesController = new DeadBodiesController() } }
    };
    Singleton<ObjectsFactory>.Instance = null;
    ArenaSession.IsActive = true;
    ArenaSession.ActiveMode = "tactical";
    ArenaSession.StartedGame = game;
    ArenaSession.StartedWorld = world;
    var match = new ArenaMatch();
    var mode = new TacticalMode { _teamGame = game, _match = match };
    match.Mode = mode;
    ArenaMatchService.Current = match;
    var bodies = Enumerable.Range(0, 6).Select(i => AddBody(world, "opponent-" + i)).ToArray();
    mode._boundTeamEnemies = bodies.Take(5).Select(c => new BotOwner { GetPlayer = Owner(c) }).ToArray();
    return (mode, world, bodies);
}
Corpse AddBody(GameWorld world, string id)
{
    var body = new Corpse { PlayerProfileID = id };
    body.Item = body.ItemOwner.RootItem = new InventoryEquipment();
    var renderer = new Renderer();
    var collider = new Collider();
    body.gameObject.Components.Add(renderer);
    body.gameObject.Components.Add(collider);
    body.gameObject.Components.Add(new AssetPoolObject { Colliders = new[] { collider } });
    var player = new Player { ProfileId = id, gameObject = body.gameObject };
    player.HealthController.IsAlive = false;
    body.gameObject.Components.Add(player);
    world.RegisterEverExisted(id, player);
    world.LootList.Add(body);
    return body;
}
Player Owner(Corpse body) => body.GetComponent<Player>();
void Sweep(int count = 1)
{
    for (int i = 0; i < count; i++)
    {
        Time.time += 6;
        CorpseCleanupService.Tick();
    }
}
void Scope(string name, Action<TacticalMode, GameWorld, Corpse[]> mutate)
{
    var (mode, world, bodies) = Setup();
    mutate(mode, world, bodies);
    Check(!mode.RetainsFrozenOpponentCorpse(Owner(bodies[0]), world), "SCOPE " + name);
}

// The old bug: the clear window trims a current-round kill, then the next
// release sweeps destroy its physical identity while Tactical still owns it.
{
    var (mode, world, bodies) = Setup();
    CorpseCleanupService.ClearAllSoon("Tactical fixture round");
    Sweep(4);
    for (int i = 0; i < 5; i++)
    {
        Check(bodies[i].GetComponent<Renderer>().forceRenderingOff, "FROZEN hidden " + i);
        Check(!bodies[i].GetComponent<Collider>().enabled, "FROZEN collider disabled " + i);
        Check(!bodies[i].Destroyed && Owner(bodies[i]).Disposals == 0, "FROZEN no physical release " + i);
        Check(mode.RetainsFrozenOpponentCorpse(Owner(bodies[i]), world), "FROZEN exact lease " + i);
    }
    Check(bodies[5].Destroyed && Owner(bodies[5]).Disposals == 1, "MAX-FIVE sixth unrelated corpse releases");
    Check(world.Removed.Count == 1, "MAX-FIVE only five additional physical bodies held");
    mode._boundTeamEnemies = Array.Empty<BotOwner>(); // real AdoptTeamFollowers / OnMatchEnd
    Sweep(3);
    Check(bodies.All(b => b.Destroyed && Owner(b).Disposals == 1), "RETIRED all held bodies release after clear");
}

// Profile IDs do not extend the lease to another body or to the next round.
{
    var (mode, world, bodies) = Setup();
    var oldOwner = Owner(bodies[0]);
    var replacement = AddBody(world, oldOwner.ProfileId);
    Check(!mode.RetainsFrozenOpponentCorpse(Owner(replacement), world), "EXACT-BODY reused profile does not inherit lease");
    Check(mode.RetainsFrozenOpponentCorpse(oldOwner, world), "EXACT-BODY original remains leased despite registry drift");
    // Registry drift is still Tactical's no-contest decision, never cleanup's
    // excuse to erase its evidence. The helper is not a roster validator.
    CorpseCleanupService.ClearAllSoon("Tactical fixture round");
    Sweep(4);
    Check(replacement.Destroyed && !bodies[0].Destroyed, "EXACT-BODY cleanup releases replacement only");
    var next = Enumerable.Range(0, 5).Select(i => AddBody(world, "opponent-" + i)).ToArray();
    mode._boundTeamEnemies = next.Select(c => new BotOwner { GetPlayer = Owner(c) }).ToArray();
    for (int i = 0; i < 5; i++)
    {
        Check(!mode.RetainsFrozenOpponentCorpse(Owner(bodies[i]), world), "REPLACED old roster no lease " + i);
        Check(mode.RetainsFrozenOpponentCorpse(Owner(next[i]), world), "REPLACED new roster leased " + i);
    }
    CorpseCleanupService.ClearAllSoon("Tactical fixture round");
    Sweep(4);
    Check(bodies.All(b => b.Destroyed), "REPLACED old bodies release");
    Check(next.All(b => !b.Destroyed), "REPLACED new five bodies remain");
}

Scope("ordinary", (_, _, _) => ArenaSession.IsActive = false);
Scope("solo Tactical", (m, _, _) => m._teamArena = false);
Scope("different match", (_, _, _) => ArenaMatchService.Current = new ArenaMatch());
Scope("different mode on same match", (m, _, _) => m._match.Mode = new object());
Scope("no current match", (_, _, _) => ArenaMatchService.Current = null);
Scope("wrong singleton world", (_, _, _) => Singleton<GameWorld>.Instance = new GameWorld());
Scope("wrong singleton game", (_, _, _) => Singleton<AbstractGame>.Instance = new LocalGame());
Scope("wrong started world", (_, _, _) => ArenaSession.StartedWorld = new GameWorld());
Scope("wrong started game", (_, _, _) => ArenaSession.StartedGame = new LocalGame());
Scope("short roster", (m, _, _) => m._boundTeamEnemies = m._boundTeamEnemies.Take(4).ToArray());
Scope("oversized roster", (m, _, b) => m._boundTeamEnemies = m._boundTeamEnemies.Append(new BotOwner { GetPlayer = Owner(b[5]) }).ToArray());
Scope("destroyed owner", (m, _, _) => m._boundTeamEnemies[0].Destroyed = true);
Scope("destroyed body", (_, _, b) => Owner(b[0]).Destroyed = true);
Scope("missing owner", (m, _, _) => m._boundTeamEnemies[0] = null);

// Ordinary modes, solo Tactical, and a completed/replaced match keep their
// previous physical release behaviour through the real Tick -> MayRelease path.
foreach (var variant in new[] { "solo", "other mode", "match replaced", "session ended" })
{
    var (mode, world, bodies) = Setup();
    if (variant == "solo") mode._teamArena = false;
    if (variant == "other mode") mode._match.Mode = new object();
    if (variant == "match replaced") ArenaMatchService.Current = new ArenaMatch();
    CorpseCleanupService.ClearAllSoon("Tactical fixture round");
    Sweep();
    if (variant == "session ended")
    {
        ArenaSession.IsActive = false;
        Sweep(3);
        Check(bodies.All(b => !b.Destroyed), "ORDINARY cleanup itself inactive");
    }
    else
    {
        Sweep(3);
        Check(bodies.All(b => b.Destroyed), "RELEASE unchanged " + variant);
    }
}

// Cleanup still requires readable DEAD health. The helper never declares a
// missing object dead, changes native registries, or masks Tactical validation.
foreach (bool missingHealth in new[] { false, true })
{
    var (mode, world, bodies) = Setup();
    mode._teamArena = false;
    if (missingHealth) Owner(bodies[0]).HealthController = null;
    else Owner(bodies[0]).HealthController.IsAlive = true;
    CorpseCleanupService.ClearAllSoon("Tactical fixture round");
    Sweep(4);
    Check(!bodies[0].Destroyed && Owner(bodies[0]).Disposals == 0, "LIVE-LOSS never infer dead from unreadable/live health");
}

// Source binding of the surrounding lifecycle, not a claim to run native Unity
// or the full Tactical mode in these contract doubles.
var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var tactical = File.ReadAllText(Path.Combine(repo, "ArenaMode.Client/Modes/TacticalMode.cs"));
string Method(string start, string end)
{
    int a = tactical.IndexOf(start, StringComparison.Ordinal);
    int b = tactical.IndexOf(end, a + start.Length, StringComparison.Ordinal);
    Check(a >= 0 && b > a, "SOURCE method boundary " + start);
    return tactical[a..b];
}
Check(tactical.Contains("_boundTeamEnemies = bodies;"), "SOURCE frozen roster assignment");
Check(Method("private TeamRetirementDecision ObserveTeamEnemyRetirement", "private void RebuildTeamLeader")
    .Contains("world.RegisteredPlayers"), "SOURCE retirement checks active registries, not corpse destruction");
var validator = Method("private bool TryValidateBoundTeamEnemies", "private");
Check(validator.Contains("a frozen Arena opponent became destroyed or unreadable"), "SOURCE unexpected destroyed body still rejects");
Check(validator.Contains("demonstrablyDead"), "SOURCE readable death validation retained");
Check(Method("public void OnMatchEnd()", "private").Contains("_boundTeamEnemies = Array.Empty<BotOwner>();"), "SOURCE match end clears lease");
Check(Method("private void AdoptTeamFollowers", "private").Contains("_boundTeamEnemies = Array.Empty<BotOwner>();"), "SOURCE next round clears lease");
Console.WriteLine($"TACTICAL_CORPSE_CHECKS_PASSED: {checks} assertions; actual cleanup and retention sources, native-shaped boundary doubles");
