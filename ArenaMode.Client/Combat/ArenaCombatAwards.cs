#nullable disable
namespace ArenaMode.Combat;

/// <summary>Presentation only. Neither awards nor their priority alter scores.</summary>
internal sealed class ArenaCombatAwards
{
    private bool _firstBloodAvailable = true;
    private long _pendingDeathKiller;
    private long _paybackBody;
    private bool _lastManAnnounced;

    internal void BeginRound(bool clearRevenge = true)
    {
        _firstBloodAvailable = true;
        _lastManAnnounced = false;
        if (clearRevenge) _pendingDeathKiller = _paybackBody = 0;
    }

    // Exact native EDamageType names, evaluated only at a death. In particular,
    // fall/health-effect deaths cannot reuse EFT's stale LastAggressor. Bleeding
    // is excluded too: the retained shooter alone cannot prove its wound source.
    internal static bool IsCombatDamage(string damageType)
        => damageType is "Bullet" or "Blunt" or "Melee" or "Explosion" or "GrenadeFragment";

    internal void LocalDeath()
    {
        // Native kill capture precedes the respawn driver's death notification.
        // An environmental/administrative death has no pending valid killer.
        _paybackBody = _pendingDeathKiller;
        _pendingDeathKiller = 0;
    }

    internal string ReportKill(ArenaKill kill, string existingLabel)
    {
        if (kill == null) return null;
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
        // One HUD/audio label per kill. Keep existing streak > multi ordering.
        return first ? "FIRST BLOOD"
            : payback ? "PAYBACK"
            : existingLabel ?? (kill.SingleHitHeadshot ? "HEADSHOT" : null);
    }

    internal bool LastManStanding(bool localAlive, int alliesAlive, int enemiesAlive)
    {
        if (_lastManAnnounced || !localAlive || alliesAlive != 1 || enemiesAlive <= 0)
            return false;
        _lastManAnnounced = true;
        return true;
    }
}
