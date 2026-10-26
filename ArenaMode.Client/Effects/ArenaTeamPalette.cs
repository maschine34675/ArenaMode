using ArenaMode.Config;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using UnityEngine;

namespace ArenaMode.Effects;

/// <summary>
/// Cosmetic mapping only. Admission and scoring keep their relative team identities;
/// the immutable TDM choice maps those teams to fixed faction colours. Nothing here
/// reads or changes a player's profile, gear, or the saved custom colour settings.
/// </summary>
internal static class ArenaTeamPalette
{
    // Lighter UI shades remain readable on dark panels and on the world monitors.
    // Tracers and lasers use the matching HDR Blue/Red palette below.
    internal const int UsecRgb = 0x7FA7FF;
    internal const int BearRgb = 0xFF6B6B;
    internal static Color UsecUi => FromRgb(UsecRgb);
    internal static Color BearUi => FromRgb(BearRgb);

    private static bool? LocalUsec => ArenaSession.IsActive
        && Shared.ArenaPopulationPolicy.IsIndependentTeam(ArenaSession.ActiveMode)
        && Singleton<AbstractGame>.Instantiated && Singleton<GameWorld>.Instantiated
        && ArenaSession.IsExactActiveRaid(Singleton<AbstractGame>.Instance, Singleton<GameWorld>.Instance)
        ? ArenaTeamSideSelection.SelectedUsec : null;

    internal static ArenaTracerColor Friendly => Resolve(true, LocalUsec,
        Settings.FriendlyTracerColor?.Value ?? ArenaTracerColor.Cyan);
    internal static ArenaTracerColor Opponent => Resolve(false, LocalUsec,
        Settings.OpponentTracerColor?.Value ?? ArenaTracerColor.Red);

    internal static Color FriendlyUi => UiColor(true, LocalUsec, Friendly);
    internal static Color OpponentUi => UiColor(false, LocalUsec, Opponent);
    internal static string FriendlyHex => "#" + ColorUtility.ToHtmlStringRGB(FriendlyUi);
    internal static string OpponentHex => "#" + ColorUtility.ToHtmlStringRGB(OpponentUi);

    internal static ArenaTracerColor Resolve(bool friendly, bool? localUsec, ArenaTracerColor fallback)
        => localUsec.HasValue
            ? (friendly == localUsec.Value ? ArenaTracerColor.Blue : ArenaTracerColor.Red)
            : fallback;

    private static Color UiColor(bool friendly, bool? localUsec, ArenaTracerColor fallback)
    {
        if (localUsec.HasValue)
        {
            return friendly == localUsec.Value ? UsecUi : BearUi;
        }

        Color color = ArenaTracerPalette.Resolve(fallback);
        float peak = System.Math.Max(color.r, System.Math.Max(color.g, color.b));
        return new Color(color.r / peak, color.g / peak, color.b / peak, .9f);
    }

    private static Color FromRgb(int rgb)
        => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f,
            (rgb & 255) / 255f, .9f);
}
