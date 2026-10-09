using ArenaMode.Modes;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace ArenaMode.Session;

/// <summary>Pre-raid menu contract; persisted values, never a guessed preset, own the display.</summary>
internal sealed class ArenaTdmMatchRulesState
{
    internal static readonly IReadOnlyList<int> KillOptions = Array.AsReadOnly(new[] { 0, 25, 50, 100 });
    internal static readonly IReadOnlyList<int> TimeOptions = Array.AsReadOnly(new[] { 0, 300, 600, 900, 1200 });
    private readonly Func<int> _getKills, _getSeconds;
    private readonly Func<int, bool> _setKills, _setSeconds;
    private readonly Func<bool> _raidLocked;
    private string _mode;
    private bool _tacticalOnly;

    internal ArenaTdmMatchRulesState(Func<int> getKills, Func<int> getSeconds,
        Func<int, bool> setKills, Func<int, bool> setSeconds, Func<bool> raidLocked)
    {
        _getKills = getKills ?? throw new ArgumentNullException(nameof(getKills));
        _getSeconds = getSeconds ?? throw new ArgumentNullException(nameof(getSeconds));
        _setKills = setKills ?? throw new ArgumentNullException(nameof(setKills));
        _setSeconds = setSeconds ?? throw new ArgumentNullException(nameof(setSeconds));
        _raidLocked = raidLocked ?? throw new ArgumentNullException(nameof(raidLocked));
    }

    internal bool Visible => !_tacticalOnly
        && string.Equals(_mode, ArenaModeCatalog.TeamDeathmatchId, StringComparison.OrdinalIgnoreCase);
    internal bool Editable => Visible && !_raidLocked();
    internal int KillLimit => _getKills();
    internal int TimeLimitSeconds => _getSeconds();
    internal string KillLabel => FormatKills(KillLimit);
    internal string TimeLabel => FormatTime(TimeLimitSeconds);

    internal void Synchronize(string mode, bool tacticalOnly)
    { _mode = mode; _tacticalOnly = tacticalOnly; }

    internal bool TrySelectKills(int value)
        => Editable && Contains(KillOptions, value) && _setKills(value) && KillLimit == value;

    internal bool TrySelectTime(int seconds)
        => Editable && Contains(TimeOptions, seconds) && _setSeconds(seconds) && TimeLimitSeconds == seconds;

    internal static string FormatKills(int value)
        => value < 0 || value > 1000 ? "Unavailable"
            : value == 0 ? "Off" : value.ToString(CultureInfo.InvariantCulture);

    internal static string FormatTime(int seconds)
    {
        if (seconds < 0 || seconds > 7200) return "Unavailable";
        if (seconds == 0) return "Off";
        return seconds % 60 == 0
            ? (seconds / 60).ToString(CultureInfo.InvariantCulture) + " min"
            : (seconds / 60).ToString(CultureInfo.InvariantCulture) + ":"
              + (seconds % 60).ToString("00", CultureInfo.InvariantCulture);
    }

    private static bool Contains(IReadOnlyList<int> choices, int value)
    {
        for (int i = 0; i < choices.Count; i++) if (choices[i] == value) return true;
        return false;
    }
}
