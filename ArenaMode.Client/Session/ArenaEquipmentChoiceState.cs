using System;

namespace ArenaMode.Session;

/// <summary>
/// Visibility and guarded persistence for the inline PMC equipment selector.
/// Mode/roster policy lives in ArenaEquipmentChoice; hidden or armed controls
/// cannot change the saved preference.
/// </summary>
internal sealed class ArenaEquipmentChoiceState
{
    private readonly Func<string, bool> _get;
    private readonly Func<string, bool, bool> _set;
    private readonly Func<bool> _locked;
    private string _mode;
    private bool _tacticalOnly;

    internal ArenaEquipmentChoiceState(Func<string, bool> get, Func<string, bool, bool> set, Func<bool> locked)
    {
        _get = get ?? throw new ArgumentNullException(nameof(get));
        _set = set ?? throw new ArgumentNullException(nameof(set));
        _locked = locked ?? throw new ArgumentNullException(nameof(locked));
    }

    /// <summary>
    /// Only solo Zombies and protected Tactical offer the row. Changing group
    /// membership re-evaluates this before any stale button can write a setting.
    /// </summary>
    internal bool Visible => Modes.ArenaEquipmentChoice.IsOfferedFor(_mode, _tacticalOnly);
    internal bool Editable => Visible && !_locked();
    internal bool OwnGear => _get(_mode);
    internal string Label => "EQUIPMENT: " + Modes.ArenaEquipmentChoice.Label(OwnGear);

    internal void Synchronize(string mode, bool tacticalOnly)
    { _mode = mode; _tacticalOnly = tacticalOnly; }

    internal bool TrySelect(bool ownGear) => Editable && _set(_mode, ownGear) && OwnGear == ownGear;
}
