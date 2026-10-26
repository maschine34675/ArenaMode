using ArenaMode.Modes;
using UnityEngine;

namespace ArenaMode.Hud;

/// <summary>
/// Top-anchored panels below the score. Both views consume the same visibility
/// and dimensions, so neither update order nor a throttled text refresh can
/// leave an overlapping row or a stale gap after changing the mode/score offset.
/// </summary>
internal static class ArenaHudStackLayout
{
    internal const float ObjectiveHeight = 52f;
    private const float ObjectiveGap = 20f;
    private const float PowerupGap = 16f;

    internal static bool ObjectivesVisible(ArenaMatch match)
        => match != null && ReferenceEquals(ArenaMatchService.Current, match)
            && match.Mode is TeamDeathmatchMode mode && mode.ObjectiveHudVisible && mode.IsSearchActive;

    internal static Vector2 ObjectivePosition
        => ScorePanel.Position - new Vector2(0f, ScorePanel.PanelHeight + ObjectiveGap);

    internal static Vector2 PowerupPosition(ArenaMatch match)
        => ObjectivesVisible(match)
            ? ObjectivePosition - new Vector2(0f, ObjectiveHeight + PowerupGap)
            : ScorePanel.Position - new Vector2(0f, ScorePanel.PanelHeight + PowerupGap);
}
