// Production-linked checks of the ArenaMode side of the crowd reactions (phase B of
// D:\ArenaMapPort\docs\PLAN-CROWD-REACTIONS.md): the optional SpectatorCrowd binder, the event
// mapping and coalescing policy, and the presentation service's lifecycle, driven through the
// same three ArenaMatch events, ArenaEvents.OnPlayerDied and the Deathmatch round events the
// game raises. Contract: docs/reviews/CROWD-REACTIONS-20261102.md.
using System.Reflection;
using ArenaMode;
using ArenaMode.Audio;
using ArenaMode.Combat;
using ArenaMode.Compat;
using ArenaMode.Events;
using ArenaMode.Modes;
using ArenaMode.Session;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using static ArenaMode.Compat.ArenaMapPortSpectatorCrowd;

int checks = 0;
void Check(bool condition, string label)
{
    checks++;
    if (!condition) throw new InvalidOperationException("FAIL: " + label);
}
bool Near(float actual, float expected) => Math.Abs(actual - expected) < 1e-5f;
var log = Plugin.ModLogger;

// ---------------------------------------------------------------- binder
FakeCrowd.Reset(); log.Clear();
Check(Bit(0) == 0 && Bit(32) == 0 && Bit(33) == 0 && Bit(-1) == 0 && Bit(1) == 2 && Bit(5) == 32
    && Bit(31) == 1 << 31, "kinds outside 1..31 have no bit");
var full = BindContract(typeof(ArenaMapPort.Client.SpectatorCrowd));
Check(full.Excitement == Part.Bound && full.Reactions == Part.Bound && full.SetExcitement != null
    && full.IsAvailable != null && full.React != null && log.Warnings.Count == 0, "full phase A surface binds both parts");
var v040 = BindContract(typeof(Variants.V040));
Check(v040.Excitement == Part.Bound && v040.Reactions == Part.Absent && v040.SetExcitement != null
    && v040.ReactionKinds == null && v040.React == null && log.Warnings.Count == 0,
    "ArenaMapPort 0.4.0 surface binds excitement without the reaction members");
var noExcitement = BindContract(typeof(Variants.NoExcitement));
Check(noExcitement.Excitement == Part.Absent && noExcitement.Reactions == Part.Bound && noExcitement.SetExcitement == null
    && log.Warnings.Count == 0, "reactions bind without SetExcitement");
var partial = BindContract(typeof(Variants.PartialReactions));
Check(partial.Reactions == Part.Absent && partial.Excitement == Part.Bound && partial.ClaimReactions == null
    && log.Warnings.Count == 0, "an incomplete reaction set reads as an older MapPort, silently");
var overloaded = BindContract(typeof(Variants.OverloadedReact));
Check(overloaded.Reactions == Part.Bound && log.Warnings.Count == 0, "an extra overload does not hide the exact member");
var wrongReact = BindContract(typeof(Variants.WrongReact));
BindContract(typeof(Variants.WrongReact));
Check(wrongReact.Reactions == Part.Incompatible && wrongReact.React == null && wrongReact.Excitement == Part.Bound
    && log.Warnings.Count == 1 && log.Warnings[0].Contains("reaction members"),
    "a changed reaction member switches reactions off with one warning per process");
var convertible = BindContract(typeof(Variants.ConvertibleClaim));
var fieldKinds = BindContract(typeof(Variants.FieldKinds));
Check(convertible.Reactions == Part.Incompatible && convertible.ClaimReactions == null
    && convertible.Excitement == Part.Bound && log.Warnings.Count == 1,
    "contravariant object claim is not the exact string contract");
Check(fieldKinds.Reactions == Part.Incompatible, "a field is not the ReactionKinds property");
// The reaction warning was written above; these reasons share it (one line per process).
var internalKinds = BindContract(typeof(Variants.InternalKinds));
Check(internalKinds.Reactions == Part.Incompatible && internalKinds.ReactionKinds == null
    && internalKinds.Excitement == Part.Bound && log.Warnings.Count == 1,
    "an internal reaction member is a changed member, not an older MapPort");
var reactReturn = BindContract(typeof(Variants.WrongReactReturn));
var releaseReturn = BindContract(typeof(Variants.WrongReleaseReturn));
Check(reactReturn.Reactions == Part.Incompatible && reactReturn.React == null && reactReturn.Excitement == Part.Bound
    && releaseReturn.Reactions == Part.Incompatible && releaseReturn.ReleaseReactions == null
    && releaseReturn.Excitement == Part.Bound && log.Warnings.Count == 1,
    "a changed return type switches only the reactions off");
var wrongExcitement = BindContract(typeof(Variants.WrongExcitement));
var wrongAvailable = BindContract(typeof(Variants.WrongAvailable));
Check(wrongExcitement.Excitement == Part.Incompatible && wrongExcitement.SetExcitement == null
    && wrongExcitement.Reactions == Part.Bound && wrongExcitement.React != null
    && wrongAvailable.Excitement == Part.Incompatible && wrongAvailable.Reactions == Part.Bound
    && log.Warnings.Count == 2 && log.Warnings[1].Contains("murmur intensity"),
    "a changed SetExcitement or IsAvailable switches only the murmur off, with one warning");
var two = BindContract(typeof(Variants.ContractTwo));
BindContract(typeof(Variants.ContractTwo));
Check(two.Excitement == Part.Incompatible && two.Reactions == Part.Incompatible && two.SetExcitement == null
    && two.React == null && log.Warnings.Count == 3 && log.Warnings[2].Contains("version 2"),
    "contract version 2 switches both parts off with one warning");
// Its own reason, so its own line even after the version-2 warning.
var noVersion = BindContract(typeof(Variants.NoVersion));
var fieldVersion = BindContract(typeof(Variants.FieldVersion));
Check(noVersion.Excitement == Part.Incompatible && noVersion.Reactions == Part.Incompatible
    && fieldVersion.Excitement == Part.Incompatible && log.Warnings.Count == 4
    && log.Warnings[3].Contains("no int ContractVersion"),
    "a contract without an int ContractVersion property binds nothing");
FakeCrowd.ThrowVersion = true;
var throwing = BindContract(typeof(Variants.ThrowingVersion));
FakeCrowd.ThrowVersion = false;
Check(throwing.Excitement == Part.Incompatible && throwing.Reactions == Part.Incompatible && log.Warnings.Count == 5,
    "a throwing contract member is contained");
var none = BindContract(null);
Check(none.Excitement == Part.Absent && none.Reactions == Part.Absent && log.Warnings.Count == 5,
    "no SpectatorCrowd type is an older MapPort");
Chainloader.PluginInfos.Clear();
Check(Bind().Excitement == Part.Absent && Bind().Summary.Contains("not loaded"), "plugin absent is normal");
Chainloader.PluginInfos[PluginGuid] = new PluginInfo { Instance = new object() };
Check(Bind().Reactions == Part.Absent, "plugin without the contract type is an older MapPort");
Chainloader.PluginInfos[PluginGuid] = new PluginInfo { Instance = new ArenaMapPort.Client.PluginAnchor() };
Check(Bind().Reactions == Part.Bound && Bind().Excitement == Part.Bound, "the loaded plugin's contract binds");
log.Clear();

// Claim object: mask check, exact handle, release once, refusal releases.
FakeCrowd.Reset();
Check(ReactionClaim.TryClaim(full, " ", FakeCrowd.AllKinds, out _, out _) == null && FakeCrowd.Claims == 0, "empty owner never claims");
FakeCrowd.Kinds = 0;
Check(ReactionClaim.TryClaim(full, "owner", FakeCrowd.AllKinds, out int noKinds, out bool noKindsFailed) == null
    && noKinds == 0 && !noKindsFailed && FakeCrowd.Claims == 0, "no claim on a map without reaction kinds");
FakeCrowd.Kinds = 1 << TakeCover;
Check(ReactionClaim.TryClaim(full, "owner", Bit(Approve) | Bit(Finish), out _, out _) == null && FakeCrowd.Claims == 0,
    "no claim when none of our kinds is offered");
FakeCrowd.Reset();
var directClaim = ReactionClaim.TryClaim(full, "owner", FakeCrowd.AllKinds, out _, out _);
Check(directClaim != null && directClaim.Alive && directClaim.Handle == FakeCrowd.Handle, "positive claim control");
Check(directClaim.Push(Approve, 0.5f) && FakeCrowd.Reactions.Count == 1, "accepted reaction keeps the claim");
Check(directClaim.Push(Approve, float.NaN) && FakeCrowd.ReactCalls == 1, "NaN is never sent");
directClaim.Release(); directClaim.Release();
Check(!directClaim.Alive && FakeCrowd.Releases == 1 && FakeCrowd.Handle == 0, "release exactly once");
FakeCrowd.Reset(); directClaim = ReactionClaim.TryClaim(full, "owner", FakeCrowd.AllKinds, out _, out _);
FakeCrowd.ReactResult = false;
Check(!directClaim.Push(Approve, 0.5f) && !directClaim.Alive && FakeCrowd.Releases == 1, "refused reaction releases the claim");

// ---------------------------------------------------------------- mapping table
long bodies = 1000;
ArenaKill LocalKill(string damage = "Bullet", bool head = false, bool eligible = true) => new()
{
    KilledByLocal = true, KillerProfileId = "local", VictimProfileId = "bot" + bodies, DamageKind = damage,
    Headshot = head, ScoreEligible = eligible, VictimBodyId = ++bodies,
};
ArenaKill LocalDeath(Player body, bool eligible = true) => new()
{
    LocalIsVictim = true, KillerProfileId = "bot", VictimProfileId = "local", DamageKind = "Bullet",
    ScoreEligible = eligible, VictimBodyId = ArenaHitHistory.BodyId(body),
};
ArenaKill OtherKill(bool? killerFriendly, bool? victimFriendly) => new()
{
    KillerProfileId = "a", VictimProfileId = "b", DamageKind = "Bullet", VictimBodyId = ++bodies,
    KillerIsFriendly = killerFriendly, VictimIsFriendly = victimFriendly,
};
bool KillMaps(ArenaKill kill, int kind, float strength)
    => ArenaCrowdReactionPolicy.TryMapKill(kill, out int k, out float s, out _) && k == kind && Near(s, strength);

Check(KillMaps(LocalKill(), Approve, 0.35f) && KillMaps(LocalKill("Flame"), Approve, 0.35f), "own kill approves at 0.35");
Check(KillMaps(LocalKill("Melee"), Approve, 0.6f) && KillMaps(LocalKill("Explosion"), Approve, 0.6f)
    && KillMaps(LocalKill("GrenadeFragment"), Approve, 0.6f) && KillMaps(LocalKill("Bullet", head: true), Approve, 0.6f),
    "melee, explosion, fragment and headshot kills approve at 0.6");
Check(KillMaps(LocalDeath(new Player()), Disapprove, 0.5f), "own death disapproves at 0.5");
Check(!ArenaCrowdReactionPolicy.TryMapKill(LocalKill(eligible: false), out _, out _, out _)
    && !ArenaCrowdReactionPolicy.TryMapKill(LocalDeath(new Player(), eligible: false), out _, out _, out _),
    "unscored kills are ignored");
Check(!ArenaCrowdReactionPolicy.TryMapKill(OtherKill(null, null), out _, out _, out _)
    && !ArenaCrowdReactionPolicy.TryMapKill(OtherKill(false, true), out _, out _, out _)
    && !ArenaCrowdReactionPolicy.TryMapKill(OtherKill(true, true), out _, out _, out _)
    && !ArenaCrowdReactionPolicy.TryMapKill(null, out _, out _, out _), "bot-versus-bot kills are ignored");
// D5: a friendly bot killing an opponent is a bot-versus-bot kill too (the local player is the
// only human); the plan's optional teammate row was not taken.
Check(!ArenaCrowdReactionPolicy.TryMapKill(OtherKill(true, false), out _, out _, out _),
    "a teammate's kill of an opponent is silent");
foreach (var label in new[] { "FIRST BLOOD", "PAYBACK", "COLLATERAL", "SHUTDOWN", "AIRBORNE", "NO SCOPE",
    "DOUBLE KILL", "TRIPLE KILL", "ULTRA KILL", "MONSTER KILL", "KILLING SPREE", "RAMPAGE", "DOMINATING",
    "UNSTOPPABLE", "GODLIKE" })
    Check(ArenaCrowdReactionPolicy.TryMapCallout(label, out float s) && Near(s, 0.9f), "award callout approves at 0.9: " + label);
Check(ArenaCrowdReactionPolicy.TryMapCallout("HEADSHOT", out float headshot) && Near(headshot, 0.6f),
    "HEADSHOT stays on the strong-kill path");
foreach (var label in new[] { null, "", "NICE THROW, GENIUS!", "WAVE 3", "headshot", "Round 2 — fight!" })
    Check(!ArenaCrowdReactionPolicy.TryMapCallout(label, out _), "other text is silent: " + label);
var cueTable = new Dictionary<ArenaAnnouncerCue, (int kind, float strength)>
{
    [ArenaAnnouncerCue.RoundStart] = (Start, 1f),
    [ArenaAnnouncerCue.RoundWon] = (Finish, 1f), [ArenaAnnouncerCue.RoundLost] = (Finish, 1f),
    [ArenaAnnouncerCue.MatchWon] = (Finish, 1f), [ArenaAnnouncerCue.MatchLost] = (Finish, 1f),
    [ArenaAnnouncerCue.Draw] = (Finish, 1f),
    // A lost lead sits below MapPort's 0.4 band: laments only (plan 6.3 as revised in 08964c1).
    [ArenaAnnouncerCue.TakenTheLead] = (Approve, 0.5f), [ArenaAnnouncerCue.LostTheLead] = (Disapprove, 0.3f),
    [ArenaAnnouncerCue.LastManStanding] = (Approve, 0.9f), [ArenaAnnouncerCue.Clutch] = (Approve, 0.9f),
    [ArenaAnnouncerCue.FinalWeapon] = (Approve, 0.9f), [ArenaAnnouncerCue.LadderComplete] = (Approve, 0.9f),
    [ArenaAnnouncerCue.Shutdown] = (Approve, 0.9f),
};
foreach (var cue in Enum.GetValues<ArenaAnnouncerCue>())
{
    bool mapped = ArenaCrowdReactionPolicy.TryMapCue(cue, out int kind, out float strength);
    if (cueTable.TryGetValue(cue, out var expected))
        Check(mapped && kind == expected.kind && Near(strength, expected.strength), "cue mapping " + cue);
    else
        Check(!mapped, "countdown, status, milestone and no-contest cues are silent: " + cue);
}

// ---------------------------------------------------------------- excitement mapping
int StepOf(ArenaAnnouncementSnapshot snapshot) => ArenaCrowdReactionPolicy.ExcitementStep(snapshot);
ArenaAnnouncementSnapshot Live(bool finalRound = false, bool matchPoint = false, double? seconds = null, int? kills = null,
    bool terminal = false, bool live = true, bool active = true)
    => new() { Active = active, Live = live, Terminal = terminal, FinalRound = finalRound, MatchPoint = matchPoint,
        SecondsRemaining = seconds, KillsRemaining = kills };
Check(StepOf(default) == 0 && StepOf(Live()) == 0, "excitement is 0 without pressure");
Check(StepOf(Live(true, true, 0, 1, terminal: true)) == 0 && StepOf(Live(true, true, 0, 1, live: false)) == 0
    && StepOf(Live(true, true, 0, 1, active: false)) == 0, "excitement is 0 when not Live or Terminal");
Check(StepOf(Live(finalRound: true)) == 12 && StepOf(Live(matchPoint: true)) == 8
    && StepOf(Live(finalRound: true, matchPoint: true)) == 12, "final round 0.6, match point 0.4");
Check(StepOf(Live(seconds: 120)) == 0 && StepOf(Live(seconds: 60)) == 0 && StepOf(Live(seconds: 30)) == 6
    && StepOf(Live(seconds: 0)) == 12 && StepOf(Live(seconds: -5)) == 12 && StepOf(Live(seconds: double.NaN)) == 0
    && StepOf(Live(seconds: double.PositiveInfinity)) == 0, "the last minute rises to 0.6");
Check(StepOf(Live(kills: 6)) == 0 && StepOf(Live(kills: 5)) == 2 && StepOf(Live(kills: 3)) == 7
    && StepOf(Live(kills: 1)) == 12 && StepOf(Live(kills: 0)) == 12, "the last five kills rise to 0.6");
Check(StepOf(Live(true, false, 0, 1)) == 19 && StepOf(Live(true, false, 30)) == 14, "pressures stack below 1");
int previous = -1;
for (double seconds = 120; seconds >= -1; seconds -= 0.25)
{
    int step = StepOf(Live(seconds: seconds));
    Check(step >= previous && step >= 0 && step <= 20, "excitement is monotone in the clock");
    previous = step;
}
foreach (bool finalRound in new[] { false, true })
foreach (bool matchPoint in new[] { false, true })
for (int kills = 10; kills >= 0; kills--)
for (double seconds = 90; seconds >= 0; seconds -= 7.5)
{
    int baseStep = StepOf(Live(finalRound, matchPoint, seconds, kills));
    Check(StepOf(Live(finalRound, matchPoint, seconds - 7.5, kills)) >= baseStep
        && StepOf(Live(finalRound, matchPoint, seconds, kills - 1)) >= baseStep
        && StepOf(Live(true, matchPoint, seconds, kills)) >= baseStep
        && StepOf(Live(finalRound, true, seconds, kills)) >= baseStep
        && baseStep <= 20, "every pressure raises the excitement monotonically");
}

// ---------------------------------------------------------------- frame coalescing
var frame = new ArenaCrowdReactionPolicy.Frame();
frame.Add(Approve, 0.35f, "kill"); frame.Add(Approve, 0.9f, "DOUBLE KILL"); frame.Add(Approve, 0.6f, "strong kill");
Check(frame.TryTake(Approve, out float best, out string bestSource) && Near(best, 0.9f) && bestSource == "DOUBLE KILL"
    && !frame.TryTake(Approve, out _, out _), "per kind the strongest entry survives, once");
frame.Add(Approve, 0.35f, "kill"); frame.Add(Disapprove, 0.5f, "death"); frame.Add(Finish, 1f, "MatchWon"); frame.Resolve();
Check(!frame.TryTake(Approve, out _, out _) && !frame.TryTake(Disapprove, out _, out _) && frame.TryTake(Finish, out _, out _),
    "a Finish drops the frame's Approve and Disapprove");
frame.Add(Approve, 0.35f, "kill"); frame.Add(Start, 1f, "RoundStart"); frame.Resolve();
Check(!frame.TryTake(Approve, out _, out _) && frame.TryTake(Start, out _, out _), "a Start drops the frame's Approve");
frame.Add(0, 1f, "x"); frame.Add(7, 1f, "purified team"); frame.Add(Approve, float.NaN, "nan"); frame.Add(Approve, -1f, "neg");
Check(frame.IsEmpty, "unknown kinds and non-positive strengths are ignored");
frame.Add(Approve, 3f, "over"); Check(frame.TryTake(Approve, out float clamped, out _) && Near(clamped, 1f), "strength is clamped to 1");
frame.Add(Approve, 0.35f, "kill"); frame.Add(Disapprove, 0.5f, "death"); frame.Add(Lull, 0.4f, "lull");
bool lullIsRoundEvent = frame.HasRoundEvent; frame.Resolve();
Check(!lullIsRoundEvent && frame.TryTake(Approve, out _, out _) && frame.TryTake(Disapprove, out _, out _)
    && frame.TryTake(Lull, out float lullStrength, out _) && Near(lullStrength, 0.4f) && frame.IsEmpty,
    "a Lull is no round event and drops nothing");
frame.Add(Lull, 0.4f, "lull"); frame.Add(Finish, 1f, "MatchWon"); frame.Resolve();
Check(!frame.TryTake(Lull, out _, out _) && frame.TryTake(Finish, out _, out _), "a Finish drops the frame's Lull");
frame.Add(Lull, 0.4f, "lull"); frame.Add(Start, 1f, "RoundStart"); frame.Resolve();
Check(!frame.TryTake(Lull, out _, out _) && frame.TryTake(Start, out _, out _), "a Start drops the frame's Lull");
Check(Array.IndexOf(ArenaCrowdReactionPolicy.Frame.Order, Lull) == ArenaCrowdReactionPolicy.Frame.Order.Length - 1,
    "the Lull is sent last (plan 4.1 priority)");

// ---------------------------------------------------------------- service
int binds = 0;
Func<Contract> Surface(Type type) => () => { binds++; return BindContract(type); };
LocalGame game = null;
ArenaMatch NewMatch(IArenaMode mode)
{
    ArenaCrowdReactions.Detach();
    FakeCrowd.Reset(); log.Clear(); ArenaAnnouncementDirector.Reset(); binds = 0;
    AnnouncerService.IsPmcVoiceBlocked = false;
    game = new LocalGame();
    var world = new GameWorld();
    Singleton<AbstractGame>.Instance = game; Singleton<GameWorld>.Instance = world;
    ArenaSession.StartedGame = game; ArenaSession.StartedWorld = world;
    ArenaSession.ExactRaid = true; ArenaSession.CrowdReactionsSelection = true;
    ArenaMatchService.TeamStartAbortPending = false;
    var match = new ArenaMatch(mode);
    ArenaMatchService.Current = match;
    return match;
}
void Frame(float seconds = 0.1f) { UnityEngine.Time.unscaledTime += seconds; ArenaCrowdReactions.Tick(); }
// Approve and Disapprove go out one tick after their tick (the hold for a result published a tick
// later); Start and Finish in their own tick. Two ticks settle a moment.
void Settle() { Frame(); Frame(); }
void Run(float seconds) { for (float t = 0; t < seconds; t += 0.1f) Frame(); }
// The announcement epoch is the mode's round identity; no pressure, so the murmur stays at 0.
ArenaAnnouncementSnapshot Epoch(int epoch, bool live = true) => new() { Active = true, Live = live, Preparing = !live, Epoch = epoch };
bool Sent(params (int kind, float strength)[] expected)
{
    bool same = FakeCrowd.Reactions.Count == expected.Length;
    for (int i = 0; same && i < expected.Length; i++)
        same = FakeCrowd.Reactions[i].kind == expected[i].kind && Near(FakeCrowd.Reactions[i].strength, expected[i].strength);
    FakeCrowd.Reactions.Clear();
    return same;
}
// A second match of the same raid, the crowd stand-in keeps its holder.
ArenaMatch NewMatchKeepingCrowd(IArenaMode mode)
{
    var match = new ArenaMatch(mode);
    ArenaMatchService.Current = match;
    return match;
}
ArenaMatch Claimed(IArenaMode mode, Type surface = null)
{
    var match = NewMatch(mode);
    ArenaCrowdReactions.Attach(match, Surface(surface ?? typeof(ArenaMapPort.Client.SpectatorCrowd)));
    Frame();
    return match;
}

// Claim and owner.
var tdm = Claimed(new TeamDeathmatchMode());
Check(FakeCrowd.Claims == 1 && FakeCrowd.Handle > 0 && binds == 1, "the first gated tick claims once");
string firstOwner = FakeCrowd.SentOwners[0];
Check(firstOwner.StartsWith(ArenaCrowdReactions.OwnerPrefix, StringComparison.Ordinal) && firstOwner.Length == 40
    && firstOwner == FakeCrowd.Owners[0], "owner is unique per match and exactly 40 characters");
Check(tdm.KillSubscribers == 2 && tdm.CueSubscribers == 1 && ArenaEvents.DiedSubscribers == 1,
    "a bound reaction part listens to the match");

// Mapping through the real events.
tdm.Kill(LocalKill()); Frame();
Check(Sent(), "an own kill waits one tick for a result published a tick later");
Frame();
Check(Sent((Approve, 0.35f)), "own kill reaches the crowd once");
tdm.Callout("DOUBLE KILL"); tdm.Kill(LocalKill()); Settle();
Check(Sent((Approve, 0.9f)), "callout and kill in one frame give one Approve at the award strength");
tdm.Callout("HEADSHOT"); tdm.Kill(LocalKill("Bullet", head: true)); Settle();
Check(Sent((Approve, 0.6f)), "headshot kill and its award give one Approve at 0.6");
tdm.Kill(OtherKill(true, false)); tdm.Kill(OtherKill(null, null)); tdm.Kill(OtherKill(false, true));
tdm.Kill(LocalKill(eligible: false)); Settle();
Check(Sent(), "bot-versus-bot (a teammate's included) and unscored kills stay silent");
tdm.Cue(ArenaAnnouncerCue.TakenTheLead); Frame();
tdm.Cue(ArenaAnnouncerCue.LostTheLead); Frame();
tdm.Cue(ArenaAnnouncerCue.TiedForTheLead); Settle();
Check(Sent((Approve, 0.5f), (Disapprove, 0.3f)), "lead taken reacts at 0.5, lead lost at 0.3, a tie stays silent");
foreach (var cue in new[] { ArenaAnnouncerCue.LastManStanding, ArenaAnnouncerCue.Clutch, ArenaAnnouncerCue.FinalWeapon,
    ArenaAnnouncerCue.LadderComplete, ArenaAnnouncerCue.Shutdown })
{
    tdm.Cue(cue); Settle();
    Check(Sent((Approve, 0.9f)), "team moment cue approves at 0.9: " + cue);
}
foreach (var cue in new[] { ArenaAnnouncerCue.PrepareToFight, ArenaAnnouncerCue.Three, ArenaAnnouncerCue.Two,
    ArenaAnnouncerCue.One, ArenaAnnouncerCue.CancelStatus, ArenaAnnouncerCue.AdvanceStatus,
    ArenaAnnouncerCue.OneMinuteRemaining, ArenaAnnouncerCue.ThirtySecondsRemaining, ArenaAnnouncerCue.TenSecondsRemaining,
    ArenaAnnouncerCue.FiveKillsRemaining, ArenaAnnouncerCue.OneKillRemaining, ArenaAnnouncerCue.MatchPoint,
    ArenaAnnouncerCue.FinalRound, ArenaAnnouncerCue.Embarrassing })
    tdm.Cue(cue);
Frame();
Check(Sent(), "countdown, status and milestone cues are silent");
int debugBefore = log.Debugs.Count, callsBefore = FakeCrowd.ReactCalls;
Run(10);
Check(Sent() && log.Debugs.Count == debugBefore && FakeCrowd.ReactCalls == callsBefore,
    "nothing is sent or logged when nothing happened");

// Local death: one decision per body across OnKill and ArenaEvents.OnPlayerDied.
var body = new Player();
tdm.Kill(LocalDeath(body)); ArenaEvents.RaisePlayerDied(body); Settle();
Check(Sent((Disapprove, 0.5f)), "local death via OnKill and OnPlayerDied gives one Disapprove");
body = new Player();
tdm.Kill(LocalDeath(body)); Frame(); ArenaEvents.RaisePlayerDied(body); Settle();
Check(Sent((Disapprove, 0.5f)), "local death notified in two frames still gives one Disapprove");
body = new Player();
ArenaEvents.RaisePlayerDied(body); Frame(); tdm.Kill(LocalDeath(body)); Settle();
Check(Sent((Disapprove, 0.5f)), "local death notified in either order gives one Disapprove");
// Also the path of a local death the mode did not admit (outside its live phase, after the
// result): ArenaMatch drops that kill before OnKill, so only OnPlayerDied reports it.
ArenaEvents.RaisePlayerDied(new Player()); Settle();
Check(Sent((Disapprove, 0.5f)), "a death without a kill record disapproves");
body = new Player();
tdm.Kill(LocalDeath(body, eligible: false)); ArenaEvents.RaisePlayerDied(body); Settle();
Check(Sent(), "an unscored local death stays silent on both paths");

// Coalescing with round events.
tdm.Cue(ArenaAnnouncerCue.RoundStart); Frame();
Check(Sent((Start, 1f)), "round start reacts at 1.0 in its own tick");
tdm.Kill(LocalKill()); tdm.Cue(ArenaAnnouncerCue.MatchWon); Settle();
Check(Sent((Finish, 1f)), "deciding kill and MatchWon give Finish only");
tdm.Cue(ArenaAnnouncerCue.Draw); Settle();
Check(Sent(), "one Finish per round");
tdm.Cue(ArenaAnnouncerCue.RoundStart); tdm.Kill(LocalDeath(new Player())); tdm.Cue(ArenaAnnouncerCue.RoundLost); Settle();
Check(Sent((Finish, 1f), (Start, 1f)), "a frame with Start and Finish sends both, round events first, no Disapprove");

// Announcer interlock.
AnnouncerService.IsPmcVoiceBlocked = true;
tdm.Kill(LocalKill()); Settle();
tdm.Kill(LocalDeath(new Player())); Settle();
tdm.Cue(ArenaAnnouncerCue.LostTheLead); Settle();
tdm.Callout("FIRST BLOOD"); tdm.Kill(LocalKill()); Settle();
tdm.Cue(ArenaAnnouncerCue.RoundStart); Frame();
tdm.Cue(ArenaAnnouncerCue.RoundWon); Frame();
// Own death 0.25 and lead lost 0.15 are both below MapPort's 0.4 band (laments only) and above
// its 0.05 floor (still sent).
Check(Sent((Approve, 0.175f), (Disapprove, 0.25f), (Disapprove, 0.15f), (Approve, 0.45f), (Start, 1f), (Finish, 1f)),
    "interlock halves Approve and Disapprove only: kill 0.175, own death 0.25, lead lost 0.15, award 0.45");
AnnouncerService.IsPmcVoiceBlocked = false;
int reactLines = log.Debugs.Count(line => line.StartsWith("Arena crowd reaction:", StringComparison.Ordinal));
Check(reactLines == FakeCrowd.ReactCalls && log.Warnings.Count == 0, "one Debug line per sent reaction, no warnings");

// NoContest: silent, released, no further reactions or claims.
tdm.Kill(LocalKill()); tdm.Cue(ArenaAnnouncerCue.NoContest);
Check(FakeCrowd.Handle == 0 && FakeCrowd.Releases == 1, "no contest releases the claim at once");
Frame(); tdm.Kill(LocalKill()); tdm.Cue(ArenaAnnouncerCue.RoundStart); Run(40);
Check(Sent() && FakeCrowd.Claims == 1 && FakeCrowd.Releases == 1, "no contest stays silent for the rest of the match");
ArenaCrowdReactions.Detach();
Check(tdm.Subscribers == 0 && ArenaEvents.DiedSubscribers == 0 && FakeCrowd.Releases == 1, "detach unsubscribes everything");

// Deathmatch round events.
var dm = new RoundsMode();
var dmMatch = Claimed(dm);
Check(dm.Subscribers == 2, "Deathmatch round events are bound");
// RoundsMode's epoch: the round while live, round + 1 from the intermission on (the round it prepares).
ArenaAnnouncementDirector.Snapshot = Epoch(1);
dm.Start(1); Frame();
Check(Sent((Start, 1f)), "Deathmatch round start reacts at 1.0");
ArenaAnnouncementDirector.Snapshot = Epoch(2, live: false);
dmMatch.Kill(LocalKill()); dm.End(1); Settle();
Check(Sent((Finish, 1f)), "non-terminal Deathmatch round end gives Finish only");
dmMatch.Cue(ArenaAnnouncerCue.Draw); Settle();
Check(Sent(), "non-terminal Deathmatch draw gives one Finish");
ArenaAnnouncementDirector.Snapshot = Epoch(2);
dm.Start(2); Frame(); Sent();
dm.IsComplete = true; dmMatch.Cue(ArenaAnnouncerCue.MatchWon); dm.End(2); Frame();
Check(Sent((Finish, 1f)), "terminal Deathmatch round end gives one Finish");
dm = new RoundsMode(); dmMatch = Claimed(dm);
dm.Start(1); Frame(); Sent();
dm.IsComplete = true; dm.End(1); Frame();
Check(Sent(), "terminal Deathmatch round end itself sends nothing");
dmMatch.Cue(ArenaAnnouncerCue.Draw); Frame();
Check(Sent((Finish, 1f)), "terminal Deathmatch draw reacts on its Draw cue only");
dm = new RoundsMode(); dmMatch = Claimed(dm);
dm.IsComplete = true; dmMatch.Cue(ArenaAnnouncerCue.NoContest); dm.End(1); Frame();
Check(Sent() && FakeCrowd.Handle == 0, "Deathmatch no contest stays silent");
ArenaCrowdReactions.Detach();
Check(dm.Subscribers == 0, "Deathmatch round events are unbound on detach");

// Tactical decides its round in its mode tick. A kill raised after that tick in the same Update
// (ArenaFireRuntime: a burn, a tracked throw) reaches the crowd one tick before its RoundWon.
var tactical = Claimed(new TacticalMode());
ArenaAnnouncementDirector.Snapshot = Epoch(1);
tactical.Cue(ArenaAnnouncerCue.RoundStart); Frame(); Sent();
tactical.Kill(LocalKill("Flame")); Frame();
tactical.Cue(ArenaAnnouncerCue.RoundWon); Settle();
Check(Sent((Finish, 1f)), "a deciding kill one tick before its result gives Finish only");
// Solo Tactical starts a round without a RoundStart cue when its squad was eliminated while it
// assembled; EndRound reports the result before it advances RoundNumber, its epoch.
ArenaAnnouncementDirector.Snapshot = Epoch(2);
tactical.Kill(LocalKill()); tactical.Cue(ArenaAnnouncerCue.RoundWon); Settle();
Check(Sent((Finish, 1f)), "a round without a RoundStart cue still sends its result");
tactical.Cue(ArenaAnnouncerCue.RoundLost); Settle();
Check(Sent(), "a second result of the same round is dropped");
ArenaCrowdReactions.Detach();

// Gating: every push needs this exact current match, a Started game and the exact active raid.
foreach (var condition in new[] { "not current", "not started", "not exact", "abort pending", "no game" })
{
    var gated = Claimed(new TacticalMode());
    Check(FakeCrowd.Handle > 0, "gating control claims: " + condition);
    gated.Kill(LocalKill());
    if (condition == "not current") ArenaMatchService.Current = new ArenaMatch(new TacticalMode());
    else if (condition == "not started") game.Status = GameStatus.Stopping;
    else if (condition == "not exact") ArenaSession.ExactRaid = false;
    else if (condition == "abort pending") ArenaMatchService.TeamStartAbortPending = true;
    else Singleton<AbstractGame>.Instance = null;
    Frame(); Run(5);
    string label = condition == "not current" ? "no push and release when the match is no longer current"
        : "no push and release when the raid gate fails: " + condition;
    Check(Sent() && FakeCrowd.Handle == 0 && FakeCrowd.Releases == 1 && FakeCrowd.Claims == 1
        && gated.Subscribers == 0, label);
}

// Exit paths: explicit detach, native stop without a tick, re-attach.
var exit = Claimed(new GunGameMode());
ArenaAnnouncementDirector.Snapshot = Live(finalRound: true); Run(1.1f);
Check(FakeCrowd.Excitement.Count == 1 && Near(FakeCrowd.Excitement[0], 0.6f), "excitement control before native stop");
ArenaCrowdReactions.OnNativePresentationStop();
Check(FakeCrowd.Handle == 0 && FakeCrowd.Releases == 1 && exit.Subscribers == 0,
    "native presentation stop releases the claim without a tick");
Check(FakeCrowd.Excitement.Count == 2 && FakeCrowd.Excitement[1] == 0f, "native presentation stop calms the murmur");
ArenaCrowdReactions.OnNativePresentationStop(); ArenaCrowdReactions.Detach();
Check(FakeCrowd.Releases == 1 && FakeCrowd.Excitement.Count == 2, "later exits release nothing twice");
Claimed(new TeamDeathmatchMode());
int firstHandle = FakeCrowd.Handle;
var second = NewMatchKeepingCrowd(new TeamDeathmatchMode());
ArenaCrowdReactions.Attach(second, Surface(typeof(ArenaMapPort.Client.SpectatorCrowd)));
Check(FakeCrowd.Handle == 0 && FakeCrowd.Releases == 1, "a new match releases the previous claim first");
Frame();
Check(FakeCrowd.Handle > 0 && FakeCrowd.Handle != firstHandle && FakeCrowd.Owners.Count == 2
    && FakeCrowd.Owners[0] != FakeCrowd.Owners[1] && FakeCrowd.SentOwners[1].Length == 40,
    "new match uses a new owner and handle");
ArenaCrowdReactions.Detach();

// A refused or throwing reaction ends the claim for the match; nothing is claimed again.
var refused = Claimed(new TeamDeathmatchMode());
FakeCrowd.ReactResult = false;
refused.Kill(LocalKill()); Settle();
int reactCalls = FakeCrowd.ReactCalls;
Check(FakeCrowd.Handle == 0 && FakeCrowd.Releases == 1, "refused reaction releases the claim");
FakeCrowd.ReactResult = true;
refused.Kill(LocalKill()); refused.Cue(ArenaAnnouncerCue.RoundStart); Run(40);
Check(FakeCrowd.ReactCalls == reactCalls && FakeCrowd.Claims == 1 && FakeCrowd.Reactions.Count == 0,
    "after a refused reaction nothing is pushed or claimed again");
var throwingReact = Claimed(new TeamDeathmatchMode());
FakeCrowd.ThrowReact = true;
throwingReact.Kill(LocalKill()); Settle();
Check(FakeCrowd.Handle == 0 && log.Warnings.Count == 1 && log.Warnings[0].Contains("stopped"),
    "a throwing reaction is contained and releases the claim");
ArenaCrowdReactions.Detach();

// Retry window: until the first gated tick plus 30 s, at most once per second.
NewMatch(new TeamDeathmatchMode());
FakeCrowd.Kinds = 0;
ArenaCrowdReactions.Attach(ArenaMatchService.Current, Surface(typeof(ArenaMapPort.Client.SpectatorCrowd)));
var noCrowdMatch = ArenaMatchService.Current;
Run(60);
noCrowdMatch.Kill(LocalKill()); Frame();
Check(FakeCrowd.Claims == 0 && Sent(), "no claim on a map without reaction kinds");
Check(FakeCrowd.KindReads <= 31 && FakeCrowd.KindReads >= 25,
    "claim attempts stay within the 30 s window, at most once per second");
Check(log.Debugs.Any(line => line.Contains("no reaction kinds on this map")), "no-crowd outcome is one Debug line");
int kindReads = FakeCrowd.KindReads; Run(30);
Check(FakeCrowd.KindReads == kindReads, "checks stop after the window");
NewMatch(new TeamDeathmatchMode());
FakeCrowd.RefuseClaim = true;
ArenaCrowdReactions.Attach(ArenaMatchService.Current, Surface(typeof(ArenaMapPort.Client.SpectatorCrowd)));
Run(90);
Check(FakeCrowd.Claims >= 25 && FakeCrowd.Claims <= 31 && log.Debugs.Any(line => line.Contains("claim refused")),
    "refused claim retried at most once per second within the window");
NewMatch(new TeamDeathmatchMode());
FakeCrowd.Kinds = 0;
ArenaCrowdReactions.Attach(ArenaMatchService.Current, Surface(typeof(ArenaMapPort.Client.SpectatorCrowd)));
Run(10);
FakeCrowd.Kinds = FakeCrowd.AllKinds;
Run(2);
Check(FakeCrowd.Claims == 1 && FakeCrowd.Handle > 0, "a reaction set published late within the window is claimed");
NewMatch(new TeamDeathmatchMode());
FakeCrowd.ThrowKinds = true;
ArenaCrowdReactions.Attach(ArenaMatchService.Current, Surface(typeof(ArenaMapPort.Client.SpectatorCrowd)));
Run(10);
Check(FakeCrowd.KindReads == 1 && FakeCrowd.Claims == 0 && log.Warnings.Count == 1, "a throwing ReactionKinds stops the attempts");
ArenaCrowdReactions.Detach();

// Excitement against the published 0.4.0 surface: works, ~1 Hz, only on change, reset on release.
var murmur = NewMatch(new TeamDeathmatchMode());
ArenaCrowdReactions.Attach(murmur, Surface(typeof(Variants.V040)));
ArenaAnnouncementDirector.Snapshot = Live(finalRound: true);
Frame();
Check(FakeCrowd.Excitement.Count == 1 && Near(FakeCrowd.Excitement[0], 0.6f), "0.4.0 surface: excitement is written");
Check(FakeCrowd.Claims == 0 && FakeCrowd.KindReads == 0 && murmur.KillSubscribers == 0 && ArenaEvents.DiedSubscribers == 0,
    "0.4.0 surface: reactions dormant without subscriptions or claims");
Check(murmur.CueSubscribers == 1, "0.4.0 surface: the murmur hears the cues for NoContest");
int reads = ArenaAnnouncementDirector.Reads;
Run(0.85f);
Check(ArenaAnnouncementDirector.Reads == reads, "excitement polls at most once per second");
Run(5);
Check(FakeCrowd.Excitement.Count == 1 && ArenaAnnouncementDirector.Reads <= reads + 6, "unchanged excitement is not resent");
ArenaAnnouncementDirector.Snapshot = Live(finalRound: true, seconds: 0); Run(1.1f);
ArenaAnnouncementDirector.Snapshot = Live(finalRound: true, seconds: 0, terminal: true); Run(1.1f);
Check(FakeCrowd.Excitement.Count == 3 && Near(FakeCrowd.Excitement[1], 0.85f) && FakeCrowd.Excitement[2] == 0f,
    "excitement follows the snapshot and calms at the result");
ArenaAnnouncementDirector.Snapshot = Live(matchPoint: true); Run(1.1f);
ArenaCrowdReactions.Detach();
Check(FakeCrowd.Excitement.Count == 5 && FakeCrowd.Excitement[4] == 0f, "excitement resets to 0 on release");
// A failed Team DM raises NoContest but leaves its snapshot Live without a result
// (TeamDeathmatchMode.Fail), so only the cue can calm the 0.4.0 murmur.
var failed = NewMatch(new TeamDeathmatchMode());
ArenaCrowdReactions.Attach(failed, Surface(typeof(Variants.V040)));
ArenaAnnouncementDirector.Snapshot = Live(seconds: 30); Run(1.1f);
Check(FakeCrowd.Excitement.Count == 1 && Near(FakeCrowd.Excitement[0], 0.3f), "0.4.0 surface: no-contest control");
failed.Cue(ArenaAnnouncerCue.RoundStart); failed.Cue(ArenaAnnouncerCue.MatchWon);
failed.Cue(ArenaAnnouncerCue.NoContest);
Check(FakeCrowd.Excitement.Count == 2 && FakeCrowd.Excitement[1] == 0f && FakeCrowd.ReactCalls == 0,
    "0.4.0 surface: no contest calms the murmur at once");
ArenaAnnouncementDirector.Snapshot = Live(seconds: 0); Run(5);
Check(FakeCrowd.Excitement.Count == 2, "0.4.0 surface: after no contest a later clock writes nothing");
ArenaCrowdReactions.Detach();
Check(failed.CueSubscribers == 0 && FakeCrowd.Excitement.Count == 2, "0.4.0 surface: detach unsubscribes the cue");
var calm = NewMatch(new TeamDeathmatchMode());
ArenaCrowdReactions.Attach(calm, Surface(typeof(Variants.V040)));
Run(5); ArenaCrowdReactions.Detach();
Check(FakeCrowd.Excitement.Count == 0, "a calm match writes nothing, not even the reset");
var bedless = NewMatch(new TeamDeathmatchMode());
FakeCrowd.Available = false;
ArenaCrowdReactions.Attach(bedless, Surface(typeof(Variants.V040)));
ArenaAnnouncementDirector.Snapshot = Live(finalRound: true); Run(5);
Check(FakeCrowd.Excitement.Count == 0, "no excitement write while the bed is unavailable");
FakeCrowd.Available = true; Run(1.1f);
Check(FakeCrowd.Excitement.Count == 1 && Near(FakeCrowd.Excitement[0], 0.6f), "the level is written once the bed is available");
ArenaCrowdReactions.Detach();
var noAvailable = NewMatch(new TeamDeathmatchMode());
ArenaCrowdReactions.Attach(noAvailable, Surface(typeof(Variants.ConvertibleClaim)));
ArenaAnnouncementDirector.Snapshot = Live(matchPoint: true); Run(1.1f);
Check(FakeCrowd.Excitement.Count == 1 && Near(FakeCrowd.Excitement[0], 0.4f), "without IsAvailable the level is written");
ArenaCrowdReactions.Detach();

// Containment: a broken excitement path leaves reactions running, and the reverse.
var brokenMurmur = Claimed(new TeamDeathmatchMode());
FakeCrowd.ThrowExcite = true;
ArenaAnnouncementDirector.Snapshot = Live(finalRound: true); Run(1.1f);
brokenMurmur.Kill(LocalKill()); Settle();
Check(Sent((Approve, 0.35f)) && log.Warnings.Count == 1 && log.Warnings[0].Contains("murmur"),
    "a throwing SetExcitement stops only the murmur");
var brokenSnapshot = Claimed(new TeamDeathmatchMode());
ArenaAnnouncementDirector.Throw = true; Run(1.1f);
brokenSnapshot.Kill(LocalKill()); Settle();
Check(Sent((Approve, 0.35f)) && FakeCrowd.Handle > 0 && log.Warnings.Count == 1
    && log.Warnings[0].Contains("snapshot unavailable"), "a throwing snapshot stops only the murmur and the Lull");
ArenaCrowdReactions.Detach();

// ---------------------------------------------------------------- lull (D14)
// No kill for 60 s of live combat gives one Lull at 0.4, at most one per 90 s, only while the
// claimed mask holds bit 6. The matches use the reaction surface without the murmur part, so every
// snapshot read is the Lull's; the claim tick's poll reads live combat and starts the quiet clock.
float quietStart = 0f;
int withLull = FakeCrowd.AllKinds | FakeCrowd.LullBit;
ArenaMatch LullMatch(int kinds)
{
    var match = NewMatch(new TeamDeathmatchMode());
    FakeCrowd.Kinds = kinds;
    ArenaAnnouncementDirector.Snapshot = Epoch(1);
    ArenaCrowdReactions.Attach(match, Surface(typeof(Variants.NoExcitement)));
    Frame();
    quietStart = UnityEngine.Time.unscaledTime;
    return match;
}
// Steps up to the poll that decides the first Lull (the clock at 60 s): it is then buffered in
// this tick's frame and not yet sent.
void StepToLullDecision()
{
    for (int guard = 0; guard < 1000; guard++)
    {
        int reads = ArenaAnnouncementDirector.Reads;
        Frame();
        if (ArenaAnnouncementDirector.Reads != reads && UnityEngine.Time.unscaledTime - quietStart >= 60f) return;
    }
    throw new InvalidOperationException("FAIL: no Lull decision poll");
}

LullMatch(withLull); Run(50);
LullMatch(withLull); Run(50);
Check(Sent(), "a release clears the quiet clock: a new match counts its quiet afresh");

var lull = LullMatch(withLull);
Check(FakeCrowd.Claims == 1 && ArenaAnnouncementDirector.Reads == 1, "Lull control: claimed, the claim tick polled");
Run(59.5f);
Check(Sent() && ArenaAnnouncementDirector.Reads >= 50, "no Lull before 60 s without a kill");
Run(3);
Check(Sent((Lull, 0.4f)) && log.Debugs.Any(line => line.StartsWith(
        "Arena crowd reaction: kind=Lull, strength=0.4, source=no kill for 60 s.", StringComparison.Ordinal)),
    "60 s without a kill in live combat give one Lull at 0.4");
Run(85);
Check(Sent(), "no second Lull within 90 s of the first");
Run(8);
Check(Sent((Lull, 0.4f)), "the next Lull comes 90 s after the previous one");

// Every admitted kill resets the clock, a bot-versus-bot one included; so does a local death
// without a kill record (it disapproves).
lull = LullMatch(withLull);
Run(50); lull.Kill(OtherKill(null, null)); Run(50);
Check(Sent(), "a bot-versus-bot kill resets the quiet clock");
Run(13);
Check(Sent((Lull, 0.4f)), "60 s after the last kill the Lull comes");
lull = LullMatch(withLull);
Run(50); ArenaEvents.RaisePlayerDied(new Player()); Settle();
Check(Sent((Disapprove, 0.5f)), "Lull control: a death without a kill record disapproves");
Run(50);
Check(Sent(), "a local death without a kill record resets the quiet clock too");

// Live combat only: preparation, a result and no snapshot stop the clock; the first live poll
// starts it, so a round that goes live without a Start still gets its Lull.
lull = LullMatch(withLull);
ArenaAnnouncementDirector.Snapshot = Epoch(2, live: false); Run(70);
ArenaAnnouncementDirector.Snapshot = Live(terminal: true); Run(70);
ArenaAnnouncementDirector.Snapshot = default; Run(70);
Check(Sent(), "no Lull outside live combat: preparation, a result, no snapshot");
ArenaAnnouncementDirector.Snapshot = Epoch(2); Run(59);
Check(Sent(), "the quiet clock starts at the first live poll");
Run(4);
Check(Sent((Lull, 0.4f)), "a round that goes live without a Start still gets its Lull");

// A Start restarts the clock; a Finish stops it (the snapshot stays live here, as a mode's may
// for a moment), and the next live poll starts it again.
lull = LullMatch(withLull);
Run(50); lull.Cue(ArenaAnnouncerCue.RoundStart); Frame();
Check(Sent((Start, 1f)), "Lull control: the round start reacts");
Run(50);
Check(Sent(), "a Start restarts the quiet clock");
Run(13);
Check(Sent((Lull, 0.4f)), "60 s after the Start the Lull comes");
lull = LullMatch(withLull);
Run(59.5f); lull.Cue(ArenaAnnouncerCue.RoundWon); Frame();
Check(Sent((Finish, 1f)), "Lull control: the result reacts");
Run(3);
Check(Sent(), "a Finish stops the quiet clock");
Run(62);
Check(Sent((Lull, 0.4f)), "after a Finish the next live poll starts the clock again");

// Coalescing: a Start or Finish in the Lull's tick or the tick after drops it; the Lull drops nothing.
lull = LullMatch(withLull);
StepToLullDecision(); lull.Cue(ArenaAnnouncerCue.MatchWon); Settle();
Check(Sent((Finish, 1f)), "a Finish in the Lull's tick drops it");
lull = LullMatch(withLull);
StepToLullDecision(); Frame(); lull.Cue(ArenaAnnouncerCue.RoundStart); Settle();
Check(Sent((Start, 1f)), "a Start one tick after the Lull drops the held Lull");
// A kill or a local death withdraws a Lull that is decided but not yet sent, from either frame: its
// premise no longer holds, and it would go out next to the kill's own reaction (plan 2.4).
lull = LullMatch(withLull);
StepToLullDecision(); lull.Kill(LocalKill()); Settle();
Check(Sent((Approve, 0.35f)), "a kill in the Lull's tick withdraws it: only the kill's Approve goes out");
lull = LullMatch(withLull);
StepToLullDecision(); Frame(); lull.Kill(OtherKill(null, null)); Settle();
Check(Sent(), "a bot-versus-bot kill one tick after the Lull withdraws the held Lull");
// The 90 s gap runs from a Lull that went out: the withdrawn one delays nothing, so the next comes
// 60 s after the kill.
Run(59);
Check(Sent(), "after a withdrawn Lull none within 60 s of the kill");
Run(4);
Check(Sent((Lull, 0.4f)), "a withdrawn Lull does not count against the 90 s gap");
lull = LullMatch(withLull);
StepToLullDecision(); ArenaEvents.RaisePlayerDied(new Player()); Settle();
Check(Sent((Disapprove, 0.5f)), "a local death in the Lull's tick withdraws it: only the death's Disapprove goes out");
// A lead cue without a kill (Domination's score moves by objective points) keeps the Lull: D14
// counts kills. The Lull drops nothing and goes last.
lull = LullMatch(withLull);
StepToLullDecision(); lull.Cue(ArenaAnnouncerCue.TakenTheLead); Settle();
Check(Sent((Approve, 0.5f), (Lull, 0.4f)), "a lead cue without a kill keeps the Lull: both go out, the Lull last");

// The mask: without bit 6 the path is dormant (no read, no Lull); a Lull-only mask is claimed and
// sends the Lull alone, which the announcer interlock leaves at 0.4.
LullMatch(FakeCrowd.AllKinds); Run(100);
Check(Sent() && FakeCrowd.Handle > 0 && ArenaAnnouncementDirector.Reads == 0,
    "a claim without the Lull bit keeps the Lull dormant: no snapshot read, no Lull");
lull = LullMatch(FakeCrowd.LullBit);
Check(FakeCrowd.Claims == 1 && FakeCrowd.Handle > 0, "a Lull-only mask is claimed");
AnnouncerService.IsPmcVoiceBlocked = true;
lull.Kill(LocalKill()); Run(62.5f);
Check(Sent((Lull, 0.4f)) && FakeCrowd.ReactCalls == 1,
    "a Lull-only claim sends the Lull alone, and the announcer interlock leaves it at 0.4");

// No contest and a failing snapshot end the Lull for the match.
lull = LullMatch(withLull);
Run(50); lull.Cue(ArenaAnnouncerCue.NoContest); Run(70);
Check(Sent() && FakeCrowd.Handle == 0, "no contest: no Lull afterwards");
LullMatch(withLull);
ArenaAnnouncementDirector.Throw = true; Run(70);
Check(Sent() && ArenaAnnouncementDirector.Reads == 2 && log.Warnings.Count == 1 && FakeCrowd.Handle > 0,
    "a throwing snapshot stops the Lull with one warning and keeps the claim");
ArenaCrowdReactions.Detach();

// Key off, Zombies, nothing bound: no bind, no claim, no excitement, no subscription.
var keyOff = NewMatch(new TeamDeathmatchMode());
ArenaSession.CrowdReactionsSelection = false;
ArenaCrowdReactions.Attach(keyOff, Surface(typeof(ArenaMapPort.Client.SpectatorCrowd)));
ArenaAnnouncementDirector.Snapshot = Live(finalRound: true);
keyOff.Kill(LocalKill()); Run(40);
Check(binds == 0 && FakeCrowd.Claims == 0 && FakeCrowd.KindReads == 0 && FakeCrowd.Excitement.Count == 0
    && keyOff.Subscribers == 0 && ArenaAnnouncementDirector.Reads == 0, "key off: nothing");
var zombies = NewMatch(new ZombieMode());
FakeCrowd.Kinds = FakeCrowd.AllKinds | FakeCrowd.LullBit;
ArenaCrowdReactions.Attach(zombies, Surface(typeof(ArenaMapPort.Client.SpectatorCrowd)));
ArenaAnnouncementDirector.Snapshot = Live(finalRound: true);
zombies.Kill(LocalKill()); ArenaEvents.RaisePlayerDied(new Player()); Run(70);
Check(binds == 0 && FakeCrowd.Claims == 0 && FakeCrowd.KindReads == 0 && FakeCrowd.Excitement.Count == 0
    && zombies.Subscribers == 0 && ArenaEvents.DiedSubscribers == 0 && ArenaAnnouncementDirector.Reads == 0 && Sent(),
    "Zombies: no bind, no claim, no excitement, no Lull");
var incompatible = NewMatch(new TeamDeathmatchMode());
ArenaCrowdReactions.Attach(incompatible, Surface(typeof(Variants.ContractTwo)));
ArenaAnnouncementDirector.Snapshot = Live(finalRound: true); Run(5);
Check(FakeCrowd.Claims == 0 && FakeCrowd.Excitement.Count == 0 && incompatible.Subscribers == 0
    && ArenaAnnouncementDirector.Reads == 0, "an incompatible contract attaches nothing");
ArenaCrowdReactions.Attach(null); ArenaCrowdReactions.Tick();
Check(FakeCrowd.Claims == 0 && FakeCrowd.Excitement.Count == 0, "a null match is ignored");

// Production Attach binds the loaded plugin; without it nothing attaches.
var production = NewMatch(new TacticalMode());
Chainloader.PluginInfos.Clear();
Chainloader.PluginInfos[PluginGuid] = new PluginInfo { Instance = new ArenaMapPort.Client.PluginAnchor() };
ArenaCrowdReactions.Attach(production); Frame();
production.Kill(LocalKill()); Settle();
Check(FakeCrowd.Claims == 1 && Sent((Approve, 0.35f)), "production Attach binds the loaded ArenaMapPort");
var absent = NewMatch(new TacticalMode());
Chainloader.PluginInfos.Clear();
ArenaCrowdReactions.Attach(absent); absent.Kill(LocalKill()); Run(5);
Check(FakeCrowd.Claims == 0 && absent.Subscribers == 0 && log.Debugs.Any(line => line.Contains("not loaded")),
    "without ArenaMapPort nothing attaches");
ArenaCrowdReactions.Detach();

// ---------------------------------------------------------------- wiring (source pins)
string Text(string key)
{
    var path = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == key).Value;
    return File.ReadAllText(path).Replace("\r\n", "\n");
}
int Count(string text, string part)
{
    int count = 0;
    for (int at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal))
        count++;
    return count;
}
int At(string text, string part, int from = 0) => text.IndexOf(part, from, StringComparison.Ordinal);
var service = Text("MatchServiceText");
int lighting = At(service, "Effects.ArenaLightingDirector.Attach(match, world);");
int attach = At(service, "Audio.ArenaCrowdReactions.Attach(match);");
int modeStart = At(service, "mode.OnMatchStart(match);");
Check(lighting >= 0 && attach > lighting && modeStart > attach && Count(service, "Audio.ArenaCrowdReactions.Attach(") == 1,
    "the crowd attaches with the presentation services before mode startup");
int startCatch = At(service, "catch (Exception ex)", modeStart);
int failureDetach = At(service, "Try(Audio.ArenaCrowdReactions.Detach, \"crowd reactions detach\");", startCatch);
int partialEnd = At(service, "if (partialMatch != null)", startCatch);
Check(startCatch > 0 && failureDetach > startCatch && partialEnd > failureDetach, "a failed match start detaches the crowd");
int raidEnd = At(service, "private static void OnRaidEnded()");
int raidEndDetach = At(service, "Try(Audio.ArenaCrowdReactions.Detach, \"crowd reactions detach\");", raidEnd);
int nullMatch = At(service, "if (match == null)", raidEnd);
Check(raidEnd > 0 && raidEndDetach > raidEnd && nullMatch > raidEndDetach, "raid end detaches the crowd before its null-match return");
int recovery = At(service, "internal static void RecoverAtNewRaidStart()");
int recoveryDetach = At(service, "Try(Audio.ArenaCrowdReactions.Detach, \"crowd reactions recovery\");", recovery);
int recoveryReturn = At(service, "if (Current == null && !TeamStartAbortPending)", recovery);
Check(recovery > 0 && recoveryDetach > recovery && recoveryReturn > recoveryDetach, "new-raid recovery detaches the crowd before its early return");
int tick = At(service, "public static void Tick()");
int abortStop = At(service, "Audio.ArenaCrowdReactions.OnNativePresentationStop();", tick);
int abortTick = At(service, "TickFailedStartAbort();", tick);
int nativeBranch = At(service, "if (ArenaRaidExit.NativeStopOwnsCurrentRaid)", tick);
int nativeStop = At(service, "Audio.ArenaCrowdReactions.OnNativePresentationStop();", nativeBranch);
int nativeReturn = At(service, "return;", nativeBranch);
int matchTick = At(service, "match.Tick(Time.deltaTime);", tick);
Check(tick > 0 && abortStop > tick && abortTick > abortStop && nativeBranch > abortTick && nativeStop > nativeBranch
    && nativeReturn > nativeStop && matchTick > nativeReturn && Count(service, "Audio.ArenaCrowdReactions.OnNativePresentationStop();") == 2,
    "ArenaMatchService releases the crowd on both early returns");
var controller = Text("ControllerText");
int announcerTick = At(controller, "Audio.AnnouncerService.Tick();");
int crowdTick = At(controller, "Audio.ArenaCrowdReactions.Tick();");
Check(announcerTick > 0 && crowdTick > announcerTick && At(controller, "Audio.ArenaPmcVoice.Tick();") > crowdTick
    && At(controller, "Modes.ArenaMatchService.Tick();") < announcerTick && Count(controller, "ArenaCrowdReactions.Tick()") == 1,
    "the crowd ticks every frame right after the announcer");
var plugin = Text("PluginText");
Check(Count(plugin, "Audio.ArenaCrowdReactions.Detach();") == 1
    && At(plugin, "Audio.ArenaCrowdReactions.Detach();") > At(plugin, "Audio.AnnouncerService.Shutdown();"),
    "plugin shutdown detaches the crowd");
var session = Text("SessionText");
Check(Count(session, "CrowdReactionsSelection = Settings.CrowdReactions?.Value == true;") == 1
    && Count(session, "CrowdReactionsSelection = false;") == Count(session, "AnnouncerEnabledSelection = false;")
    && Count(session, "CrowdReactionsSelection = false;") >= 6, "the key is frozen at arm and cleared with the announcer selection");
var settings = Text("SettingsText");
int bindAt = At(settings, "\"Audio\", \"CrowdReactions\", true,");
Check(bindAt > 0 && At(settings, "CrowdReactions = config.Bind(") > 0 && At(settings, "ArenaMapPort 0.4.0", bindAt) > bindAt
    && At(settings, "newer ArenaMapPort", bindAt) > bindAt, "one default-on Audio key that names the ArenaMapPort versions");
var layout = Text("LayoutText");
int audioSection = At(layout, "new Section(\"Audio\", false,");
Check(audioSection > 0 && At(layout, "I(\"CrowdReactions\",", audioSection) > audioSection
    && At(layout, "I(\"CrowdReactions\",", audioSection) < At(layout, "new Section(\"Effects\"", audioSection),
    "the key has its F12 row in the Audio section");
foreach (var key in new[] { "BinderText", "PolicyText", "ServiceText" })
{
    var source = Text(key);
    Check(!source.Contains("ModulePatch") && !source.Contains("HarmonyLib") && !source.Contains("[Harmony")
        && !source.Contains("LogInfo("), "no Harmony patch and no Info logging: " + key);
}

// ---------------------------------------------------------------- real surface (installed ArenaMapPort)
// The installed ArenaMapPort client (crowd reactions phase A, MapPort d70acd2 or later), loaded
// read-only (RealMapPort.cs). The binder binds its real SpectatorCrowd, and the service runs against
// the real CrowdReactionQueue and CrowdBedState. The recorder in RealSurface only notes what the
// delegates BindContract bound on the real type return. Last in the program, so every mutant above
// still fails its own named assertion first. A missing DLL or member fails here, never skips.
int checksBeforeReal = checks;
// The guard below fails loudly on a client without the reaction members, such as the published 0.4.0.
string v040Missing = RealMapPort.MissingMembers(typeof(Variants.V040));
Check(RealMapPort.MissingMembers(typeof(ArenaMapPort.Client.SpectatorCrowd)) == null && v040Missing != null
    && new[] { "ReactionKinds", "ClaimReactions", "React(", "ReleaseReactions" }.All(part => v040Missing.Contains(part))
    && !v040Missing.Contains("SetExcitement") && !v040Missing.Contains("ContractVersion"),
    "real surface: the installed-client guard names the four reaction members a 0.4.0 client lacks");
var real = RealMapPort.Load(Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
    .Single(attribute => attribute.Key == "MapPortClientDll").Value);
Console.WriteLine($"CROWD_REACTION_REAL_SURFACE: loaded {real.Path} (ProductVersion {real.ProductVersion}, SHA256 {real.Sha256})");

// MapPort documents the kind numbers and keeps them as internal constants; ArenaMode mirrors them.
// The stand-in surfaces record whatever ArenaMode sends, so only this pins the numbers themselves.
Check(Approve == real.KindConstant("Approve") && Disapprove == real.KindConstant("Disapprove")
    && TakeCover == real.KindConstant("TakeCover") && Start == real.KindConstant("Start")
    && Finish == real.KindConstant("Finish") && Lull == real.KindConstant("Lull")
    && HighestKind == real.KindConstant("MaxKind") && real.KindConstant("PurifiedTeam") == 7,
    "real surface: ArenaMode's kind numbers are MapPort's CrowdReactionKind numbers");
int implementedMask = real.KindConstant("ImplementedMask");
Check(implementedMask == 62 && (implementedMask & Bit(Lull)) == 0 && (implementedMask & Bit(7)) == 0,
    "real surface: phase A implements kinds 1 to 5 (mask 62); Lull and PurifiedTeam have no bit");
string ownerShape = ArenaCrowdReactions.OwnerPrefix + new string('f', 31);
Check(real.OwnerMax == 64 && real.CleanOwner(ownerShape) == ownerShape && real.CleanOwner(new string('a', 70)).Length == 64,
    "real surface: MapPort keeps 64 owner characters; ArenaMode's 40 pass unchanged");
// A refused React ends ArenaMode's claim for the match, so nothing ArenaMode sends may fall below
// MapPort's floor: the interlock halves Approve and Disapprove, Start, Finish and Lull stay whole.
float weakestSent = Math.Min(new[] { ArenaCrowdReactionPolicy.KillStrength, ArenaCrowdReactionPolicy.StrongKillStrength,
        ArenaCrowdReactionPolicy.AwardStrength, ArenaCrowdReactionPolicy.DeathStrength,
        ArenaCrowdReactionPolicy.LeadTakenStrength, ArenaCrowdReactionPolicy.LeadLostStrength }.Min()
    * ArenaCrowdReactionPolicy.InterlockFactor,
    Math.Min(ArenaCrowdReactionPolicy.RoundStrength, ArenaCrowdReactionPolicy.LullStrength));
Check(Near(real.MinStrength, 0.05f) && weakestSent >= real.MinStrength,
    "real surface: every strength ArenaMode sends is at or above MapPort's MinStrength");
Console.WriteLine(FormattableString.Invariant($"CROWD_REACTION_REAL_SURFACE: kinds Approve..Lull = 1..6 as MapPort's, ImplementedMask {implementedMask}, ")
    + FormattableString.Invariant($"owner limit {real.OwnerMax} (ArenaMode sends 40), MinStrength {real.MinStrength:0.###} (ArenaMode's weakest {weakestSent:0.###})"));

log.Clear();
var realBound = BindContract(real.SpectatorCrowd);
Check(realBound.Excitement == Part.Bound && realBound.Reactions == Part.Bound && realBound.IsAvailable != null
    && realBound.SetExcitement != null && realBound.ReactionKinds != null && realBound.ClaimReactions != null
    && realBound.React != null && realBound.ReleaseReactions != null && log.Warnings.Count == 0
    && realBound.Summary == "excitement=bound, reactions=bound",
    "real surface: BindContract binds both parts of the installed SpectatorCrowd with the exact signatures");
Console.WriteLine($"CROWD_REACTION_REAL_SURFACE: BindContract(installed SpectatorCrowd): {realBound.Summary}, no warning");

var realClaims = new List<(string owner, int handle)>();
var realReacts = new List<(int handle, int kind, float strength, bool queued)>();
var realReleases = new List<(int handle, bool released)>();
Contract RealSurface()
{
    var contract = BindContract(real.SpectatorCrowd);
    var claim = contract.ClaimReactions;
    var react = contract.React;
    var release = contract.ReleaseReactions;
    contract.ClaimReactions = owner => { int handle = claim(owner); realClaims.Add((owner, handle)); return handle; };
    contract.React = (handle, kind, strength) =>
    {
        bool queued = react(handle, kind, strength);
        realReacts.Add((handle, kind, strength, queued));
        return queued;
    };
    contract.ReleaseReactions = handle => { bool released = release(handle); realReleases.Add((handle, released)); return released; };
    return contract;
}
// A match on a map whose crowd published its kinds during raid loading (62) and has a bed.
ArenaMatch RealMatch(IArenaMode mode)
{
    var match = NewMatch(mode);
    real.Fresh(62);
    real.Available = true;
    realClaims.Clear(); realReacts.Clear(); realReleases.Clear();
    ArenaCrowdReactions.Attach(match, RealSurface);
    return match;
}
bool Drained(params (int kind, float strength)[] expected)
{
    var drained = real.Drain();
    bool same = drained.Count == expected.Length;
    for (int i = 0; same && i < expected.Length; i++)
        same = drained[i].kind == expected[i].kind && Near(drained[i].strength, expected[i].strength);
    return same;
}
int MapPortKind(string name) => real.KindConstant(name);

// The service flow against the real queue.
var realTdm = RealMatch(new TeamDeathmatchMode());
Frame();
int realHandle = real.Holder;
string realOwner = real.HolderOwner;
Check(realClaims.Count == 1 && realHandle > 0 && realClaims[0].handle == realHandle && realOwner == realClaims[0].owner
    && realOwner.StartsWith(ArenaCrowdReactions.OwnerPrefix, StringComparison.Ordinal) && realOwner.Length == 40
    && log.Debugs.Any(line => line.StartsWith($"Arena crowd reactions claimed: handle={realHandle}, kinds=0x3E, attempt=1.",
        StringComparison.Ordinal)),
    "real surface: the first gated tick claims with ArenaMode's owner: handle > 0, ReactionKinds read 62");
long realAccepted = real.Accepted;
realTdm.Kill(LocalKill()); Settle();
Check(realReacts.Count == 1 && realReacts[0].handle == realHandle && realReacts[0].queued && real.Accepted == realAccepted + 1
    && Drained((MapPortKind("Approve"), 0.35f)), "real surface: an own kill's Approve 0.35 is queued: React true, Accepted + 1");
AnnouncerService.IsPmcVoiceBlocked = true;
realTdm.Cue(ArenaAnnouncerCue.LostTheLead); Settle();
AnnouncerService.IsPmcVoiceBlocked = false;
Check(realReacts.Count == 2 && realReacts[1].queued && real.Accepted == realAccepted + 2 && Drained((MapPortKind("Disapprove"), 0.15f)),
    "real surface: the weakest strength ArenaMode sends, a lost lead under the announcer at 0.15, is queued");
ArenaAnnouncementDirector.Snapshot = Epoch(1);
long realRefused = real.Refused;
Run(65);
Check((real.Kinds & Bit(Lull)) == 0 && realReacts.Count == 2 && real.Accepted == realAccepted + 2 && real.Refused == realRefused
    && ArenaAnnouncementDirector.Reads >= 60 && !log.Debugs.Any(line => line.Contains("kind=Lull")),
    "real surface: 65 s of live combat without a kill send no Lull: the real mask has no bit 6");
realTdm.Cue(ArenaAnnouncerCue.RoundStart); Frame();
realTdm.Kill(LocalKill()); realTdm.Cue(ArenaAnnouncerCue.MatchWon); Settle();
Check(realReacts.Count == 4 && realReacts.All(entry => entry.queued && entry.handle == realHandle)
    && real.Accepted == realAccepted + 4 && Drained((MapPortKind("Start"), 1f), (MapPortKind("Finish"), 1f)),
    "real surface: a Start and a Finish are queued at 1.0 (the deciding kill folded into the Finish)");
ArenaAnnouncementDirector.Snapshot = Live(finalRound: true); Run(1.1f);
float realLevel = real.Excitement;
ArenaCrowdReactions.Detach();
Check(Near(realLevel, 0.6f) && real.Excitement == 0f, "real surface: SetExcitement reaches the real State.Excitement (0.6) and returns to 0 on release");
Check(realReleases.Count == 1 && realReleases[0] == (realHandle, true) && real.HolderOwner == null && real.Refused == realRefused
    && log.Warnings.Count == 0, "real surface: detach releases: ReleaseReactions true, HolderOwner null");
Console.WriteLine($"CROWD_REACTION_REAL_SURFACE: service flow: claim handle {realHandle} for a {realOwner.Length}-character owner, "
    + "kinds read 0x3E; queued Approve 0.35, Disapprove 0.15, Start 1, Finish 1 (Accepted +4, Refused +0); no Lull in 65 s; "
    + FormattableString.Invariant($"excitement {realLevel:0.###} -> 0; release true"));

// MapPort's own claim rules, as ArenaMode meets them: the developer demo yields, another plugin does not.
var demoMatch = RealMatch(new TeamDeathmatchMode());
int demoHandle = real.Claim(real.DemoOwner);
Frame();
Check(demoHandle > 0 && realClaims.Count == 1 && realClaims[0].handle > demoHandle && real.HolderOwner == realClaims[0].owner
    && !real.React(demoHandle, MapPortKind("Approve"), 0.5f) && real.Claim(real.DemoOwner) == 0,
    "real surface: the developer demo's claim yields to ArenaMode's and cannot take it back");
ArenaCrowdReactions.Detach();
var foreignMatch = RealMatch(new TeamDeathmatchMode());
int foreignHandle = real.Claim("another plugin");
foreignMatch.Kill(LocalKill()); Run(35);
ArenaCrowdReactions.Detach();
Check(foreignHandle > 0 && realClaims.Count >= 25 && realClaims.Count <= 31 && realClaims.All(entry => entry.handle == 0)
    && realReacts.Count == 0 && realReleases.Count == 0 && real.HolderOwner == "another plugin"
    && log.Debugs.Any(line => line.Contains("claim refused")),
    "real surface: another plugin's claim refuses ArenaMode's within the retry window; nothing is sent or released");
int foreignAttempts = realClaims.Count;

// A second match of the same raid gives the first claim back and claims anew.
RealMatch(new TeamDeathmatchMode()); Frame();
int firstRealHandle = real.Holder;
var nextRealMatch = NewMatchKeepingCrowd(new TeamDeathmatchMode());
ArenaCrowdReactions.Attach(nextRealMatch, RealSurface); Frame();
Check(realReleases.Count == 1 && realReleases[0] == (firstRealHandle, true) && realClaims.Count == 2
    && real.Holder > firstRealHandle && real.HolderOwner == realClaims[1].owner && realClaims[0].owner != realClaims[1].owner,
    "real surface: a second match releases the first claim and claims a new handle with a new owner");
ArenaCrowdReactions.Detach();

// The crowd is removed mid-match (ArenaSpectators.OnDestroy): the handle is void.
var removedMatch = RealMatch(new TeamDeathmatchMode()); Frame();
int removedHandle = real.Holder;
real.Reset();
removedMatch.Kill(LocalKill()); Settle();
real.SetKinds(62);
removedMatch.Kill(LocalKill()); removedMatch.Cue(ArenaAnnouncerCue.RoundStart); Run(40);
Check(realReacts.Count == 1 && realReacts[0].handle == removedHandle && !realReacts[0].queued
    && realReleases.Count == 1 && realReleases[0] == (removedHandle, false) && realClaims.Count == 1
    && real.HolderOwner == null && real.Accepted == 0 && log.Debugs.Any(line => line.StartsWith(
        $"Arena crowd reactions released: handle={removedHandle}, reason=reaction refused", StringComparison.Ordinal)),
    "real surface: after a real Reset (crowd removed) the next push returns false; ArenaMode releases and stops pushing");
ArenaCrowdReactions.Detach();

// MapPort's developer key Spectators/Reactions (or a disabled SpectatorAudio) withdraws the kinds
// mid-raid and keeps the holder. Withdrawn and republished without a push between: the claim works.
// With a push between: React false, ArenaMode releases (still the holder, so true) and does not
// claim again for the rest of the match, even once the kinds are back (accepted behaviour; the next
// match claims anew).
var keyedMatch = RealMatch(new TeamDeathmatchMode()); Frame();
int keyedHandle = real.Holder;
real.SetKinds(0); real.SetKinds(62);
keyedMatch.Kill(LocalKill()); Settle();
Check(realReacts.Count == 1 && realReacts[0].queued && real.HolderOwner == realClaims[0].owner,
    "real surface: kinds withdrawn and republished with no push between leave ArenaMode's claim working");
real.SetKinds(0);
keyedMatch.Kill(LocalKill()); Settle();
real.SetKinds(62);
keyedMatch.Kill(LocalKill()); keyedMatch.Cue(ArenaAnnouncerCue.RoundStart); Run(40);
Check(realReacts.Count == 2 && !realReacts[1].queued && realReleases.Count == 1 && realReleases[0] == (keyedHandle, true)
    && real.HolderOwner == null && realClaims.Count == 1 && real.Kinds == 62,
    "real surface: kinds withdrawn mid-match: React false, ArenaMode releases and does not claim again in that match");
var afterKeyMatch = NewMatchKeepingCrowd(new TeamDeathmatchMode());
ArenaCrowdReactions.Attach(afterKeyMatch, RealSurface); Frame();
Check(realClaims.Count == 2 && realClaims[1].handle > keyedHandle && real.HolderOwner == realClaims[1].owner,
    "real surface: the next match claims the republished kinds anew");
ArenaCrowdReactions.Detach();
Check(log.Warnings.Count == 0 && real.HolderOwner == null, "real surface: no warning, nothing left claimed");
Console.WriteLine($"CROWD_REACTION_REAL_SURFACE: demo yields to ArenaMode; another plugin's claim refused {foreignAttempts} times in the window; "
    + "a second match releases (true) and claims a new handle; Reset: React false, release false, no re-claim; "
    + "kinds withdrawn with a push between: React false, release true, no re-claim in that match, the next match claims");
Console.WriteLine($"CROWD_REACTION_REAL_SURFACE: {checks - checksBeforeReal} assertions against the installed assembly");

Console.WriteLine($"CROWD_REACTION_CHECKS_PASSED ({checks} assertions)");
