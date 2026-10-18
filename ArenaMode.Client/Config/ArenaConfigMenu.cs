#nullable disable
using BepInEx.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ArenaMode.Config;

/// <summary>
/// Read by SPT's ConfigurationManager through reflection, by its type NAME
/// (SettingEntryBase.SetFromAttributes copies public fields onto the setting's
/// same-named properties). No assembly reference is involved, so this must
/// keep exactly this name. A tag of any other type ends the manager's tag loop
/// (it breaks out on the first unknown object), so this tag is always passed
/// first.
/// </summary>
internal sealed class ConfigurationManagerAttributes
{
    public string DispName;
    public int? Order;
    public bool? Browsable;
    public bool? IsAdvanced;
    public bool? ShowRangeAsPercent;
}

/// <summary>
/// Binds ArenaMode settings with their F12 display data from
/// <see cref="ArenaConfigMenuLayout"/> and puts the sections into the layout's
/// order once everything is bound. Section and key are passed through to
/// BepInEx unchanged, so saved .cfg files keep working.
/// </summary>
internal static class ArenaConfigMenu
{
    private static readonly List<string> Unmapped = new List<string>();

    internal static ConfigEntry<T> BindArena<T>(this ConfigFile config, string section, string key, T defaultValue, string description)
        => config.Bind(section, key, defaultValue, Describe(section, key, description, null, null));

    internal static ConfigEntry<T> BindArena<T>(this ConfigFile config, string section, string key, T defaultValue, ConfigDescription description)
        => config.Bind(section, key, defaultValue,
            Describe(section, key, description?.Description ?? string.Empty, description?.AcceptableValues, description?.Tags));

    private static ConfigDescription Describe(string section, string key, string description, AcceptableValueBase range, object[] tags)
    {
        var kept = tags == null ? new List<object>() : tags.Where(t => t != null).ToList();
        if (!ArenaConfigMenuLayout.TryResolve(section, key, out var layout))
        {
            Unmapped.Add(section + " / " + key);
            return new ConfigDescription(description, range, kept.ToArray());
        }
        var attributes = new ConfigurationManagerAttributes
        {
            DispName = layout.DisplayName,
            Order = layout.Order,
            Browsable = layout.Hidden ? false : (bool?)null,
            IsAdvanced = layout.Advanced ? true : (bool?)null,
            ShowRangeAsPercent = PercentRange(range) ? layout.Percent : (bool?)null,
        };
        kept.Insert(0, attributes);
        return new ConfigDescription(description, range, kept.ToArray());
    }

    private static bool PercentRange(AcceptableValueBase range)
    {
        if (range == null) return false;
        var type = range.GetType();
        var min = type.GetProperty("MinValue", BindingFlags.Instance | BindingFlags.Public)?.GetValue(range, null);
        var max = type.GetProperty("MaxValue", BindingFlags.Instance | BindingFlags.Public)?.GetValue(range, null);
        return ArenaConfigMenuLayout.LooksLikePercentRange(min, max);
    }

    /// <summary>
    /// Puts the plugin's entries into the layout's section order. The F12 window
    /// shows sections in the order of their first entry in the ConfigFile, which is
    /// bind order; ArenaMode binds Diagnostics and Development first and some
    /// sections in several places, and its migrations read entries bound earlier,
    /// so the bind code itself is left as it is. Reorders only the in-memory
    /// entry table: same entry objects, same values and events; BepInEx writes the
    /// .cfg sections alphabetically either way. Fails open (menu keeps bind order).
    /// </summary>
    internal static void ApplySectionOrder(ConfigFile config)
    {
        try
        {
            if (Unmapped.Count > 0)
                Plugin.ModLogger.LogWarning("Arena settings without an F12 layout entry (shown under their raw key): "
                    + string.Join(", ", Unmapped.Distinct()));
            Unmapped.Clear();
            if (config == null) return;
            var property = typeof(ConfigFile).GetProperty("Entries", BindingFlags.Instance | BindingFlags.NonPublic);
            if (!(property?.GetValue(config, null) is Dictionary<ConfigDefinition, ConfigEntryBase> entries))
            {
                Plugin.ModLogger.LogDebug("Arena F12 layout: ConfigFile entry table not found; sections keep bind order.");
                return;
            }
            var lockObject = typeof(ConfigFile).GetField("_ioLock", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(config) ?? new object();
            lock (lockObject)
            {
                var current = entries.ToList();
                var ordered = current
                    .Select((pair, index) => (pair, index))
                    .OrderBy(x => ArenaConfigMenuLayout.SectionRank(x.pair.Key.Section))
                    .ThenBy(x => x.index)
                    .Select(x => x.pair)
                    .ToList();
                bool changed = false;
                for (int i = 0; i < current.Count && !changed; i++)
                    changed = !ReferenceEquals(current[i].Value, ordered[i].Value);
                if (!changed) return;
                try
                {
                    entries.Clear();
                    foreach (var pair in ordered)
                        entries.Add(pair.Key, pair.Value);
                }
                catch
                {
                    // Never lose an entry: put the original table back, in its order.
                    entries.Clear();
                    foreach (var pair in current)
                        entries[pair.Key] = pair.Value;
                    throw;
                }
            }
            Plugin.ModLogger.LogDebug($"Arena F12 layout: {entries.Count} entries in {ArenaConfigMenuLayout.Sections.Length} listed sections ordered for the configuration window.");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning("Arena F12 layout: section order not applied; the configuration window keeps bind order. " + ex.GetBaseException().Message);
        }
    }
}
