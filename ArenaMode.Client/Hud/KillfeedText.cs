using ArenaMode.Combat;
using ArenaMode.Effects;
using System;

namespace ArenaMode.Hud;

/// <summary>Uniform attacker/victim headline, with metadata on a separate line.</summary>
internal static class KillfeedText
{
    internal static string Format(ArenaKill kill, string friendlyColor, string opponentColor)
    {
        var killerColor = TeamColor(kill.KillerProfileId, friendlyColor, opponentColor);
        var victimColor = TeamColor(kill.VictimProfileId, friendlyColor, opponentColor);
        var names = $"<color={killerColor}>{Literal(kill.KillerName)}</color> ➔ "
            + $"<color={victimColor}>{Literal(kill.VictimName)}</color>";
        var roles = $"{Literal(kill.KillerRoleLabel)} ➔ {Literal(kill.VictimRoleLabel)}";
        var weapon = string.IsNullOrWhiteSpace(kill.WeaponName) ? string.Empty : $" · {Literal(kill.WeaponName)}";
        // Every kill direction includes distance. A cleaned-up aggressor can no
        // longer supply a position; distinguish that from a genuine close kill.
        var distance = kill.DistanceKnown && kill.Distance >= 0f && !float.IsInfinity(kill.Distance)
            ? $"{Math.Round(kill.Distance)} m" : "? m";
        return $"{names}\n<size=14><color=#D5DAE0>{roles} · {distance}{weapon}</color></size>";
    }

    private static string Literal(string value)
        => string.IsNullOrWhiteSpace(value) ? "?" : value.Replace('<', '‹').Replace('>', '›');

    internal static string FormatBarbedDeath(ArenaBarbedDeath death, string friendlyColor, string opponentColor)
    {
        var victimColor = TeamColor(death.VictimProfileId, friendlyColor, opponentColor);
        return $"<color={victimColor}>{Literal(death.VictimName)}</color>"
            + $"\n<size=14><color=#D5DAE0>{Literal(death.VictimRoleLabel)} · BARBED WIRE</color></size>";
    }

    // Push formats once during the synchronous captured-kill event. These IDs
    // come from the original Player/IPlayer, not role or faction guesses. Keep
    // unresolved team rosters neutral and never change scoring classification.
    private static string TeamColor(string profileId, string friendlyColor, string opponentColor)
        => ArenaTracerTeams.Classify(profileId, verifiedPlayerIdentity: true) switch
        {
            ArenaTracerSide.Friendly => friendlyColor,
            ArenaTracerSide.Opponent => opponentColor,
            _ => "#D5DAE0",
        };
}
