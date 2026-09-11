using EFT.UI.Screens;
using UnityEngine;

namespace ArenaMode.Hud;

/// <summary>Presentation only: leave native inventory/menu screens unobstructed.</summary>
internal static class ArenaHudVisibility
{
    internal static bool IsBattleUiActive
    {
        get
        {
            var screens = EftScreenManager.Instance;
            return screens != null && screens.CheckCurrentScreen(EEftScreenType.BattleUI);
        }
    }

    internal static void Apply(Canvas canvas)
    {
        // Disable drawing, not the GameObject or ticking. Notifications still
        // expire while hidden and TMP components keep their normal lifecycle.
        if (canvas != null) canvas.enabled = IsBattleUiActive;
    }
}
