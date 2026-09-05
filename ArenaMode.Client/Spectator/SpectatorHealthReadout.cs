using EFT;
using EFT.HealthSystem;
using System;
using System.Globalization;

namespace ArenaMode.Spectator;

/// <summary>
/// A fresh read of the selected physical body for each GUI event. No profile-ID
/// lookup, cached HP, event subscription or health changes survive a target switch.
/// </summary>
internal static class SpectatorHealthReadout
{
    internal static string ReadStatus(
        bool arenaActive,
        bool teamArenaActive,
        bool ownsCamera,
        ArenaSpectatorRequest request,
        ArenaSpectatorTarget target,
        Player frozenBody)
    {
        if (!arenaActive || !teamArenaActive || !ownsCamera || target == null)
            return null;

        try
        {
            Player player = target.Player;
            if (!RequestIsCurrent(request)
                || ReferenceEquals(player, null)
                || player == null
                || !ReferenceEquals(player, frozenBody)
                || player.IsYourPlayer
                || player.gameObject == null
                || !player.gameObject.activeInHierarchy
                || string.IsNullOrEmpty(target.ProfileId)
                || !string.Equals(target.ProfileId, player.ProfileId?.Trim(), StringComparison.Ordinal))
            {
                return null;
            }

            IHealthController health = player.HealthController;
            if (health?.IsAlive != true)
                return null;

            // Native Common aggregates Current/Maximum from every body part.
            // Read the bot's own maximum; PFT and other mods may change its health.
            ValueStruct total = health.GetBodyPartHealth(EBodyPart.Common);
            if (!ReferenceEquals(health, player.HealthController)
                || !health.IsAlive
                || !RequestIsCurrent(request))
            {
                return null;
            }

            string side = target.Side == ArenaSpectatorSide.Ally ? "ALLY" : "OPPONENT";
            string name = string.IsNullOrEmpty(target.DisplayName) ? target.ProfileId : target.DisplayName;
            // Names are plain text in the banner, including mod-provided names.
            name = name.Replace('\r', ' ').Replace('\n', ' ');
            return $"SPECTATING {side}: {name}\n{FormatHealth(total)}    LMB/RMB switch";
        }
        catch
        {
            // Destruction/rebind can invalidate a Unity body or its controller
            // between Tick and OnGUI. Hide this event rather than show old HP.
            return null;
        }
    }

    private static bool RequestIsCurrent(ArenaSpectatorRequest request)
    {
        Player deadPlayer = request?.DeadPlayer;
        return request?.ReadEpoch != null
               && request.IsEligible != null
               && request.ReadEpoch() == request.Epoch
               && request.IsEligible()
               && !ReferenceEquals(deadPlayer, null)
               && deadPlayer != null
               && deadPlayer.IsYourPlayer
               && deadPlayer.HealthController?.IsAlive == false;
    }

    private static string FormatHealth(ValueStruct total)
    {
        if (float.IsNaN(total.Current) || float.IsInfinity(total.Current)
            || float.IsNaN(total.Maximum) || float.IsInfinity(total.Maximum)
            || total.Current < 0f || total.Maximum <= 0f || total.Current > total.Maximum)
        {
            return "HP unavailable";
        }

        // Whole HP keeps the small banner legible. Use the same rounding as the
        // native Common readout while always retaining the actual bot maximum.
        string current = Math.Round(total.Current).ToString("0", CultureInfo.InvariantCulture);
        string maximum = Math.Round(total.Maximum).ToString("0", CultureInfo.InvariantCulture);
        return $"HP {current} / {maximum}";
    }
}
