using ArenaMode.Modes;
using System;

namespace ArenaMode.Session;

/// <summary>
/// Owns the map-screen choice independently of Unity. Constructing a view does
/// not persist anything; a requested selection changes only after its writer
/// accepts the stable catalog id.
/// </summary>
internal sealed class ArenaModeSelectionState
{
    private readonly Func<string, bool> _onSelected;
    private bool _initialSelectionNeedsCommit;

    public ArenaModeSelectionState(string selectedMode, bool tacticalOnly,
        Func<string, bool> onSelected)
    {
        _onSelected = onSelected ?? throw new ArgumentNullException(nameof(onSelected));
        TacticalOnly = tacticalOnly;
        var resolved = ArenaModeCatalog.ResolveSelection(selectedMode, tacticalOnly);
        SelectedIndex = FindIndex(resolved);
        _initialSelectionNeedsCommit = !string.Equals(selectedMode?.Trim(), resolved,
            StringComparison.OrdinalIgnoreCase);
    }

    public string SelectedMode => SelectedDefinition.Id;
    public int SelectedIndex { get; private set; }
    public bool TacticalOnly { get; }
    public ArenaModeDefinition SelectedDefinition => ArenaModeCatalog.All[SelectedIndex];

    public bool CanSelectIndex(int index)
        => index >= 0 && index < ArenaModeCatalog.All.Count
           && ArenaModeCatalog.CanSelect(ArenaModeCatalog.All[index].Id, TacticalOnly);

    /// <summary>
    /// Repair an invalid stored choice or a protected team's former solo mode
    /// only once the caller has successfully constructed its controls.
    /// </summary>
    public void CommitInitialSelection()
    {
        if (!_initialSelectionNeedsCommit)
            return;

        if (!_onSelected(SelectedMode))
            throw new InvalidOperationException(
                $"Arena could not apply mode '{SelectedMode}'.");

        _initialSelectionNeedsCommit = false;
    }

    public bool TrySelectIndex(int index)
    {
        if (!CanSelectIndex(index))
            return false;

        if (index == SelectedIndex)
            return true;

        var requestedMode = ArenaModeCatalog.All[index].Id;
        if (!_onSelected(requestedMode))
            return false;

        SelectedIndex = index;
        _initialSelectionNeedsCommit = false;
        return true;
    }

    private static int FindIndex(string mode)
    {
        for (var index = 0; index < ArenaModeCatalog.All.Count; index++)
        {
            if (string.Equals(ArenaModeCatalog.All[index].Id, mode,
                    StringComparison.Ordinal))
                return index;
        }

        throw new InvalidOperationException($"Arena mode '{mode}' is missing from the catalog.");
    }
}
