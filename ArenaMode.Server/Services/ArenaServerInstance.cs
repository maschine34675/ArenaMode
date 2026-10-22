using System.Text.Json;
using System.Text.Json.Serialization;
using ArenaMode.Shared.Session;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;

namespace ArenaMode.Server.Services;

/// <summary>
/// One loaded server mod as the ping route reports it (spec section 8): the
/// client evaluates these against the Arena whitelist and hands the folders of
/// the allowed ones to the session helper, which copies them into the disposable
/// Arena server tree.
/// </summary>
/// <remarks>
/// <paramref name="Dependencies"/> are the GUIDs the mod lists in
/// <c>IModMetadata.ModDependencies</c>. SPT's mod validator treats an unmet entry
/// as fatal for every mod, not just for the one that lists it, so the client needs
/// them to keep a dependency of an allowed mod in the session tree.
/// </remarks>
internal sealed record ArenaServerModInfo(
    [property: JsonPropertyName("guid")] string Guid,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("folder")] string Folder,
    [property: JsonPropertyName("dependencies")] IReadOnlyList<string> Dependencies);

/// <summary>
/// What this server process knows about the Arena session feature: whether it is
/// the Arena instance (the disposable tree the session helper starts), the policy
/// hash the helper recorded in that tree's marker and which mods it loaded. All
/// three are fixed for the life of the process, so they are read once from
/// <see cref="Initialize"/>, not per request.
/// </summary>
/// <remarks>
/// A normal server never has the marker file, so for it everything here is inert
/// apart from the two extra ping fields. See docs/ARENA-SESSION-20261021.md,
/// section 8.
/// </remarks>
internal static class ArenaServerInstance
{
    // The ordinary-start refusal travels as SPT's own error body, not as an
    // exception. The EFT client retries every failure it classifies as "Default"
    // (an HTTP 500 from a faulted task, or an err code it does not map) after 20,
    // 40 and then 60 seconds, five times: that is minutes of apparent hang. Codes
    // ClientRequestErrorHandler.HandleError reports as "Expected" are not retried;
    // the start then fails at once and the client goes back to the main menu with
    // its standard "matching aborted" screen. UnknownScriptExecutionError is one of
    // those and, unlike the item or quest codes in that group, carries no meaning
    // of its own that the player could misread.
    internal const BackendErrorCodes OrdinaryStartRefusalCode = BackendErrorCodes.UnknownScriptExecutionError;

    internal const string OrdinaryStartRefusal =
        "Arena session: only Arena raids can be started on this server. "
        + "Use RETURN TO NORMAL SESSION in the Arena menu to play ordinary PMC or Scav raids.";

    private sealed record State(bool ArenaInstance, IReadOnlyList<ArenaServerModInfo> Mods, string? PolicyHash);

    private static volatile State _state = new(false, [], null);

    /// <summary>True when <c>arena-session.json</c> existed in the working directory at startup.</summary>
    internal static bool IsArenaInstance => _state.ArenaInstance;

    /// <summary>
    /// The <c>policyHash</c> member of the marker, read once at startup. Null on a normal server
    /// and on an Arena instance whose marker has no usable hash (see <see cref="ReadPolicyHash"/>).
    /// The ping reports it so the client can tell which policy the running server tree was built
    /// for.
    /// </summary>
    internal static string? PolicyHash => _state.PolicyHash;

    /// <summary>Every loaded server mod, in the server's own load order (GUID, ordinal ignoring case).</summary>
    internal static IReadOnlyList<ArenaServerModInfo> Mods => _state.Mods;

    /// <summary>
    /// Reads the marker (existence and policy hash) and snapshots the loaded mods.
    /// <paramref name="workingDirectory"/> defaults to the process working directory,
    /// which is where SPT resolves <c>user/mods</c> and where the session helper puts
    /// the marker. A normal server has no marker, so its hash is null without any file
    /// access beyond the existence check.
    /// </summary>
    internal static void Initialize(IEnumerable<SptMod>? loadedMods, string? workingDirectory = null)
    {
        string directory = workingDirectory ?? Directory.GetCurrentDirectory();
        bool arena = HasMarker(directory);
        _state = new State(arena, Describe(loadedMods), arena ? ReadPolicyHash(directory) : null);
    }

    /// <summary>True when the marker file exists directly in <paramref name="workingDirectory"/>.</summary>
    internal static bool HasMarker(string? workingDirectory)
        => !string.IsNullOrEmpty(workingDirectory)
           && File.Exists(Path.Combine(workingDirectory, ArenaSessionContract.MarkerFileName));

    /// <summary>
    /// The <c>policyHash</c> string of the marker in <paramref name="workingDirectory"/>, or null
    /// when there is nothing usable to report: no marker, a marker that cannot be read (locked,
    /// removed meanwhile, access denied), text that is not one JSON object, a missing, JSON-null,
    /// non-string, empty or blank <c>policyHash</c>. The value is returned as written, never
    /// trimmed or normalised, because the client compares it with the hash of its own policy.
    /// Never throws.
    /// </summary>
    internal static string? ReadPolicyHash(string? workingDirectory)
    {
        if (string.IsNullOrEmpty(workingDirectory))
            return null;

        string text;
        try
        {
            text = File.ReadAllText(Path.Combine(workingDirectory, ArenaSessionContract.MarkerFileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                       or ArgumentException or System.Security.SecurityException)
        {
            return null;
        }

        // GetString is null for anything but an object that has a string member of that name.
        if (!ArenaMiniJson.TryParse(text, out object? root, out _))
            return null;

        string? hash = ArenaMiniJson.GetString(root, "policyHash");
        return string.IsNullOrWhiteSpace(hash) ? null : hash;
    }

    /// <summary>
    /// GUID, name, version and absolute folder of every mod. SPT records a mod's
    /// directory as the path it enumerated (<c>./user/mods/&lt;folder&gt;</c>, relative
    /// to the working directory), so the folder is resolved against it. A folder
    /// that cannot be resolved is reported as an empty string, never as a relative
    /// path the helper might resolve against its own directory.
    /// </summary>
    internal static IReadOnlyList<ArenaServerModInfo> Describe(IEnumerable<SptMod>? loadedMods)
    {
        var mods = new List<ArenaServerModInfo>();
        if (loadedMods == null)
            return mods;

        foreach (SptMod? mod in loadedMods)
        {
            IModMetadata? metadata = mod?.ModMetadata;
            if (mod == null || metadata == null)
                continue;

            mods.Add(new ArenaServerModInfo(
                metadata.ModGuid ?? string.Empty,
                metadata.Name ?? string.Empty,
                metadata.Version?.ToString() ?? string.Empty,
                ResolveFolder(mod.Directory),
                DescribeDependencies(metadata)));
        }

        return mods;
    }

    /// <summary>The GUIDs of <c>ModDependencies</c>, sorted ordinally so the ping is stable; never null.</summary>
    private static string[] DescribeDependencies(IModMetadata metadata)
        => metadata.ModDependencies == null
            ? []
            : metadata.ModDependencies.Keys
                .Where(guid => !string.IsNullOrWhiteSpace(guid))
                .Order(StringComparer.Ordinal)
                .ToArray();

    private static string ResolveFolder(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return string.Empty;

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// True when a local raid start must be refused: this is the Arena instance
    /// and the start is not an Arena-tagged one. Arena-tagged starts (the protected
    /// transaction) are never touched by this.
    /// </summary>
    internal static bool RefusesStart(bool arenaTagged) => IsArenaInstance && !arenaTagged;

    /// <summary>
    /// The response body for a refused start, in SPT's <c>{err, errmsg, data}</c>
    /// shape. See <see cref="OrdinaryStartRefusalCode"/> for why this is a body and
    /// not a thrown exception.
    /// </summary>
    internal static string BuildOrdinaryStartRefusalBody()
        => JsonSerializer.Serialize(new
        {
            err = (int)OrdinaryStartRefusalCode,
            errmsg = OrdinaryStartRefusal,
            data = (object?)null,
        });

    /// <summary>
    /// The single startup line of an Arena instance, or null on a normal server.
    /// </summary>
    internal static string? StartupLine()
    {
        State state = _state;
        if (!state.ArenaInstance)
            return null;

        string names = string.Join(", ", state.Mods.Select(m => m.Name.Length > 0 ? m.Name : m.Guid));
        return $"Arena session server: {state.Mods.Count} mods loaded ({names})";
    }

    /// <summary>
    /// The warning of an Arena instance whose marker gave no policy hash, or null when the hash is
    /// there or this is a normal server. The ping then reports <c>policyHash</c> as JSON null and the
    /// client cannot confirm which policy this server tree was built for.
    /// </summary>
    internal static string? MissingPolicyHashWarning()
    {
        State state = _state;
        return state.ArenaInstance && state.PolicyHash == null
            ? $"Arena session server: {ArenaSessionContract.MarkerFileName} has no readable policyHash; "
              + "the client cannot confirm which policy this server was built for."
            : null;
    }

    /// <summary>
    /// Writes <see cref="StartupLine"/> at Info, followed by <see cref="MissingPolicyHashWarning"/>
    /// at Warning when it applies; nothing at all on a normal server.
    /// </summary>
    internal static void LogStartup(ISptLogger<PatchManager> logger)
    {
        if (StartupLine() is { } line)
            logger.Info($"[ArenaMode] {line}");
        if (MissingPolicyHashWarning() is { } warning)
            logger.Warning($"[ArenaMode] {warning}");
    }
}
