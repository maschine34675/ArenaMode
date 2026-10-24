#nullable enable

using System;
using System.Linq;

namespace ArenaMode.Shared;

/// <summary>
/// Known reflected baselines plus forward-version admission. A newer build may
/// attempt the integration, but must still pass every reflected API and profile
/// protection contract. Admission is not a compatibility certification.
/// The previously reviewed 0.10.0 remains admitted; 0.10.1/0.10.2 were not reviewed.
/// </summary>
public static class PitFireTeamVersions
{
    public static readonly string[] VerifiedPluginVersions = { "0.10.0", "0.10.3", "1.0.0" };
    public static readonly Version[] VerifiedAssemblyVersions =
    {
        new Version(0, 10, 0, 0),
        new Version(0, 10, 3, 0),
        new Version(1, 0, 0, 0)
    };
    public static readonly Version MinimumForwardVersion = new Version(0, 10, 3, 0);
    public static string Describe => "0.10.0 or 0.10.3+ (reviewed: " + string.Join(", ", VerifiedPluginVersions) + ")";

    public static bool IsSupportedPluginVersion(string? version)
        => IsSupportedAssemblyVersion(ParsePluginVersion(version));

    public static bool IsSupportedAssemblyVersion(Version? version)
    {
        Version? normalized = Normalize(version);
        return normalized != null
            && (normalized.Equals(VerifiedAssemblyVersions[0])
                || normalized.CompareTo(MinimumForwardVersion) >= 0);
    }

    /// <summary>Metadata and assembly must identify the same admitted build.</summary>
    public static bool IsSupportedIdentity(string? pluginVersion, Version? assemblyVersion)
    {
        Version? plugin = ParsePluginVersion(pluginVersion);
        Version? assembly = Normalize(assemblyVersion);
        return IsSupportedAssemblyVersion(plugin) && plugin!.Equals(assembly);
    }

    public static bool IsNewerThanVerified(Version? version)
    {
        Version? normalized = Normalize(version);
        // Keep unreviewed intervening releases advisory too. Reviewing 1.0.0
        // does not certify 0.10.4 or change the minimum/proficiency contract.
        return normalized != null && normalized.CompareTo(MinimumForwardVersion) > 0
            && !VerifiedAssemblyVersions.Contains(normalized);
    }

    // BepInEx exposes numeric System.Version metadata, not GitHub beta tags.
    // Three/four numeric components describe the same build when revision is 0.
    private static Version? ParsePluginVersion(string? value)
    {
        if (value == null) return null;
        string text = value.Trim();
        string[] parts = text.Split('.');
        if ((parts.Length != 3 && parts.Length != 4)
            || parts.Any(part => part.Length == 0 || part.Any(c => c < '0' || c > '9'))
            || !Version.TryParse(text, out Version? version)) return null;
        return Normalize(version);
    }

    private static Version? Normalize(Version? version)
        => version == null || version.Build < 0 ? null
            : new Version(version.Major, version.Minor, version.Build, Math.Max(0, version.Revision));
}
