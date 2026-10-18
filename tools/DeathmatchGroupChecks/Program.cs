using ArenaMode.Bots;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using UnityEngine;

const string GroupPatch = "ArenaBotDeathmatchIndividualGroupPatch";
const string CreationPatch = "ArenaBotDeathmatchAfterCreationPatch";
int assertions = 0;
void Check(bool condition, string reason)
{
    assertions++;
    if (!condition) throw new InvalidOperationException(reason);
}

(TestGame game, BotSpawner spawner, BotZone zone) Setup(bool ffa = true, bool active = true, string mode = "deathmatch")
{
    ArenaSession.DeathmatchSelection = null;
    ArenaSession.IsActive = ArenaSession.ArmedForNextRaid = false;
    ArenaBotPatches.ResetRaidState();
    TeamDeathmatchRelations.Enabled = false;
    Time.time = 0;
    ArenaMode.Plugin.ModLogger.Warnings.Clear();
    var game = new TestGame();
    var spawner = new BotSpawner { BotGame = game };
    game.BotsController.BotSpawner = spawner;
    Singleton<AbstractGame>.Instance = game;
    Singleton<IBotGame>.Instance = game;
    Singleton<GameWorld>.Instance = new GameWorld();
    ArenaSession.ExactGame = game;
    ArenaSession.ExactWorld = Singleton<GameWorld>.Instance;
    ArenaSession.IsActive = active;
    ArenaSession.ArmedForNextRaid = !active;
    ArenaSession.ActiveMode = ArenaSession.ArmedMode = mode;
    ArenaSession.ArmedMap = "factory4_day";
    ArenaSession.DeathmatchSelection = new() { FreeForAll = ffa };
    return (game, spawner, new BotZone());
}

(BotOwner bot, BotsGroup group) RegisterResult(BotSpawner spawner, BotZone zone, string id,
    WildSpawnType role = WildSpawnType.assault, bool duplicateNativeRefs = false, bool nativeBossRegistry = false)
{
    var bot = new BotOwner(id, role);
    var group = new BotsGroup(bot, spawner.BotGame);
    var previous = GroupProbe.Enter(GroupPatch, spawner);
    try
    {
        // Arrange a native-created result via the ACTUAL installed registry's
        // Add, then call the unchanged production postfix. This helper is not a
        // substitute for GetGroupAndSetEnemies or its constructor/activation.
        spawner.Groups.Add(zone, bot.Side, group, isBossOrFollower: nativeBossRegistry);
        if (duplicateNativeRefs)
        {
            spawner.Groups[zone]._noConnectionGroups.Add(group);
            spawner.Groups[zone]._noConnectionGroups.Add(group);
        }
        GroupProbe.Call(GroupPatch, "Postfix", spawner, zone, bot, group, previous);
    }
    finally { GroupProbe.Finish(GroupPatch, spawner, previous); }
    return (bot, group);
}

void Created(BotSpawner spawner, BotOwner bot)
{
    var previous = GroupProbe.Enter(CreationPatch, spawner);
    try { GroupProbe.Call(CreationPatch, "Postfix", bot); }
    finally { GroupProbe.Finish(CreationPatch, spawner, previous); }
}
void Activate(BotOwner bot)
{
    // These are explicit native I/O inputs, not a reimplementation of activation.
    // The installed BotMemory.Activate calls AddMember before ordinary ticks.
    bot.BotState = EBotState.Active;
    bot.BotsGroup.AddMember(bot, onActivation: true);
}
void Tick(float advance = 1.1f) { Time.time += advance; ArenaBotPatches.TickIndividualGroups(); }

// Both patch boundaries restore either preexisting value even when the native
// operation throws. This invokes the actual finalizer body; Harmony scheduling
// itself belongs to the native build/GPU test, not this reflection harness.
foreach (var patch in new[] { GroupPatch, CreationPatch })
foreach (bool old in new[] { false, true })
{
    var c = Setup(); c.spawner._freeForAll = old;
    var state = GroupProbe.Enter(patch, c.spawner);
    Check(state == old && !c.spawner._freeForAll, "owned FFA operation temporarily disables native no-key group appends");
    try { throw new ApplicationException("controlled native failure"); }
    catch (ApplicationException) { }
    finally { GroupProbe.Finish(patch, c.spawner, state); }
    Check(c.spawner._freeForAll == old, "native freeForAll flag is restored after exceptional operation");
}

var cohort = Setup();
cohort.spawner._freeForAll = true;
var a = RegisterResult(cohort.spawner, cohort.zone, "a", duplicateNativeRefs: true);
Created(cohort.spawner, a.bot);
var b = RegisterResult(cohort.spawner, cohort.zone, "b");
Created(cohort.spawner, b.bot);
var cBot = RegisterResult(cohort.spawner, cohort.zone, "c");
Created(cohort.spawner, cBot.bot);
var registry = cohort.spawner.Groups[cohort.zone];
Check(registry._bossGroup.SetEquals([a.group, b.group, cBot.group]), "same-role individuals retain all exact native group references");
Check(registry.data.Values.All(value => value == null) && registry._noConnectionGroups.Count == 0,
    "individual groups have one native registry owner and no duplicate disposal paths");
Check(a.group.Locked && b.group.Locked && cBot.group.Locked && GroupProbe.Tracked == 3,
    "each returned native group is locked and tracked individually");
Check(!cohort.spawner.Groups.TryGetValue(cohort.zone, EPlayerSide.Savage, WildSpawnType.assault, out _, false),
    "actual native lookup cannot reuse a prior ordinary same-role group");
Check(!cohort.spawner.Groups.TryGetValue(cohort.zone, EPlayerSide.Savage, WildSpawnType.assault, out _, true),
    "actual native boss lookup rejects locked individual groups too");
Check(registry.GetGroups(false).SetEquals([a.group, b.group, cBot.group]) && registry.GetGroups(true).Count == 0,
    "actual native enumeration retains pending individuals but excludes empty preactive members");
Tick(40);
Check(a.group.DisposeCalls == 0 && b.group.DisposeCalls == 0 && cBot.group.DisposeCalls == 0 && GroupProbe.Tracked == 3,
    "PreActive zero-member groups survive after AfterCreation and a long loading delay");
Check(a.group.AddEnemyCalls == 0 && b.group.AddEnemyCalls == 0,
    "PreActive creation callback does not pretend membership or admit unready opponents");
Activate(a.bot); Activate(b.bot); Activate(cBot.bot);
Tick();
Check(registry.GetGroups(true).SetEquals([a.group, b.group, cBot.group]), "actual native enumeration sees all activated same-role individuals");
foreach (var source in new[] { a, b, cBot })
{
    Check(source.group.Enemies.Count == 2 && source.group.AddEnemyCalls == 2,
        "activated cohort obtains both reciprocal enemy relationships");
    Check(!source.group.Enemies.ContainsKey(source.bot.GetPlayer), "reconciliation never admits the group's own player");
}
Tick();
Check(a.group.AddEnemyCalls == 2 && b.group.AddEnemyCalls == 2 && cBot.group.AddEnemyCalls == 2,
    "existing enemy admissions are not repeated on later sweeps");
a.group.Enemies.Remove(b.bot.GetPlayer);
Tick(0.1f);
Check(!a.group.Enemies.ContainsKey(b.bot.GetPlayer), "repair respects one-second sweep pacing");
Tick();
Check(a.group.Enemies.ContainsKey(b.bot.GetPlayer) && a.group.AddEnemyCalls == 3 && b.group.AddEnemyCalls == 2,
    "only the missing direction is repaired after the bounded interval");

// Exact-reference retirement includes aliases left by another native/mod path,
// preserves other groups, and cannot dispose a body before native member removal.
var unrelated = new BotsGroup(new BotOwner("foreign", WildSpawnType.pmcUSEC), cohort.game) { MembersCount = 1 };
registry.data[WildSpawnType.pmcUSEC] = unrelated;
registry.data[WildSpawnType.marksman] = a.group;
registry._noConnectionGroups.Add(a.group);
registry._noConnectionGroups.Add(a.group);
a.bot.IsDead = true; a.bot.HealthController.IsAlive = false;
Tick();
Check(a.group.DisposeCalls == 0 && registry._bossGroup.Contains(a.group), "dead bot group remains owned until native member removal");
a.group.MembersCount = 0;
Tick();
Check(a.group.DisposeCalls == 1 && GroupProbe.Tracked == 2 && !registry._bossGroup.Contains(a.group),
    "dead empty observed group is retired exactly once");
Check(registry.data[WildSpawnType.marksman] == null && registry._noConnectionGroups.Count == 0
    && ReferenceEquals(registry.data[WildSpawnType.pmcUSEC], unrelated),
    "retirement removes only matching references from all native registries");
Tick();
Check(a.group.DisposeCalls == 1 && b.group.DisposeCalls == 0, "later sweeps do not dispose retired or live groups again");
registry.Dispose();
Check(a.group.DisposeCalls == 1 && b.group.DisposeCalls == 1 && cBot.group.DisposeCalls == 1 && unrelated.DisposeCalls == 1,
    "actual native final disposal visits surviving groups once and excludes the retired group");

var pmc = Setup();
var pmcA = RegisterResult(pmc.spawner, pmc.zone, "pmc-a", WildSpawnType.pmcUSEC, nativeBossRegistry: true);
var pmcB = RegisterResult(pmc.spawner, pmc.zone, "pmc-b", WildSpawnType.pmcUSEC, nativeBossRegistry: true);
var pmcRegistry = pmc.spawner.Groups[pmc.zone];
Check(pmcRegistry._bossGroup.SetEquals([pmcA.group, pmcB.group]) && pmcRegistry._noConnectionGroups.Count == 0,
    "native PMC boss-path groups remain separate without adding duplicate ownership");
Check(!pmc.spawner.Groups.TryGetValue(pmc.zone, EPlayerSide.Usec, WildSpawnType.pmcUSEC, out _, true),
    "native same-PMC lookup cannot reuse a locked previous individual");
pmcRegistry.Dispose();
Check(pmcA.group.DisposeCalls == 1 && pmcB.group.DisposeCalls == 1,
    "native PMC registry disposes each individual only once");

// A death between sweeps may have never exposed nonzero membership to the mod.
var rapid = Setup();
var rapidBot = RegisterResult(rapid.spawner, rapid.zone, "fast-death");
Created(rapid.spawner, rapidBot.bot);
rapidBot.bot.IsDead = true; rapidBot.bot.HealthController.IsAlive = false;
Tick();
Check(rapidBot.group.DisposeCalls == 1 && GroupProbe.Tracked == 0,
    "native IsDead allows safe empty-group retirement even without an observed membership tick");

var pending = Setup();
var neverCreated = RegisterResult(pending.spawner, pending.zone, "constructor-only");
Tick(90);
Check(neverCreated.group.DisposeCalls == 0 && GroupProbe.Tracked == 1,
    "constructor-only pending group is not treated as dead or activated");
Created(pending.spawner, neverCreated.bot); Activate(neverCreated.bot); Tick();
neverCreated.group.MembersCount = 0; // native removal after activation, without fabricating a death flag
neverCreated.group.ThrowOnDispose = true;
Tick(); Tick();
Check(neverCreated.group.DisposeCalls == 1 && GroupProbe.Tracked == 0 && ArenaMode.Plugin.ModLogger.Warnings.Count == 1,
    "throwing native disposal cannot retain or repeatedly dispose the retired group");

// A new spawner cannot take over disposal or relationships from an old game.
var stale = Setup();
var staleBot = RegisterResult(stale.spawner, stale.zone, "old");
Created(stale.spawner, staleBot.bot); Activate(staleBot.bot); Tick();
staleBot.group.MembersCount = 0;
stale.game.BotsController.BotSpawner = new BotSpawner { BotGame = stale.game };
Tick();
Check(GroupProbe.Tracked == 0 && staleBot.group.DisposeCalls == 0 && stale.spawner.Groups[stale.zone]._bossGroup.Contains(staleBot.group),
    "stale spawner loses mod bookkeeping but retains its native disposal ownership");

var failedAdmission = Setup();
var failA = RegisterResult(failedAdmission.spawner, failedAdmission.zone, "failing-source");
var failB = RegisterResult(failedAdmission.spawner, failedAdmission.zone, "healthy-source-b");
var failC = RegisterResult(failedAdmission.spawner, failedAdmission.zone, "healthy-source-c");
foreach (var item in new[] { failA, failB, failC }) { Created(failedAdmission.spawner, item.bot); Activate(item.bot); }
// Activation of the first bot happens before the next AfterCreation callback;
// clear these incidental admissions to isolate a fresh three-member sweep.
foreach (var item in new[] { failA, failB, failC }) { item.group.Enemies.Clear(); item.group.AddEnemyCalls = 0; }
failA.group.ThrowOnAddEnemy = true;
Tick();
Check(failA.group.AddEnemyCalls == 1 && failB.group.Enemies.Count == 2 && failC.group.Enemies.Count == 2,
    "one throwing native admission does not prevent other sources from repairing their cohort");
Tick();
Check(failA.group.AddEnemyCalls == 1 && ArenaMode.Plugin.ModLogger.Warnings.Count == 1,
    "a failed source waits five seconds and reports only a bounded warning");
Tick(4);
Check(failA.group.AddEnemyCalls == 2 && ArenaMode.Plugin.ModLogger.Warnings.Count == 1,
    "persistent native failure retries after backoff without log spam");
failA.group.ThrowOnAddEnemy = false;
Tick(5.1f);
Check(failA.group.Enemies.Count == 2 && failB.group.AddEnemyCalls == 2 && failC.group.AddEnemyCalls == 2,
    "recovered admission repairs only its missing relationships");

var reentrant = Setup();
var reA = RegisterResult(reentrant.spawner, reentrant.zone, "reentrant-a");
var reB = RegisterResult(reentrant.spawner, reentrant.zone, "reentrant-b");
Created(reentrant.spawner, reA.bot); Created(reentrant.spawner, reB.bot);
Activate(reA.bot); Activate(reB.bot);
BotOwner lateBot = null;
BotsGroup lateGroup = null;
reA.group.OnAddEnemy = _ =>
{
    reA.group.OnAddEnemy = null;
    var later = RegisterResult(reentrant.spawner, reentrant.zone, "created-during-admission");
    lateBot = later.bot; lateGroup = later.group;
    Activate(lateBot);
    Created(reentrant.spawner, lateBot); // nested production reconciliation must stand down
};
Tick();
Check(lateBot != null && GroupProbe.Tracked == 3 && reA.group.AddEnemyCalls == 1 && reB.group.AddEnemyCalls == 1,
    "native creation callback during admission cannot reenter or invalidate the snapshot enumeration");
Tick();
Check(reA.group.Enemies.Count == 2 && reB.group.Enemies.Count == 2 && lateGroup.Enemies.Count == 2,
    "a cohort added during reconciliation receives reciprocal admission on the next sweep");

var ending = Setup();
var endA = RegisterResult(ending.spawner, ending.zone, "end-a");
var endB = RegisterResult(ending.spawner, ending.zone, "end-b");
var endC = RegisterResult(ending.spawner, ending.zone, "end-c");
foreach (var item in new[] { endA, endB, endC }) Created(ending.spawner, item.bot);
foreach (var item in new[] { endA, endB, endC }) Activate(item.bot);
endA.group.OnAddEnemy = _ => { ArenaSession.IsActive = false; ArenaSession.ArmedForNextRaid = false; };
Tick();
Check(endA.group.AddEnemyCalls == 1 && endB.group.AddEnemyCalls == 0 && endC.group.AddEnemyCalls == 0,
    "native callback that ends the raid prevents any further cohort admission");

foreach (var scenario in new[] { "all-vs-player", "ordinary", "tactical", "teamdeathmatch", "wrong-world", "foreign-spawner", "armed-wrong-map" })
{
    var scope = Setup(ffa: scenario != "all-vs-player", active: scenario != "armed-wrong-map",
        mode: scenario is "tactical" or "teamdeathmatch" ? scenario : "deathmatch");
    if (scenario == "ordinary") { ArenaSession.IsActive = false; ArenaSession.ArmedForNextRaid = false; }
    if (scenario == "wrong-world") Singleton<GameWorld>.Instance = new GameWorld();
    if (scenario == "armed-wrong-map") Singleton<GameWorld>.Instance.LocationId = "laboratory";
    var used = scenario == "foreign-spawner" ? new BotSpawner { BotGame = scope.game } : scope.spawner;
    used._freeForAll = true;
    var untouched = RegisterResult(used, scope.zone, "untouched");
    Created(used, untouched.bot); Activate(untouched.bot); Tick();
    Check(used._freeForAll && !untouched.group.Locked && GroupProbe.Tracked == 0 && untouched.group.DisposeCalls == 0,
        scenario + " is not modified by individual FFA group hooks");
    Check(ReferenceEquals(used.Groups[scope.zone].data[WildSpawnType.assault], untouched.group)
        && used.Groups[scope.zone]._bossGroup.Count == 0,
        scenario + " keeps the native group registration unchanged");
    if (scenario is "ordinary" or "tactical" or "teamdeathmatch" or "wrong-world" or "armed-wrong-map")
    {
        var other = new BotOwner("other", WildSpawnType.assault);
        object[] enemyArgs = [untouched.group, other.GetPlayer, true];
        GroupProbe.Call("ArenaIsPlayerEnemyPatch", "Postfix", enemyArgs);
        object[] friendArgs = [untouched.group, other.GetPlayer, true];
        Check((bool)enemyArgs[2] && (bool)GroupProbe.Invoke("ArenaBotDeathmatchBotFriendshipPatch", "Prefix", friendArgs)
            && (bool)friendArgs[2], scenario + " retains native AI hostility and aggression admission");
    }
}

var armed = Setup(active: false);
var preRaid = RegisterResult(armed.spawner, armed.zone, "armed");
Created(armed.spawner, preRaid.bot);
Tick(50);
Check(preRaid.group.Locked && GroupProbe.Tracked == 1 && preRaid.group.DisposeCalls == 0,
    "matching armed loading owns group creation without starting active cleanup");
ArenaSession.IsActive = true; ArenaSession.ArmedForNextRaid = false;
ArenaBotPatches.ResetRaidState();
Check(GroupProbe.Tracked == 1, "raid-start reset preserves the same confirmed armed cohort");
ArenaSession.IsActive = false;
ArenaBotPatches.ResetRaidState();
Check(GroupProbe.Tracked == 0 && armed.spawner.Groups[armed.zone]._bossGroup.Contains(preRaid.group) && preRaid.group.DisposeCalls == 0,
    "ordinary/ended reset clears managed records while native teardown owns disposal");

var transition = Setup();
ArenaSession.ExactGame = new TestGame();
ArenaSession.ExactWorld = new GameWorld();
ArenaSession.ActiveMode = "tactical"; // stale latch from a missed old GameEnd
ArenaSession.ArmedForNextRaid = true;
var newArmGroup = RegisterResult(transition.spawner, transition.zone, "new-confirmed-arm");
Check(ArenaBotPatches.DeathmatchRulesActive() && newArmGroup.group.Locked && GroupProbe.Tracked == 1,
    "confirmed new same-map arm survives a stale active latch from a different exact raid pair");
ArenaSession.ArmedForNextRaid = false;
var unarmedStale = RegisterResult(transition.spawner, transition.zone, "unarmed-stale-latch");
Check(!ArenaBotPatches.DeathmatchRulesActive() && !unarmedStale.group.Locked && GroupProbe.Tracked == 1,
    "stale active latch without a confirmed arm cannot adopt a new group");
ArenaSession.ArmedForNextRaid = true; ArenaSession.ArmedMap = "laboratory";
Check(!ArenaBotPatches.DeathmatchRulesActive(), "stale active latch cannot bypass the new armed map identity");
var exactOtherMode = Setup(mode: "tactical");
ArenaSession.ArmedForNextRaid = true; ArenaSession.ArmedMode = "deathmatch";
Check(!ArenaBotPatches.DeathmatchRulesActive(), "exact current non-Deathmatch raid takes precedence over a contradictory arm");

// Relationship patches use the frozen policy and exact group game identity.
foreach (bool ffa in new[] { false, true })
{
    var relations = Setup(ffa);
    var own = new BotsGroup(new BotOwner("source", WildSpawnType.assault), relations.game);
    var target = new BotOwner("target", WildSpawnType.assault);
    _ = new BotsGroup(target, relations.game);
    object[] enemyArgs = [own, target.GetPlayer, !ffa];
    GroupProbe.Call("ArenaIsPlayerEnemyPatch", "Postfix", enemyArgs);
    Check((bool)enemyArgs[2] == ffa, "owned AI relationship follows frozen FFA/all-vs-player choice");
    object[] friendArgs = [own, target.GetPlayer, true];
    var runNative = (bool)GroupProbe.Invoke("ArenaBotDeathmatchBotFriendshipPatch", "Prefix", friendArgs);
    Check(runNative == ffa && (ffa || !(bool)friendArgs[2]), "all-vs-player blocks forced bot-bot aggression admission");
    object[] selfArgs = [own, own._initialBot.GetPlayer, true];
    GroupProbe.Call("ArenaIsPlayerEnemyPatch", "Postfix", selfArgs);
    Check(!(bool)selfArgs[2], "a group's own AI remains outside the enemy relation");
    var foreign = new BotsGroup(new BotOwner("old-source", WildSpawnType.assault), new TestGame());
    object[] foreignEnemy = [foreign, target.GetPlayer, !ffa];
    GroupProbe.Call("ArenaIsPlayerEnemyPatch", "Postfix", foreignEnemy);
    Check((bool)foreignEnemy[2] == !ffa, "foreign game AI relation remains native while Deathmatch runs");
    object[] foreignFriend = [foreign, target.GetPlayer, true];
    Check((bool)GroupProbe.Invoke("ArenaBotDeathmatchBotFriendshipPatch", "Prefix", foreignFriend)
        && (bool)foreignFriend[2], "foreign game aggression remains native while Deathmatch runs");
    object[] humanFriend = [own, new Player { IsAI = false }, true];
    Check((bool)GroupProbe.Invoke("ArenaBotDeathmatchBotFriendshipPatch", "Prefix", humanFriend),
        "all-vs-player friendship never blocks aggression against the human PMC");
}

Console.WriteLine($"DEATHMATCH_GROUP_RUNTIME_PASSED: {assertions} assertions");
