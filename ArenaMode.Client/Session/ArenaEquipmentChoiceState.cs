using System;

namespace ArenaMode.Session;

/// <summary>
/// Pre-raid menu contract for item 1 of the 2026-10-06 follow-up ("own gear to
/// the left"): a single two-way equipment choice row in the LEFT mode panel,
/// mode-independent the same way <see cref="ArenaDeathmatchRulesState"/> and
/// <see cref="ArenaTdmMatchRulesState"/> are — this class owns no setting of
/// its own, it only decides visibility/editability and forwards an accepted
/// choice to whatever getter/setter the caller wired up for the CURRENT mode.
///
/// Mode-awareness is delegated entirely to <see cref="Modes.
/// ArenaEquipmentChoice.IsOfferedFor"/>: today only Zombie offers a choice, so
/// this row is built once in <see cref="ArenaModeSelectionView"/> and simply
/// stays invisible for every other mode, following the same HIDE-when-
/// inapplicable convention the TDM/Deathmatch rules rows already use (not the
/// right-hand mutators panel's "grey out + N/A" convention — see
/// ArenaEquipmentChoice's own doc comment on why this needed its own class
/// rather than reusing <c>ArenaMutatorSelection</c>'s per-row availability
/// check).
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
    /// Tactical is excluded the same way DM/TDM already exclude themselves
    /// under a protected team (<c>!_tacticalOnly &amp;&amp;</c>), even though
    /// <c>ArenaEquipmentChoice.IsOfferedFor</c> can never itself resolve to
    /// Tactical today — belt and suspenders against a future mode table entry
    /// that would otherwise leak a toggle into Tactical's own, different,
    /// automatic own-gear path.
    /// </summary>
    internal bool Visible => !_tacticalOnly && Modes.ArenaEquipmentChoice.IsOfferedFor(_mode);
    internal bool Editable => Visible && !_locked();
    internal bool OwnGear => _get(_mode);
    internal string Label => "EQUIPMENT: " + Modes.ArenaEquipmentChoice.Label(OwnGear);

    internal void Synchronize(string mode, bool tacticalOnly)
    { _mode = mode; _tacticalOnly = tacticalOnly; }

    internal bool TrySelect(bool ownGear) => Editable && _set(_mode, ownGear) && OwnGear == ownGear;
}
