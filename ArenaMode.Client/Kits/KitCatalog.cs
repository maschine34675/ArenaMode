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

    /// <summary>Last kit handed out, so random mode does not repeat it back to back.</summary>
    private static string _lastKitId;

    /// <summary>
    /// Lets a mode decide what the next life gets. Gun Game owns this while it
    /// runs, so a respawn hands back the tier the player had reached instead of
    /// starting them over on a random loadout. Cleared at raid end.
    /// </summary>
    public static Func<KitDefinition> KitProvider { get; set; }

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
    /// The kit for the next life: a random one, the configured one, or the first
    /// BUILDABLE catalog entry — null means a naked respawn. Kits referencing
    /// unknown templates (hand-edited kits.json, removed item mods) are skipped;
    /// building one would abort halfway through the equipment swap.
    ///
    /// Asked once per raid start and once per respawn, so random mode hands out
    /// a fresh loadout every life.
    /// </summary>
    public static KitDefinition GetSelectedKit()
    {
        // A mode that owns the loadout outranks both the random draw and the
        // configured kit; a provider that returns nothing falls through.
        var provider = KitProvider;
        if (provider != null)
        {
            try
            {
                var supplied = provider();
                if (supplied != null)
                    return supplied;
            }
            catch (Exception ex)
            {
                Plugin.ModLogger.LogWarning($"Arena kit provider failed ({ex.Message}) — falling back.");
            }
        }

        return GetBaseKit();
    }

    /// <summary>
    /// The kit the raid would hand out on its own — the random draw or the
    /// configured one, ignoring any mode that has taken the choice over.
    /// Gun Game builds on this: it decides the gun and nothing else, so it needs
    /// to ask what the armour and rig would have been without asking itself.
    /// </summary>
    public static KitDefinition GetBaseKit()
    {
        EnsureLoaded();
        if (_kits == null || _kits.Count == 0)
            return null;

        if (Settings.RandomKit.Value)
            return PickRandomKit();

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

    /// <summary>
    /// A random buildable kit, avoiding an immediate repeat while more than one
    /// kit exists — with a handful of kits, true randomness feels broken when it
    /// deals the same loadout twice in a row. The previous kit stays in the draw
    /// as a last resort, so a catalog with one usable entry still works.
    /// </summary>
    private static KitDefinition PickRandomKit()
    {
        var fresh = new List<KitDefinition>(_kits.Count);
        var repeats = new List<KitDefinition>(1);

        foreach (var kit in _kits)
        {
            if (_lastKitId != null && string.Equals(kit.Id, _lastKitId, StringComparison.OrdinalIgnoreCase))
                repeats.Add(kit);
            else
                fresh.Add(kit);
        }

        var picked = DrawBuildable(fresh) ?? DrawBuildable(repeats);
        if (picked == null)
        {
            Plugin.ModLogger.LogError("No buildable kit in the catalog — arena respawns will be naked.");
            return null;
        }

        _lastKitId = picked.Id;
        Plugin.ModLogger.LogInfo($"Arena kit drawn: {picked.Name ?? picked.Id}.");
        return picked;
    }

    /// <summary>Draws in random order and returns the first kit that builds.</summary>
    private static KitDefinition DrawBuildable(List<KitDefinition> candidates)
    {
        while (candidates.Count > 0)
        {
            var index = UnityEngine.Random.Range(0, candidates.Count);
            var kit = candidates[index];
            candidates.RemoveAt(index);

            var invalid = KitBuilder.ValidateKit(kit);
            if (invalid.Count == 0)
                return kit;

            Plugin.ModLogger.LogError(
                $"Kit '{kit.Id}' skipped — unknown templates: {string.Join(", ", invalid)}");
        }

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
