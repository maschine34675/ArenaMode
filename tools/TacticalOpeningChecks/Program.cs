using System.Net.Http;
using ArenaMode.Modes;
using ArenaMode.Shared;

// Solo Tactical opening rounds, client half (docs/reviews/TACTICAL-OPENING-ROUNDS-20261119.md):
//  - TacticalOpeningBandGate (the report, the hold, the resend, the failure shapes) runs here against a
//    fake clock and a scripted sender, as compiled production source;
//  - TacticalRoundReply.Parse runs against the server's reply shapes;
//  - the glue in TacticalMode.cs, the HTTP sender and the server's use of the shared refusal constant are
//    pinned as source text (the game's types cannot be linked), with every pin backed by a mutant in
//    Verify.ps1 that must fail exactly the assertion it names.
// A failed assertion prints "FAIL: <message>" and exits 1; Verify.ps1 compares those messages.
internal static partial class Program
{
    private static int _assertions;

    private static int Main(string[] args)
    {
        try
        {
            CheckReplyParser();
            CheckHoldAndCap();
            CheckAckRules();
            CheckStageRule();
            CheckFailureShapes();
            CheckRefusals();
            CheckSeqAndToken();
            CheckNeverThrows();
            CheckGearLevel();
            CheckOpponentNote();
            CheckSharedConstant();
            CheckSources();
            Console.WriteLine($"ARENA_TACTICAL_OPENING_CHECKS_PASSED ({_assertions} assertions)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL: {ex.Message}");
            return 1;
        }
    }

    private static void Assert(bool condition, string message)
    {
        _assertions++;
        if (!condition)
            throw new InvalidOperationException(message);
    }

    /// <summary>True when <paramref name="action"/> throws.</summary>
    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (Exception)
        {
            return true;
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("ArenaMode repository root not found");
    }

    /// <summary>The production source text, from the environment variable a mutant sets or from the repository.</summary>
    private static string Source(string variable, params string[] relative)
    {
        string path = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(path))
            path = Path.Combine(new[] { FindRepositoryRoot() }.Concat(relative).ToArray());
        return File.ReadAllText(path).Replace("\r\n", "\n");
    }

    /// <summary>What the scripted sender does with call number i.</summary>
    internal enum Plan { Pending, Fail, Throw, Ack, Duplicate, Refuse, RaidEnding, NotApplying }

    internal static TacticalRoundReply Ok(int round, int seq, bool applies = true, bool duplicate = false)
        => new TacticalRoundReply { Accepted = true, Applies = applies, Round = round, Seq = seq, Duplicate = duplicate };

    internal static TacticalRoundReply Refused(string reason, int round = 1, int seq = 0)
        => new TacticalRoundReply { Accepted = false, Applies = false, Round = round, Seq = seq, Reason = reason };

    /// <summary>A gate on a fake clock with a scripted sender, token and gear numbers.</summary>
    internal sealed class Rig
    {
        public float Now;
        public string Token = "arm-token";
        public bool TokenThrows;
        public bool ClockThrows;
        public bool ContextThrows;
        public TacticalGearContext? Context = new TacticalGearContext(ArenaDifficultyStep.Maximum, 1, 66, true);
        public readonly List<(string Token, int Round, int Seq)> Calls = new();
        public readonly List<TaskCompletionSource<TacticalRoundReply>> Sources = new();
        public readonly Dictionary<int, Plan> Plans = new();
        public Plan DefaultPlan = Plan.Pending;
        public readonly List<string> Debug = new();
        public readonly List<string> Warn = new();
        public readonly TacticalOpeningBandGate Gate;

        public Rig(bool resetSeq = true)
        {
            if (resetSeq)
                TacticalOpeningBandGate.LastSeqForTests = 100;
            Gate = new TacticalOpeningBandGate(
                () => ClockThrows ? throw new InvalidOperationException("scripted clock failure") : Now,
                Send,
                () => TokenThrows ? throw new InvalidOperationException("scripted token failure") : Token,
                () => ContextThrows ? throw new InvalidOperationException("scripted context failure") : Context,
                Debug.Add,
                Warn.Add);
        }

        private Task<TacticalRoundReply> Send(string token, int round, int seq)
        {
            int index = Calls.Count;
            Calls.Add((token, round, seq));
            var source = new TaskCompletionSource<TacticalRoundReply>();
            Sources.Add(source);
            switch (Plans.TryGetValue(index, out var plan) ? plan : DefaultPlan)
            {
                case Plan.Throw: throw new InvalidOperationException("scripted sender failure");
                case Plan.Fail: source.SetException(new HttpRequestException("scripted transport failure")); break;
                case Plan.Ack: source.SetResult(Ok(round, seq)); break;
                case Plan.Duplicate: source.SetResult(Ok(round, seq, duplicate: true)); break;
                case Plan.Refuse: source.SetResult(Refused("arm token did not match")); break;
                case Plan.RaidEnding: source.SetResult(Refused(Protocol.TacticalRoundRaidEndingRefusal)); break;
                case Plan.NotApplying: source.SetResult(Ok(round, seq, applies: false)); break;
            }
            return source.Task;
        }

        /// <summary>The server answers call i later, by hand.</summary>
        public void Complete(int index, TacticalRoundReply reply) => Sources[index].SetResult(reply);

        /// <summary>Reports <paramref name="round"/> and lets the server acknowledge it at once.</summary>
        public void Next(int round)
        {
            Plans[Calls.Count] = Plan.Ack;
            Gate.Report(round);
            Gate.Tick();
        }

        /// <summary>Ticks the gate every half second up to <paramref name="to"/> (the clock ends exactly there).</summary>
        public void TickTo(float to)
        {
            while (Now < to)
            {
                Now = Math.Min(to, Now + 0.5f);
                Gate.Tick();
            }
        }

        /// <summary>Acknowledged rounds 1 and 2 at time 0 (a raid in progress at round 3's report).</summary>
        public static Rig AtRound(int acknowledged)
        {
            var rig = new Rig();
            for (int round = 1; round <= acknowledged; round++)
                rig.Next(round);
            return rig;
        }
    }

    // ------------------------------------------------------------------------------------------

    private static void CheckReplyParser()
    {
        var reply = TacticalRoundReply.Parse("{\"accepted\":true,\"applies\":true,\"round\":2,\"seq\":7,\"duplicate\":false,\"reason\":null}");
        Assert(reply.Accepted && reply.Applies && reply.Round == 2 && reply.Seq == 7 && !reply.Duplicate && reply.Reason == null,
            "the reply parser reads accepted, applies, the stored round and seq, duplicate and reason");
        var refusal = TacticalRoundReply.Parse("{\"accepted\":false,\"applies\":false,\"round\":1,\"seq\":0,\"duplicate\":false,\"reason\":\"raid is ending\"}");
        Assert(!refusal.Accepted && refusal.Reason == "raid is ending",
            "the reply parser reads a refusal and its reason");
        Assert(TacticalRoundReply.Parse("{\"accepted\":true,\"applies\":true,\"round\":3,\"seq\":9,\"duplicate\":true}").Duplicate,
            "the reply parser reads a duplicate reply");
        foreach (string malformed in new[]
                 {
                     "", "   ", "not json", "[]", "{}",
                     "{\"applies\":true,\"round\":1,\"seq\":1}",
                     "{\"accepted\":true,\"round\":1,\"seq\":1}",
                     "{\"accepted\":true,\"applies\":true,\"seq\":1}",
                     "{\"accepted\":true,\"applies\":true,\"round\":1}",
                     "{\"accepted\":\"yes\",\"applies\":true,\"round\":1,\"seq\":1}",
                     "{\"accepted\":true,\"applies\":true,\"round\":\"2\",\"seq\":1}",
                 })
            Assert(Throws(() => TacticalRoundReply.Parse(malformed)),
                $"a reply without a usable accepted, applies, round and seq is malformed: {malformed}");
    }

    private static void CheckHoldAndCap()
    {
        // A fresh gate has nothing to wait for: it never made a report.
        var fresh = new Rig();
        Assert(fresh.Gate.ShouldRelease(2) && fresh.Gate.Evaluate(2) == TacticalBandRelease.NothingOutstanding,
            "ShouldRelease is true when no report is outstanding");

        var rig = Rig.AtRound(1);
        Assert(rig.Gate.AckedRound == 1 && rig.Gate.Settled(1) && !rig.Gate.Settled(2) && rig.Gate.Applies,
            "an acknowledged report settles exactly its own round");

        rig.Now = 100f;
        rig.Gate.Report(2);
        rig.Gate.MarkHeld(2);
        Assert(!rig.Gate.ShouldRelease(2) && rig.Gate.Evaluate(2) == TacticalBandRelease.Hold,
            "a round whose gear level differs from the acknowledged round's waits for the ack");
        rig.TickTo(102.9f);
        Assert(!rig.Gate.ShouldRelease(2), "the hold lasts the full 3 seconds");
        rig.TickTo(103f);
        Assert(rig.Gate.ShouldRelease(2) && rig.Gate.Evaluate(2) == TacticalBandRelease.CapExpired,
            "the hold ends at the 3 second cap, whatever the ack");
        Assert(rig.Gate.AckedRound == 1 && rig.Gate.CurrentGearLevel == 25,
            "the cap leaves the acknowledged round alone: the client keeps expecting what it knows");
        Assert(Math.Abs(rig.Gate.HeldSeconds(2) - 3f) < 0.001f && rig.Gate.HeldSeconds(3) == 0f,
            "HeldSeconds reports the real held time of the round, and 0 for a round never held");

        // The cap runs from MarkHeld (the moment the request would go out), not from the report, and a
        // later MarkHeld of the same round keeps the first stamp.
        var late = Rig.AtRound(1);
        late.Now = 0f;
        late.Gate.Report(2);
        late.TickTo(6f);
        late.Gate.MarkHeld(2);
        Assert(!late.Gate.ShouldRelease(2), "the hold clock starts when the request is held, not when the round was reported");
        late.TickTo(7f);
        late.Gate.MarkHeld(2);
        late.TickTo(8.9f);
        Assert(!late.Gate.ShouldRelease(2), "the hold lasts 3 seconds from the first stamp");
        late.TickTo(9f);
        Assert(late.Gate.ShouldRelease(2), "later MarkHeld calls keep the first stamp");

        // The ack before the cap releases at once.
        var early = Rig.AtRound(1);
        early.Now = 50f;
        early.Gate.Report(2);
        early.Gate.MarkHeld(2);
        early.Now = 51f;
        early.Complete(early.Calls.Count - 1, Ok(2, early.Calls[^1].Seq));
        early.Gate.Tick();
        Assert(early.Gate.Evaluate(2) == TacticalBandRelease.NothingOutstanding && early.Gate.Settled(2)
               && early.Gate.AckedRound == 2,
            "the ack releases the hold before the cap");
    }

    private static void CheckAckRules()
    {
        // READY pressed at once: the aftermath (6 s) is the shortest gap from the report to the request.
        var ready = Rig.AtRound(1);
        ready.Now = 100f;
        ready.Plans[ready.Calls.Count] = Plan.Ack;
        ready.Gate.Report(2);
        ready.TickTo(106f);
        ready.Gate.MarkHeld(2);
        Assert(ready.Gate.ShouldRelease(2) && ready.Gate.HeldSeconds(2) == 0f && ready.Gate.AckedRound == 2,
            "READY pressed at once: the ack long arrived, the request goes out without any wait");

        // ... and the same with a route that is dead: its four sends are spent by then, so nothing is outstanding.
        var dead = Rig.AtRound(1);
        dead.Now = 100f;
        dead.DefaultPlan = Plan.Fail;
        dead.Gate.Report(2);
        dead.TickTo(107f);
        dead.Gate.MarkHeld(2);
        Assert(dead.Gate.ShouldRelease(2) && dead.Gate.AckedRound == 1,
            "READY pressed at once with a dead route: released, still at the last acknowledged round");

        // The STORED round of the reply wins over the request's.
        var stored = Rig.AtRound(2);
        stored.Plans[stored.Calls.Count] = Plan.Pending;
        stored.Gate.Report(3);
        stored.Complete(stored.Calls.Count - 1, Ok(2, stored.Calls[^1].Seq));
        stored.Gate.Tick();
        Assert(stored.Gate.AckedRound == 2,
            "the stored round of the reply is trusted over the request's");
        Assert(!stored.Gate.Settled(3),
            "an acknowledgement of another round does not settle the round");

        // A Duplicate reply (a resend the server already holds) is an ack.
        var duplicate = Rig.AtRound(1);
        duplicate.Plans[duplicate.Calls.Count] = Plan.Duplicate;
        duplicate.Gate.Report(2);
        duplicate.Gate.Tick();
        Assert(duplicate.Gate.AckedRound == 2, "a Duplicate reply counts as acknowledged");

        // A late answer to an older report must not set the client back; the newest one counts.
        var older = Rig.AtRound(1);
        older.Now = 10f;
        older.Plans[older.Calls.Count] = Plan.Pending;
        older.Gate.Report(2);
        int first = older.Calls.Count - 1;
        older.Now = 20f;
        older.Plans[older.Calls.Count] = Plan.Pending;
        older.Gate.Report(3);
        int second = older.Calls.Count - 1;
        older.Complete(first, Ok(2, older.Calls[first].Seq));
        older.Gate.Tick();
        Assert(older.Gate.AckedRound == 1,
            "a late answer to an older report does not set the client back");
        older.Complete(second, Ok(3, older.Calls[second].Seq));
        older.Gate.Tick();
        Assert(older.Gate.AckedRound == 3 && older.Gate.Settled(3),
            "the answer to the newest report is the acknowledged round");

        // Reset drops the state; a late completion of the old match is ignored.
        var reset = new Rig();
        reset.Gate.Report(1);
        reset.Gate.Reset();
        reset.Complete(0, Ok(1, reset.Calls[0].Seq));
        reset.Gate.Tick();
        Assert(reset.Gate.AckedRound == 0 && reset.Gate.Applies && reset.Gate.ShouldRelease(2),
            "Reset ignores a late completion of the earlier match");
    }

    private static void CheckStageRule()
    {
        // The hold applies only where the stage can change: round 1 never waits ...
        var one = new Rig();
        one.Gate.Report(1);
        one.Gate.MarkHeld(1);
        Assert(one.Gate.ShouldRelease(1) && one.Gate.Evaluate(1) == TacticalBandRelease.SameLevel,
            "round 1 never waits for the acknowledgement (no acknowledged round counts as round 1)");

        // ... round 3 waits when the last acknowledged round was lighter ...
        var three = Rig.AtRound(2);
        three.Plans[three.Calls.Count] = Plan.Pending;
        three.Gate.Report(3);
        three.Gate.MarkHeld(3);
        Assert(!three.Gate.ShouldRelease(3), "round 3 waits for the acknowledgement of a lighter round before it");

        // ... and a round from 4 on never does, even with a dead route.
        var four = Rig.AtRound(3);
        four.DefaultPlan = Plan.Fail;
        four.Now = 200f;
        four.Gate.Report(4);
        four.Gate.MarkHeld(4);
        Assert(four.Gate.ShouldRelease(4) && four.Gate.Evaluate(4) == TacticalBandRelease.SameLevel,
            "round 4 with a dead route is released at once");
        four.Gate.Report(5);
        four.Gate.MarkHeld(5);
        Assert(four.Gate.ShouldRelease(5), "round 5 with a dead route is released at once");

        // A route that dies AFTER an early ack: acknowledged round 2, report 3 never answered. Round 3
        // holds (its level differs from the acknowledged one) and ends at the cap, but the acknowledged
        // round never advances, so round 4 must not compare itself with round 2 and hold again.
        var late = Rig.AtRound(2);
        late.DefaultPlan = Plan.Pending;
        late.Gate.Report(3);
        late.Gate.MarkHeld(3);
        late.Now = 3f;
        Assert(late.Gate.Evaluate(3) == TacticalBandRelease.CapExpired,
            "round 3 after an early ack and a lost report waits until the cap");
        late.Gate.Report(4);
        late.Gate.MarkHeld(4);
        Assert(late.Gate.Evaluate(4) == TacticalBandRelease.SameLevel,
            "round 4 never waits, even when the route died after an acknowledgement of round 2 or earlier");
        late.Gate.Report(5);
        late.Gate.MarkHeld(5);
        Assert(late.Gate.ShouldRelease(5), "round 5 never waits after an early acknowledgement either");

        // The hold does not look at the squad's role: the server writes the band role-blind, so a scav
        // squad waits for the acknowledgement exactly like a PMC squad (only the purge and the buy line are PMC-only).
        var scavHold = Rig.AtRound(2);
        scavHold.Context = new TacticalGearContext(ArenaDifficultyStep.Maximum, 1, 66, false);
        scavHold.Plans[scavHold.Calls.Count] = Plan.Pending;
        scavHold.Gate.Report(3);
        scavHold.Gate.MarkHeld(3);
        Assert(!scavHold.Gate.ShouldRelease(3),
            "a scav squad is held for the acknowledgement like a PMC squad");

        // A map whose opening does not differ from the full level holds nothing, not even round 2.
        var flat = Rig.AtRound(1);
        flat.Context = new TacticalGearContext(ArenaDifficultyStep.Maximum, 1, 20, true);
        flat.Plans[flat.Calls.Count] = Plan.Pending;
        flat.Gate.Report(2);
        flat.Gate.MarkHeld(2);
        Assert(flat.Gate.ShouldRelease(2), "a map whose opening rounds equal the full level holds nothing");

        // No numbers (the map does not scale the bots' tier): nothing to hold.
        var none = Rig.AtRound(1);
        none.Context = null;
        none.Plans[none.Calls.Count] = Plan.Pending;
        none.Gate.Report(2);
        none.Gate.MarkHeld(2);
        Assert(none.Gate.ShouldRelease(2), "a map without gear numbers holds nothing");

        // Round 3 after a lost round-2 ack: the last acknowledged level is round 1's, so round 3 waits
        // (the comparison is with the acknowledged round, not with the previous round number).
        var gap = Rig.AtRound(1);
        gap.Plans[gap.Calls.Count] = Plan.Pending;
        gap.Gate.Report(3);
        gap.Gate.MarkHeld(3);
        Assert(!gap.Gate.ShouldRelease(3), "the level of a round is compared with the last ACKNOWLEDGED round");
    }

    private static void CheckFailureShapes()
    {
        // Lost report, answered by the resend after two seconds.
        var lost = Rig.AtRound(1);
        lost.Now = 10f;
        lost.Plans[lost.Calls.Count] = Plan.Fail;
        lost.Plans[lost.Calls.Count + 1] = Plan.Ack;
        lost.Gate.Report(2);
        lost.Gate.MarkHeld(2);
        lost.Gate.Tick();
        lost.Now = 11.9f;
        lost.Gate.Tick();
        Assert(lost.Calls.Count == 2, "a failed report is resent only after 2 seconds");
        lost.Now = 12f;
        lost.Gate.Tick();
        lost.Gate.Tick();
        Assert(lost.Calls.Count == 3 && lost.Calls[2].Seq == lost.Calls[1].Seq && lost.Calls[2].Round == 2,
            "the resend carries the same round and seq");
        Assert(lost.Gate.Evaluate(2) == TacticalBandRelease.NothingOutstanding && lost.Gate.Settled(2)
               && lost.Gate.HeldSeconds(2) < HoldCap,
            "the resend's answer releases the hold before the cap");

        // A route that stays dead after an earlier success: four sends, the cap, one warning, no change.
        var dead = Rig.AtRound(1);
        dead.Now = 10f;
        dead.DefaultPlan = Plan.Fail;
        dead.Gate.Report(2);
        dead.Gate.MarkHeld(2);
        dead.TickTo(12.9f);
        Assert(!dead.Gate.ShouldRelease(2), "a dead route holds the request until the cap");
        dead.TickTo(13f);
        Assert(dead.Gate.Evaluate(2) == TacticalBandRelease.CapExpired && dead.Gate.AckedRound == 1,
            "a dead route releases at the 3 second cap, still at the last acknowledged round");
        dead.TickTo(100f);
        Assert(dead.Calls.Count == 1 + 4,
            "a report is sent at most four times");
        Assert(dead.Gate.Applies && dead.Gate.AckedRound == 1 && dead.Warn.Count == 1,
            "a failure after an earlier success keeps the feature, stays at the last acknowledged round, and warns once");
        Assert(dead.Gate.Evaluate(2) == TacticalBandRelease.NothingOutstanding,
            "a report whose sends are spent is no longer waited for");
        dead.Gate.Report(3);
        dead.TickTo(120f);
        Assert(dead.Warn.Count == 1, "one warning per raid for a failing route");

        // The FIRST report that never gets through ends the feature (an old server without the route).
        var first = new Rig();
        first.DefaultPlan = Plan.Fail;
        first.Gate.Report(1);
        first.TickTo(7f);
        Assert(!first.Gate.Applies && first.Warn.Count == 1 && first.Calls.Count == 4,
            "a first report that never reaches the server ends the feature, with one warning");
        first.Gate.Report(2);
        first.TickTo(20f);
        Assert(first.Calls.Count == 4 && first.Gate.Evaluate(2) == TacticalBandRelease.NotApplying,
            "once the feature is off nothing is sent and nothing is held");
        // The warning is re-armed by Reset (the next raid).
        first.Gate.Reset();
        first.Gate.Report(1);
        first.TickTo(30f);
        Assert(first.Warn.Count == 2, "the once-per-raid warning is written again in the next raid");

        // Lost ack: the report arrived, the answer did not.
        var hang = Rig.AtRound(1);
        hang.Now = 10f;
        hang.Plans[hang.Calls.Count] = Plan.Pending;
        hang.Plans[hang.Calls.Count + 1] = Plan.Duplicate;
        hang.Gate.Report(2);
        hang.Gate.MarkHeld(2);
        hang.TickTo(13f);
        Assert(hang.Gate.Evaluate(2) == TacticalBandRelease.CapExpired && hang.Gate.AckedRound == 1,
            "a lost ack releases at the cap, at the last acknowledged round");
        hang.TickTo(17.9f);
        Assert(hang.Calls.Count == 2, "a send without an answer is not lost before 8 seconds");
        hang.TickTo(18f);
        Assert(hang.Calls.Count == 3 && hang.Calls[2].Seq == hang.Calls[1].Seq,
            "a send without an answer counts as lost after 8 seconds, and the report is sent again with the same seq");
        hang.Gate.Tick();
        Assert(hang.Gate.AckedRound == 2 && hang.Warn.Count == 0,
            "the resend of a lost ack gets a Duplicate reply, which acknowledges");

        // Late ack: the first send answers after the cap.
        var late = Rig.AtRound(1);
        late.Now = 10f;
        late.Plans[late.Calls.Count] = Plan.Pending;
        late.Gate.Report(2);
        late.Gate.MarkHeld(2);
        late.TickTo(13f);
        Assert(late.Gate.ShouldRelease(2) && late.Gate.AckedRound == 1, "the cap released the request");
        late.TickTo(15f);
        late.Complete(1, Ok(2, late.Calls[1].Seq));
        late.Gate.Tick();
        Assert(late.Gate.AckedRound == 2 && late.Gate.CurrentGearLevel == 45,
            "a late acknowledgement raises the acknowledged round");

        // Four hanging sends: the budget is four, and a late answer after the give-up still counts.
        var spent = Rig.AtRound(1);
        spent.Now = 10f;
        spent.Plans[spent.Calls.Count] = Plan.Pending;
        spent.Plans[spent.Calls.Count + 1] = Plan.Pending;
        spent.Plans[spent.Calls.Count + 2] = Plan.Pending;
        spent.Plans[spent.Calls.Count + 3] = Plan.Pending;
        spent.Gate.Report(2);
        spent.TickTo(200f);
        Assert(spent.Calls.Count == 1 + 4, "four hanging sends are the budget of a report");
        spent.Complete(2, Ok(2, spent.Calls[2].Seq));
        spent.Gate.Tick();
        Assert(spent.Gate.AckedRound == 2, "an answer after the budget is spent is still welcome");
    }

    private const float HoldCap = TacticalOpeningBandGate.HoldCapSeconds;

    private static void CheckRefusals()
    {
        // Any other refusal at the first report ends the feature, without a warning.
        var first = new Rig();
        first.Plans[0] = Plan.Refuse;
        first.Gate.Report(1);
        first.Gate.Tick();
        first.TickTo(10f);
        Assert(!first.Gate.Applies && first.Warn.Count == 0 && first.Calls.Count == 1,
            "a refusal ends the feature for the raid: nothing is resent, nothing is warned");

        // ... and at any later report too, which also takes the gear level away.
        var later = Rig.AtRound(2);
        Assert(later.Gate.CurrentGearLevel == 45, "round 2 is acknowledged at the 45 centre");
        later.Plans[later.Calls.Count] = Plan.Refuse;
        later.Gate.Report(3);
        later.Gate.Tick();
        Assert(!later.Gate.Applies && later.Gate.Evaluate(3) == TacticalBandRelease.NotApplying,
            "a refusal in a later round ends the feature and releases every hold");
        Assert(later.Gate.CurrentGearLevel == null,
            "after a refusal there is no gear level to enforce");

        // applies:false is the opted-out map: the feature ends, no warning.
        var optOut = new Rig();
        optOut.Plans[0] = Plan.NotApplying;
        optOut.Gate.Report(1);
        optOut.Gate.Tick();
        Assert(!optOut.Gate.Applies && optOut.Warn.Count == 0 && optOut.Gate.CurrentGearLevel == null,
            "applies:false ends the feature for the raid without a warning");

        // "raid is ending" is TRANSIENT: Applies stays, no warning, not an ack, the resend follows.
        var ending = Rig.AtRound(2);
        ending.Now = 20f;
        ending.Plans[ending.Calls.Count] = Plan.RaidEnding;
        ending.Plans[ending.Calls.Count + 1] = Plan.Ack;
        ending.Gate.Report(3);
        ending.Gate.MarkHeld(3);
        ending.Gate.Tick();
        Assert(ending.Gate.Applies && ending.Warn.Count == 0 && ending.Gate.AckedRound == 2 && !ending.Gate.Settled(3),
            "the raid-ending refusal is transient: the feature stays on, nothing is warned, nothing is acknowledged");
        Assert(!ending.Gate.ShouldRelease(3), "a transient refusal is not an answer: the request keeps waiting");
        ending.TickTo(22f);
        ending.Gate.Tick();
        Assert(ending.Calls.Count == 4 && ending.Gate.AckedRound == 3,
            "after the raid-ending refusal the resend is acknowledged");

        // The transient refusal at every send: the budget runs out silently and the gate stays at the last ack.
        var endless = Rig.AtRound(2);
        endless.Now = 20f;
        endless.DefaultPlan = Plan.RaidEnding;
        endless.Gate.Report(3);
        endless.Gate.MarkHeld(3);
        endless.TickTo(60f);
        Assert(endless.Calls.Count == 2 + 4 && endless.Gate.Applies && endless.Gate.AckedRound == 2,
            "the raid-ending refusal is resent within the send budget and leaves the acknowledged round alone");
        Assert(endless.Warn.Count == 0,
            "the raid-ending refusal never warns, even when the send budget is spent");

        // The same at the FIRST report: not a failed first report, the feature stays on.
        var firstEnding = new Rig();
        firstEnding.DefaultPlan = Plan.RaidEnding;
        firstEnding.Gate.Report(1);
        firstEnding.TickTo(30f);
        Assert(firstEnding.Gate.Applies && firstEnding.Warn.Count == 0,
            "a first report that meets the raid-ending refusal does not end the feature");
    }

    private static void CheckSeqAndToken()
    {
        // One process-wide counter, starting at 1.
        TacticalOpeningBandGate.LastSeqForTests = 0;
        var a = new Rig(resetSeq: false);
        a.Gate.Report(1);
        Assert(a.Calls.Count == 1 && a.Calls[0].Seq == 1 && a.Calls[0].Round == 1 && a.Calls[0].Token == "arm-token",
            "the first report is sent at once with the arm token, round 1 and seq 1");
        int s1 = a.Calls[0].Seq;
        a.Gate.Reset();
        a.Gate.Report(1);
        var b = new Rig(resetSeq: false);
        b.Gate.Report(1);
        Assert(a.Calls[1].Seq == s1 + 1 && b.Calls[0].Seq == s1 + 2,
            "the seq counter is process-wide: a new match and a new gate report above every earlier seq");

        // Never outside 1..TacticalMaxSeq.
        var edge = new Rig(resetSeq: false);
        TacticalOpeningBandGate.LastSeqForTests = Protocol.TacticalMaxSeq - 1;
        edge.Plans[0] = Plan.Ack;
        edge.Gate.Report(1);
        edge.Gate.Tick();
        Assert(edge.Calls.Count == 1 && edge.Calls[0].Seq == Protocol.TacticalMaxSeq && edge.Gate.Applies,
            "the last seq the server accepts is sent");
        edge.Gate.Report(2);
        Assert(edge.Calls.Count == 1 && !edge.Gate.Applies,
            "a seq outside 1..1,000,000 is never sent");

        // A blank arm token sends nothing and ends the feature; the token is read at every send.
        foreach (string token in new string[] { null, "", "   " })
        {
            var blank = new Rig { Token = token };
            Assert(!Throws(() => blank.Gate.Report(1)), "a blank arm token does not throw");
            Assert(blank.Calls.Count == 0 && !blank.Gate.Applies && blank.Gate.ShouldRelease(2),
                "a blank arm token sends nothing and ends the feature");
        }
    }

    private static void CheckNeverThrows()
    {
        var token = new Rig { TokenThrows = true };
        Assert(!Throws(() => token.Gate.Report(1)) && !Throws(() => token.Gate.Report(2)),
            "a failing report never throws into the match start");
        Assert(token.Warn.Count == 1 && token.Gate.ShouldRelease(2),
            "a fault in the gate is logged once and holds nothing");

        var clock = new Rig();
        clock.Gate.Report(1);
        clock.ClockThrows = true;
        Assert(!Throws(() => clock.Gate.Tick()) && !Throws(() => clock.Gate.Tick()),
            "a failing poll never throws into the frame loop");
        Assert(clock.Gate.ShouldRelease(1) && !Throws(() => clock.Gate.MarkHeld(1)),
            "a gate with a failing clock holds nothing");

        var context = Rig.AtRound(1);
        context.Plans[context.Calls.Count] = Plan.Pending;
        context.Gate.Report(2);
        context.Gate.MarkHeld(2);
        context.ContextThrows = true;
        Assert(context.Gate.ShouldRelease(2) && context.Gate.CurrentGearLevel == null,
            "a gate whose gear numbers fail releases the held request");

        var sender = new Rig { DefaultPlan = Plan.Throw };
        Assert(!Throws(() => sender.Gate.Report(1)), "a sender that throws is a failed send, not an exception");
        sender.TickTo(7f);
        Assert(sender.Calls.Count == 4 && !sender.Gate.Applies && sender.Warn.Count == 1,
            "a sender that always throws spends the budget and ends the feature at the first report");
    }

    private static void CheckGearLevel()
    {
        Assert(new Rig().Gate.CurrentGearLevel == null, "before any acknowledgement there is no gear level");
        var maximum = new Rig();
        maximum.Next(1);
        Assert(maximum.Gate.CurrentGearLevel == 25, "round 1 acknowledged: the Maximum opening centre is 25");
        maximum.Next(2);
        Assert(maximum.Gate.CurrentGearLevel == 45, "round 2 acknowledged: the Maximum opening centre is 45");
        maximum.Next(3);
        Assert(maximum.Gate.CurrentGearLevel == 66, "round 3 acknowledged: the full level");
        maximum.Next(4);
        Assert(maximum.Gate.CurrentGearLevel == 66 && maximum.Gate.AckedRound == 4, "round 4 acknowledged: still the full level");

        var reduced = new Rig { Context = new TacticalGearContext(ArenaDifficultyStep.Reduced, 1, 66, true) };
        reduced.Next(1);
        int r1 = reduced.Gate.CurrentGearLevel ?? -1;
        reduced.Next(2);
        int r2 = reduced.Gate.CurrentGearLevel ?? -1;
        reduced.Next(3);
        Assert(r1 == 15 && r2 == 27 && reduced.Gate.CurrentGearLevel == 40,
            "Reduced opens at 15 and 27 and reaches its full level 40");

        var scav = new Rig { Context = new TacticalGearContext(ArenaDifficultyStep.Maximum, 1, 66, false) };
        scav.Next(1);
        Assert(scav.Gate.AckedRound == 1 && scav.Gate.Applies && scav.Gate.CurrentGearLevel == null,
            "a scav squad is reported and acknowledged but has no gear level");
        var flat = new Rig { Context = new TacticalGearContext(ArenaDifficultyStep.Maximum, 1, 20, true) };
        flat.Next(1);
        Assert(flat.Gate.CurrentGearLevel == null, "a map whose opening equals the full level has no gear level");
        var none = new Rig { Context = null };
        none.Next(1);
        Assert(none.Gate.CurrentGearLevel == null, "a map without gear numbers has no gear level");

        // The squad's role is read live: the same gate gives a level again when the setting flips back.
        var flip = new Rig();
        flip.Next(2);
        flip.Context = new TacticalGearContext(ArenaDifficultyStep.Maximum, 1, 66, false);
        bool scavNull = flip.Gate.CurrentGearLevel == null;
        flip.Context = new TacticalGearContext(ArenaDifficultyStep.Maximum, 1, 66, true);
        Assert(scavNull && flip.Gate.CurrentGearLevel == 45,
            "the squad's role is read at every call, not frozen at the match start");
    }

    private static void CheckSharedConstant()
    {
        Assert(Protocol.TacticalRoundRaidEndingRefusal == "raid is ending",
            "the raid-ending refusal text is the wire contract (the server's xunit test pins the same text)");
        Assert(Protocol.TacticalMaxSeq == 1_000_000, "the seq bound is the server's 1,000,000");
    }
}
