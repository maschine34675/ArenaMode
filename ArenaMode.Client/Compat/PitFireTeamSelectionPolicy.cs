using ArenaMode.Shared;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ArenaMode.Compat;

internal enum PitFireTeamContractState
{
    Absent,
    Compatible,
    Incompatible
}

/// <summary>
/// Plain-data input for the PitFireTeam menu gate. Keeping this free of EFT and
/// BepInEx types lets the offline compatibility harness exercise the exact
/// production policy instead of maintaining a second approximation of it.
/// </summary>
internal sealed class PitFireTeamSelectionInput
{
    public PitFireTeamContractState Contract { get; set; }
    public bool IsFika { get; set; }
    public bool IsCoop { get; set; }
    public string GameMode { get; set; }
    public string CurrentPlayerAccountId { get; set; }
    public IReadOnlyList<string> NativeGroupAccountIds { get; set; }
    public IReadOnlyList<string> SelectedPitAccountIds { get; set; }
    public IReadOnlyList<string> SelectedPitProfileIds { get; set; }
}

internal sealed class PitFireTeamSelectionDecision
{
    private static readonly IReadOnlyList<string> EmptyRoster = Array.Empty<string>();
    private static readonly IReadOnlyDictionary<string, string> EmptyProfiles =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private PitFireTeamSelectionDecision(bool allowed, bool isTeam, string reason,
        IReadOnlyList<string> companionAccountIds,
        IReadOnlyDictionary<string, string> companionProfilesByAccountId)
    {
        Allowed = allowed;
        IsTeam = isTeam;
        Reason = reason ?? string.Empty;
        CompanionAccountIds = companionAccountIds ?? EmptyRoster;
        CompanionProfilesByAccountId = companionProfilesByAccountId ?? EmptyProfiles;
    }

    public bool Allowed { get; }
    public bool IsTeam { get; }
    public string Reason { get; }
    public IReadOnlyList<string> CompanionAccountIds { get; }
    public IReadOnlyDictionary<string, string> CompanionProfilesByAccountId { get; }

    public static PitFireTeamSelectionDecision Solo()
        => new(true, false, string.Empty, EmptyRoster, EmptyProfiles);

    public static PitFireTeamSelectionDecision Team(
        IReadOnlyList<string> companionAccountIds,
        IReadOnlyDictionary<string, string> companionProfilesByAccountId)
        => new(true, true, string.Empty, companionAccountIds, companionProfilesByAccountId);

    public static PitFireTeamSelectionDecision Reject(string reason)
        => new(false, false, reason, EmptyRoster, EmptyProfiles);
}

/// <summary>
/// One authoritative policy for the Arena button, its click, the later claim
/// and the final raid-start check. PitFireTeam's reflected adapter supplies the
/// two identity sets; this type only decides whether they prove a safe roster.
/// </summary>
internal static class PitFireTeamSelectionPolicy
{
    public const int MaximumCompanionCount = 4;

    public static bool IsSupportedCompanionCount(int count)
        => count >= 1 && count <= MaximumCompanionCount;

    // PitFireTeam 0.10.0 routes these synthetic RaidPlayer profiles through its
    // separate boss-spawn list. Mixing one into the ordinary companion list
    // can make PFT inject both boss and companion bodies, so team Arena accepts
    // only stored teammate profiles.
    private static readonly HashSet<string> UnsupportedSpecialProfileIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "677c4e0cc7a538c4210d4d47", // Knight
        "677c4e0cc7a538c4210d4d48", // Big Pipe
        "677c4e0cc7a538c4210d4d49", // Birdeye
    };

    public static bool IsUnsupportedSpecialProfileId(string profileId)
        => !string.IsNullOrWhiteSpace(profileId)
           && UnsupportedSpecialProfileIds.Contains(profileId.Trim());

    public static bool IsAuthoritativeStoredProfileIdentity(
        string requestedAccountId,
        string returnedAccountId,
        string storedProfileId)
    {
        if (string.IsNullOrWhiteSpace(requestedAccountId)
            || !string.Equals(
                requestedAccountId.Trim(),
                returnedAccountId?.Trim(),
                StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(storedProfileId)
            || storedProfileId.Length != 24)
        {
            return false;
        }

        foreach (char character in storedProfileId)
        {
            if (!Uri.IsHexDigit(character))
                return false;
        }

        return true;
    }

    public static PitFireTeamSelectionDecision Evaluate(
        PitFireTeamSelectionInput input,
        bool allowPendingModeSelection = false)
    {
        if (input == null)
            return PitFireTeamSelectionDecision.Reject("Arena could not inspect the current raid group.");

        if (input.IsFika)
            return PitFireTeamSelectionDecision.Reject("Arena is unavailable with Fika.");

        if (input.IsCoop)
            return PitFireTeamSelectionDecision.Reject("Arena is unavailable in co-op raids.");

        var current = input.CurrentPlayerAccountId?.Trim();
        if (string.IsNullOrEmpty(current))
            return PitFireTeamSelectionDecision.Reject("Arena could not authenticate the local raid-group member.");

        if (!TryReadIdentitySet(input.NativeGroupAccountIds, out var native, out var nativeProblem))
            return PitFireTeamSelectionDecision.Reject("The native raid group is invalid: " + nativeProblem);

        if (!native.Contains(current))
            return PitFireTeamSelectionDecision.Reject("The native raid group does not contain the local player.");

        var nativeCompanions = native.Where(id => !string.Equals(id, current, StringComparison.Ordinal)).ToArray();

        // An absent or unsupported adapter must remain invisible to a genuine
        // solo raid. A non-solo native group cannot be authenticated as a Pit
        // roster, so it remains closed rather than falling through as Arena.
        if (input.Contract != PitFireTeamContractState.Compatible)
        {
            if (nativeCompanions.Length == 0)
                return PitFireTeamSelectionDecision.Solo();

            return PitFireTeamSelectionDecision.Reject(
                input.Contract == PitFireTeamContractState.Incompatible
                    ? "PitFireTeam team Arena requires PitFireTeam "
                      + PitFireTeamVersions.Describe + " and its supported runtime layout."
                    : "Arena supports only solo play or one to four authenticated PitFireTeam companions.");
        }

        if (!TryReadIdentitySet(input.SelectedPitAccountIds, out var selected, out var selectedProblem))
            return PitFireTeamSelectionDecision.Reject("The PitFireTeam selection is invalid: " + selectedProblem);

        if (!TryReadIdentitySet(
                input.SelectedPitProfileIds,
                out var selectedProfiles,
                out var profileProblem,
                StringComparer.OrdinalIgnoreCase))
            return PitFireTeamSelectionDecision.Reject(
                "The PitFireTeam selected-profile list is invalid: " + profileProblem);

        // MainMenuControllerPatch.GroupPlayers is the companion-only set in
        // PitFireTeam 0.10.0. Treat a defensive copy containing the local AID as
        // corrupt instead of silently deleting it and accepting a different set.
        if (selected.Contains(current))
            return PitFireTeamSelectionDecision.Reject("PitFireTeam returned the local player as a selected companion.");

        if (selected.Count == 0)
        {
            return nativeCompanions.Length == 0 && selectedProfiles.Count == 0
                ? PitFireTeamSelectionDecision.Solo()
                : PitFireTeamSelectionDecision.Reject(
                    "The native raid group contains members that PitFireTeam did not select.");
        }

        if (selectedProfiles.Count != selected.Count)
        {
            return PitFireTeamSelectionDecision.Reject(
                "PitFireTeam returned mismatched companion account and profile identities.");
        }

        if (UnsupportedSpecialProfileIds.Overlaps(selectedProfiles))
        {
            return PitFireTeamSelectionDecision.Reject(
                "PitFireTeam's Knight, Big Pipe and Birdeye special companions are unavailable in Arena.");
        }

        if (!IsSupportedCompanionCount(selected.Count))
        {
            return PitFireTeamSelectionDecision.Reject(
                $"Team Arena requires one to four selected PitFireTeam companions (found {selected.Count}).");
        }

        if (!allowPendingModeSelection && !SupportsMode(input.GameMode))
            return PitFireTeamSelectionDecision.Reject(
                "PitFireTeam companions require Tactical or Zombies Arena.");

        if (nativeCompanions.Length != selected.Count
            || !selected.SetEquals(nativeCompanions))
        {
            return PitFireTeamSelectionDecision.Reject(
                "The native raid group does not exactly match the selected PitFireTeam companions.");
        }

        // Preserve the account-to-profile pairing from each selected RaidPlayer.
        // The runtime body check uses it to reject a stale/wrong profile that
        // happens to reuse an otherwise selected account ID.
        Dictionary<string, string> profilesByAccount = new(StringComparer.Ordinal);
        for (int i = 0; i < input.SelectedPitAccountIds.Count; i++)
        {
            profilesByAccount.Add(
                input.SelectedPitAccountIds[i].Trim(),
                input.SelectedPitProfileIds[i].Trim());
        }

        // Sort the snapshot so logs and comparisons remain deterministic.
        var roster = selected.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        return PitFireTeamSelectionDecision.Team(roster, profilesByAccount);
    }

    internal static bool SupportsMode(string mode)
        => string.Equals(mode, "tactical", StringComparison.OrdinalIgnoreCase)
           || string.Equals(mode, "zombie", StringComparison.OrdinalIgnoreCase);

    private static bool TryReadIdentitySet(
        IReadOnlyList<string> values,
        out HashSet<string> identities,
        out string problem,
        StringComparer comparer = null)
    {
        identities = new HashSet<string>(comparer ?? StringComparer.Ordinal);
        problem = string.Empty;

        if (values == null)
        {
            problem = "the member list is missing.";
            return false;
        }

        foreach (var value in values)
        {
            var identity = value?.Trim();
            if (string.IsNullOrEmpty(identity))
            {
                problem = "a member has no account ID.";
                return false;
            }

            if (!identities.Add(identity))
            {
                problem = $"account ID '{identity}' occurs more than once.";
                return false;
            }
        }

        return true;
    }
}
