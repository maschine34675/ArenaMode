#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ArenaMode.Shared;

// Structural fingerprints from reviewed TarkovaCola 2.2.1 commit
// 163bb9b1192d050b9e0da5aada8686e8c43877dd, client PerkDefs/Synergy/
// Achievements*. No version ceiling: an unchanged semantic catalog is accepted.
// Server catalog equality is independently tested to prevent accidental drift.
internal static class TarkovaColaResearchCatalog
{
    internal const string ResearchFingerprint = "ea2109653203d5d45b8ce2ffaecbe1aae2127d43544514ae36e81882e4f8db1d";
    internal const string AchievementFingerprint = "27df213626b8cf9632db43e2838db5dd2e515075223e9a9a304c11d6ff09d3c5";

    internal static string Perk(string id) => "perk|" + Key(id);
    internal static string Challenge(string perk, string id, int goal) => "aug|" + Key(perk) + "|" + Key(id) + "|" + Goal(goal);
    internal static string Synergy(string id, int goal) => "syn|" + Key(id) + "|" + Goal(goal);
    internal static string Achievement(string key, string serverId) => "ach|" + Key(key) + "|" + Key(serverId);
    internal static void RequireResearch(IEnumerable<string> descriptions) => Require(descriptions, ResearchFingerprint);
    internal static void RequireAchievements(IEnumerable<string> descriptions) => Require(descriptions, AchievementFingerprint);

    internal static string Fingerprint(IEnumerable<string> descriptions)
    {
        if (descriptions == null) throw new InvalidOperationException("Missing Tarkova Cola catalog.");
        var values = descriptions.Take(513).ToArray();
        if (values.Length == 0 || values.Length > 512 || values.Any(v => string.IsNullOrEmpty(v) || v.Length > 160)
            || values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new InvalidOperationException("Invalid Tarkova Cola catalog.");
        Array.Sort(values, StringComparer.Ordinal);
        using (var hash = SHA256.Create())
            return string.Concat(hash.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", values)))
                .Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
    }

    private static void Require(IEnumerable<string> descriptions, string expected)
    {
        if (!string.Equals(Fingerprint(descriptions), expected, StringComparison.Ordinal))
            throw new InvalidOperationException("Tarkova Cola research or achievement definitions changed.");
    }
    private static string Goal(int goal) => goal > 0 && goal <= 1_000_000
        ? goal.ToString(CultureInfo.InvariantCulture) : throw new InvalidOperationException("Invalid Tarkova Cola goal.");
    private static string Key(string value) => !string.IsNullOrEmpty(value) && value.Length <= 64
        && value.All(c => c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '_')
        ? value : throw new InvalidOperationException("Invalid Tarkova Cola catalog identity.");
}
