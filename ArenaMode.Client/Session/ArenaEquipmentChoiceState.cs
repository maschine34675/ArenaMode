using System;

namespace ArenaMode.Session;

/// <summary>
/// Visibility and guarded persistence for the inline PMC equipment selector.
/// Mode/roster policy lives in ArenaEquipmentChoice; hidden or armed controls
/// cannot change the saved preference. The getter and setter take the mode AND
/// the roster, because Tactical's preference belongs to its protected squad
/// alone (Tactical/TeamOwnGear; a solo Tactical player has none) while the
/// other modes carry one each.
/// </summary>
internal sealed class ArenaEquipmentChoiceState
{
    private readonly Func<string, bool, bool> _get;
    private readonly Func<string, bool, bool, bool> _set;
    private readonly Func<bool> _locked;
    private string _mode;
    private bool _tacticalOnly;

    internal ArenaEquipmentChoiceState(Func<string, bool, bool> get, Func<string, bool, bool, bool> set, Func<bool> locked)
    {
        _get = get ?? throw new ArgumentNullException(nameof(get));
        _set = set ?? throw new ArgumentNullException(nameof(set));
        _locked = locked ?? throw new ArgumentNullException(nameof(locked));
    }

    /// <summary>
    /// Every mode but Gun Game offers the row solo; protected Tactical offers it
    /// for its squad. Changing group membership re-evaluates this before any
    /// stale button can write a setting.
    /// </summary>
    internal bool Visible => Modes.ArenaEquipmentChoice.IsOfferedFor(_mode, _tacticalOnly);
    internal bool Editable => Visible && !_locked();
    internal bool OwnGear => _get(_mode, _tacticalOnly);
    internal string Label => "EQUIPMENT: " + Modes.ArenaEquipmentChoice.Label(OwnGear);
    internal string Mode => _mode;
    internal bool ProtectedTeam => _tacticalOnly;

    internal void Synchronize(string mode, bool tacticalOnly)
    { _mode = mode; _tacticalOnly = tacticalOnly; }

    internal bool TrySelect(bool ownGear) => Editable && _set(_mode, _tacticalOnly, ownGear) && OwnGear == ownGear;
}
