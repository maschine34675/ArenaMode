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
        // Solo Tactical enters Live before asynchronous profiles arrive. Its
        // existing mode policy starts combat with the first real opponent.
        if (!_teamArena && _phase == Phase.Live && !_playerDown
            && _squadRequested > 0 && _squad.Count == 0)
        {
            heading = "ASSEMBLING OPPONENTS";
            detail = $"ROUND {RoundNumber}  |  Waiting for the first of {_squadRequested} opponents";
            return true;
        }
        return false;
    }
}
