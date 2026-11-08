using ArenaMode.Modes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ArenaMode.Hud;

/// <summary>
/// The rows the scoreboard surfaces show, selected once per scoreboard version and shared
/// by the F9 panel and the hold-to-peek table (2026-11-08). Team modes (Team DM,
/// Domination, Tactical) show both current rosters; the solo modes show the overall
/// standings with the local row pinned. Pure selection: no drawing, no input.
/// </summary>
internal sealed class ArenaScoreboardRows
{
    private int _version = -1;
    private ArenaMatch _match;
    private bool _teams;

    internal List<ArenaScoreboardEntry> Friendly { get; private set; } = new();
    internal List<ArenaScoreboardEntry> Opponent { get; private set; } = new();
    internal List<ArenaScoreboardEntry> Overall { get; private set; } = new();
    internal int Hidden { get; private set; }
    /// <summary>True when the rows are the two team tables rather than the overall standings.</summary>
    internal bool Teams => _teams;
    internal ArenaMatch Match => _match;

    internal static bool IsTeamMode(ArenaMatch match)
        => match?.Mode is TacticalMode || match?.Mode is TeamDeathmatchMode;

    /// <summary>Recompute when the match or its scoreboard version changed; otherwise keep the cached rows.</summary>
    internal void Refresh(ArenaMatch match)
    {
        if (match?.Scoreboard == null) { Clear(); return; }
        bool teams = IsTeamMode(match);
        if (ReferenceEquals(_match, match) && _version == match.Scoreboard.Version && _teams == teams)
            return;

        _match = match;
        _version = match.Scoreboard.Version;
        _teams = teams;
        if (teams)
        {
            Friendly = match.Scoreboard.Standings(
                    ArenaScoreboardSide.Friendly,
                    currentRosterOnly: true,
                    maxRows: match.Mode is TeamDeathmatchMode ? 12 : 5,
                    pinLocal: true)
                .ToList();
            Opponent = match.Scoreboard.Standings(
                    ArenaScoreboardSide.Opponent,
                    currentRosterOnly: true,
                    maxRows: 15,
                    pinLocal: false)
                .ToList();
            Overall.Clear();
            Hidden = Math.Max(
                0,
                match.Scoreboard.Count(ArenaScoreboardSide.Friendly, true) - Friendly.Count
                + match.Scoreboard.Count(ArenaScoreboardSide.Opponent, true) - Opponent.Count);
            return;
        }

        Overall = match.Scoreboard.Standings(
                side: null,
                currentRosterOnly: false,
                maxRows: 12,
                pinLocal: true)
            .ToList();
        Friendly.Clear();
        Opponent.Clear();
        Hidden = Math.Max(0, match.Scoreboard.HistoricalParticipantCount - Overall.Count);
    }

    internal void Clear()
    {
        _match = null;
        _version = -1;
        Friendly.Clear();
        Opponent.Clear();
        Overall.Clear();
        Hidden = 0;
    }
}
