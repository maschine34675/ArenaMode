#nullable enable

using System;
using System.Linq;

namespace ArenaMode.Shared;

/// <summary>
/// The exact PitFireTeam builds Arena's reflected contract has been verified
/// against, in one place because six separate runtime gates read it: the client
/// adapter's plugin and assembly checks, the round lifecycle's own pair, the
/// optional goal-context reference and the server persistence guard.
///
/// A SET, never a range or a minimum. Arena reflects private PitFireTeam members
/// by exact signature and patches its private activation seam; "0.10 or newer"
/// would be a promise about builds nobody compared. 0.10.1 and 0.10.2 stay
/// rejected for exactly that reason — they were never diffed against 0.10.0.
///
/// 0.10.3 was added after a member-by-member source diff of every reflected
/// type, field, property, method and Harmony target found no signature change.
/// Its own additions are internal to PitFireTeam: follower proficiency values
/// threaded through its spawn path (an extra optional argument on an overload
/// Arena does not reflect), a combat-layer release notification during follower
/// teardown, squad kill-report bookkeeping, and a new teammate proficiency
/// route. None of those is part of the reflected contract; the last one is not
/// covered by Arena's management gate, so a proficiency edit made in the menu
/// after a raid persists like PitFireTeam's other unguarded menu edits.
///
/// Every gate still fails closed on top of this list: a build that passes here
/// and then drifts in any reflected member disables team Arena and leaves solo
/// Arena running.
/// </summary>
public static class PitFireTeamVersions
{
    /// <summary>BepInEx plugin metadata versions, in the order they were verified.</summary>
    public static readonly string[] SupportedPluginVersions = { "0.10.0", "0.10.3" };

    /// <summary>
    /// Assembly identities for the same builds. Both halves ship the plugin
    /// version with a zero revision, and this is checked separately from the
    /// metadata string because they are separately forgeable.
    /// </summary>
    public static readonly Version[] SupportedAssemblyVersions =
    {
        new Version(0, 10, 0, 0),
        new Version(0, 10, 3, 0)
    };

    /// <summary>For a rejection message, so the log names what would be accepted.</summary>
    public static string Describe => string.Join(" or ", SupportedPluginVersions);

    public static bool IsSupportedPluginVersion(string? version)
        => version != null
           && SupportedPluginVersions.Contains(version.Trim(), StringComparer.Ordinal);

    public static bool IsSupportedAssemblyVersion(Version? version)
        => version != null && SupportedAssemblyVersions.Contains(version);
}
