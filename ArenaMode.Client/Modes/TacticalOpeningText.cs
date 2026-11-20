using ArenaMode.Shared;

namespace ArenaMode.Modes;

/// <summary>
/// The one line the buy screen says about the enemy squad's gear in the opening rounds of solo Tactical
/// (docs/reviews/TACTICAL-OPENING-ROUNDS-20261119.md). Pure, so it is unit-tested without the game.
///
/// The line is its own label (never appended to the plan line, which is a single 1000 px line already
/// holding up to 102 characters) and at most <see cref="MaxLength"/> characters. It says what is in
/// effect for the round the player is buying for, never more:
/// <list type="bullet">
/// <item>a round at the opening level of round 1: light gear;</item>
/// <item>a round at the opening level of round 2: medium gear;</item>
/// <item>the first round at the full level after a lighter round, that round only: full gear from now on;</item>
/// <item>every other round: no line.</item>
/// </list>
/// "Lighter" is exactly <see cref="ArenaDifficultyPolicy.TacticalGearLevel"/> being below the step's full
/// level, the same function the server band and the client purge use, so the text cannot claim a stage
/// the bots are not in. The caller shows it only when the feature applies, the squad is PMC (a scav
/// squad's gear barely follows the level, so nothing is claimed) and the current round's report is
/// acknowledged. The words describe the opponents' kit class, not a measured equivalence with what the
/// player can buy.
/// </summary>
internal static class TacticalOpeningText
{
    /// <summary>The longest line the buy screen has room for next to its state row.</summary>
    public const int MaxLength = 45;

    public const string Light = "Enemy squad this round: light gear";
    public const string Medium = "Enemy squad this round: medium gear";
    public const string FullFromNowOn = "Enemy squad from now on: full gear";

    /// <summary>The line for <paramref name="round"/>, or an empty string when there is nothing true to say.</summary>
    public static string Note(ArenaDifficultyStep step, int startLevel, int endLevel, bool squadIsPmc, int round)
    {
        if (!squadIsPmc || round < 1)
            return string.Empty;

        int full = ArenaDifficultyPolicy.GearLevel(step, startLevel, endLevel);
        int level = ArenaDifficultyPolicy.TacticalGearLevel(step, startLevel, endLevel, round);
        if (level != full)
            return round == 1 ? Light : Medium;

        // The first full round after a lighter one: only inside the rounds that can be lighter plus the one after.
        if (round > 1 && round <= ArenaDifficultyPolicy.TacticalOpeningRounds + 1
            && ArenaDifficultyPolicy.TacticalGearLevel(step, startLevel, endLevel, round - 1) != full)
            return FullFromNowOn;

        return string.Empty;
    }
}
