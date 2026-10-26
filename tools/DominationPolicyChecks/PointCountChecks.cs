using ArenaMode.Modes;

internal static class PointCountChecks
{
    private static int _checks;
    private static void Need(bool value, string reason)
    { _checks++; if (!value) throw new Exception("FAIL: " + reason); }
    private static void Near(float actual, float expected, string reason)
        => Need(Math.Abs(actual - expected) < .00001f, reason);
    private static void Reject(Action action, string reason)
    {
        bool rejected = false;
        try { action(); }
        catch (ArgumentOutOfRangeException error) { rejected = error.ParamName == "pointCount"; }
        Need(rejected, reason);
    }
    private static DominationOccupancy[] Occupants(int count, int usec, int bear)
        => Enumerable.Repeat(new DominationOccupancy(usec, bear), count).ToArray();
    private static DominationMatchPolicy Live(int count, int score = 0, int time = 0)
    {
        // Existing first/second positional arguments retain their meaning.
        var match = new DominationMatchPolicy(score, time, pointCount: count);
        Need(match.Points.Count == count, "constructor freezes requested objective count");
        Need(match.ScoreLimit == score && match.TimeLimitSeconds == time,
            "point count does not reinterpret score or time arguments");
        match.UpdateStart(0, true);
        match.Advance(2, Occupants(count, 3, 0), true);
        Need(match.Points.All(p => p.OwnerUsec == null && p.CaptureProgress == 0),
            "authored count does not bypass countdown");
        match.UpdateStart(3, true);
        return match;
    }

    internal static int Run()
    {
        foreach (int invalid in new[] { int.MinValue, -1, 0, 17, int.MaxValue })
            Reject(() => new DominationMatchPolicy(pointCount: invalid),
                "match rejects invalid point count through shared policy bound");
        Need(new DominationMatchPolicy(pointCount: 16).Points.Count == 16,
            "match retains existing bounded policy maximum");

        foreach (int count in new[] { 1, 2, 3 })
        {
            var match = Live(count);
            var view = match.Points;
            match.Advance(7.5f, Occupants(count, 20, 0), true);
            Need(match.Points.All(p => p.OwnerUsec == null), "authored counts retain full neutral capture duration");
            Near(match.Points[0].CaptureProgress, .9f, "authored counts retain five second capture speed");
            match.Advance(8, Occupants(count, 20, 0), true);
            Need(match.Points.All(p => p.OwnerUsec == true) && match.UsecScore == 0,
                "authored counts capture without retroactive income");
            match.Advance(10, Occupants(count, 0, 0), true);
            Need(match.UsecScore == 2 * count && match.BearScore == 0,
                "authored count pays one point per held point per second");
            match.Advance(12, Occupants(count, 1, 1), true);
            Need(match.UsecScore == 2 * count && match.Points.All(p => p.Contested && p.CaptureProgress == 1),
                "authored count contest freezes progress and income");
            match.Advance(14, Occupants(count, 0, 2), true);
            Need(match.Points.All(p => p.OwnerUsec == true && p.UnderAttack) && match.UsecScore == 2 * count,
                "authored count hostile presence immediately pauses owner income");
            Near(match.Points[0].CaptureProgress, .6f, "authored count enemy neutralization retains speed");
            match.Advance(17, Occupants(count, 0, 2), true);
            Need(match.Points.All(p => p.OwnerUsec == null && p.CaptureProgress == 0),
                "authored count neutralizes before opposite capture");
            match.Advance(22, Occupants(count, 0, 2), true);
            Need(match.Points.All(p => p.OwnerUsec == false && p.CaptureProgress == -1) && match.BearScore == 0,
                "authored count opposite capture retains second five seconds");
            match.Advance(24, Occupants(count, 0, 0), false);
            Need(match.FriendlyScore == 2 * count && match.OpponentScore == 2 * count,
                "authored count retains empty ownership and selected faction mapping");
            match.Advance(25, Occupants(count, 1, 0), false);
            match.Advance(26, Occupants(count, 0, 1), false);
            Need(match.BearScore == 3 * count && match.Points.All(p => p.OwnerUsec == false && p.CaptureProgress == -1),
                "authored count friendly repair resumes income");
            Need(ReferenceEquals(view, match.Points) && view.Count == count,
                "point collection identity and size remain fixed through ownership changes");
            bool readOnly = false;
            try { ((IList<DominationPointState>)view).Add(default); }
            catch (NotSupportedException) { readOnly = true; }
            Need(readOnly && view.Count == count, "caller cannot resize authored objective set");

            var atomic = Live(count);
            bool wrongLength = false;
            try { atomic.Advance(10, Occupants(count == 1 ? 3 : 1, 1, 0), true); }
            catch (ArgumentException) { wrongLength = true; }
            Need(wrongLength && atomic.UsecScore == 0 && atomic.Points.All(p => p.CaptureProgress == 0),
                "authored point layouts reject wrong length samples atomically");
            atomic.Advance(10, Occupants(count, 1, 0), true);
            Need(atomic.UsecScore == 2 * count, "wrong point count cannot consume live match time");

            foreach (bool usec in new[] { true, false })
            {
                var cap = Live(count, score: 7);
                cap.Advance(30, Occupants(count, usec ? 1 : 0, usec ? 0 : 1), usec);
                Need(cap.Phase == TdmMatchPhase.Finished && cap.FriendlyScore == 7 && cap.OpponentScore == 0
                    && cap.Result == TdmMatchResult.Won && cap.Reason == "Score limit reached",
                    "authored count reaches the unscaled score limit exactly");
                Near(cap.FinishedAt, 8f + 7f / count, "authored count cap clips actual per point income time");
                var frozen = cap.Points.ToArray();
                cap.Advance(31, Occupants(count, usec ? 0 : 1, usec ? 1 : 0), !usec);
                Need(cap.FriendlyScore == 7 && cap.Points.SequenceEqual(frozen),
                    "authored count terminal result and points remain frozen");
            }
            var deadline = Live(count, score: 0, time: 7);
            deadline.Advance(30, Occupants(count, 0, 1), true);
            Need(deadline.FinishedAt == 10 && deadline.BearScore == 2 * count && deadline.Result == TdmMatchResult.Lost,
                "authored count keeps deadline and loss accounting");
            var captureDeadline = Live(count, score: 0, time: 5);
            captureDeadline.Advance(20, Occupants(count, 1, 0), true);
            Need(captureDeadline.FinishedAt == 8 && captureDeadline.UsecScore == 0 && captureDeadline.Result == TdmMatchResult.Draw,
                "authored count capture at deadline cannot score retroactively");
        }

        var twoPointTie = Live(2, score: 4);
        twoPointTie.Advance(25, new[] { new DominationOccupancy(1, 0), new DominationOccupancy(0, 1) }, true);
        Need(twoPointTie.UsecScore == 4 && twoPointTie.BearScore == 4
            && twoPointTie.Result == TdmMatchResult.Draw && twoPointTie.FinishedAt == 12,
            "two point match preserves simultaneous scoring tie");
        var split = Live(2);
        split.Advance(8, new[] { new DominationOccupancy(1, 0), new DominationOccupancy(0, 1) }, true);
        split.Advance(10, new[] { new DominationOccupancy(1, 1), default }, true);
        Need(split.UsecScore == 0 && split.BearScore == 2,
            "two point contest pauses only the contested point");
        split.Advance(12, new[] { new DominationOccupancy(1, 0), new DominationOccupancy(1, 1) }, true);
        Need(split.UsecScore == 2 && split.BearScore == 2,
            "two point contest remains faction symmetric");

        // Embedding a one-point match in the default three-point policy with two
        // neutral empty points must not change any income or terminal decision.
        // This is independent of the capture implementation, including random
        // capture, empty, contested and owner-reversal intervals.
        var random = new Random(1261);
        for (int trial = 0; trial < 50; trial++)
        {
            var one = Live(1, score: 11, time: 90);
            var padded = Live(3, score: 11, time: 90);
            var fine = Live(1, score: 11, time: 90);
            float now = 3;
            for (int sampleIndex = 0; sampleIndex < 40 && one.Phase == TdmMatchPhase.Live; sampleIndex++)
            {
                var sample = new DominationOccupancy(random.Next(3), random.Next(3));
                int quarters = random.Next(1, 41);
                float duration = quarters / 4f;
                one.Advance(now + duration, new[] { sample }, true);
                padded.Advance(now + duration, new[] { sample, default, default }, true);
                for (int part = 1; part <= quarters; part++) fine.Advance(now + part / 4f, new[] { sample }, true);
                now += duration;
                foreach (var equivalent in new[] { padded, fine })
                {
                    Need(one.UsecScore == equivalent.UsecScore && one.BearScore == equivalent.BearScore
                        && one.Phase == equivalent.Phase && one.Result == equivalent.Result,
                        "one point remains equivalent under empty padding and time partition");
                    Need(one.Points[0].OwnerUsec == equivalent.Points[0].OwnerUsec,
                        "one point owner remains equivalent under padding and partition");
                    Near(one.Points[0].CaptureProgress, equivalent.Points[0].CaptureProgress,
                        "one point progress remains equivalent under padding and partition");
                    Near(one.FinishedAt, equivalent.FinishedAt,
                        "one point finish time remains equivalent under padding and partition");
                }
            }
        }
        return _checks;
    }
}
