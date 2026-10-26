using ArenaMode.Modes;
using System.Collections;

int checks = 0;
void Need(bool ok, string reason) { checks++; if (!ok) throw new Exception("FAIL: " + reason); }
void Near(float actual, float expected, string reason) => Need(Math.Abs(actual - expected) < .00001f, reason);
void Throws<T>(Action action, string reason) where T : Exception
{
    bool thrown = false;
    try { action(); } catch (T) { thrown = true; }
    Need(thrown, reason);
}
DominationOccupancy U(int count = 1) => new(count, 0);
DominationOccupancy B(int count = 1) => new(0, count);
DominationOccupancy E() => new(0, 0);
DominationOccupancy C() => new(1, 1);
void Step(DominationPolicy p, float dt, params DominationOccupancy[] sample) => p.Advance(dt, sample);
void Same(DominationPolicy a, DominationPolicy b, string name)
{
    Need(a.UsecScore == b.UsecScore && a.BearScore == b.BearScore, name + " scores");
    for (int i = 0; i < a.Points.Count; i++)
    {
        Need(a.Points[i].OwnerUsec == b.Points[i].OwnerUsec, name + " ownership");
        Near(a.Points[i].CaptureProgress, b.Points[i].CaptureProgress, name + " progress");
    }
}

var p = new DominationPolicy();
Need(p.Points.Count == 3 && p.Points.All(x => x.OwnerUsec == null && x.CaptureProgress == 0), "initial neutral points");
Need(p.UsecScore == 0 && p.BearScore == 0, "initial zero scores");
Need(p.Points is not DominationPointState[], "views do not expose writable array");
Throws<NotSupportedException>(() => ((IList<DominationPointState>)p.Points)[0] = default, "read-only point view");
Step(p, 2, U(), B(), E());
Near(p.Points[0].CaptureProgress, .4f, "USEC captures forward");
Near(p.Points[1].CaptureProgress, -.4f, "BEAR captures backward");
Need(p.Points.All(x => x.OwnerUsec == null) && p.UsecScore == 0 && p.BearScore == 0, "partial capture awards no income");
Step(p, 3, U(), B(), E());
Need(p.Points[0].OwnerUsec == true && p.Points[1].OwnerUsec == false && p.Points[2].OwnerUsec == null, "both factions capture independently");
Need(p.UsecScore == 0 && p.BearScore == 0, "capture interval never pays retroactively");
Step(p, 2, E(), E(), E());
Need(p.UsecScore == 2 && p.BearScore == 2, "empty owned points earn income");
Need(p.Points[0].OwnerUsec == true && p.Points[1].OwnerUsec == false, "empty points retain ownership");
Step(p, 3, C(), C(), C());
Need(p.UsecScore == 2 && p.BearScore == 2, "contested points pause income");
Near(p.Points[0].CaptureProgress, 1, "contested freezes capture");
Need(p.Points.All(x => x.Contested) && p.Points[0].UnderAttack && !p.Points[2].UnderAttack, "contested and attack views");
Step(p, 2, B(), U(), E());
Need(p.UsecScore == 2 && p.BearScore == 2, "enemy presence stops old owner income");
Near(p.Points[0].CaptureProgress, .6f, "enemy erodes owned progress");
Need(p.Points[0].OwnerUsec == true && p.Points[0].UnderAttack && !p.Points[0].Contested, "owner retained until neutralized");
Step(p, 3, B(), U(), E());
Need(p.Points[0].OwnerUsec == null && p.Points[1].OwnerUsec == null, "enemy first neutralizes");
Near(p.Points[0].CaptureProgress, 0, "neutralization boundary is zero");
Step(p, 2, B(), U(), E());
Need(p.Points[0].OwnerUsec == null && p.Points[1].OwnerUsec == null, "opposite partial capture stays neutral");
Step(p, 3, E(), E(), E());
Near(p.Points[0].CaptureProgress, -.4f, "empty partial progress persists");
Need(p.UsecScore == 2 && p.BearScore == 2, "empty neutral point earns nothing");
Step(p, 4, B(), U(), E());
Need(p.Points[0].OwnerUsec == false && p.Points[1].OwnerUsec == true, "enemy finally captures after neutralization");
Need(p.UsecScore == 3 && p.BearScore == 3, "only actual post-capture remainder pays");

var single = new DominationPolicy(1);
var crowd = new DominationPolicy(1);
Step(single, 2.5f, U()); Step(crowd, 2.5f, U(20));
Same(single, crowd, "headcount does not accelerate");
Need(crowd.Points[0].UsecOccupants == 20 && crowd.Points[0].BearOccupants == 0, "occupancy counts exposed");
Step(crowd, 2.5f, U(20));
Step(crowd, .5f, B());
Step(crowd, .5f, U());
Near(crowd.Points[0].CaptureProgress, 1, "friendly arrival repairs partial neutralization");
Step(crowd, .5f, U());
Need(crowd.UsecScore == 1 && crowd.BearScore == 0, "friendly recapture keeps owned income");

var whole = new DominationPolicy(3);
var slices = new DominationPolicy(3);
Step(whole, 7.75f, U(), U(), B());
for (int i = 0; i < 31; i++) Step(slices, .25f, U(), U(), B());
Same(whole, slices, "partitioned initial capture");
Need(whole.UsecScore == 5 && whole.BearScore == 2, "each held point contributes independently");
Step(whole, 12.25f, B(), B(), U());
for (int i = 0; i < 49; i++) Step(slices, .25f, B(), B(), U());
Same(whole, slices, "partitioned ownership reversal");
Need(whole.UsecScore == 7 && whole.BearScore == 7, "faction income stays separate across reversal");
Step(whole, 1.5f, C(), E(), C());
for (int i = 0; i < 6; i++) Step(slices, .25f, C(), E(), C());
Same(whole, slices, "partitioned contested interval");
Need(whole.UsecScore == 7 && whole.BearScore == 8, "only unopposed point contributes");

var fractional = new DominationPolicy(1);
Step(fractional, 5, U());
for (int i = 0; i < 100; i++) Step(fractional, .01f, U());
Need(fractional.UsecScore == 1, "float partition preserves whole-second income");
var fractionalCapture = new DominationPolicy(1);
for (int i = 0; i < 500; i++) Step(fractionalCapture, .01f, U());
Need(fractionalCapture.Points[0].OwnerUsec == true, "float partition reaches exact capture boundary");
Step(fractionalCapture, 1, U());
Need(fractionalCapture.UsecScore == 1, "capture rounding does not lose next second");

var maximum = new DominationPolicy(16, .1f);
var allUsec = Enumerable.Repeat(U(), 16).ToArray();
Step(maximum, 30, allUsec);
Need(maximum.UsecScore == 478 && maximum.BearScore == 0, "bounded maximal step keeps real capture cost");
foreach (int invalid in new[] { -1, 0, 17, int.MaxValue })
    Throws<ArgumentOutOfRangeException>(() => new DominationPolicy(invalid), "invalid point count");
foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -.1f, 0f, .09f, 60.1f })
    Throws<ArgumentOutOfRangeException>(() => new DominationPolicy(3, invalid), "invalid capture duration");
Throws<ArgumentOutOfRangeException>(() => U(-1), "negative USEC occupancy rejected");
Throws<ArgumentOutOfRangeException>(() => B(-1), "negative BEAR occupancy rejected");
var guarded = new DominationPolicy(2);
Step(guarded, 6, U(), B());
var before = guarded.Points.ToArray();
int beforeUsec = guarded.UsecScore, beforeBear = guarded.BearScore;
foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -.1f, 0f, 30.001f, float.MaxValue })
    Throws<ArgumentOutOfRangeException>(() => Step(guarded, invalid, B(), U()), "invalid time rejected");
Throws<ArgumentNullException>(() => guarded.Advance(1, null), "null occupancy rejected");
Throws<ArgumentException>(() => Step(guarded, 1, U()), "mismatched occupancy rejected");
Throws<InvalidOperationException>(() => guarded.Advance(1, new ThrowingOccupancy()), "sample read failure surfaced");
Need(guarded.UsecScore == beforeUsec && guarded.BearScore == beforeBear
    && guarded.Points.SequenceEqual(before), "invalid sample cannot partially mutate points or score");

// Mirror both teams through varied intervals and occupants, independently
// verifying symmetry without duplicating the capture algorithm.
var usecFirst = new DominationPolicy(3);
var bearFirst = new DominationPolicy(3);
var random = new Random(991);
var stepWhole = new DominationPolicy(3);
var stepSlices = new DominationPolicy(3);
for (int i = 0; i < 100; i++)
{
    var sample = Enumerable.Range(0, 3).Select(_ => new DominationOccupancy(random.Next(3), random.Next(3))).ToArray();
    int quarters = random.Next(1, 121);
    Step(stepWhole, quarters / 4f, sample);
    for (int part = 0; part < quarters; part++) Step(stepSlices, .25f, sample);
    Same(stepWhole, stepSlices, "varied timestep partition");
}
for (int i = 0; i < 1000; i++)
{
    var sample = Enumerable.Range(0, 3).Select(_ => new DominationOccupancy(random.Next(3), random.Next(3))).ToArray();
    var reverse = sample.Select(x => new DominationOccupancy(x.Bear, x.Usec)).ToArray();
    float dt = (random.Next(30) + 1) / 4f;
    Step(usecFirst, dt, sample); Step(bearFirst, dt, reverse);
    Need(usecFirst.UsecScore == bearFirst.BearScore && usecFirst.BearScore == bearFirst.UsecScore, "mirrored scores are faction neutral");
    for (int point = 0; point < 3; point++)
    {
        var left = usecFirst.Points[point]; var right = bearFirst.Points[point];
        Need(left.OwnerUsec == (right.OwnerUsec.HasValue ? !right.OwnerUsec.Value : (bool?)null), "mirrored point owners");
        Near(left.CaptureProgress, -right.CaptureProgress, "mirrored capture progress");
        Need(left.CaptureProgress >= -1f && left.CaptureProgress <= 1f, "progress stays bounded");
        Need(!left.OwnerUsec.HasValue || (left.OwnerUsec.Value ? left.CaptureProgress >= 0f : left.CaptureProgress <= 0f), "owner agrees with progress sign");
    }
}
// Score-limit clipping is checked against closed-form independent examples,
// including unequal capture completion times and retained fractional income.
var capped = new DominationPolicy(3);
double cappedDuration = capped.AdvanceUntilScoreLimit(30, new[] { U(), U(), B() }, 7);
Need(Math.Abs(cappedDuration - 8.5d) < .000001d, "analytic cap stops at first income crossing");
Need(capped.UsecScore == 7 && capped.BearScore == 3, "cap never gives losing team post-terminal income");
var staged = new DominationPolicy(3);
Step(staged, 2, U(), E(), B());
double stagedDuration = staged.AdvanceUntilScoreLimit(12, new[] { U(), U(), B() }, 4);
Need(Math.Abs(stagedDuration - 6d) < .000001d && staged.UsecScore == 4 && staged.BearScore == 3,
    "staggered capture changes scoring slope at completion");
var early = new DominationPolicy(3);
Step(early, 5, U(), E(), E());
Need(early.AdvanceUntilScoreLimit(30, new[] { U(), U(), U() }, 1) == 1d
    && early.Points[1].OwnerUsec == null && early.Points[1].CaptureProgress == .2f,
    "crossing before later capture freezes unfinished points");
var tied = new DominationPolicy(2);
Need(Math.Abs(tied.AdvanceUntilScoreLimit(30, new[] { U(), B() }, 4) - 9d) < .000001d
    && tied.UsecScore == 4 && tied.BearScore == 4, "simultaneous score crossing stays tied");
var capAttack = new DominationPolicy(2);
Step(capAttack, 5, U(), U());
Need(Math.Abs(capAttack.AdvanceUntilScoreLimit(30, new[] { B(), C() }, 1) - 11d) < .000001d
    && capAttack.UsecScore == 0 && capAttack.BearScore == 1, "cap excludes hostile and contested owner income");
var capFraction = new DominationPolicy(3);
Step(capFraction, 5.25f, U(), U(), B());
Need(Math.Abs(capFraction.AdvanceUntilScoreLimit(10, new[] { U(), U(), B() }, 1) - .25d) < .000001d,
    "cap includes prior fractional holding income");
var thirds = new DominationPolicy(3);
Step(thirds, 5, U(), U(), U());
Need(Math.Abs(thirds.AdvanceUntilScoreLimit(30, new[] { U(), U(), U() }, 7) - 7d / 3d) < .000001d
    && thirds.UsecScore == 7, "nonbinary crossing reaches exact whole score");
Need(thirds.AdvanceUntilScoreLimit(1, new[] { B(), B(), B() }, 7) == 0d
    && thirds.Points.All(x => x.CaptureProgress == 1), "already reached limit integrates no extra time");
Throws<ArgumentOutOfRangeException>(() => thirds.AdvanceUntilScoreLimit(1, new[] { U(), U(), U() }, 0), "invalid cap rejected");
Throws<ArgumentOutOfRangeException>(() => thirds.AdvanceUntilScoreLimit(double.NaN, new[] { U(), U(), U() }, 9), "invalid capped time rejected");

DominationMatchPolicy Live(int score = 200, int time = 600, float start = 3)
{
    var match = new DominationMatchPolicy(score, time);
    match.UpdateStart(start - 3, true);
    match.UpdateStart(start, true);
    Need(match.Phase == TdmMatchPhase.Live && match.StartedAt == start, "ready match enters live after countdown");
    return match;
}
var defaults = new DominationMatchPolicy();
Need(defaults.ScoreLimit == 200 && defaults.TimeLimitSeconds == 600
    && defaults.Points.Count == 3
    && defaults.Phase == TdmMatchPhase.Assembling && defaults.Result == TdmMatchResult.None, "objective defaults and initial lifecycle");
foreach (int invalid in new[] { -1, 1001 })
    Throws<ArgumentOutOfRangeException>(() => new DominationMatchPolicy(invalid), "invalid score limit rejected");
foreach (int invalid in new[] { -1, 7201 })
    Throws<ArgumentOutOfRangeException>(() => new DominationMatchPolicy(200, invalid), "invalid match duration rejected");
defaults.Advance(10, null, true);
defaults.UpdateStart(10, false);
Need(defaults.Points.All(x => x.CaptureProgress == 0) && defaults.Elapsed(10) == 0, "assembling never captures or charges time");
defaults.UpdateStart(10, true);
Need(defaults.Phase == TdmMatchPhase.Countdown && defaults.CountdownRemaining(10) == 3, "ready roster begins countdown");
defaults.UpdateStart(12, true);
defaults.Advance(12, new[] { U(), U(), U() }, true);
Need(defaults.Points.All(x => x.CaptureProgress == 0) && defaults.CountdownRemaining(12) == 1, "countdown does not capture");
defaults.UpdateStart(11, false);
Need(defaults.Phase == TdmMatchPhase.Countdown, "backward readiness sample ignored");
defaults.UpdateStart(12, false);
Need(defaults.Phase == TdmMatchPhase.Assembling, "lost readiness cancels countdown");
defaults.UpdateStart(14, true);
defaults.UpdateStart(20, true);
Need(defaults.Phase == TdmMatchPhase.Live && defaults.StartedAt == 20 && defaults.Elapsed(20) == 0,
    "late release never charges pre-live time");
defaults.UpdateStart(25, false);
Need(defaults.Phase == TdmMatchPhase.Live, "live roster loss does not reset objective match");
defaults.Advance(25, new[] { U(), U(), B() }, false);
Need(defaults.UsecScore == 0 && defaults.BearScore == 0, "live capture consumes full duration before scoring");
defaults.Advance(26, new[] { U(), U(), B() }, false);
Need(defaults.UsecScore == 2 && defaults.BearScore == 1 && defaults.FriendlyScore == 1 && defaults.OpponentScore == 2,
    "live scores map to selected local faction");
var pausedView = defaults.Points.ToArray();
foreach (float now in new[] { 26f, 25f, float.NaN, float.PositiveInfinity, -1f })
    defaults.Advance(now, new[] { B(), B(), U() }, true);
Need(defaults.Points.SequenceEqual(pausedView) && defaults.FriendlyScore == 1 && defaults.OpponentScore == 2,
    "paused backward and invalid clocks award nothing");

var win = Live(5);
win.Advance(8, new[] { U(), U(), B() }, false);
win.Advance(30, new[] { U(), U(), B() }, false);
Need(win.Phase == TdmMatchPhase.Finished && win.UsecScore == 5 && win.BearScore == 2,
    "lifecycle freezes on exact score crossing");
Need(win.FinishedAt == 10.5f && win.Elapsed(100) == 7.5f && win.Result == TdmMatchResult.Lost
    && win.Reason == "Score limit reached", "score result uses correct faction and actual finish time");
var finalView = win.Points.ToArray();
win.Advance(31, new[] { B(), B(), U() }, true);
win.UpdateStart(31, false);
Need(win.Points.SequenceEqual(finalView) && win.FriendlyScore == 2 && win.OpponentScore == 5
    && win.Result == TdmMatchResult.Lost && win.FinishedAt == 10.5f, "terminal state and local result cannot change");
var draw = Live(4);
draw.Advance(25, new[] { U(), B(), E() }, true);
Need(draw.Result == TdmMatchResult.Draw && draw.UsecScore == 4 && draw.BearScore == 4 && draw.FinishedAt == 12,
    "simultaneous terminal crossings draw");
var timed = Live(100, 7);
timed.Advance(30, new[] { U(), U(), B() }, true);
Need(timed.Result == TdmMatchResult.Won && timed.UsecScore == 4 && timed.BearScore == 2
    && timed.FinishedAt == 10 && timed.Reason == "Time limit reached", "deadline clips capture and income before result");
var captureDeadline = Live(100, 5);
captureDeadline.Advance(20, new[] { U(), U(), U() }, true);
Need(captureDeadline.Result == TdmMatchResult.Draw && captureDeadline.UsecScore == 0
    && captureDeadline.FinishedAt == 8, "capture completing at deadline pays nothing");
var sameDeadline = Live(4, 7);
sameDeadline.Advance(30, new[] { U(), U(), B() }, true);
Need(sameDeadline.FinishedAt == 10 && sameDeadline.UsecScore == 4
    && sameDeadline.Reason == "Score limit reached", "simultaneous score and time limit has deterministic reason");
var noScoreLimit = Live(0, 20);
noScoreLimit.Advance(23, new[] { U(), U(), U() }, true);
Need(noScoreLimit.UsecScore == 45 && noScoreLimit.FinishedAt == 23
    && noScoreLimit.Reason == "Time limit reached", "disabled score limit retains deadline accounting");
var noTimeLimit = Live(2, 0);
noTimeLimit.Advance(9, new[] { U(), U(), U() }, true);
Need(noTimeLimit.UsecScore == 2 && noTimeLimit.Result == TdmMatchResult.Won,
    "disabled time limit still stops at objective threshold");
var stall = Live(200, 100);
stall.Advance(40, new[] { U(), U(), U() }, true);
Need(stall.Phase == TdmMatchPhase.Live && stall.Points.All(x => x.OwnerUsec == null)
    && stall.UsecScore == 0, "long unsampled gap invents no capture or points");
stall.Advance(45, new[] { U(), U(), U() }, true);
stall.Advance(46, new[] { U(), U(), U() }, true);
Need(stall.UsecScore == 3, "sampling resumes from rebased time after long gap");
stall.Advance(140, new[] { U(), U(), U() }, true);
Need(stall.Phase == TdmMatchPhase.Finished && stall.FinishedAt == 103 && stall.UsecScore == 3,
    "long gap still honors actual match deadline");
var atomicMatch = Live();
Throws<ArgumentException>(() => atomicMatch.Advance(10, new[] { U() }, true), "invalid match sample rejected");
atomicMatch.Advance(10, new[] { U(), U(), B() }, true);
Need(atomicMatch.UsecScore == 4 && atomicMatch.BearScore == 2, "invalid sample cannot consume match clock");

for (int trial = 0; trial < 100; trial++)
{
    var coarse = Live(13, 100);
    var fine = Live(13, 100);
    float now = 3;
    for (int interval = 0; interval < 20 && coarse.Phase != TdmMatchPhase.Finished; interval++)
    {
        var sample = Enumerable.Range(0, 3).Select(_ => new DominationOccupancy(random.Next(3), random.Next(3))).ToArray();
        int quarters = random.Next(1, 121);
        coarse.Advance(now + quarters / 4f, sample, true);
        for (int part = 1; part <= quarters; part++) fine.Advance(now + part / 4f, sample, true);
        now += quarters / 4f;
        Need(coarse.UsecScore == fine.UsecScore && coarse.BearScore == fine.BearScore
            && coarse.Phase == fine.Phase && coarse.Result == fine.Result, "lifecycle partition preserves scores and terminal result");
        Near(coarse.FinishedAt, fine.FinishedAt, "lifecycle partition preserves finish time");
        for (int point = 0; point < 3; point++)
        {
            Need(coarse.Points[point].OwnerUsec == fine.Points[point].OwnerUsec, "capped partition preserves owners");
            Near(coarse.Points[point].CaptureProgress, fine.Points[point].CaptureProgress, "capped partition preserves progress");
        }
    }
}

checks += PointCountChecks.Run();

// Keep the established kill-shaped TDM policy linked, independently verifying
// that Domination has not replaced its limits or original result semantics.
var tdm = new TeamDeathmatchMatchPolicy(5, 60);
tdm.Update(0, true, 0, 0); tdm.Update(3, true, 0, 0); tdm.Update(4, true, 5, 1);
Need(tdm.Phase == TdmMatchPhase.Finished && tdm.KillLimit == 5 && tdm.FriendlyKills == 5
    && tdm.OpponentKills == 1 && tdm.Reason == "Kill limit reached", "original TDM kill lifecycle remains separate");
Console.WriteLine($"DOMINATION_POLICY_CHECKS_PASSED ({checks} assertions)");

sealed class ThrowingOccupancy : IReadOnlyList<DominationOccupancy>
{
    public int Count => 2;
    public DominationOccupancy this[int index] => index == 0 ? new(0, 1) : throw new InvalidOperationException("sample unavailable");
    public IEnumerator<DominationOccupancy> GetEnumerator() => throw new NotSupportedException();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
