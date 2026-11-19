using ArenaMode.Config;
using ArenaMode.Modes;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using System;

namespace ArenaMode.Bots;

/// <summary>
/// The Tactical opening state the rest of the client reads (the pool purge and the buy-screen line
/// of the next stage): which gate belongs to the running match, what it has acknowledged, and the gear
/// level the server writes for that round. The live state is the gate's own; this class only
/// publishes the current gate, binds it to the world it was started in (so a state left behind by an
/// earlier raid is inert in the next one) and reads the map numbers.
/// </summary>
internal static class ArenaTacticalOpening
{
    private static TacticalOpeningBandGate _gate;
    private static object _world;

    /// <summary>The world object the current gate was bound in; null when none is bound.</summary>
    internal static object World => _world;

    /// <summary>True while the bound gate has not been told that the feature is off for this raid.</summary>
    internal static bool Applies => IsCurrentWorld() && _gate?.Applies == true;

    /// <summary>The round the server acknowledged, 0 when none was (or the gate belongs to another world).</summary>
    internal static int AckedRound => IsCurrentWorld() && _gate != null ? _gate.AckedRound : 0;

    /// <summary>
    /// The gear level (band centre) of the acknowledged round: null when the feature does not apply,
    /// nothing is acknowledged, the squad is scav, the map has no tier scaling, the opening does not
    /// differ from the full level, or the gate was bound in another world.
    /// </summary>
    internal static int? CurrentGearLevel => IsCurrentWorld() ? _gate?.CurrentGearLevel : null;

    /// <summary>Publishes <paramref name="gate"/> for the world it was started in.</summary>
    internal static void Bind(TacticalOpeningBandGate gate, object world)
    {
        _gate = gate;
        _world = world;
    }

    /// <summary>Publishes <paramref name="gate"/> for the world that is running now (null world when none is, which keeps it inert).</summary>
    internal static void BindToCurrentWorld(TacticalOpeningBandGate gate)
        => Bind(gate, Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null);

    /// <summary>Forgets <paramref name="gate"/> if it is the bound one (a match end must not unbind a newer match's gate).</summary>
    internal static void Unbind(TacticalOpeningBandGate gate)
    {
        if (!ReferenceEquals(_gate, gate))
            return;
        _gate = null;
        _world = null;
    }

    internal static void Reset()
    {
        _gate = null;
        _world = null;
    }

    /// <summary>
    /// The numbers the gate turns a round into a gear level with, read live: the frozen difficulty
    /// step, the map's start and end level and the squad's role (<c>Settings.TacticalSquadIsPmc</c> is
    /// read at every request, so it is read here at every call). Null when the map's tier scaling is
    /// off, where the server writes no band at all and there is nothing to hold or announce.
    /// </summary>
    internal static TacticalGearContext? ReadGearContext()
    {
        string map = Singleton<GameWorld>.Instantiated
            ? Singleton<GameWorld>.Instance.LocationId
            : ArenaSession.ArmedMap;
        if (string.IsNullOrEmpty(map) || !ArenaClientConfig.GetScaleBotTier(map))
            return null;
        return new TacticalGearContext(
            ArenaDifficulty.Step,
            ArenaClientConfig.GetBotTierStartLevel(map),
            ArenaClientConfig.GetBotTierEndLevel(map),
            Settings.TacticalSquadIsPmc.Value);
    }

    private static bool IsCurrentWorld()
        => _gate != null && Singleton<GameWorld>.Instantiated
            && ReferenceEquals(_world, Singleton<GameWorld>.Instance);
}
