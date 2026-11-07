using System;
using ArenaMode.Combat;
using ArenaMode.Modes;
using static ArenaMode.Compat.ArenaMapPortSpectatorCrowd;

namespace ArenaMode.Audio;

/// <summary>
/// Pure, Unity-free rules of the crowd presentation: which ArenaMode event gives which MapPort
/// reaction kind at which strength, how one frame's reactions coalesce, and how the announcer
/// snapshot maps to the murmur's sustained excitement and to live combat for the Lull. No match
/// state is read or changed here. Strengths are the plan's proposals
/// (D:\ArenaMapPort\docs\PLAN-CROWD-REACTIONS.md, table 6.3, as revised in ArenaMapPort 08964c1
/// with the user's subtitles of every crowd clip, 2.4), not listened values. MapPort turns a
/// strength into a voice count (up to 1/3: one voice, up to 2/3: two, above: three), for two
/// kinds a sub-pool (plan 2.4, 4.1: from 0.8 the first Approve voice is drawn from the strong-only
/// shouts, below 0.4 Disapprove draws only the lament clips) and, since MapPort e3e4290
/// (2026-11-07), a roar from every stand: Arena's Cheer bed for Approve, Start and Finish, its
/// Angry bed for Disapprove, at amplitude 0.35 + 0.65 * strength for the reaction's length
/// (1.5 to 5 s, then a 2 s release); a roar of the other kind is cut in 0.5 s. The neutral
/// murmur is lifted by the sustained excitement only (SetExcitement, up to +60 percent), never
/// by a reaction. Below 0.05 MapPort's React refuses (false), which would end the claim for the
/// match; nothing here goes below 0.15. Never into gain. Contract and decisions:
/// docs/reviews/CROWD-REACTIONS-20261102.md.
/// </summary>
internal static class ArenaCrowdReactionPolicy
{
    internal const float KillStrength = 0.35f;
    // Melee, explosions and headshots are the kills a crowd sees; still below the awards.
    internal const float StrongKillStrength = 0.6f;
    internal const float AwardStrength = 0.9f;
    // An own death sits in MapPort's band from 0.4, which draws the whole Angry bank: the 10
    // laments and the 8 heckles of the dead player, which fit an own death only (plan 2.4).
    internal const float DeathStrength = 0.5f;
    internal const float LeadTakenStrength = 0.5f;
    // 0.3, below the 0.4 band (plan 6.3 as revised in 08964c1): a lost lead draws the laments only.
    // The heckles speak to the dead player in the second person, and losing the lead is no death;
    // one voice (up to 1/3) instead of the two of 0.5.
    internal const float LeadLostStrength = 0.3f;
    // Start and Finish carry no win/loss colouring in v1 (decision D7): every result is 1.0.
    internal const float RoundStrength = 1f;
    // While the announcer speaks or has speech queued, the crowd's own reactions to the same
    // moment go out at half strength (fewer voices) so they do not bury it. Start and Finish
    // keep full strength: they are the crowd's part of the moment, not a second commentary.
    // Against MapPort's bands: a kill 0.35 -> 0.175 and a strong kill 0.6 -> 0.3 (two voices ->
    // one); an award 0.9 -> 0.45 (three -> two, and the first voice no longer comes from the
    // strong-only shouts); lead taken 0.5 -> 0.25 (two -> one); lead lost 0.3 -> 0.15 (one voice
    // and laments either way, only the roar shrinks, 0.55 -> 0.45; still three times MapPort's
    // 0.05 floor). An own death 0.5 -> 0.25 falls below the 0.4 band and draws laments only, one
    // voice. Intended: a second-person heckle of the dead player spoken over the announcer is the
    // worse outcome; a lament under it still says what happened. The roar keeps the moment
    // audible either way: MapPort's floor of 0.35 gives even a halved kill (0.175) a roar of 0.46
    // from every stand, against 0.58 unhalved.
    internal const float InterlockFactor = 0.5f;
    // The roar layer makes an Approve and a Disapprove within a second of each other a crowd that
    // cheers and then boos (MapPort cuts the first roar in 0.5 s for the second). For the two frames
    // of one flush Frame.KeepOneMood decides before anything is sent; across flushes the service
    // remembers the last mood it sent: within this window the opposite kind goes out only when it
    // is stronger than what went out (the crowd changes its mind for the bigger moment: the death
    // right after the kill that traded for it), a weaker one is dropped. One second is the "same
    // moment"; a Start, a Finish or a Lull is a new moment and ends the window. The window covers
    // the quick trade only, not the roar's whole life (hold up to 5 s and a 2 s release): a death
    // two seconds after a kill is a second moment, both react, and MapPort's 0.5 s cut of the
    // first roar is the transition. Compared as sent, after the interlock: that is the roar the
    // crowd heard. A weaker mood of the same kind inside the window leaves the memory alone.
    internal const float MoodWindowSeconds = 1f;

    // Lull (kind 6), decision D14 of 2026-11-02 ("wie empfohlen", plan 6.3 and 8): no kill for
    // 60 s of live combat gives one Lull at 0.4 (two voices from the whole LowDeathIntensity bank,
    // complaints that the match is dull), at most one per 90 s. ArenaCrowdReactions owns the clock.
    internal const float LullStrength = 0.4f;
    internal const float LullQuietSeconds = 60f;
    internal const float LullGapSeconds = 90f;

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
            ArenaAnnouncerCue.TakenTheLead => (Approve, LeadTakenStrength),
            ArenaAnnouncerCue.LostTheLead => (Disapprove, LeadLostStrength),
            ArenaAnnouncerCue.LastManStanding or ArenaAnnouncerCue.Clutch or ArenaAnnouncerCue.FinalWeapon
                or ArenaAnnouncerCue.LadderComplete or ArenaAnnouncerCue.Shutdown => (Approve, AwardStrength),
            _ => (0, 0f),
        };
        return kind != 0;
    }

    // The Lull is not interlocked: it answers a minute without a kill, not a moment the announcer
    // is commenting on.
    internal static bool IsInterlocked(int kind) => IsMood(kind);

    // The two moods of the roar layer (Cheer for Approve, Angry for Disapprove), which contradict
    // each other within a moment; Start and Finish are round events, the Lull is neither.
    internal static bool IsMood(int kind) => kind == Approve || kind == Disapprove;

    /// <summary>
    /// Live combat as the announcer snapshot reports it, for the murmur and the Lull alike: the
    /// mode has a snapshot (Active; Zombies and an ended mode have none), its Live flag is set and
    /// no result exists (Terminal). Live is the mode's own combat phase: Team DM and Domination
    /// MatchPhase Live, Deathmatch the rules' Live phase, Tactical the Live phase once the solo squad
    /// is combat-ready, Gun Game after the initial kit swap. Preparation, countdown, intermission
    /// and the result hold are not live combat.
    /// </summary>
    internal static bool IsLiveCombat(in ArenaAnnouncementSnapshot snapshot)
        => snapshot.Active && snapshot.Live && !snapshot.Terminal;

    /// <summary>
    /// The sustained excitement in steps of 1/<see cref="ExcitementSteps"/>, monotone in every
    /// pressure: final round, match point, the last minute of the clock and the last five kills
    /// each raise it, combined as 1 - product of (1 - part) so they stack without ever reaching
    /// above 1 (MapPort's factor 1 + 0.6 * level stays within its 1.6 and the baked clip gain).
    /// Zero outside live combat (<see cref="IsLiveCombat"/>). The snapshot does not cover
    /// Domination objectives, the Tactical round score or Zombies waves (plan 6.4). Steps keep
    /// "only on change" meaningful while the clock runs.
    /// </summary>
    internal static int ExcitementStep(in ArenaAnnouncementSnapshot snapshot)
    {
        if (!IsLiveCombat(snapshot)) return 0;
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
    /// must not cheer the kill and then the result as two reactions. It drops a Lull too: a crowd
    /// complaining about a dull match at a round start or a result contradicts the moment. A Lull
    /// is no round event and drops nothing. The service holds a tick's Approve, Disapprove and
    /// Lull for one more tick, for a result published a tick after its kill
    /// (ArenaCrowdReactions.Flush), and sends one mood per flush across the frame that goes out
    /// and the one that waits (<see cref="KeepOneMood"/>). Fixed arrays: no allocation.
    /// </summary>
    internal sealed class Frame
    {
        // Send order, plan 4.1's priority: the round events first, then the moment, TakeCover
        // (never sent from here), the Lull last.
        internal static readonly int[] Order = { Finish, Start, Approve, Disapprove, TakeCover, Lull };
        private readonly float[] _strength = new float[Lull + 1];
        private readonly string[] _source = new string[Lull + 1];

        internal bool IsEmpty
        {
            get
            {
                for (int kind = Approve; kind <= Lull; kind++)
                    if (_strength[kind] > 0f) return false;
                return true;
            }
        }

        internal void Add(int kind, float strength, string source)
        {
            if (kind < Approve || kind > Lull || !(strength > 0f) || strength <= _strength[kind]) return;
            _strength[kind] = Math.Min(1f, strength);
            _source[kind] = source;
        }

        internal bool HasRoundEvent => _strength[Start] > 0f || _strength[Finish] > 0f;

        internal void Resolve()
        {
            if (!HasRoundEvent) return;
            _strength[Approve] = _strength[Disapprove] = 0f;
            _source[Approve] = _source[Disapprove] = null;
            _strength[Lull] = 0f;
            _source[Lull] = null;
        }

        internal bool TryTake(int kind, out float strength, out string source)
        {
            strength = 0f;
            source = null;
            if (kind < Approve || kind > Lull || _strength[kind] <= 0f) return false;
            strength = _strength[kind];
            source = _source[kind];
            _strength[kind] = 0f;
            _source[kind] = null;
            return true;
        }

        /// <summary>
        /// One mood per flush. Since MapPort's roar layer (e3e4290, 2026-11-07) every Approve
        /// roars Arena's Cheer bed and every Disapprove its Angry bed from all stands, and the later
        /// one cuts the other in 0.5 s, so an Approve and a Disapprove for the same moment (a trade:
        /// the own kill and the own death in one tick or in neighbouring ticks, one in the frame
        /// that goes out and one in the frame that waits) would make the whole crowd cheer and
        /// then boo within a second. The strongest entry of the two kinds across both frames
        /// decides; the other kind is dropped from both frames (on a tie Approve stays: plan 4.1's
        /// order lists it first). With separate voices this was a mixed crowd; with roars it is a
        /// contradiction. Returns what was dropped, its strongest entry, for the service's Debug
        /// line. The interlock halves both kinds alike, so it does not change the decision.
        /// </summary>
        internal static bool KeepOneMood(Frame going, Frame waiting, out int dropped, out float droppedStrength, out string droppedSource)
        {
            dropped = 0;
            droppedStrength = 0f;
            droppedSource = null;
            float waitingApprove = waiting?._strength[Approve] ?? 0f, waitingDisapprove = waiting?._strength[Disapprove] ?? 0f;
            float approve = Math.Max(going._strength[Approve], waitingApprove);
            float disapprove = Math.Max(going._strength[Disapprove], waitingDisapprove);
            if (!(approve > 0f) || !(disapprove > 0f)) return false;
            dropped = approve >= disapprove ? Disapprove : Approve;
            droppedStrength = dropped == Approve ? approve : disapprove;
            float droppedWaiting = dropped == Approve ? waitingApprove : waitingDisapprove;
            droppedSource = going._strength[dropped] >= droppedWaiting ? going._source[dropped] : waiting._source[dropped];
            going.Drop(dropped);
            waiting?.Drop(dropped);
            return true;
        }

        private void Drop(int kind)
        {
            _strength[kind] = 0f;
            _source[kind] = null;
        }

        internal void Clear()
        {
            Array.Clear(_strength, 0, _strength.Length);
            Array.Clear(_source, 0, _source.Length);
        }
    }
}
