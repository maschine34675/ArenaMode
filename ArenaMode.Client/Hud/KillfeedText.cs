using ArenaMode.Combat;
using System;

namespace ArenaMode.Hud;

/// <summary>Uniform attacker/victim headline, with metadata on a separate line.</summary>
internal static class KillfeedText
{
    internal static string Format(ArenaKill kill)
    {
        var killerColor = kill.KilledByLocal ? "#7FDBFF" : "#D5DAE0";
        var victimColor = kill.LocalIsVictim ? "#FF8585" : RoleColor(kill.VictimRole);
        var names = $"<color={killerColor}>{Literal(kill.KillerName)}</color> ➔ "
            + $"<color={victimColor}>{Literal(kill.VictimName)}</color>";
        var roles = $"{Literal(kill.KillerRoleLabel)} ➔ {Literal(kill.VictimRoleLabel)}";
        var weapon = string.IsNullOrWhiteSpace(kill.WeaponName) ? string.Empty : $" · {Literal(kill.WeaponName)}";
        var headshot = kill.Headshot ? " · <color=#FF8585>HS</color>" : string.Empty;
        // Every kill direction includes distance. A cleaned-up aggressor can no
        // longer supply a position; distinguish that from a genuine close kill.
        var distance = kill.DistanceKnown && kill.Distance >= 0f && !float.IsInfinity(kill.Distance)
            ? $"{Math.Round(kill.Distance)} m" : "? m";
        return $"{names}\n<size=14><color=#D5DAE0>{roles} · {distance}{weapon}{headshot}</color></size>";
    }

    private static string Literal(string value)
        => string.IsNullOrWhiteSpace(value) ? "?" : value.Replace('<', '‹').Replace('>', '›');

    private static string RoleColor(ArenaRole role)
    {
        switch (role)
        {
            case ArenaRole.Pmc: return "#7FDBFF";
            case ArenaRole.Boss: return "#FF851B";
            case ArenaRole.Raider: return "#FFDC00";
            case ArenaRole.Follower: return "#FFDC00";
            default: return "#DDDDDD";
        }
    }
}
