using System.Text.Json;
using System.Text.Json.Nodes;
using ArenaMode.Shared;

namespace ArenaMode.Server.Services;

public enum ConfigResult { Success, Busy, Failure }

/// <summary>
/// The one place that reads and writes the mod's config files at runtime, so
/// the web page can change them without the server being restarted.
///
/// That this works at all is a property of the code around it rather than of
/// this class: every consumer of ArenaContext.Config — the location mutator,
/// the hostility patch, the bot cap, the tier ramp, the router and the session
/// service — reads it at the moment it needs it, per raid or per request. None
/// of them snapshots it at load. So reassigning the reference here is the whole
/// mechanism; the next raid picks it up. Values already baked into a running
/// raid's location clone do NOT change, and cannot: the clone is fixed for the
/// raid's duration by design.
/// </summary>
public static class ConfigStore
{
    private static readonly object Gate = new();
    private static string _configDir = string.Empty;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static string ConfigDir => _configDir;

    /// <summary>Set once by ConfigLoader, which knows the mod folder.</summary>
    public static void Initialize(string configDir) => _configDir = configDir;

    public static string ConfigFile => Path.Combine(_configDir, "config.json");

    /// <summary>
    /// Reads the file, topping up anything a newer version of the mod added, and
    /// writes the repaired file back so the config page shows the same thing the
    /// server is running on.
    /// </summary>
    private static ArenaModeConfig? Deserialize(string json)
    {
        var saved = JsonNode.Parse(json);
        var defaults = JsonSerializer.SerializeToNode(new ArenaModeConfig(), WriteOptions);

        if (FillMissingKeys(saved, defaults))
        {
            try
            {
                File.WriteAllText(ConfigFile, saved!.ToJsonString(WriteOptions));
            }
            catch
            {
                // A read-only config still works — the values are already merged
                // in memory. Only the file stays behind.
            }
        }

        return saved.Deserialize<ArenaModeConfig>(ReadOptions);
    }
    private static string KitsFile => Path.Combine(_configDir, "kits.json");
    private static string LadderFile => Path.Combine(_configDir, "ladder.json");

    /// <summary>Re-reads every config file from disk, discarding unsaved edits.</summary>
    public static ConfigResult Reload(out string message)
    {
        lock (Gate)
        {
            try
            {
                if (string.IsNullOrEmpty(_configDir))
                {
                    message = "Config directory unknown — the mod has not finished loading.";
                    return ConfigResult.Failure;
                }

                var config = File.Exists(ConfigFile)
                    ? Deserialize(File.ReadAllText(ConfigFile))
                    : new ArenaModeConfig();

                if (config == null)
                {
                    message = "config.json could not be parsed — nothing was changed.";
                    return ConfigResult.Failure;
                }

                ArenaContext.Config = config;

                if (File.Exists(KitsFile))
                    ArenaContext.KitsJson = File.ReadAllText(KitsFile);

                if (File.Exists(LadderFile))
                    ArenaContext.LadderJson = File.ReadAllText(LadderFile);

                message = $"Reloaded from disk — {config.Maps.Count} map(s).";
                return ConfigResult.Success;
            }
            catch (Exception ex)
            {
                message = $"Reload failed: {ex.Message}";
                return ConfigResult.Failure;
            }
        }
    }

    /// <summary>
    /// Writes the edited config and installs it. Written first, installed second:
    /// a failed write must not leave the running server on a config that is not
    /// on disk, because the next reload would silently undo it.
    /// </summary>
    public static ConfigResult Save(ArenaModeConfig config, out string message)
    {
        lock (Gate)
        {
            try
            {
                if (string.IsNullOrEmpty(_configDir))
                {
                    message = "Config directory unknown — the mod has not finished loading.";
                    return ConfigResult.Failure;
                }

                Directory.CreateDirectory(_configDir);
                File.WriteAllText(ConfigFile, JsonSerializer.Serialize(config, WriteOptions));
                ArenaContext.Config = config;

                message = "Saved. It takes effect on the next raid — a raid already running keeps the settings it started with.";
                return ConfigResult.Success;
            }
            catch (Exception ex)
            {
                message = $"Save failed: {ex.Message}";
                return ConfigResult.Failure;
            }
        }
    }

    /// <summary>
    /// Fills in keys the saved file has never heard of, and reports whether it
    /// had to.
    ///
    /// Deserialising alone cannot do this. A field absent from the JSON gets the
    /// C# default, which is right for a top-level setting and WRONG for a
    /// per-map one: the Maps dictionary is replaced wholesale by whatever the
    /// file holds, so a map section written before a field existed silently
    /// keeps the generic default instead of that map's override. The Lab's
    /// hunt-sweep values were the case that surfaced it — an installed config
    /// left the Lab on Factory's numbers with nothing to indicate it.
    ///
    /// Only ADDS. A value the user has set is never touched, including one that
    /// happens to equal the default.
    /// </summary>
    private static bool FillMissingKeys(JsonNode? saved, JsonNode? defaults)
    {
        if (saved is not JsonObject savedObject || defaults is not JsonObject defaultObject)
            return false;

        var added = false;

        foreach (var (key, value) in defaultObject)
        {
            if (!savedObject.TryGetPropertyValue(key, out var existing))
            {
                savedObject[key] = value?.DeepClone();
                added = true;
                continue;
            }

            if (FillMissingKeys(existing, value))
                added = true;
        }

        return added;
    }

    /// <summary>A detached copy, so the editor cannot half-apply itself to the live config.</summary>
    public static ArenaModeConfig Copy()
    {
        try
        {
            return JsonSerializer.Deserialize<ArenaModeConfig>(
                JsonSerializer.Serialize(ArenaContext.Config), ReadOptions) ?? new ArenaModeConfig();
        }
        catch
        {
            return new ArenaModeConfig();
        }
    }
}
