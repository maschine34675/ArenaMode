using ArenaMode.Modes;

int checks = 0;
void Need(bool ok, string reason) { checks++; if (!ok) throw new Exception("FAIL: " + reason); }
void Near(float actual, float expected, string reason) => Need(Math.Abs(actual - expected) < .0005f, reason);

// ---------------------------------------------------------------- constructor clamps
var clamped = new TacticalObjectivePolicy(-5f, 1f);
Need(clamped.AppearAfterSeconds == 0f && clamped.HoldSeconds == TacticalObjectivePolicy.MinimumHoldSeconds,
    "appear and hold seconds clamp at their floors");
clamped = new TacticalObjectivePolicy(1000f, 100f);
Need(clamped.AppearAfterSeconds == TacticalObjectivePolicy.MaximumAppearSeconds
    && clamped.HoldSeconds == TacticalObjectivePolicy.MaximumHoldSeconds, "appear and hold seconds clamp at their ceilings");
clamped = new TacticalObjectivePolicy(float.NaN, float.NaN);
Need(clamped.AppearAfterSeconds == 0f && clamped.HoldSeconds == TacticalObjectivePolicy.MinimumHoldSeconds, "NaN settings fall to the floors");

// ---------------------------------------------------------------- before the round
var policy = new TacticalObjectivePolicy(60f, 10f);
Need(!policy.Started && !policy.Active && policy.UntilActive(5f) == 0f && policy.Describe(5f) == string.Empty,
    "a policy that has not begun is inert");
Need(policy.Advance(5f, 1, 0) == TacticalObjectiveSide.None && policy.Progress == 0f, "steps before the round change nothing");

// ---------------------------------------------------------------- appearance
policy.Begin(100f);
Need(policy.Started && !policy.Active && policy.UntilActive(100f) == 60f && policy.Describe(100f) == "POINT in 60s",
    "the point waits for the appear delay");
Need(policy.Advance(130f, 1, 0) == TacticalObjectiveSide.None && !policy.Active && policy.Progress == 0f,
    "standing on the point before it appears earns nothing");
Need(policy.Describe(130f) == "POINT in 30s", "the countdown to the point counts down");
Need(policy.Advance(159.9f, 0, 0) == TacticalObjectiveSide.None && !policy.Active, "the point waits for the full delay");
Need(policy.Advance(160f, 0, 0) == TacticalObjectiveSide.None && policy.Active && policy.UntilActive(160f) == 0f
    && policy.Describe(160f) == "POINT free", "the point appears at the delay");

// ---------------------------------------------------------------- a clean friendly hold
float t = 160f;
TacticalObjectiveSide result = TacticalObjectiveSide.None;
int completions = 0;
for (int i = 0; i < 25; i++)
{
    t += .5f;
    result = policy.Advance(t, 1, 0);
    if (result != TacticalObjectiveSide.None) completions++;
    if (i == 7) { Near(policy.Progress, .4f, "progress accumulates at the hold rate"); Need(policy.Describe(t) == "POINT you 40%", "the status line shows the friendly hold"); }
}
Need(completions == 1 && policy.Captured == TacticalObjectiveSide.Friendly, "a hold completes exactly once");
Need(policy.Progress == 1f && policy.Describe(t) == "POINT held by you", "a completed hold is terminal and named");
Need(policy.Advance(t + 1f, 0, 1) == TacticalObjectiveSide.None && policy.Captured == TacticalObjectiveSide.Friendly,
    "nothing changes after the capture");

// ---------------------------------------------------------------- contested freezes, empty resets, switch restarts
policy = new TacticalObjectivePolicy(0f, 10f);
policy.Begin(0f);
Need(policy.Advance(0f, 0, 0) == TacticalObjectiveSide.None && policy.Active, "a zero delay puts the point up at once");
for (int i = 1; i <= 4; i++) policy.Advance(i, 0, 1);
Near(policy.Progress, .4f, "the enemy accumulates alone");
Need(policy.Holder == TacticalObjectiveSide.Enemy && policy.Describe(4f) == "POINT enemy 40%", "the enemy is the holder");
policy.Advance(5f, 1, 1);
Need(policy.Contested && Math.Abs(policy.Progress - .4f) < .0005f && policy.Describe(5f) == "POINT contested",
    "contested points pause the hold without resetting it");
policy.Advance(6f, 0, 1);
Near(policy.Progress, .5f, "the hold resumes when the contest ends");
policy.Advance(7f, 0, 0);
Need(policy.Progress == 0f && policy.Holder == TacticalObjectiveSide.None && policy.Describe(7f) == "POINT free",
    "an empty point resets the hold");
for (int i = 8; i <= 11; i++) policy.Advance(i, 0, 1);
Near(policy.Progress, .4f, "the enemy starts over");
policy.Advance(12f, 1, 0);
Need(policy.Holder == TacticalObjectiveSide.Friendly && Math.Abs(policy.Progress - .1f) < .0005f,
    "a change of holder restarts the hold for the new holder");

// ---------------------------------------------------------------- time discipline
policy = new TacticalObjectivePolicy(0f, 10f);
policy.Begin(0f);
policy.Advance(1f, 1, 0);
Need(policy.Advance(.5f, 1, 0) == TacticalObjectiveSide.None && Math.Abs(policy.Progress - .1f) < .0005f,
    "time running backwards changes nothing");
Need(policy.Advance(float.NaN, 1, 0) == TacticalObjectiveSide.None && Math.Abs(policy.Progress - .1f) < .0005f,
    "a NaN time changes nothing");
policy.Advance(2f, 1, 0);
Near(policy.Progress, .2f, "the clock continues after a refused step");
policy.Advance(40f, 1, 0);
Near(policy.Progress, .2f, "a step over thirty seconds awards nothing");
policy.Advance(41f, 1, 0);
Near(policy.Progress, .3f, "the hold continues after a stall");
Need(policy.Advance(42f, -3, 0) == TacticalObjectiveSide.None && policy.Holder == TacticalObjectiveSide.None,
    "negative occupancy counts as nobody");

// ---------------------------------------------------------------- the enemy capture and reset
policy = new TacticalObjectivePolicy(0f, 3f);
policy.Begin(10f);
policy.Advance(10f, 0, 1);
Need(policy.Advance(13f, 0, 1) == TacticalObjectiveSide.Enemy && policy.Describe(13f) == "POINT held by enemy",
    "the enemy completes the hold at the threshold");
policy.Reset();
Need(!policy.Started && !policy.Active && policy.Captured == TacticalObjectiveSide.None && policy.Progress == 0f,
    "reset clears the round");

// ---------------------------------------------------------------- lanes
var squad = new TacticalLanePoint(0f, 1f, 0f);
var players = new TacticalLanePoint(0f, 1f, 100f);
var lanes = TacticalObjectiveLanes.Compute(squad, players, 5);
Need(lanes.Length == 5, "one lane per squad bot");
Need(lanes.All(p => Math.Abs(p.Z - 50f) < .001f && Math.Abs(p.Y - 1f) < .001f), "lanes lie across the middle between the groups");
Need(Math.Abs(lanes.Sum(p => p.X)) < .001f, "lanes are centred on the axis");
float spacing = lanes[1].X - lanes[0].X;
Need(Math.Abs(Math.Abs(spacing) - 15f) < .001f, "lane spacing is 15 % of the distance");
Need(Enumerable.Range(1, 4).All(i => Math.Abs((lanes[i].X - lanes[i - 1].X) - spacing) < .001f), "lanes are evenly spaced");
var swapped = TacticalObjectiveLanes.Compute(players, squad, 5);
Need(lanes.All(l => swapped.Any(s => Math.Abs(s.X - l.X) < .001f && Math.Abs(s.Z - l.Z) < .001f)),
    "lanes do not depend on which group is named first");
var close = TacticalObjectiveLanes.Compute(squad, new TacticalLanePoint(0f, 1f, 20f), 3);
Need(Math.Abs(Math.Abs(close[1].X - close[0].X) - TacticalObjectiveLanes.MinSpacing) < .001f, "lane spacing floors at six metres");
var far = TacticalObjectiveLanes.Compute(squad, new TacticalLanePoint(0f, 1f, 400f), 3);
Need(Math.Abs(Math.Abs(far[1].X - far[0].X) - TacticalObjectiveLanes.MaxSpacing) < .001f, "lane spacing caps at eighteen metres");
var diagonal = TacticalObjectiveLanes.Compute(new TacticalLanePoint(0f, 0f, 0f), new TacticalLanePoint(100f, 0f, 100f), 2);
Need(Math.Abs((diagonal[1].X - diagonal[0].X) * 1f + (diagonal[1].Z - diagonal[0].Z) * 1f) < .001f,
    "lanes stay perpendicular on a diagonal axis");
Need(TacticalObjectiveLanes.Compute(null, players, 5).Length == 0 && TacticalObjectiveLanes.Compute(squad, null, 5).Length == 0,
    "a missing centroid yields no lanes");
Need(TacticalObjectiveLanes.Compute(squad, players, 0).Length == 0, "no bots, no lanes");
var same = TacticalObjectiveLanes.Compute(squad, squad, 3);
Need(same.Length == 3 && Math.Abs(same[1].X - same[0].X) > 1f, "coincident centroids still fan out along a default axis");

Console.WriteLine($"PASS: {checks} Tactical objective policy and lane checks.");
Console.WriteLine("TACTICAL_OBJECTIVE_CHECKS_PASSED");
