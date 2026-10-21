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
internal sealed record ArenaServerModInfo(
    [property: JsonPropertyName("guid")] string Guid,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("folder")] string Folder);

/// <summary>
/// What this server process knows about the Arena session feature: whether it is
/// the Arena instance (the disposable tree the session helper starts) and which
/// mods it loaded. Both are fixed for the life of the process, so they are read
/// once from <see cref="Initialize"/>, not per request.
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

    private sealed record State(bool ArenaInstance, IReadOnlyList<ArenaServerModInfo> Mods);

    private static volatile State _state = new(false, []);

    /// <summary>True when <c>arena-session.json</c> existed in the working directory at startup.</summary>
    internal static bool IsArenaInstance => _state.ArenaInstance;

    /// <summary>Every loaded server mod, in the server's own load order (GUID, ordinal ignoring case).</summary>
    internal static IReadOnlyList<ArenaServerModInfo> Mods => _state.Mods;

    /// <summary>
    /// Reads the marker and snapshots the loaded mods. <paramref name="workingDirectory"/>
    /// defaults to the process working directory, which is where SPT resolves
    /// <c>user/mods</c> and where the session helper puts the marker.
    /// </summary>
    internal static void Initialize(IEnumerable<SptMod>? loadedMods, string? workingDirectory = null)
    {
        bool arena = HasMarker(workingDirectory ?? Directory.GetCurrentDirectory());
        _state = new State(arena, Describe(loadedMods));
    }

    /// <summary>True when the marker file exists directly in <paramref name="workingDirectory"/>.</summary>
    internal static bool HasMarker(string? workingDirectory)
        => !string.IsNullOrEmpty(workingDirectory)
           && File.Exists(Path.Combine(workingDirectory, ArenaSessionContract.MarkerFileName));

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
                ResolveFolder(mod.Directory)));
        }

        return mods;
    }

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

    /// <summary>Writes <see cref="StartupLine"/> at Info; nothing at all on a normal server.</summary>
    internal static void LogStartup(ISptLogger<PatchManager> logger)
    {
        if (StartupLine() is { } line)
            logger.Info($"[ArenaMode] {line}");
    }
}
