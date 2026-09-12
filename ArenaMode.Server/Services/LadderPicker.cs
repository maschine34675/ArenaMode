using System.Text.Json;
using System.Text.Json.Nodes;

namespace ArenaMode.Server.Services;

/// <summary>
/// Draws one raid's Gun Game ladder out of the weapon pool.
///
/// The pool arrives from the generator already ordered weakest to strongest and
/// holding every weapon the game can build — around a hundred and forty of them.
/// A ladder is twenty-four rungs cut from it, one drawn at random from each
/// power band, so the climb keeps its shape while the guns along the way change
/// from raid to raid. Reaching the top used to mean having seen the same
/// twenty-four weapons every time.
///
/// Drawn once per raid rather than per request: the client asks for the ladder
/// while the raid loads, and a fresh draw on every ask would hand out a
/// different gun than the one the player is standing there holding.
/// </summary>
public static class LadderPicker
{
    private static readonly Random Rng = new();
    private static readonly object Lock = new();

    private static string _current = "[]";

    /// <summary>The ladder for the raid that is running now.</summary>
    public static string Current
    {
        get
        {
            lock (Lock)
                return _current;
        }
    }

    /// <summary>
    /// Cuts a new ladder from the pool. Called when a raid starts; a pool that
    /// is missing, unreadable or already ladder-sized is passed through
    /// unchanged, which also keeps a hand-written ladder.json working.
    /// </summary>
    public static void DrawForRaid(string poolJson, int rungs, Action<string>? log = null)
        => Redraw(poolJson, rungs, log);

    /// <summary>
    /// Cuts a new ladder and returns it, for a player who has climbed the whole
    /// of the last one and would otherwise walk the same twenty-four weapons a
    /// second time in the same raid.
    ///
    /// Installs it as Current as well, so a respawn asking for the ladder after
    /// this point gets the one the player is actually climbing.
    /// </summary>
    public static string Redraw(string poolJson, int rungs, Action<string>? log = null,
        ArenaWeaponVariants? variants = null, IEnumerable<string?>? excludedTemplates = null)
    {
        var drawn = Draw(poolJson, rungs, log, ArenaTemplateEligibility.ExclusionSet(excludedTemplates));
        if (variants != null)
        {
            try
            {
                int seed;
                lock (Rng) seed = Rng.Next();
                drawn = variants.CreateCatalog(drawn, seed, out var report);
                log?.Invoke(ArenaWeaponVariants.Summarize("Gun Game", report));
            }
            catch (Exception ex)
            { log?.Invoke($"[ArenaMode] Gun Game attachment draw failed ({ex.Message}); original drawn builds retained."); }
        }
        lock (Lock)
            _current = drawn;

        return drawn;
    }

    /// <summary>
    /// Swaps one of the entry's interchangeable cartridges into every place it
    /// holds ammunition.
    ///
    /// Without this a weapon carries the same round in every raid it ever
    /// appears in, because the pool is generated once. It shows worst on
    /// shotguns, where the choice between buckshot and a slug is most of what
    /// the gun feels like.
    ///
    /// Every ammunition site is rewritten together — the magazine's stack, the
    /// chambered round, and a revolver's individual chambers — because a
    /// cylinder loaded with one round and chambered with another is not a
    /// weapon anyone wants to be handed.
    ///
    /// Choices the configuration excludes are never drawn. The pool is
    /// generated at server start and the exclusions are meant to work without
    /// one — a round found to misbehave mid-evening goes into the list and is
    /// gone from the next raid — and the shipped fallback pool predates the
    /// list altogether.
    /// </summary>
    private static void LoadWithOneOf(JsonNode entry, HashSet<string> excluded)
    {
        var usable = UsableChoices(entry, excluded);
        if (usable.Count == 0)
            return;

        int pick;
        lock (Rng)
            pick = Rng.Next(0, usable.Count);

        var ammo = usable[pick];
        if (entry["items"] is not JsonArray nodes)
            return;

        foreach (var node in nodes)
        {
            var slot = node?["slotId"]?.GetValue<string>();
            if (slot == null)
                continue;

            if (slot == "cartridges" || slot == "patron_in_weapon" || slot.StartsWith("camora_"))
                node!["_tpl"] = ammo;
        }
    }

    /// <summary>
    /// The entry's cartridge choices minus the excluded ones, in pool order.
    /// An entry without choices (a hand-written ladder) is judged by what it
    /// is loaded with: the same list, so the band draw below can pass it over
    /// when that load is excluded too.
    /// </summary>
    private static List<string> UsableChoices(JsonNode entry, HashSet<string> excluded)
    {
        IEnumerable<JsonNode?> source = entry["ammoChoices"] is JsonArray choices && choices.Count > 0
            ? choices
            : (entry["items"] as JsonArray ?? [])
                .Where(node => IsAmmunitionSite(node?["slotId"]?.GetValue<string>()))
                .Select(node => node?["_tpl"]);

        return source
            .Select(node => node?.GetValue<string>())
            .Where(tpl => !string.IsNullOrEmpty(tpl) && !excluded.Contains(tpl!))
            .Select(tpl => tpl!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsAmmunitionSite(string? slot)
        => slot == "cartridges" || slot == "patron_in_weapon" || slot?.StartsWith("camora_", StringComparison.Ordinal) == true;

    private static string Draw(string poolJson, int rungs, Action<string>? log, HashSet<string> excluded)
    {
        try
        {
            if (JsonNode.Parse(poolJson) is not JsonArray pool || pool.Count == 0)
                return poolJson;

            if (rungs < 1 || pool.Count <= rungs)
                return poolJson;

            var ladder = new JsonArray();
            var passedOver = 0;
            for (var i = 0; i < rungs; i++)
            {
                // Equal slices of the sorted pool, so rung one is always drawn
                // from the weakest guns and the last from the strongest.
                var low = i * pool.Count / rungs;
                var high = Math.Max(low + 1, (i + 1) * pool.Count / rungs);

                // Within the band, only weapons that can still be loaded with
                // something the configuration allows. A band with none left
                // draws as before rather than leaving a hole in the ladder.
                var loadable = Enumerable.Range(low, high - low)
                    .Where(j => UsableChoices(pool[j]!, excluded).Count > 0)
                    .ToList();
                passedOver += high - low - loadable.Count;

                int index;
                lock (Rng)
                    index = loadable.Count > 0 ? loadable[Rng.Next(0, loadable.Count)] : Rng.Next(low, high);

                // Detached before re-parenting: a node still owned by the pool
                // array cannot be added to another one.
                var picked = pool[index]!.DeepClone();
                picked["order"] = i;
                picked["id"] = $"rung{i + 1:00}";
                LoadWithOneOf(picked, excluded);
                ladder.Add(picked);
            }

            if (passedOver > 0)
                log?.Invoke($"[ArenaMode] Gun Game ladder: {passedOver} pool weapon(s) passed over, every cartridge of theirs is excluded.");

            log?.Invoke($"[ArenaMode] Gun Game ladder drawn: {rungs} rungs from {pool.Count} weapons.");
            return ladder.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        }
        catch (Exception ex)
        {
            log?.Invoke($"[ArenaMode] Ladder draw failed ({ex.Message}) — serving the pool as-is.");
            return poolJson;
        }
    }
}
