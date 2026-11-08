namespace ArenaMode.Hud;

/// <summary>
/// Tap-or-hold decision for the inventory key in an Arena raid (2026-11-08, adopted from
/// the live Arena table): a tap opens the inventory as usual, a hold shows the team table
/// for as long as the key stays down. Pure clock state, so the harness can drive it; the
/// Unity node around it owns key reading and the forwarding.
/// </summary>
internal sealed class InventoryKeyHold
{
    internal const float HoldSeconds = 0.25f;

    private float _pressedAt = -1f;

    /// <summary>True while the key has been down for at least <see cref="HoldSeconds"/>.</summary>
    internal bool Held { get; private set; }

    internal bool Pressed => _pressedAt >= 0f;

    /// <summary>The key went down (the command was seen). A second press while one is pending restarts the clock.</summary>
    internal void Press(float now)
    {
        _pressedAt = now;
        Held = false;
    }

    /// <summary>
    /// Advance with the current key state. Returns true exactly once, when the key was
    /// released before the hold threshold: that release is the tap to forward. A release
    /// after the threshold only closes the table.
    /// </summary>
    internal bool Update(float now, bool keyDown)
    {
        if (_pressedAt < 0f) return false;
        float elapsed = now - _pressedAt;
        if (keyDown)
        {
            Held = elapsed >= HoldSeconds;
            return false;
        }
        bool tap = elapsed < HoldSeconds;
        _pressedAt = -1f;
        Held = false;
        return tap;
    }

    /// <summary>Forget a pending press without forwarding anything (screen change, teardown).</summary>
    internal void Cancel()
    {
        _pressedAt = -1f;
        Held = false;
    }
}
