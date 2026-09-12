using System;
using ArenaMode.Modes;

namespace ArenaMode.Session;

internal sealed class ArenaDeathmatchRulesState
{
    internal static readonly int[][] Choices = { new[] { 0, 1, 3, 5, 10 }, new[] { 0, 10, 20, 50, 100 },
        new[] { 0, 120, 300, 600, 1200 }, new[] { 0, 1 } };
    private readonly Func<int, int> _get;
    private readonly Func<int, int, bool> _set;
    private readonly Func<bool> _locked;
    internal ArenaDeathmatchRulesState(Func<int, int> get, Func<int, int, bool> set, Func<bool> locked)
    { _get = get; _set = set; _locked = locked; }
    internal bool Visible { get; private set; }
    internal bool Editable => Visible && !_locked();
    internal void Synchronize(string mode, bool tacticalOnly)
        => Visible = !tacticalOnly && ArenaModeCatalog.NormalizeId(mode) == ArenaModeCatalog.DeathmatchId;
    internal int Value(int row) => _get(row);
    internal bool CanSelect(int row, int value)
    {
        if (!Editable || row < 0 || row >= Choices.Length || Array.IndexOf(Choices[row], value) < 0) return false;
        int rounds = row == 0 ? value : Value(0), kills = row == 1 ? value : Value(1), seconds = row == 2 ? value : Value(2);
        return rounds == 0 || kills > 0 || seconds > 0;
    }
    internal bool Select(int row, int value) => CanSelect(row, value) && _set(row, value) && Value(row) == value;
    internal string Label(int row) => row switch {
        0 => "ROUNDS: " + (Value(row) == 0 ? "Endless" : Value(row).ToString()),
        1 => "KILLS / ROUND: " + (Value(row) == 0 ? "Off" : Value(row).ToString()),
        2 => "TIME / ROUND: " + ArenaTdmMatchRulesState.FormatTime(Value(row)),
        _ => "OPPONENTS: " + (Value(row) == 0 ? "Free-for-all" : "All vs player") };
    internal static string ChoiceLabel(int row, int value) => row == 3 ? (value == 0 ? "FFA" : "All vs player")
        : value == 0 ? (row == 0 ? "Endless" : "Off") : (row == 2 ? value / 60 : value).ToString();
}
