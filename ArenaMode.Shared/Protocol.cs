using System;

namespace ArenaMode.Shared;

/// <summary>
/// Route constants shared between ArenaMode.Client and ArenaMode.Server
/// (linked source file, compiled into both assemblies).
/// </summary>
public static class Protocol
{
    public const string NativeStartIdentityPrefix = "arenamode-start:";
    public const string NativeOrdinaryStartIdentityPrefix = "arenamode-ordinary-start:";
    public const string NativeEndIdentitySeparator = "|arenamode:";

    public const string PingRoute = "/arenamode/ping";
    public const string ConfigRoute = "/arenamode/config";
    public const string KitsRoute = "/arenamode/kits";
    public const string LadderRoute = "/arenamode/ladder";

    /// <summary>Cut a fresh ladder mid-raid, for when the player finishes the one they had.</summary>
    public const string LadderRedrawRoute = "/arenamode/ladder/redraw";
    public const string RaidStartRoute = "/arenamode/raid-start";
    public const string RaidCancelRoute = "/arenamode/raid-cancel";
    public const string RaidConfirmRoute = "/arenamode/raid-confirm";
    public const string RaidEndRoute = "/arenamode/raid-end";
    public const string OrdinaryConfirmRoute = "/arenamode/ordinary-confirm";
    public const string OrdinaryRecoveryRoute = "/arenamode/ordinary-recover";
    public const string StatusRoute = "/arenamode/status";

    /// <summary>The client uploads its own BepInEx settings here so the config page can show them.</summary>
    public const string ClientSettingsPushRoute = "/arenamode/client-settings/push";

    /// <summary>The client asks here whether the page changed anything since it last looked.</summary>
    public const string ClientSettingsRoute = "/arenamode/client-settings";

    public static string BuildNativeStartIdentity(string armToken)
        => NativeStartIdentityPrefix + armToken;

    public static string BuildNativeOrdinaryStartIdentity(
        string processId,
        string requestId)
        => NativeOrdinaryStartIdentityPrefix + processId + ":" + requestId;

    public static string BuildNativeEndIdentity(string nativeServerId, string armToken)
        => nativeServerId + NativeEndIdentitySeparator + armToken;

    public static bool IsNativeStartIdentity(string candidate, string armToken)
        => !string.IsNullOrWhiteSpace(candidate)
           && !string.IsNullOrWhiteSpace(armToken)
           && string.Equals(
               candidate,
               BuildNativeStartIdentity(armToken),
               StringComparison.Ordinal);

    public static bool IsNativeOrdinaryStartIdentity(string candidate)
        => TryParseNativeOrdinaryStartIdentity(candidate, out _, out _);

    public static bool TryParseNativeOrdinaryStartIdentity(
        string candidate,
        out string processId,
        out string requestId)
    {
        processId = string.Empty;
        requestId = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate)
            || !candidate.StartsWith(
                NativeOrdinaryStartIdentityPrefix,
                StringComparison.Ordinal))
        {
            return false;
        }

        string[] parts = candidate[NativeOrdinaryStartIdentityPrefix.Length..]
            .Split(':');
        if (parts.Length != 2
            || !IsLifecycleRequestId(parts[0])
            || !IsLifecycleRequestId(parts[1]))
        {
            return false;
        }

        processId = parts[0];
        requestId = parts[1];
        return true;
    }

    public static bool HasNativeOrdinaryStartMarker(string candidate)
        => !string.IsNullOrWhiteSpace(candidate)
           && candidate.TrimStart().StartsWith(
               "arenamode-ordinary",
               StringComparison.OrdinalIgnoreCase);

    public static bool IsLifecycleRequestId(string candidate)
        => !string.IsNullOrWhiteSpace(candidate)
           && Guid.TryParseExact(candidate, "N", out _);

    public static bool HasNativeStartMarker(string candidate)
        => !string.IsNullOrWhiteSpace(candidate)
           && candidate.TrimStart().StartsWith(
               "arenamode",
               StringComparison.OrdinalIgnoreCase);

    public static bool HasNativeEndIdentity(string candidate, string armToken)
        => !string.IsNullOrWhiteSpace(candidate)
           && !string.IsNullOrWhiteSpace(armToken)
           && candidate.EndsWith(
               NativeEndIdentitySeparator + armToken,
               StringComparison.Ordinal);

    /// <summary>
    /// Exact Arena local-end tag. Substring "arenamode" would also match
    /// <c>arenamode-start:</c> and <c>arenamode-ordinary-start:</c>, which are
    /// start identities, not end transactions.
    /// </summary>
    public static bool HasNativeEndMarker(string candidate)
        => !string.IsNullOrWhiteSpace(candidate)
           && candidate.Contains(
               NativeEndIdentitySeparator,
               StringComparison.OrdinalIgnoreCase);
}
