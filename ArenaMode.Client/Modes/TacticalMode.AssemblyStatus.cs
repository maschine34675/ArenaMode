namespace ArenaMode.Modes;

public sealed partial class TacticalMode
{
    /// <summary>Read-only waiting notice; never advances the round or retries a spawn.</summary>
    internal bool TryGetAssemblyNotice(out string heading, out string detail)
    {
        heading = detail = null;
        if (IsComplete || _phase == Phase.Ended) return false;

        if (_teamArena && _phase == Phase.Staging)
        {
            heading = _teamEnemiesRequested ? "ASSEMBLING OPPONENTS" : "ASSEMBLING TEAMMATES";
            detail = _teamEnemiesRequested
                ? $"ROUND {RoundNumber}  |  {_squad.Count}/{TeamEnemyCount} opponents arrived — waiting for combat readiness"
                : $"ROUND {RoundNumber}  |  Waiting for all four teammates";
            return true;
        }
        if (_teamArena && _phase == Phase.Rebinding)
        {
            heading = "PREPARING NEXT ROUND";
            detail = $"ROUND {RoundNumber}  |  Rebuilding teams";
            return true;
        }
        // Solo's async request uses Live as its lease, but callbacks may still
        // be PreActive. Keep the notice until the combat-ready transition.
        if (!_teamArena && _phase == Phase.Live && !_playerDown
            && _squadRequested > 0 && !_soloCombatReady)
        {
            heading = "ASSEMBLING OPPONENTS";
            detail = $"ROUND {RoundNumber}  |  {_squad.Count}/{_squadRequested} opponents arrived — waiting for combat readiness";
            return true;
        }
        return false;
    }
}
