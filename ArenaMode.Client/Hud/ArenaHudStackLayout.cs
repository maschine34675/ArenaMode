using ArenaMode.Modes;
using UnityEngine;

namespace ArenaMode.Hud;

/// <summary>
/// The score owns its objective row. Shared live dimensions keep its surface
/// and the pickup panel below it aligned even between text refreshes.
/// </summary>
internal static class ArenaHudStackLayout
{
    internal const float ObjectiveHeight = 36f;
    private const float PowerupGap = 16f;

    internal static bool ObjectivesVisible(ArenaMatch match)
        => match != null && ReferenceEquals(ArenaMatchService.Current, match)
            && match.Mode is TeamDeathmatchMode mode && mode.ObjectiveHudVisible && mode.IsSearchActive;

    internal static float ScoreHeight(ArenaMatch match)
        => ScorePanel.PanelHeight + (ObjectivesVisible(match) ? ObjectiveHeight : 0f);

    internal static Vector2 PowerupPosition(ArenaMatch match)
        => ScorePanel.Position - new Vector2(0f, ScoreHeight(match) + PowerupGap);
}
