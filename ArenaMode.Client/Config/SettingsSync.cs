using ArenaMode.Session;
using ArenaMode.Shared;
using BepInEx.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ArenaMode.Config;

/// <summary>
/// Puts the in-game settings on the server's config page, and lets that page
/// change them back.
///
/// The client stays the owner. It reports what it has; the page reports what it
/// WANTS, carried by a revision number. Values are applied only when that number
/// changes — never on every poll — because a page that pushed continuously would
/// silently revert any F12 edit made after the last page edit. As written, the
/// last button pressed wins, which is what a person expects of a remote control.
///
/// Every value travels as the string BepInEx itself would write to the .cfg
/// (GetSerializedValue / SetSerializedValue). That is not laziness about types:
/// it is the one representation that already round-trips every type the config
/// can hold — enums, floats under an invariant culture, and KeyboardShortcut —
/// without this file having to know about any of them.
/// </summary>
internal static class SettingsSync
{
    /// <summary>Not yet applied anything. -1 rather than 0 so a fresh server's revision 0 still counts as seen.</summary>
    private static int _appliedRevision = -1;

    private static float _nextPollAt;
    private static bool _reportedOnce;

    private const float MenuPollSeconds = 10f;

    /// <summary>
    /// Called every frame by ArenaController. Polls only OUTSIDE a raid: in a
    /// raid the settings have already been read for this life, and a synchronous
    /// HTTP call on the render thread is not something to do while shooting.
    /// </summary>
    public static void Tick()
    {
        if (ArenaSession.IsActive || Time.realtimeSinceStartup < _nextPollAt)
            return;

        _nextPollAt = Time.realtimeSinceStartup + MenuPollSeconds;

        // KnownAvailable, not IsServerModAvailable: the latter probes and then
        // latches its answer forever. Letting a menu poll be the first to ask
        // would disable arena mode for the whole session if the game happened to
        // start before the server. Raid start does the probing; this only rides
        // along once it has succeeded.
        if (!ArenaServerClient.IsServerModKnownAvailable)
            return;

        Sync();
    }

    /// <summary>
    /// Called at raid start, where the server is being talked to anyway. This is
    /// the moment the page's promise — "applies at the start of the next raid" —
    /// has to come true even if the menu poll never ran.
    /// </summary>
    public static void SyncNow() => Sync();

    private static void Sync()
    {
        try
        {
            var desired = ArenaServerClient.GetClientSettings();
            var applied = desired != null && ApplyIfNewer(desired);

            // Report after applying, never before: otherwise the page would be
            // told the old values in the same breath as they were replaced, and
            // would show them until the next poll.
            if (applied || !_reportedOnce)
            {
                Report();
                _reportedOnce = true;
            }
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena settings sync failed: {ex.Message}");
        }
    }

    private static bool ApplyIfNewer(ClientSettingsSchema desired)
    {
        if (desired.Entries == null || desired.Entries.Count == 0)
            return false;

        // First contact: adopt the revision without applying. The server's stored
        // values are whatever this client last uploaded, so applying them would
        // be a no-op at best — and at worst would undo an F12 edit made while the
        // game was running with no server contact.
        if (_appliedRevision < 0)
        {
            _appliedRevision = desired.Revision;
            return false;
        }

        if (desired.Revision == _appliedRevision)
            return false;

        var live = Entries();
        var changed = 0;

        foreach (var wanted in desired.Entries)
        {
            if (!live.TryGetValue(wanted.Id, out var entry))
                continue;

            try
            {
                var current = entry.GetSerializedValue();
                if (string.Equals(current, wanted.Value, StringComparison.Ordinal))
                    continue;

                // SetSerializedValue clamps against the entry's own
                // AcceptableValueRange, so the page cannot push a value out of
                // range even by hand-typing one.
                entry.SetSerializedValue(wanted.Value);
                changed++;
            }
            catch (Exception ex)
            {
                Plugin.ModLogger.LogWarning($"Arena settings: '{wanted.Id}' rejected '{wanted.Value}': {ex.Message}");
            }
        }

        _appliedRevision = desired.Revision;

        if (changed > 0)
        {
            Settings.File.Save();
            Plugin.ModLogger.LogInfo($"Arena settings: applied {changed} change(s) from the config page (revision {desired.Revision}).");
        }

        return true;
    }

    /// <summary>Uploads what this client currently has, so the page shows the truth.</summary>
    public static void Report()
    {
        try
        {
            var schema = new ClientSettingsSchema
            {
                Known = true,
                Revision = _appliedRevision,
                UpdatedAt = DateTime.Now.ToString("HH:mm:ss"),
                Entries = Entries().Values.Select(Describe).OrderBy(e => e.Section).ThenBy(e => e.Key).ToList(),
            };

            ArenaServerClient.PostClientSettings(schema);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena settings: could not report to the server: {ex.Message}");
        }
    }

    private static Dictionary<string, ConfigEntryBase> Entries()
    {
        var entries = new Dictionary<string, ConfigEntryBase>(StringComparer.Ordinal);

        if (Settings.File == null)
            return entries;

        foreach (var pair in Settings.File)
            entries[$"{pair.Key.Section}/{pair.Key.Key}"] = pair.Value;

        return entries;
    }

    private static ClientSettingEntry Describe(ConfigEntryBase entry)
    {
        var described = new ClientSettingEntry
        {
            Section = entry.Definition.Section,
            Key = entry.Definition.Key,
            Description = entry.Description?.Description ?? string.Empty,
            Type = TypeOf(entry),
            Value = Serialized(entry, entry.BoxedValue),
            Default = Serialized(entry, entry.DefaultValue),
        };

        switch (entry.Description?.AcceptableValues)
        {
            case AcceptableValueList<string> list:
                described.Options = list.AcceptableValues.ToList();
                break;

            case AcceptableValueBase acceptable:
                // Ranges are typed (int, float, ...) and there is no non-generic
                // way in, so the min/max come out of the description string the
                // acceptable-value object already knows how to write. It reads
                // "Acceptable value range: From 1 to 2.5".
                var text = acceptable.ToDescriptionString();
                var from = text.IndexOf("From ", StringComparison.Ordinal);
                var to = text.IndexOf(" to ", StringComparison.Ordinal);
                if (from >= 0 && to > from)
                {
                    described.Min = text.Substring(from + 5, to - from - 5).Trim();
                    described.Max = text.Substring(to + 4).Trim();
                }
                break;
        }

        if (entry.SettingType.IsEnum)
            described.Options = Enum.GetNames(entry.SettingType).ToList();

        return described;
    }

    private static string TypeOf(ConfigEntryBase entry)
    {
        if (entry.SettingType == typeof(bool))
            return "bool";

        if (entry.SettingType.IsEnum || entry.Description?.AcceptableValues is AcceptableValueList<string>)
            return "enum";

        if (entry.SettingType == typeof(int))
            return "int";

        if (entry.SettingType == typeof(float) || entry.SettingType == typeof(double))
            return "float";

        return "string";
    }

    /// <summary>
    /// The value as BepInEx would write it to the .cfg. Done through the entry
    /// so the type converter is the game's own — hand-formatting a float here
    /// would be a second, disagreeing implementation of the same rule.
    /// </summary>
    private static string Serialized(ConfigEntryBase entry, object value)
    {
        try
        {
            return TomlTypeConverter.ConvertToString(value, entry.SettingType);
        }
        catch
        {
            return value?.ToString() ?? string.Empty;
        }
    }
}
