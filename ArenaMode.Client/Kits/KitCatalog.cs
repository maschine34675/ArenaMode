using ArenaMode.Config;
using ArenaMode.Shared;
using Newtonsoft.Json;
using SPT.Common.Http;
using System;
using System.Collections.Generic;

namespace ArenaMode.Kits;

/// <summary>
/// Kit catalog, fetched once per game session from the server
/// (user/mods/ArenaMode/config/kits.json via /arenamode/kits).
/// </summary>
public static class KitCatalog
{
    private static List<KitDefinition> _kits;

    public static IReadOnlyList<KitDefinition> Kits
    {
        get
        {
            EnsureLoaded();
            return _kits;
        }
    }

    /// <summary>Fetch the catalog during a loading screen instead of a gameplay frame.</summary>
    public static void Preload() => EnsureLoaded();

    /// <summary>
    /// The configured kit, or the first BUILDABLE catalog entry, or null (naked
    /// respawns). Kits referencing unknown templates (user-edited kits.json, removed
    /// item mods) are skipped — building one would abort mid-equipment-swap.
    /// </summary>
    public static KitDefinition GetSelectedKit()
    {
        EnsureLoaded();
        if (_kits == null || _kits.Count == 0)
            return null;

        var wanted = Settings.SelectedKit.Value;
        KitDefinition preferred = null;
        if (!string.IsNullOrEmpty(wanted))
        {
            preferred = _kits.Find(k => string.Equals(k.Id, wanted, StringComparison.OrdinalIgnoreCase));
            if (preferred == null)
                Plugin.ModLogger.LogWarning($"Selected kit '{wanted}' not in catalog — using first valid kit.");
        }

        foreach (var kit in preferred != null ? Prepend(preferred, _kits) : _kits)
        {
            var invalid = KitBuilder.ValidateKit(kit);
            if (invalid.Count == 0)
                return kit;

            Plugin.ModLogger.LogError(
                $"Kit '{kit.Id}' skipped — unknown templates: {string.Join(", ", invalid)}");
        }

        Plugin.ModLogger.LogError("No buildable kit in the catalog — arena respawns will be naked.");
        return null;
    }

    private static IEnumerable<KitDefinition> Prepend(KitDefinition first, List<KitDefinition> rest)
    {
        yield return first;
        foreach (var kit in rest)
        {
            if (!ReferenceEquals(kit, first))
                yield return kit;
        }
    }

    private static void EnsureLoaded()
    {
        if (_kits != null)
            return;

        try
        {
            var json = RequestHandler.GetJson(Protocol.KitsRoute);
            var kits = JsonConvert.DeserializeObject<List<KitDefinition>>(json);
            if (kits != null)
            {
                kits.Sort((a, b) => a.Order.CompareTo(b.Order));
                _kits = kits;
            }
            else
            {
                _kits = new List<KitDefinition>();
            }

            Plugin.ModLogger.LogInfo($"Kit catalog loaded: {_kits.Count} kits.");
        }
        catch (Exception ex)
        {
            // Leave _kits null so a transient server hiccup is retried on next access
            // instead of downgrading every later raid this session.
            Plugin.ModLogger.LogError($"Kit catalog load failed ({ex.Message}) — will retry on next arena raid.");
        }
    }
}
