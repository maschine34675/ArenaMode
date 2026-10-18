using System;
using System.Collections.Generic;
using ArenaMode.Shared;
using BepInEx.Configuration;

namespace ArenaMode.Config;

/// <summary>Map-screen preferences. A running raid reads its frozen session contract.</summary>
internal static class ArenaParticipantSettings
{
    private static readonly Dictionary<string, ConfigEntry<int>> Entries = new(StringComparer.OrdinalIgnoreCase);
    private static ConfigFile _file;

    internal static void Init(ConfigFile file)
    {
        _file = file ?? throw new ArgumentNullException(nameof(file));
        Entries.Clear();
        foreach (var map in ArenaMapCatalog.DefaultClientAllowlist.Split(','))
        foreach (var mode in new[] { "deathmatch", "teamdeathmatch" })
        {
            var key = Key(map, mode);
            Entries.Add(key, file.BindArena("Map participants", key, ArenaParticipantPolicy.DefaultTotal(map, mode),
                new ConfigDescription("Total participants including your PMC. Select on the Arena map screen. "
                    + "Deathmatch and Gun Game share a count; independent Team Deathmatch has its own even count.",
                    null)));
        }
    }

    internal static int Get(string map, string mode)
    {
        if (!Supports(map, mode)) return 0;
        return Entries.TryGetValue(Key(map, mode), out var entry)
            && ArenaParticipantPolicy.TryResolve(map, mode, false, false, entry.Value, out _, out _)
            ? entry.Value : ArenaParticipantPolicy.DefaultTotal(map, mode);
    }

    internal static bool TrySelect(string map, string mode, int total)
    {
        if (Session.ArenaSession.IsActive || Session.ArenaSession.ArmedForNextRaid
            || !Supports(map, mode) || _file == null
            || !Entries.TryGetValue(Key(map, mode), out var entry)
            || !ArenaParticipantPolicy.TryResolve(map, mode, false, false, total, out _, out _)) return false;

        // Explicitly save even when the host has disabled SaveOnConfigSet. Do
        // not acknowledge an in-memory change whose backing file was refused.
        int previous = entry.Value;
        bool automaticSave = _file.SaveOnConfigSet;
        try
        {
            _file.SaveOnConfigSet = false;
            entry.Value = total;
            _file.Save();
            return entry.Value == total;
        }
        catch (Exception ex)
        {
            entry.Value = previous;
            Plugin.ModLogger?.LogWarning($"Arena participant count could not be saved: {ex.Message}");
            return false;
        }
        finally { _file.SaveOnConfigSet = automaticSave; }
    }

    private static bool Supports(string map, string mode)
        => ArenaMapCatalog.IsSupportedMap(map) && (ArenaParticipantPolicy.IsSolo(mode) || ArenaParticipantPolicy.IsTeam(mode));

    private static string Key(string map, string mode)
        => map.ToLowerInvariant() + (ArenaParticipantPolicy.IsTeam(mode) ? "_team" : "_solo");
}
