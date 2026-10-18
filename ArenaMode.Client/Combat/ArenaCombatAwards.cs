#nullable disable
namespace ArenaMode.Combat;

/// <summary>Presentation only. Neither awards nor their priority alter scores.</summary>
internal sealed class ArenaCombatAwards
{
    private bool _firstBloodAvailable = true;
    private long _pendingDeathKiller;
    private long _paybackBody;
    private bool _lastManAnnounced;
    private bool _clutchCandidate;
    private bool _clutchInvalidated;
    private bool _clutchVictoryObserved;

    internal void BeginRound(bool clearRevenge = true)
    {
        _firstBloodAvailable = true;
        _lastManAnnounced = false;
        _clutchCandidate = _clutchInvalidated = _clutchVictoryObserved = false;
        if (clearRevenge) _pendingDeathKiller = _paybackBody = 0;
    }

    // Exact native EDamageType names, evaluated only at a death. In particular,
    // fall/health-effect deaths cannot reuse EFT's stale LastAggressor. Bleeding
    // is excluded too: the retained shooter alone cannot prove its wound source.
    internal static bool IsCombatDamage(string damageType)
        => damageType is "Bullet" or "Blunt" or "Melee" or "Explosion" or "GrenadeFragment";

    internal void LocalDeath()
    {
        InvalidateClutch();
        // Native kill capture precedes the respawn driver's death notification.
        // An environmental/administrative death has no pending valid killer.
        _paybackBody = _pendingDeathKiller;
        _pendingDeathKiller = 0;
    }

    internal string ReportKill(ArenaKill kill, string existingLabel)
    {
        if (kill == null) return null;
        if (kill.LocalIsVictim) InvalidateClutch();
        bool payback = kill.VictimBodyId != 0 && kill.VictimBodyId == _paybackBody;
        // Another actor can kill the revenge target; never award its successor.
        if (payback) _paybackBody = 0;
        if (kill.VictimBodyId != 0 && kill.VictimBodyId == _pendingDeathKiller)
            _pendingDeathKiller = 0;
        if (!kill.AwardEligible) return null;

        bool first = _firstBloodAvailable;
        _firstBloodAvailable = false; // bot-first consumes this round's award too
        if (kill.LocalIsVictim)
        {
            _pendingDeathKiller = kill.KillerBodyId;
            return null;
        }
        if (!kill.KilledByLocal) return null;
        // One HUD/audio label per kill. The match supplies Shutdown or its
        // selected multi/spree label. Collateral takes precedence; posture-only
        // shot awards still yield to multi-kills and the existing special awards.
        // These are presentation facts, with no new score or streak bonus.
        return kill.ScoreEligible
            && ArenaShotAwardHistory.ReportCollateralDeath(kill.CollateralProof, kill.VictimBodyId) ? "COLLATERAL"
            : first ? "FIRST BLOOD"
            : payback ? "PAYBACK"
            : existingLabel is "SHUTDOWN" or "DOUBLE KILL" or "TRIPLE KILL"
                or "MULTI KILL" or "MONSTER KILL" ? existingLabel
            : kill.ScoreEligible && kill.AirborneHipFire ? "AIRBORNE"
            : kill.ScoreEligible && kill.BoltActionHipFire ? "NO SCOPE"
            : existingLabel ?? (kill.SingleHitHeadshot ? "HEADSHOT" : null);
    }

    internal bool LastManStanding(bool localAlive, int alliesAlive, int enemiesAlive)
    {
        if (!localAlive) InvalidateClutch();
        _clutchVictoryObserved = !_clutchInvalidated && _clutchCandidate
            && localAlive && alliesAlive == 1 && enemiesAlive == 0;
        if (alliesAlive != 1) _clutchCandidate = false;
        if (!_clutchInvalidated && localAlive && alliesAlive == 1 && enemiesAlive > 0)
            _clutchCandidate = true;
        if (_lastManAnnounced || !localAlive || alliesAlive != 1 || enemiesAlive <= 0)
            return false;
        _lastManAnnounced = true;
        return true;
    }

    internal void InvalidateClutch()
    {
        _clutchInvalidated = true;
        _clutchCandidate = _clutchVictoryObserved = false;
    }

    internal bool ConsumeClutch()
    {
        bool clutch = !_clutchInvalidated && _clutchCandidate && _clutchVictoryObserved;
        _clutchInvalidated = true; // a settled result cannot earn another clutch in this round
        _clutchCandidate = _clutchVictoryObserved = false;
        return clutch;
    }
}
