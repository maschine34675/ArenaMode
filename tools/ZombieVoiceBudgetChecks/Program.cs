using ArenaMode;
using ArenaMode.Bots;
using ArenaMode.Compat;
using ArenaMode.Config;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using UnityEngine;

int assertions = NativeContract.Verify(), sequence = 0;
void Check(bool ok, string name) { assertions++; if (!ok) throw new Exception("FAILED: " + name); }
GameWorld world = null;
void Start()
{
    ZombieVoiceBudget.Reset(); PitFireTeamCompat.Team.Clear();
    Settings.ZombieVoiceBudgetEnabled.Value = true;
    Time.realtimeSinceStartup = 0;
    world = new();
    var game = new LocalGame();
    Singleton<GameWorld>.Instance = world; Singleton<AbstractGame>.Instance = game;
    ArenaSession.StartedWorld = world; ArenaSession.StartedGame = game;
    ArenaSession.IsActive = true; ArenaSession.ActiveMode = "zombie";
    world.MainPlayer = new() { ProfileId = "local", IsAI = false, IsYourPlayer = true, GameWorld = world };
    world.Alive["local"] = world.MainPlayer;
    Plugin.ModLogger.Infos.Clear(); Plugin.ModLogger.Warnings.Clear();
}
Player Bot(float distance = 30)
{
    var body = new Player { ProfileId = "z" + sequence++, GameWorld = world, Position = new(distance) };
    body.AIData.BotOwner = new() { GetPlayer = body };
    world.Alive[body.ProfileId] = body;
    return body;
}
Player[] Fill(int count = 4, float distance = 30)
{
    var bots = new Player[count];
    for (int i = 0; i < count; i++) { bots[i] = Bot(distance); Check(bots[i].Say(EPhraseTrigger.OnMutter), "fill " + i); }
    return bots;
}
string Report()
{
    Time.realtimeSinceStartup += 31;
    ZombieVoiceBudget.Tick();
    return Plugin.ModLogger.Infos.Last();
}

ZombieVoiceBudget.EnablePatch(); ZombieVoiceBudget.EnablePatch();
Check(SPT.Reflection.Patching.ModulePatch.Installed.Count == 1, "install idempotent");
Check(SPT.Reflection.Patching.ModulePatch.Installed[0].Name == "OnPhraseTold", "audio-only target");
Start(); Fill();
var refused = Bot();
Check(!refused.Say(EPhraseTrigger.OnMutter), "fifth distant refused");
Check(refused.SourceCreations == 0 && refused.OriginalCalls == 0, "skip before source allocation");
Check(refused.GlobalPhraseEvents == 1 && refused.Speaker.Busy && refused.Speaker.OtherSubscriberCalls == 1
    && refused.Speaker.ChainCalls == 1, "AI event, speaker state, subscriber and chain preserved");
Check(Bot(10).Say(EPhraseTrigger.OnMutter) && Bot(15).Say(EPhraseTrigger.OnMutter), "two nearby slots reserved");
Check(!Bot(10).Say(EPhraseTrigger.OnMutter), "six actual repetitive sources maximum");
Check(Bot(-8).Say(EPhraseTrigger.OnEnemyConversation), "attack behind player exempt at eight metres");
Check(!Bot(-8.01f).Say(EPhraseTrigger.OnEnemyConversation), "attack outside exemption competes");
Check(!Bot(2).Say(EPhraseTrigger.OnMutter), "near mutter not threat exempt");
foreach (var phrase in new[] { EPhraseTrigger.OnFight, EPhraseTrigger.OnAgony, EPhraseTrigger.OnBeingHurt, EPhraseTrigger.OnDeath, EPhraseTrigger.Other })
    Check(Bot().Say(phrase), "critical/other native " + phrase);
string report = Report();
Check(report.Contains("skipped=4") && report.Contains("wouldSkip=4") && report.Contains("exempt=6"), "aggregate counts");

foreach (var role in new[] { WildSpawnType.infectedAssault, WildSpawnType.infectedCivil, WildSpawnType.infectedLaborant, WildSpawnType.infectedPmc })
{
    Start(); Fill(); var body = Bot(); body.Profile.Info.Settings.Role = role;
    Check(!body.Say(EPhraseTrigger.OnMutter), "generic role budgeted " + role);
}
foreach (var alter in new Action<Player>[] {
    b => b.Profile.Info.Settings.Role = WildSpawnType.infectedTagilla,
    b => b.Profile.Info.Settings.Role = WildSpawnType.pmcUSEC,
    b => b.IsAI = false, b => b.IsYourPlayer = true,
    b => b.HealthController.IsAlive = false,
    b => b.AIData.BotOwner.IsDead = true,
    b => b.AIData.BotOwner.BotState = EBotState.PreActive,
    b => b.AIData.BotOwner.Follower = true,
    b => b.AIData.BotOwner.BotFollower.HaveBoss = true,
    b => PitFireTeamCompat.Team.Add(b),
    b => b.AIData.BotOwner.Brain.BaseBrain = new CustomInfectedBrain(),
    b => b.AIData.BotOwner.Brain.BaseBrain = null,
    b => b.AIData.BotOwner.GetPlayer = new(),
    b => b.GameWorld = new(),
    b => world.Alive[b.ProfileId] = new(),
    b => b.Profile.Info.Settings = null,
    b => b.Position = new(float.NaN),
    b => b.Position = new(float.PositiveInfinity),
    b => world.MainPlayer.HealthController.IsAlive = false,
    b => world.Alive["local"] = new(),
    b => ArenaSession.IsActive = false,
    b => ArenaSession.ActiveMode = "deathmatch",
    b => Singleton<AbstractGame>.Instance = new LocalGame(),
    b => Singleton<GameWorld>.Instance = new GameWorld(),
    b => ((LocalGame)Singleton<AbstractGame>.Instance).Status = GameStatus.Stopping,
    b => world.ThrowLookup = true })
{
    Start(); Fill(); var body = Bot(); alter(body);
    Check(body.Say(EPhraseTrigger.OnMutter), "scope/identity/failure keeps native " + assertions);
}

foreach (var interrupt in new Action<Player>[] {
    b => b.ExistingSource.source1.isPlaying = false,
    b => b.ExistingSource.source1.clip = new(),
    b => b.ExistingSource.Destroyed = true,
    b => b.Destroyed = true,
    b => b.HealthController.IsAlive = false,
    b => world.Alive[b.ProfileId] = new(),
    b => b.SpeechSource = new(),
    b => b.Say(EPhraseTrigger.OnFight) })
{
    Start(); var bodies = Fill(); interrupt(bodies[0]);
    Check(Bot().Say(EPhraseTrigger.OnMutter), "source end/interruption/death/stale frees capacity " + assertions);
}
Start();
Singleton<AbstractGame>.Instance = new LocalGame();
for (int i = 0; i < 10; i++) Check(Bot().Say(EPhraseTrigger.OnMutter), "missed-end stale game never budgets subsequent voices");
Start();
world = new GameWorld(); Singleton<GameWorld>.Instance = world;
world.MainPlayer = new Player { ProfileId = "local", IsAI = false, IsYourPlayer = true, GameWorld = world };
world.Alive["local"] = world.MainPlayer;
for (int i = 0; i < 10; i++) Check(Bot().Say(EPhraseTrigger.OnMutter), "missed-end stale world never budgets current replacement bodies");
Start(); var moving = Fill(); moving[0].Position = new(10);
Check(Bot().Say(EPhraseTrigger.OnMutter), "distance re-evaluated, distant voice became nearby");
Start(); var existing = Fill();
Check(existing[0].Say(EPhraseTrigger.OnMutter), "replacement reuses existing slot");
existing[0].NativeInRange = false;
Check(existing[0].Say(EPhraseTrigger.OnMutter), "native decline leaves old source playing");
Check(!Bot().Say(EPhraseTrigger.OnMutter), "declined replacement does not free old voice");
Start();
for (int i = 0; i < 10; i++) { var b = Bot(); b.NativeInRange = false; Check(b.Say(EPhraseTrigger.OnMutter), "out of range passes native"); }
for (int i = 0; i < 10; i++) { var b = Bot(); b.NativeCanPlay = false; Check(b.Say(EPhraseTrigger.OnMutter), "disabled source passes native"); }
Fill(); Check(!Bot().Say(EPhraseTrigger.OnMutter), "unplayed clips never reserve slots");
Check(Report().Contains("nativeUnplayed=20"), "native unplayed aggregate");

Start(); Fill(); Settings.ZombieVoiceBudgetEnabled.Value = false;
Check(Bot().Say(EPhraseTrigger.OnMutter), "disabled budget retains native fifth");
Check(Bot().Say(EPhraseTrigger.OnMutter), "disabled budget retains native sixth");
report = Report();
Check(report.Contains("enabled=False") && report.Contains("skipped=0") && report.Contains("wouldSkip=2"), "baseline reports real occupancy would-skip");
Settings.ZombieVoiceBudgetEnabled.Value = true;
Check(!Bot().Say(EPhraseTrigger.OnMutter), "toggle retains already playing baseline voices");
Start(); Fill(); ArenaSession.IsActive = false; ZombieVoiceBudget.Tick();
ArenaSession.IsActive = true;
Check(Bot().Say(EPhraseTrigger.OnMutter), "inactive tick clears observations");
Start(); Fill();
var old = Bot();
Check(ZombieVoiceBudget.BeforePhrase(old, EPhraseTrigger.OnMutter, new(), new(), out var badSpeaker), "foreign speaker fails open");
Check(!badSpeaker.Track, "foreign speaker never tracked");
Start();
var throws = Bot(); throws.ThrowOriginal = true;
try { throws.Say(EPhraseTrigger.OnMutter); throw new Exception("expected native exception"); }
catch (InvalidOperationException) { }
Fill(); Check(!Bot().Say(EPhraseTrigger.OnMutter), "throwing original never reserves a phantom slot");
Start();
var longVoice = Fill();
Time.realtimeSinceStartup += 500;
Check(!Bot().Say(EPhraseTrigger.OnMutter), "elapsed wall time does not expire still-playing voices");
longVoice[0].ExistingSource.source1.isPlaying = false;
Check(Bot().Say(EPhraseTrigger.OnMutter), "actual playback end releases even long-running clip");
Start(); var deferred = Bot(); var deferredClip = new TaggedClip();
Check(ZombieVoiceBudget.BeforePhrase(deferred, EPhraseTrigger.OnMutter, deferredClip, deferred.Speaker, out var state), "capture before playback");
Start(); deferred.OnPhraseTold(EPhraseTrigger.OnMutter, deferredClip, new(), deferred.Speaker);
ZombieVoiceBudget.AfterPhrase(state); Fill(); Check(!Bot().Say(EPhraseTrigger.OnMutter), "stale postfix cannot contaminate new world");

// Repeated admission after warmup must avoid managed per-call allocations.
Start(); Fill(); var allocationBody = Bot(); var allocationClip = new TaggedClip();
for (int i = 0; i < 100; i++) ZombieVoiceBudget.BeforePhrase(allocationBody, EPhraseTrigger.OnMutter, allocationClip, allocationBody.Speaker, out _);
long before = GC.GetAllocatedBytesForCurrentThread();
for (int i = 0; i < 1000; i++) ZombieVoiceBudget.BeforePhrase(allocationBody, EPhraseTrigger.OnMutter, allocationClip, allocationBody.Speaker, out _);
Check(GC.GetAllocatedBytesForCurrentThread() - before == 0, "steady admission allocates zero bytes in native doubles");
Console.WriteLine($"ZOMBIE_VOICE_BUDGET_CHECKS_PASSED assertions={assertions}");
