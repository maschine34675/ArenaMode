using System.Text.Json;
using ArenaMode.Shared;

namespace ArenaMode.Server.Services;

/// <summary>
/// A mirror of the client's own BepInEx settings, kept only so both halves of
/// the mod can be edited on one page.
///
/// The client stays the owner. This holds what it last reported plus, after an
/// edit here, what it should change to — and a revision number. The client
/// applies the desired values only when that number CHANGES, which is what
/// keeps an F12 edit from being silently reverted the next time the client
/// looks: the page is a remote control, not a second source of truth.
/// </summary>
public static class ClientSettingsStore
{
    private static readonly object Gate = new();
    private static ClientSettingsSchema _schema = new();

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private static string File_ => Path.Combine(ConfigStore.ConfigDir, "client-settings.json");

    /// <summary>What the page renders. Never handed out by reference.</summary>
    public static ClientSettingsSchema Snapshot()
    {
        lock (Gate)
            return Clone(_schema);
    }

    /// <summary>What the client asks for: the desired values and the revision they belong to.</summary>
    public static string Serialize()
    {
        lock (Gate)
            return JsonSerializer.Serialize(_schema);
    }

    /// <summary>
    /// The client reporting what it actually has. Keeps the revision, because
    /// the client is describing reality, not asking for a change — and bumping
    /// here would make every upload look like a fresh instruction to apply.
    /// </summary>
    public static void AcceptFromClient(ClientSettingsSchema? uploaded)
    {
        if (uploaded == null)
            return;

        lock (Gate)
        {
            var revision = _schema.Revision;
            _schema = Clone(uploaded);
            _schema.Revision = revision;
            _schema.Known = _schema.Entries.Count > 0;
            _schema.UpdatedAt = DateTime.Now.ToString("HH:mm:ss");
            Persist();
        }
    }

    /// <summary>An edit from the page. Bumping the revision IS the instruction to apply.</summary>
    public static void SaveFromPage(List<ClientSettingEntry> entries)
    {
        lock (Gate)
        {
            _schema.Entries = entries.Select(Clone).ToList();
            _schema.Revision++;
            Persist();
        }
    }

    public static void LoadFromDisk()
    {
        lock (Gate)
        {
            try
            {
                if (!System.IO.File.Exists(File_))
                    return;

                var loaded = JsonSerializer.Deserialize<ClientSettingsSchema>(
                    System.IO.File.ReadAllText(File_), ReadOptions);

                if (loaded != null)
                    _schema = loaded;
            }
            catch
            {
                // A corrupt mirror is not worth failing the mod load over: the
                // client re-uploads the truth the next time it starts.
            }
        }
    }

    private static void Persist()
    {
        try
        {
            if (string.IsNullOrEmpty(ConfigStore.ConfigDir))
                return;

            Directory.CreateDirectory(ConfigStore.ConfigDir);
            System.IO.File.WriteAllText(File_, JsonSerializer.Serialize(_schema, WriteOptions));
        }
        catch
        {
            // Losing the mirror costs nothing that the next client upload does
            // not restore, so it must never take the request down with it.
        }
    }

    private static ClientSettingsSchema Clone(ClientSettingsSchema source) => new()
    {
        Revision = source.Revision,
        Known = source.Known,
        UpdatedAt = source.UpdatedAt,
        Entries = source.Entries.Select(Clone).ToList(),
    };

    private static ClientSettingEntry Clone(ClientSettingEntry source) => new()
    {
        Section = source.Section,
        Key = source.Key,
        Description = source.Description,
        Type = source.Type,
        Value = source.Value,
        Default = source.Default,
        Min = source.Min,
        Max = source.Max,
        Options = source.Options?.ToList(),
    };
}
