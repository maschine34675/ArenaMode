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
    /// The base kit the loading screen drew for the coming raid, handed back by
    /// the NEXT GetBaseKit call and then forgotten.
    ///
    /// The loading-screen preload has to warm the kit the raid will actually
    /// start in, and with the random draw on there is no such kit until
    /// something draws it. So the preload draws it, through the same path the
    /// raid-start swap uses, and parks it here for that swap to collect. One
    /// shot, because a reservation that outlived its raid would hand the next
    /// raid's first life a kit drawn for a different one; the preloader clears
    /// it whenever the raid it was drawn for is discarded or ends.
    /// </summary>
    private static KitDefinition _reservedBaseKit;

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
    /// <summary>
    /// Re-fetches the catalogue for the coming raid.
    ///
    /// The clear is the point. EnsureLoaded returns immediately once _kits is
    /// set, so without it this cached the catalogue for the whole GAME process
    /// and an edit to kits.json needed the game restarted, not just the server —
    /// while LadderCatalog, four files away, already reset itself every raid.
    /// One raid is the right lifetime for both.
    /// </summary>
    public static void Preload()
    {
        _kits = null;
        EnsureLoaded();
    }

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
        var reserved = _reservedBaseKit;
        if (reserved != null)
        {
            _reservedBaseKit = null;
            return reserved;
        }

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
    /// Draws the raid's base kit now and keeps it for the raid-start swap. See
    /// the field. Returns what was drawn so the caller can warm it; null when
    /// the catalogue has nothing buildable, in which case nothing is kept.
    /// </summary>
    public static KitDefinition ReserveBaseKit()
    {
        _reservedBaseKit = null;
        var kit = GetBaseKit();
        _reservedBaseKit = kit;
        return kit;
    }

    /// <summary>Forgets a reservation the raid it was drawn for never collected.</summary>
    public static void ClearReservedBaseKit() => _reservedBaseKit = null;

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
        Plugin.ModLogger.LogDebug($"Arena kit drawn: {picked.Name ?? picked.Id}.");
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

            Plugin.ModLogger.LogDebug($"Kit catalog loaded: {_kits.Count} kits.");
        }
        catch (Exception ex)
        {
            // Leave _kits null so a transient server hiccup is retried on next access
            // instead of downgrading every later raid this session.
            Plugin.ModLogger.LogError($"Kit catalog load failed ({ex.Message}) — will retry on next arena raid.");
        }
    }
}
