using ArenaMode.Combat;
using ArenaMode.Modes;
using ArenaMode.Respawn;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using UnityEngine;

int assertions = 0;
void Check(bool condition, string reason)
{
    assertions++;
    if (!condition) throw new Exception(reason);
}
void Reject(Action action, string reason)
{
    try { action(); } catch (ArgumentException) { assertions++; return; }
    throw new Exception(reason);
}
DeathmatchRules Rules(int rounds = 0, int kills = 2, int seconds = 0, bool ffa = true)
{
    var rules = new DeathmatchRules(new(rounds, kills, seconds, ffa, 3, false), "local", "You");
    rules.StartRound(0);
    return rules;
}
void Frag(DeathmatchRules rules, string id = "local", double now = 0, string victim = "victim")
    => rules.RecordKill(now, id, id, victim);

Reject(() => new DeathmatchOptions(-1, 1, 0, true, 3, false), "negative rounds rejected");
Reject(() => new DeathmatchOptions(0, -1, 0, true, 3, false), "negative kills rejected");
Reject(() => new DeathmatchOptions(0, 1, -1, true, 3, false), "negative seconds rejected");
Reject(() => new DeathmatchOptions(0, 1, 0, true, -1, false), "negative intermission rejected");
Reject(() => new DeathmatchOptions(1, 0, 0, true, 3, false), "finite match must have round end condition");
Reject(() => new DeathmatchRules(new(0, 1, 0, true, 3, false), " ", "You"), "missing local identity rejected");

var r = Rules(rounds: 1);
Frag(r, "bot-a"); Frag(r, "bot-b");
Check(r.Phase == DeathmatchPhase.Live, "distinct bot kills must not aggregate into one opponent");
Frag(r, "bot-a");
Check(r.Phase == DeathmatchPhase.Finished && r.WinnerProfileId == "bot-a" && r.Outcome == DeathmatchOutcome.Lost,
    "individual bot reaching kill limit wins");
Frag(r); Frag(r);
Check(r.LocalKills == 0 && r.WinnerProfileId == "bot-a", "terminal winner and personal score remain frozen");

r = Rules(rounds: 1);
Frag(r);
Check(r.Phase == DeathmatchPhase.Live, "kill limit boundary remains live until target");
Frag(r);
Check(r.Outcome == DeathmatchOutcome.Won, "local reaching kill limit wins");

r = Rules(rounds: 1, seconds: 10);
Frag(r, "bot-a");
Frag(r, now: 10);
Check(r.WinnerProfileId == "bot-a" && r.LocalKills == 0 && r.RoundReason == "Time limit",
    "deadline is checked before a late kill changes standings");
r = Rules(rounds: 1, kills: 0, seconds: 10);
Frag(r); Frag(r, "bot-a"); r.Tick(10);
Check(r.Outcome == DeathmatchOutcome.Draw && r.WinnerProfileId == null, "time limit preserves equal individual scores");
r = Rules(rounds: 1, kills: 0, seconds: 10);
r.Tick(10);
Check(r.Outcome == DeathmatchOutcome.Draw, "scoreless FFA time limit is draw");
r = Rules(rounds: 1, kills: 0, seconds: 10);
Frag(r); Frag(r); Frag(r, "bot-a"); r.Tick(10);
Check(r.Outcome == DeathmatchOutcome.Won, "time limit picks highest individual score");
r = Rules(rounds: 1, kills: 0, seconds: 10);
Frag(r, "early-leader"); Frag(r, "early-leader");
for (int i = 0; i < 80; i++) Frag(r, "later-bot-" + i);
r.Tick(10);
Check(r.WinnerProfileId == "early-leader" && r.RetainedKillCounters == 81,
    "early winner survives more bot generations than scoreboard history retains");

r = Rules(kills: 1);
for (int i = 0; i < 50; i++)
{
    Frag(r, i % 2 == 0 ? "local" : "bot-" + i, i);
    Check(r.Phase == DeathmatchPhase.Intermission && r.RetainedWinCounters == 0, "zero rounds keeps rounds coming without winner history");
    Check(r.StartRound(i + 1) && r.LocalKills == 0, "new round clears per-round scores");
}

r = Rules(rounds: 5, kills: 1);
foreach (string id in new[] { "local", "bot-a", "bot-b", "bot-c", "local" })
{
    Frag(r, id);
    if (r.Phase == DeathmatchPhase.Intermission) r.StartRound(0);
}
Check(r.Outcome == DeathmatchOutcome.Won && r.LocalRoundWins == 2 && r.LocalRoundLosses == 3,
    "finite FFA match compares individual round wins not combined opponents");
r = Rules(rounds: 2, kills: 1);
Frag(r); r.StartRound(1); Frag(r, "bot-a", 1);
Check(r.Outcome == DeathmatchOutcome.Draw, "equal round wins draw finite FFA series");

r = Rules(rounds: 1, ffa: false);
Frag(r, "bot-a"); Frag(r, "bot-a"); Frag(r, "bot-b");
Check(r.Phase == DeathmatchPhase.Live && r.RetainedKillCounters == 0, "all-vs-player ignores opponent kill counts");
Frag(r); Frag(r);
Check(r.Outcome == DeathmatchOutcome.Won, "all-vs-player local target completes round");
r = Rules(rounds: 1, seconds: 5, ffa: false);
Frag(r); r.Tick(5);
Check(r.Outcome == DeathmatchOutcome.Lost && r.LocalRoundLosses == 1, "all-vs-player missed timed target is loss");
r = Rules(rounds: 1, kills: 0, seconds: 5, ffa: false);
Frag(r); r.Tick(5);
Check(r.Outcome == DeathmatchOutcome.Complete && r.LocalRoundWins == 0, "timer-only all-vs-player has neutral completion");
r = Rules(rounds: 2, kills: 1, seconds: 5, ffa: false);
Frag(r); r.StartRound(5); r.Tick(10);
Check(r.Outcome == DeathmatchOutcome.Draw, "all-vs-player equal wins and missed targets draw series");

r = Rules(kills: 0, seconds: 0);
for (int i = 0; i < DeathmatchRules.ContestantCapacity * 2; i++) Frag(r, "bot-" + i, i);
Frag(r); r.RecordLocalDeath(0); r.Tick(double.MaxValue);
Check(r.Phase == DeathmatchPhase.Live && r.RetainedKillCounters == 0 && r.RetainedWinCounters == 0
    && r.LocalKills == 1 && r.LocalDeaths == 1, "continuous play has no breaks or unbounded competitor history");
r = Rules(kills: 0, seconds: 10000);
for (int i = 0; i < DeathmatchRules.ContestantCapacity; i++) Frag(r, "bot-" + i);
Check(r.RetainedKillCounters == DeathmatchRules.ContestantCapacity && r.Phase == DeathmatchPhase.Live,
    "counter capacity itself remains playable");
Frag(r, "overflow");
Check(r.Outcome == DeathmatchOutcome.NoContest && r.RetainedKillCounters == DeathmatchRules.ContestantCapacity,
    "counter overflow must fail honestly without dropping a winner");
r = Rules(rounds: DeathmatchRules.ContestantCapacity + 1, kills: 1);
for (int i = 0; i <= DeathmatchRules.ContestantCapacity; i++)
{
    Frag(r, "winner-" + i);
    if (r.Phase == DeathmatchPhase.Intermission) r.StartRound(0);
}
Check(r.Outcome == DeathmatchOutcome.NoContest && r.RetainedWinCounters == DeathmatchRules.ContestantCapacity,
    "finite match winner history has explicit bounded failure");
r = Rules(rounds: 1);
Check(!r.RecordKill(0, "local", "You", "local") && !r.RecordKill(0, "", "unknown", "victim")
    && !r.RecordKill(0, "local", "You", " ") && !r.RecordKill(double.NaN, "local", "You", "victim")
    && r.LocalKills == 0, "suicides unknown victims and invalid clocks do not award kills");
r.Stop();
Check(r.Phase == DeathmatchPhase.Stopped && r.Outcome == DeathmatchOutcome.None && !r.StartRound(10),
    "ordinary match exit cannot manufacture a round result");

(RoundsMode mode, ArenaMatch match, LocalGame game, GameWorld world) StartMode(
    int rounds = 0, int kills = 1, int seconds = 0, bool respawn = true, bool tickReady = true)
{
    Time.time = Time.unscaledTime = 0;
    ArenaSession.Exact = true;
    ArenaProtection.IsProtectedNow = false;
    ArenaProtection.Requests = 0;
    ArenaRespawnDriver.IsRespawnInProgress = false;
    LiveKitRespawn.Requests = 0;
    LiveKitRespawn.Accepted = false;
    LiveKitRespawn.IsInitialSwapPending = false;
    LiveKitRespawn.ThrowOnRequest = false;
    LiveKitRespawn.SynchronousResult = null;
    LiveKitRespawn.Complete = null;
    LiveKitRespawn.Wanted = null;
    TdmMatchExit.PipelineRunning = false;
    TdmMatchExit.Calls = 0;
    var game = new LocalGame(); var world = new GameWorld();
    game.LocalPlayer = world.MainPlayer;
    Singleton<AbstractGame>.Instance = game;
    Singleton<GameWorld>.Instance = world;
    var match = new ArenaMatch();
    ArenaMatchService.Current = match;
    var mode = new RoundsMode(new(rounds, kills, seconds, true, 3, respawn));
    mode.OnMatchStart(match);
    if (tickReady) mode.Tick(0);
    return (mode, match, game, world);
}
void Score(RoundsMode mode, ArenaMatch match, string killer = "local", string victim = "victim")
{
    if (!mode.TryBeginScoredEvent()) return;
    if (victim == "local" && mode.TryRecordLocalDeath()) mode.OnLocalDeath();
    if (killer == "local") match.Score += 100;
    mode.OnKill(new ArenaKill { KillerProfileId = killer, KillerName = killer, VictimProfileId = victim,
        KilledByLocal = killer == "local", LocalIsVictim = victim == "local" });
}

var c = StartMode(rounds: 1, seconds: 10, tickReady: false);
Check(!c.mode.TryBeginScoredEvent() && c.mode.ShouldBlockMatchDamage(c.world.MainPlayer),
    "mode defers round start until initial swap can register and protects staging body");
LiveKitRespawn.IsInitialSwapPending = true;
Time.time = 50; c.mode.Tick(0);
Check(!c.mode.TryBeginScoredEvent() && !c.mode.HasResult, "initial kit loading cannot spend round clock");
LiveKitRespawn.IsInitialSwapPending = false;
TdmMatchExit.PipelineRunning = true;
c.mode.Tick(0);
Check(!c.mode.TryBeginScoredEvent(), "initial readiness waits for actual native pipeline");
TdmMatchExit.PipelineRunning = false;
c.game.LocalPlayer = new Player(); c.mode.Tick(0);
Check(!c.mode.TryBeginScoredEvent(), "initial readiness requires exact current native body binding");
c.game.LocalPlayer = c.world.MainPlayer;
c.mode.Tick(0);
Check(c.mode.TryBeginScoredEvent() && c.mode.TimeRemaining == 10 && !c.mode.ShouldBlockMatchDamage(c.world.MainPlayer),
    "initial readiness starts full round clock and releases staging protection");
c = StartMode(rounds: 1);
int ended = 0;
c.mode.OnRoundEnded += _ => ended++;
Score(c.mode, c.match, "bot-a", "local");
Check(c.mode.HasResult && c.mode.HoldRespawn && c.mode.ControlsOwnSpawns && !c.mode.InIntermission
    && c.mode.Round.Deaths == 1 && ended == 1 && c.match.Cues == 1,
    "final local victim is counted before immutable terminal hold");
Check(!c.mode.TryRecordLocalDeath(), "driver notification cannot duplicate final local death");
Check(LiveKitRespawn.Requests == 0 && ArenaProtection.Requests == 0,
    "final result never launches intermission respawn or overlapping protection");
Score(c.mode, c.match);
Check(c.mode.Round.Kills == 0 && c.mode.Round.Deaths == 1 && ended == 1, "post-result events do not score or republish round");
Check(c.mode.ShouldBlockMatchDamage(c.world.MainPlayer)
    && !c.mode.ShouldBlockMatchDamage(new Player()), "result protection is exact current body only");
c.mode.Tick(0);
Time.unscaledTime = 14;
c.mode.Tick(0);
Check(TdmMatchExit.Calls == 0, "result display receives full fifteen seconds");
Time.unscaledTime = 15;
c.mode.Tick(0);
Check(TdmMatchExit.Calls == 1 && ReferenceEquals(TdmMatchExit.LastGame, c.game)
    && ReferenceEquals(TdmMatchExit.LastWorld, c.world) && TdmMatchExit.LastProfile == "local",
    "result exit uses captured exact raid and immutable profile");
c.mode.Tick(0);
Check(TdmMatchExit.Calls == 1, "native stop retries are bounded");
c.mode.OnMatchEnd();
Check(ended == 1 && !c.mode.ResultOwnsCurrentRaid && !c.mode.ShouldBlockMatchDamage(c.world.MainPlayer),
    "teardown revokes result ownership without fake round event");

c = StartMode(rounds: 1);
Score(c.mode, c.match);
TdmMatchExit.PipelineRunning = true;
Time.unscaledTime = 100;
c.mode.RequestResultExit(); c.mode.Tick(0);
Check(TdmMatchExit.Calls == 0, "result exit waits for native replacement pipeline");
TdmMatchExit.PipelineRunning = false;
c.mode.Tick(0);
Check(TdmMatchExit.Calls == 1, "explicit exit begins after pipeline settles");
c = StartMode(rounds: 1);
Score(c.mode, c.match);
TdmMatchExit.PipelineRunning = true;
Time.unscaledTime = 100; c.mode.Tick(0);
TdmMatchExit.PipelineRunning = false;
c.mode.Tick(0);
Check(c.mode.ResultDisplayRemaining == 15 && TdmMatchExit.Calls == 0,
    "result display clock excludes native pipeline wait");

c = StartMode();
LiveKitRespawn.Accepted = true;
Score(c.mode, c.match);
var oldCallback = LiveKitRespawn.Complete;
var oldWanted = LiveKitRespawn.Wanted;
Check(c.mode.InIntermission && oldWanted(), "round break requests live replacement");
Time.time = 4;
c.mode.Tick(0);
Check(c.mode.Round.Number == 1, "pending break preload prevents early next round");
Time.time = 34;
c.mode.Tick(0);
Check(c.mode.Round.Number == 2 && !oldWanted(), "stuck break grace revokes old replacement request");
Score(c.mode, c.match);
var currentCallback = LiveKitRespawn.Complete;
oldCallback(false);
Check(c.mode.IntermissionRemaining == 3 && ArenaProtection.Requests == 0,
    "old break callback cannot heal or reset later intermission");
Time.time = 38; c.mode.Tick(0);
Check(c.mode.Round.Number == 2, "old callback must not release current pending respawn");
currentCallback(true);
Time.time = 41; c.mode.Tick(0);
Check(c.mode.Round.Number == 3, "current completed respawn starts complete next round");

c = StartMode();
LiveKitRespawn.Accepted = true;
LiveKitRespawn.SynchronousResult = true;
Score(c.mode, c.match);
Time.time = 3; c.mode.Tick(0);
Check(c.mode.Round.Number == 2, "synchronous native callback cannot leave pending latch stuck");

c = StartMode();
LiveKitRespawn.ThrowOnRequest = true;
ended = 0; c.mode.OnRoundEnded += _ => ended++;
Score(c.mode, c.match);
Time.time = 3; c.mode.Tick(0);
Check(ended == 1 && c.mode.Round.Number == 2 && ArenaProtection.Requests == 1,
    "throwing native request preserves event and safe fallback without pending latch");

c = StartMode(respawn: false);
RoundsMode.RoundState savedRound = null;
c.mode.OnRoundEnded += value => savedRound = value;
Score(c.mode, c.match);
Check(ArenaProtection.Requests == 1 && c.world.MainPlayer.ActiveHealthController.Heals == 1
    && c.world.MainPlayer.Recomputed == 2, "intermission fallback heals and recomputes only local body");
Check(!c.mode.IntermissionProtected, "input freeze cannot outlive actual protection");
ArenaProtection.IsProtectedNow = true;
Check(c.mode.IntermissionProtected, "input can freeze while actual intermission protection holds");
Time.time = 3; c.mode.Tick(0);
Check(c.mode.Round.Number == 2 && c.mode.Round.Kills == 0 && savedRound.Number == 1 && savedRound.Kills == 1,
    "finished event snapshot survives mutable current round reset");

c = StartMode();
LiveKitRespawn.Accepted = true;
Score(c.mode, c.match);
oldCallback = LiveKitRespawn.Complete;
oldWanted = LiveKitRespawn.Wanted;
c.mode.OnMatchEnd(); oldCallback(false);
Check(!oldWanted() && !c.mode.InIntermission && ArenaProtection.Requests == 0,
    "ended raid suppresses pending heal protection and replacement predicate");
c = StartMode();
ended = 0; c.mode.OnRoundEnded += _ => ended++;
c.mode.OnMatchEnd();
Check(ended == 0 && !c.mode.HasResult && ArenaProtection.Requests == 0 && LiveKitRespawn.Requests == 0,
    "manual live raid exit does not award a round or start a break");

c = StartMode(rounds: 1);
Score(c.mode, c.match); c.mode.RequestResultExit();
ArenaSession.Exact = false; c.mode.Tick(0);
Check(TdmMatchExit.Calls == 0 && !c.mode.ShouldBlockMatchDamage(c.world.MainPlayer),
    "ordinary raid cannot inherit result exit or damage protection");
ArenaSession.Exact = true;
Singleton<GameWorld>.Instance = new GameWorld();
c.mode.Tick(0);
Check(TdmMatchExit.Calls == 0, "replacement world cannot inherit terminal operation");
Singleton<GameWorld>.Instance = c.world;
c.game.Status = GameStatus.Stopping;
c.mode.Tick(0);
Check(TdmMatchExit.Calls == 0 && !c.mode.TryBeginScoredEvent(), "native stopping prevents retry and scoring before GameEnd");

Console.WriteLine($"DEATHMATCH_RULES_RUNTIME_PASSED: {assertions} assertions");
