namespace ArenaMode.Server.Rewards;

/// <summary>
/// The reward card's sentence for a start whose reward step did not run, chosen from the reason code that
/// <see cref="ArenaRewardStartup"/> recorded, and the same sentence in the private Arena session, where the
/// session lease carries the code (<see cref="RewardOfflineLease.AuthorityUnavailable"/>). "Temporarily
/// unavailable" is reserved for what a plain restart may clear (another server process, an unexpected
/// exception); everything that recurs at every start as long as the installation stays as it is says so,
/// because waiting does not help and the server log names the cause (maschine34675/ArenaMode#1: a refusal
/// that recurred at every start was shown as temporary, and the Arena session's card as "Activation
/// pending", for as long as the other mod was installed).
/// </summary>
internal static class RewardUnavailableText
{
    internal const string Temporary = "Rewards are temporarily unavailable.";
    internal const string RecordsMismatch = "Rewards are off for this profile: its reward records no longer match it or this installation. See the server log.";
    internal const string StorageUnusable = "Rewards are off: this installation's reward folder cannot be used. See the server log.";
    internal const string EarlierCallback = "Rewards are off: another mod's startup step runs before Arena's reward step. See the server log.";
    internal const string UnreviewedStart = "Rewards are off: this SPT server version or start is not reviewed for rewards. See the server log.";
    internal const string ProfilesLoadedEarly = "Rewards are off: another mod loads the profiles before Arena's reward step. See the server log.";

    // The lease stores the code with its origin, because the entry's own read of a profile's reward authority and the
    // start's refusal share no vocabulary: "startup:earlier-startup-callback", "entry:runtime-enrollment-origin-mismatch".
    internal const string StartupScope = "startup:", EntryScope = "entry:";
    private const int MaxCodeLength = 120;
    private const string ServerLog = "See the server log.", NormalServerLog = "See the normal server's log.";

    /// <summary>
    /// The reason codes of ArenaRewardStartup that recur at every start until the installation changes.
    /// "another-server-or-unknown-process" is not listed although FindConflictingServer also reports it for a server
    /// started from an image it does not recognise (not SPT.Server*.exe in the root) or a peer process it cannot
    /// identify, which recur too: the common case is a second server that was left running, and the log names the
    /// code either way.
    /// </summary>
    internal static bool Recurs(string reason) => reason is "earlier-startup-callback" or "startup-callback-unlisted"
        or "unreviewed-native-startup-version" or "not-native-server-startup" or "runtime-root-mismatch"
        or "native-profiles-already-loaded"
        or "reward-storage-linked" or "reward-storage-access-denied" or "reward-storage-unavailable";

    internal static string ForStartupReason(string reason) => reason switch
    {
        "earlier-startup-callback" or "startup-callback-unlisted" => EarlierCallback,
        "unreviewed-native-startup-version" or "not-native-server-startup" or "runtime-root-mismatch" => UnreviewedStart,
        "native-profiles-already-loaded" => ProfilesLoadedEarly,
        "reward-storage-linked" or "reward-storage-access-denied" or "reward-storage-unavailable" => StorageUnusable,
        _ => Temporary,
    };

    // The entry's read of the profile's reward records (RewardOfflineSession.ReadEntryAuthority): a sharing violation
    // or denied access after the stop may be a scanner holding the file for a moment (MayClearOnItsOwn), so that is no
    // mismatch; everything else it reports (a missing or damaged enrollment, a ledger that left the profile, a root
    // that is not this installation's) is one.
    internal static string ForEntryReason(string reason) => reason is "IOException" or "UnauthorizedAccessException" ? Temporary : RecordsMismatch;

    /// <summary>
    /// The sentence for a session lease's reason, or null for a lease that carries none or one of an unknown shape. It is
    /// spoken in the private session, whose own server log holds nothing about it: the cause is in the log of the normal
    /// server's start (the refusal) or of the entry, so the sentence points there.
    /// </summary>
    internal static string? ForLeaseReason(string? scoped)
    {
        if (scoped is null) return null;
        string? sentence = scoped.StartsWith(StartupScope, StringComparison.Ordinal) ? ForStartupReason(scoped.Substring(StartupScope.Length))
            : scoped.StartsWith(EntryScope, StringComparison.Ordinal) ? ForEntryReason(scoped.Substring(EntryScope.Length))
            : null;
        return sentence?.Replace(ServerLog, NormalServerLog, StringComparison.Ordinal);
    }

    /// <summary>
    /// The code as the lease carries it: the leading run of the characters <see cref="IsLeaseReason"/> allows, at most 120
    /// of them, "unknown" when the reason starts with none ("cola-unpaid-plan-invalid: IOException", the shape of a wrapped
    /// RewardDataException, becomes "cola-unpaid-plan-invalid"). A reason that does not fit must never make the lease
    /// invalid: Validate runs inside the entry preflight, where an invalid lease declines the Arena entry.
    /// </summary>
    internal static string LeaseCode(string reason)
    {
        int n = 0;
        while (n < reason.Length && n < MaxCodeLength && IsCodeChar(reason[n])) n++;
        return n == 0 ? "unknown" : reason.Substring(0, n);
    }

    /// <summary>A scoped reason as the lease may carry it: one of the two scopes, then 1 to 120 characters of a reason code or exception type name.</summary>
    internal static bool IsLeaseReason(string value)
    {
        string? code = value.StartsWith(StartupScope, StringComparison.Ordinal) ? value.Substring(StartupScope.Length)
            : value.StartsWith(EntryScope, StringComparison.Ordinal) ? value.Substring(EntryScope.Length) : null;
        if (code is null || code.Length is 0 or > MaxCodeLength) return false;
        foreach (char c in code)
            if (!IsCodeChar(c)) return false;
        return true;
    }

    private static bool IsCodeChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.';
}
