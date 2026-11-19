using ArenaMode.Modes;
using ArenaMode.Shared;

// The buy-screen line about the enemy squad's gear (TacticalOpeningText) and the gate's guard around it
// (TacticalOpeningBandGate.OpponentGearNote): what the text says per round and map, and when it says nothing.
internal static partial class Program
{
    private static void CheckOpponentNote()
    {
        const string none = "";
        string Say(ArenaDifficultyStep step, int start, int end, bool pmc, int round)
            => TacticalOpeningText.Note(step, start, end, pmc, round);

        Assert(TacticalOpeningText.MaxLength == 45, "the line is at most 45 characters");
        Assert(TacticalOpeningText.Light == "Enemy squad this round: light gear"
               && TacticalOpeningText.Medium == "Enemy squad this round: medium gear"
               && TacticalOpeningText.FullFromNowOn == "Enemy squad from now on: full gear",
            "the three decided texts");
        Assert(TacticalOpeningText.Light.Length <= TacticalOpeningText.MaxLength
               && TacticalOpeningText.Medium.Length <= TacticalOpeningText.MaxLength
               && TacticalOpeningText.FullFromNowOn.Length <= TacticalOpeningText.MaxLength,
            "every text fits the 45 characters of the label");

        var maximum = ArenaDifficultyStep.Maximum;
        Assert(Say(maximum, 1, 66, true, 1) == TacticalOpeningText.Light, "round 1 at the opening level of round 1 says light gear");
        Assert(Say(maximum, 1, 66, true, 2) == TacticalOpeningText.Medium, "round 2 at the opening level of round 2 says medium gear");
        Assert(Say(maximum, 1, 66, true, 3) == TacticalOpeningText.FullFromNowOn, "the first full round after a lighter round says full gear from now on");
        Assert(Say(maximum, 1, 66, true, 4) == none && Say(maximum, 1, 66, true, 9) == none, "every later round says nothing");
        Assert(Say(maximum, 1, 66, true, 0) == none && Say(maximum, 1, 66, true, -3) == none, "a round below 1 says nothing");
        Assert(Say(ArenaDifficultyStep.Reduced, 1, 66, true, 1) == TacticalOpeningText.Light
               && Say(ArenaDifficultyStep.Reduced, 1, 66, true, 2) == TacticalOpeningText.Medium
               && Say(ArenaDifficultyStep.Reduced, 1, 66, true, 3) == TacticalOpeningText.FullFromNowOn,
            "Reduced says the same words at its own levels");
        Assert(Say(maximum, 1, 66, false, 1) == none && Say(maximum, 1, 66, false, 2) == none && Say(maximum, 1, 66, false, 3) == none,
            "a scav squad is told nothing");

        // Custom maps: the text follows the level of the round, never the round number alone.
        Assert(Say(maximum, 1, 45, true, 1) == TacticalOpeningText.Light && Say(maximum, 1, 45, true, 2) == TacticalOpeningText.FullFromNowOn
               && Say(maximum, 1, 45, true, 3) == none,
            "a map whose round 2 is already full says light gear, then full gear from now on, in round 2");
        Assert(Say(maximum, 1, 30, true, 1) == none && Say(maximum, 1, 30, true, 2) == none && Say(maximum, 1, 30, true, 3) == none,
            "a map whose opening is not a tier below the full level says nothing");
        Assert(Say(maximum, 1, 20, true, 1) == none && Say(maximum, 1, 20, true, 2) == none,
            "a map with a low end level says nothing");
        Assert(Say(ArenaDifficultyStep.Reduced, 40, 66, true, 1) == none && Say(ArenaDifficultyStep.Reduced, 40, 66, true, 3) == none,
            "a step whose full level is the start level says nothing");
        Assert(Say(maximum, 40, 66, true, 1) == TacticalOpeningText.Light && Say(maximum, 40, 66, true, 2) == TacticalOpeningText.Medium,
            "a raised start level keeps the opening lighter than the full level");

        // The gate's guard: only an applying feature and an acknowledged report of exactly this round speak.
        var rig = new Rig();
        Assert(rig.Gate.OpponentGearNote(1) == none, "no line before a report is made");
        rig.Gate.Report(1);
        Assert(rig.Gate.OpponentGearNote(1) == none, "no line while the report is unanswered");
        rig.Complete(0, Ok(1, rig.Calls[0].Seq));
        rig.Gate.Tick();
        Assert(rig.Gate.OpponentGearNote(1) == TacticalOpeningText.Light, "an acknowledged report says light gear for its round");
        Assert(rig.Gate.OpponentGearNote(2) == none, "a round whose own report is not acknowledged says nothing, whatever the last one said");
        rig.Next(2);
        Assert(rig.Gate.OpponentGearNote(2) == TacticalOpeningText.Medium && rig.Gate.OpponentGearNote(1) == none,
            "the line follows the acknowledged round");
        rig.Next(3);
        Assert(rig.Gate.OpponentGearNote(3) == TacticalOpeningText.FullFromNowOn, "round 3 acknowledged says full gear from now on");
        rig.Next(4);
        Assert(rig.Gate.OpponentGearNote(4) == none, "round 4 acknowledged says nothing");

        var off = new Rig { DefaultPlan = Plan.NotApplying };
        off.Gate.Report(1);
        off.Gate.Tick();
        Assert(!off.Gate.Applies && off.Gate.OpponentGearNote(1) == none, "a feature the server does not apply says nothing");
        var refused = new Rig { DefaultPlan = Plan.Refuse };
        refused.Gate.Report(1);
        refused.Gate.Tick();
        Assert(refused.Gate.OpponentGearNote(1) == none, "a refused feature says nothing");
        var scav = new Rig { Context = new TacticalGearContext(ArenaDifficultyStep.Maximum, 1, 66, false) };
        scav.Next(1);
        Assert(scav.Gate.AckedRound == 1 && scav.Gate.OpponentGearNote(1) == none, "a scav squad is told nothing even when acknowledged");
        var noNumbers = new Rig { Context = null };
        noNumbers.Next(1);
        Assert(noNumbers.Gate.OpponentGearNote(1) == none, "a map without gear numbers says nothing");
        var failing = new Rig();
        failing.Next(1);
        failing.ContextThrows = true;
        Assert(!Throws(() => failing.Gate.OpponentGearNote(1)) && failing.Gate.OpponentGearNote(1) == none,
            "a failing gear context gives no line and never throws into the frame");
        failing.ContextThrows = false;
        var live = new Rig();
        live.Next(1);
        live.Context = new TacticalGearContext(ArenaDifficultyStep.Maximum, 1, 66, false);
        bool silent = live.Gate.OpponentGearNote(1) == none;
        live.Context = new TacticalGearContext(ArenaDifficultyStep.Maximum, 1, 66, true);
        Assert(silent && live.Gate.OpponentGearNote(1) == TacticalOpeningText.Light,
            "the squad's role is read at every call, so the line follows the setting");
    }
}
