using ArenaMode.Combat;
using EFT;

namespace ArenaMode.Modes;

/// <summary>
/// v1 mode: no win condition, pure counter. Milestone callouts every
/// 10 local kills keep long sessions readable.
/// </summary>
public sealed class EndlessDeathmatchMode : IArenaMode
{
    private const int MilestoneEvery = 10;

    private ArenaMatch _match;
    private int _nextMilestone = MilestoneEvery;

    public string Id => "endless";

    public string DisplayName => "Endless Deathmatch";

    public bool IsComplete => false;

    /// <summary>Instant respawn leaves no room for one.</summary>
    public float DeathReplaySeconds => 0f;

    /// <summary>Death costs nothing here — you are back before the body lands.</summary>
    public bool HoldRespawn => false;

    /// <summary>Population comes from the ambient burst spawner.</summary>
    public bool ControlsOwnSpawns => false;

    public string HudStatusLine => null;

    public void OnMatchStart(ArenaMatch match) => _match = match;

    public void Tick(float deltaTime)
    {
    }

    public void OnKill(ArenaKill kill)
    {
        if (_match == null || !kill.KilledByLocal || kill.LocalIsVictim)
            return;

        if (_match.Kills >= _nextMilestone)
        {
            Plugin.ModLogger.LogInfo($"Arena milestone: {_match.Kills} kills, score {_match.Score}.");
            ArenaToasts.Show($"{_match.Kills} kills — score {_match.Score:n0}");
            _nextMilestone += MilestoneEvery;
        }
    }

    public void OnLocalDeath()
    {
    }

    public void OnLocalRespawn(Player player)
    {
    }

    public void OnMatchEnd()
    {
    }
}
