using System;
using ArenaMode.Combat;
using ArenaMode.Modes;
using static ArenaMode.Compat.ArenaMapPortSpectatorCrowd;

namespace ArenaMode.Audio;

/// <summary>
/// Pure, Unity-free rules of the crowd presentation: which ArenaMode event gives which MapPort
/// reaction kind at which strength, how one frame's reactions coalesce, and how the announcer
/// snapshot maps to the murmur's sustained excitement. No match state is read or changed here.
/// Strengths are the plan's proposals (D:\ArenaMapPort\docs\PLAN-CROWD-REACTIONS.md, 6.3), not
/// listened values; MapPort turns a strength into a voice count (up to 1/3: one voice, up to 2/3:
/// two, above: three) and a bed swell, never into gain. Contract and decisions:
/// docs/reviews/CROWD-REACTIONS-20261102.md.
/// </summary>
internal static class ArenaCrowdReactionPolicy
{
    internal const float KillStrength = 0.35f;
    // Melee, explosions and headshots are the kills a crowd sees; still below the awards.
    internal const float StrongKillStrength = 0.6f;
    internal const float AwardStrength = 0.9f;
    internal const float DeathStrength = 0.5f;
    internal const float LeadStrength = 0.5f;
    // Start and Finish carry no win/loss colouring in v1 (decision D7): every result is 1.0.
    internal const float RoundStrength = 1f;
    // While the announcer speaks or has speech queued, the crowd's own reactions to the same
    // moment go out at half strength (fewer voices) so they do not bury it. Start and Finish
    // keep full strength: they are the crowd's part of the moment, not a second commentary.
    internal const float InterlockFactor = 0.5f;

    internal const int ExcitementSteps = 20;          // the level is sent in steps of 0.05
    internal const double LateClockSeconds = 60d;     // the last minute raises the murmur
    internal const int FewKills = 5;                  // the last five kills raise the murmur
    internal const float FinalRoundLevel = 0.6f, MatchPointLevel = 0.4f, ClockPeak = 0.6f, KillsPeak = 0.6f;

    /// <summary>
    /// Kills, local-centric (decision D5): only the local player's own kill or own death reacts.
    /// Every other kill is bot-versus-bot, because the local player is the only human in an Arena
    /// match (Team DM and Domination build the friendly side from their own bots only, PitFireTeam
    /// companions are bots too), so a friendly bot's kill of an opponent stays silent as well: D5
    /// says "no bot-vs-bot kills", and the plan's optional teammate row was not taken.
    ///
    /// Ignored, too: a kill record the mode did not score (ScoreEligible false: friendly fire and
    /// unattributed fire in a live Team DM or Domination match). Kills the mode does not admit at
    /// all (Team DM, Domination and Deathmatch outside their live phase or after the result, where
    /// TryBeginScoredEvent refuses them) never reach ArenaMatch.OnKill; a local death there reaches
    /// the crowd only through ArenaEvents.OnPlayerDied and disapproves like any death without a kill
    /// record (plan 6.3). Those modes protect the local player in most of those phases
    /// (ShouldBlockMatchDamage; the Deathmatch intermission's round-break respawn or protection),
    /// so that is rare, and the player did die in front of the crowd.
    /// The caller owns the one-reaction-per-life rule for a local death reported on both paths.
    /// </summary>
    internal static bool TryMapKill(ArenaKill kill, out int kind, out float strength, out string source)
    {
        kind = 0;
        strength = 0f;
        source = null;
        if (kill == null || !kill.ScoreEligible) return false;
        if (kill.LocalIsVictim)
        {
            kind = Disapprove; strength = DeathStrength; source = "death";
            return true;
        }
        if (kill.KilledByLocal)
        {
            bool strong = kill.Headshot || kill.DamageKind is "Melee" or "Explosion" or "GrenadeFragment";
            kind = Approve; strength = strong ? StrongKillStrength : KillStrength; source = strong ? "strong kill" : "kill";
            return true;
        }
        return false;
    }

    /// <summary>
    /// The award labels of ArenaCombatAwards.ReportKill and ScoreRules (multi-kill and streak),
    /// raised only for the local player's own kills. HEADSHOT is the single-hit headshot award: the
    /// kill itself already reacts at the strong-kill strength, so the award stays on that path
    /// instead of reaching award strength.
    /// </summary>
    internal static bool TryMapCallout(string label, out float strength)
    {
        strength = label switch
        {
            "FIRST BLOOD" or "PAYBACK" or "COLLATERAL" or "SHUTDOWN" or "AIRBORNE" or "NO SCOPE"
                or "DOUBLE KILL" or "TRIPLE KILL" or "ULTRA KILL" or "MONSTER KILL"
                or "KILLING SPREE" or "RAMPAGE" or "DOMINATING" or "UNSTOPPABLE" or "GODLIKE" => AwardStrength,
            "HEADSHOT" => StrongKillStrength,
            _ => 0f,
        };
        return strength > 0f;
    }

    /// <summary>
    /// Lifecycle cues. NoContest is handled by the caller (it silences the match). Silent by
    /// decision D7: CancelStatus, AdvanceStatus, the countdown (PrepareToFight, Three, Two, One)
    /// and the clock and kill milestones (they feed the sustained excitement instead). Silent
    /// because nothing fits: TiedForTheLead, and Embarrassing (an own-grenade death, whose death
    /// already disapproves through ArenaEvents.OnPlayerDied). The Shutdown cue means what the
    /// SHUTDOWN callout means (the local player ended an opponent's streak), so it maps the same.
    /// </summary>
    internal static bool TryMapCue(ArenaAnnouncerCue cue, out int kind, out float strength)
    {
        (kind, strength) = cue switch
        {
            ArenaAnnouncerCue.RoundStart => (Start, RoundStrength),
            ArenaAnnouncerCue.RoundWon or ArenaAnnouncerCue.RoundLost or ArenaAnnouncerCue.MatchWon
                or ArenaAnnouncerCue.MatchLost or ArenaAnnouncerCue.Draw => (Finish, RoundStrength),
            ArenaAnnouncerCue.TakenTheLead => (Approve, LeadStrength),
            ArenaAnnouncerCue.LostTheLead => (Disapprove, LeadStrength),
            ArenaAnnouncerCue.LastManStanding or ArenaAnnouncerCue.Clutch or ArenaAnnouncerCue.FinalWeapon
                or ArenaAnnouncerCue.LadderComplete or ArenaAnnouncerCue.Shutdown => (Approve, AwardStrength),
            _ => (0, 0f),
        };
        return kind != 0;
    }

    internal static bool IsInterlocked(int kind) => kind == Approve || kind == Disapprove;

    /// <summary>
    /// The sustained excitement in steps of 1/<see cref="ExcitementSteps"/>, monotone in every
    /// pressure: final round, match point, the last minute of the clock and the last five kills
    /// each raise it, combined as 1 - product of (1 - part) so they stack without ever reaching
    /// above 1 (MapPort's factor 1 + 0.6 * level stays within its 1.6 and the baked clip gain).
    /// Zero outside live combat: not Active, not Live (preparation, countdown, intermission) or
    /// Terminal. The snapshot does not cover Domination objectives, the Tactical round score or
    /// Zombies waves (plan 6.4). Steps keep "only on change" meaningful while the clock runs.
    /// </summary>
    internal static int ExcitementStep(in ArenaAnnouncementSnapshot snapshot)
    {
        if (!snapshot.Active || !snapshot.Live || snapshot.Terminal) return 0;
        double phase = snapshot.FinalRound ? FinalRoundLevel : snapshot.MatchPoint ? MatchPointLevel : 0d;
        double clock = 0d;
        if (snapshot.SecondsRemaining is double seconds && !double.IsNaN(seconds) && !double.IsInfinity(seconds))
        {
            double left = Math.Max(0d, seconds);
            if (left < LateClockSeconds) clock = ClockPeak * (LateClockSeconds - left) / LateClockSeconds;
        }
        double kills = 0d;
        if (snapshot.KillsRemaining is int remaining && remaining <= FewKills)
            kills = KillsPeak * (FewKills + 1 - Math.Max(1, remaining)) / FewKills;
        double level = 1d - (1d - phase) * (1d - clock) * (1d - kills);
        int step = (int)Math.Round(level * ExcitementSteps, MidpointRounding.AwayFromZero);
        return Math.Max(0, Math.Min(ExcitementSteps, step));
    }

    /// <summary>
    /// The reactions of one tick. Every kind keeps its strongest entry; a Start or a Finish drops
    /// Approve and Disapprove, because the deciding kill's OnKill arrives before its MatchWon cue
    /// (ArenaMatch.ReportKill raises OnKill, then mode.OnKill publishes the result) and the crowd
    /// must not cheer the kill and then the result as two reactions. The service holds a tick's
    /// Approve and Disapprove for one more tick, for a result published a tick after its kill
    /// (ArenaCrowdReactions.Flush). Fixed arrays: no allocation.
    /// </summary>
    internal sealed class Frame
    {
        // Send order: the round events first, then the moment, TakeCover last (never sent from here).
        internal static readonly int[] Order = { Finish, Start, Approve, Disapprove, TakeCover };
        private readonly float[] _strength = new float[Finish + 1];
        private readonly string[] _source = new string[Finish + 1];

        internal bool IsEmpty
        {
            get
            {
                for (int kind = Approve; kind <= Finish; kind++)
                    if (_strength[kind] > 0f) return false;
                return true;
            }
        }

        internal void Add(int kind, float strength, string source)
        {
            if (kind < Approve || kind > Finish || !(strength > 0f) || strength <= _strength[kind]) return;
            _strength[kind] = Math.Min(1f, strength);
            _source[kind] = source;
        }

        internal bool HasRoundEvent => _strength[Start] > 0f || _strength[Finish] > 0f;

        internal void Resolve()
        {
            if (!HasRoundEvent) return;
            _strength[Approve] = _strength[Disapprove] = 0f;
            _source[Approve] = _source[Disapprove] = null;
        }

        internal bool TryTake(int kind, out float strength, out string source)
        {
            strength = 0f;
            source = null;
            if (kind < Approve || kind > Finish || _strength[kind] <= 0f) return false;
            strength = _strength[kind];
            source = _source[kind];
            _strength[kind] = 0f;
            _source[kind] = null;
            return true;
        }

        internal void Clear()
        {
            Array.Clear(_strength, 0, _strength.Length);
            Array.Clear(_source, 0, _source.Length);
        }
    }
}
