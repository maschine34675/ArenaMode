using System;

namespace ArenaMode.Session;

/// <summary>
/// Owns only one already-created native map row. Mode/F12 changes must not rerun
/// ShowInternal: vanilla would append views and subscribe to time/group events again.
/// The Unity adapter supplies visibility and native deselection, never database edits.
/// </summary>
internal sealed class ArenaConditionalMapView
{
    private Action<bool> _setVisible;
    private Action _clearSelection;
    private readonly bool _originalVisible;
    private bool _visible;

    internal ArenaConditionalMapView(bool originalVisible, Action<bool> setVisible, Action clearSelection)
    {
        _originalVisible = _visible = originalVisible;
        _setVisible = setVisible ?? throw new ArgumentNullException(nameof(setVisible));
        _clearSelection = clearSelection ?? throw new ArgumentNullException(nameof(clearSelection));
    }

    internal void Refresh(bool available, bool selected)
    {
        if (_setVisible == null) return;
        if (!available && selected) _clearSelection();
        if (_visible == available) return;
        _setVisible(available);
        _visible = available;
    }

    internal void Close()
    {
        var restore = _setVisible;
        _setVisible = null;
        _clearSelection = null;
        if (restore != null && _visible != _originalVisible) restore(_originalVisible);
    }
}
