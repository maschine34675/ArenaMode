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
/// (it breaks out on the first unknown object), so this tag is always placed
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
/// Gives every ArenaMode setting its F12 display data from
/// <see cref="ArenaConfigMenuLayout"/> and puts the sections into the layout's
/// order, once, after everything is bound. The bind calls themselves stay
/// plain <c>config.Bind</c> (several offline harnesses compile the settings
/// files against stub ConfigFiles, and parallel work edits them often), so
/// section, key, default, description and range are BepInEx's own, untouched.
/// </summary>
internal static class ArenaConfigMenu
{
    private static readonly FieldInfo DescriptionField =
        typeof(ConfigEntryBase).GetField("<Description>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly PropertyInfo EntriesProperty =
        typeof(ConfigFile).GetProperty("Entries", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo IoLockField =
        typeof(ConfigFile).GetField("_ioLock", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>
    /// Call once every ArenaMode entry is bound (end of InitializeRuntime; the
    /// incendiary prototype binds late, in ArenaFireRuntime.Enable). Fails open:
    /// whatever cannot be applied keeps BepInEx's plain presentation.
    /// </summary>
    internal static void Apply(ConfigFile config)
    {
        if (config == null) return;
        try
        {
            if (!(EntriesProperty?.GetValue(config, null) is Dictionary<ConfigDefinition, ConfigEntryBase> entries))
            {
                Plugin.ModLogger.LogWarning("Arena F12 layout: ConfigFile entry table not found; settings keep their raw keys and bind order.");
                return;
            }
            object lockObject = IoLockField?.GetValue(config) ?? new object();
            int tagged = 0;
            var unmapped = new List<string>();
            lock (lockObject)
            {
                foreach (var pair in entries)
                {
                    if (!ArenaConfigMenuLayout.TryResolve(pair.Key.Section, pair.Key.Key, out var layout))
                    {
                        unmapped.Add(pair.Key.Section + " / " + pair.Key.Key);
                        continue;
                    }
                    if (Tag(pair.Value, layout)) tagged++;
                }
                Reorder(entries);
            }
            if (unmapped.Count > 0)
                Plugin.ModLogger.LogWarning("Arena settings without an F12 layout entry (shown under their raw key): " + string.Join(", ", unmapped));
            Plugin.ModLogger.LogDebug($"Arena F12 layout: {tagged} of {entries.Count} entries named and ordered in {ArenaConfigMenuLayout.Sections.Length} listed sections.");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning("Arena F12 layout not applied; the configuration window keeps raw keys and bind order. " + ex.GetBaseException().Message);
        }
    }

    /// <summary>
    /// Replaces the entry's description with an equal one whose first tag is the
    /// display data. Description text, acceptable values and any existing tags
    /// are carried over unchanged. Idempotent.
    /// </summary>
    private static bool Tag(ConfigEntryBase entry, in ArenaConfigMenuLayout.Resolved layout)
    {
        if (entry == null || DescriptionField == null) return false;
        var old = entry.Description;
        var tags = old?.Tags ?? Array.Empty<object>();
        if (tags.Any(t => t is ConfigurationManagerAttributes)) return false;
        var attributes = new ConfigurationManagerAttributes
        {
            DispName = layout.DisplayName,
            Order = layout.Order,
            Browsable = layout.Hidden ? false : (bool?)null,
            IsAdvanced = layout.Advanced ? true : (bool?)null,
            ShowRangeAsPercent = PercentRange(old?.AcceptableValues) ? layout.Percent : (bool?)null,
        };
        var merged = new object[tags.Length + 1];
        merged[0] = attributes;
        Array.Copy(tags, 0, merged, 1, tags.Length);
        DescriptionField.SetValue(entry, new ConfigDescription(old?.Description ?? string.Empty, old?.AcceptableValues, merged));
        return true;
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
    /// The F12 window shows a plugin's sections in the order of their first entry
    /// in the ConfigFile, i.e. bind order; ArenaMode binds Diagnostics and
    /// Development first, some sections in several places, and its migrations read
    /// entries bound earlier, so the bind code itself is left as it is. Reorders
    /// only the in-memory entry table by the layout's section rank (stable inside
    /// a section): same entry objects, values and events; BepInEx writes the .cfg
    /// sections alphabetically either way. Puts the original table back on any
    /// failure.
    /// </summary>
    private static void Reorder(Dictionary<ConfigDefinition, ConfigEntryBase> entries)
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
}
